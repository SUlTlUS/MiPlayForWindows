#!/usr/bin/env python3
"""
Targeted DEX trace helper for the MiPlay phone-firmware route.

This intentionally avoids whole-APK JADX. It parses DEX tables, field_ids,
method_ids, class_data, and code_item references just far enough to trace
selected method/field/string evidence such as StatsUtils ref_* builders,
MiDevice.ref_channel accessors, and CmdSessionControl entry points.
"""

from __future__ import annotations

import argparse
import json
from dataclasses import dataclass
from pathlib import Path
from struct import unpack_from


class DexError(RuntimeError):
    pass


def read_uleb128(data: bytes, offset: int) -> tuple[int, int]:
    result = 0
    shift = 0
    pos = offset
    while True:
        if pos >= len(data):
            raise DexError("truncated uleb128")
        value = data[pos]
        pos += 1
        result |= (value & 0x7F) << shift
        if (value & 0x80) == 0:
            return result, pos
        shift += 7
        if shift > 35:
            raise DexError("uleb128 too large")


def instruction_width(insns: list[int], index: int) -> int:
    op = insns[index] & 0xFF
    if op == 0x00:
        # payload pseudo instructions are identified by the high byte of the
        # first code unit. They are only relevant for skipping switch/array data.
        ident = insns[index] >> 8
        if ident == 0x01 and index + 1 < len(insns):
            return 2 + insns[index + 1] * 2
        if ident == 0x02 and index + 1 < len(insns):
            return 4 + insns[index + 1] * 4
        if ident == 0x03 and index + 3 < len(insns):
            size = insns[index + 2] | (insns[index + 3] << 16)
            element_width = insns[index + 1]
            return 4 + ((size * element_width + 1) // 2)
        return 1
    if op in {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        0x11, 0x12, 0x1D, 0x1E, 0x21, 0x27, 0x28,
    }:
        return 1
    if op in {
        0x13, 0x14, 0x15, 0x16, 0x19, 0x1A, 0x1B, 0x1C,
        0x1F, 0x20, 0x22, 0x23, 0x29, 0x2D, 0x2E, 0x2F,
        0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37,
        0x38, 0x39, 0x3A, 0x3B, 0x3C, 0x3D,
        *range(0x44, 0x6E),
        *range(0x90, 0xB0),
        *range(0xD0, 0xE3),
    }:
        return 2
    if op in {
        0x17, 0x24, 0x25, 0x26, 0x2A, 0x2B, 0x2C,
        *range(0x6E, 0x73),
        *range(0x74, 0x79),
    }:
        return 3
    if op == 0x18:
        return 5
    return 1


@dataclass(frozen=True)
class MethodDef:
    method_idx: int
    access_flags: int
    code_off: int
    class_descriptor: str
    name: str
    proto: str

    @property
    def signature(self) -> str:
        return f"{self.class_descriptor}->{self.name}{self.proto}"


class DexFile:
    def __init__(self, path: Path):
        self.path = path
        self.data = path.read_bytes()
        if self.data[:4] != b"dex\n":
            raise DexError(f"not a dex file: {path}")
        self.string_ids_size, self.string_ids_off = unpack_from("<II", self.data, 0x38)
        self.type_ids_size, self.type_ids_off = unpack_from("<II", self.data, 0x40)
        self.proto_ids_size, self.proto_ids_off = unpack_from("<II", self.data, 0x48)
        self.field_ids_size, self.field_ids_off = unpack_from("<II", self.data, 0x50)
        self.method_ids_size, self.method_ids_off = unpack_from("<II", self.data, 0x58)
        self.class_defs_size, self.class_defs_off = unpack_from("<II", self.data, 0x60)
        self.strings = [self._read_string(i) for i in range(self.string_ids_size)]
        self.types = [self.strings[unpack_from("<I", self.data, self.type_ids_off + i * 4)[0]] for i in range(self.type_ids_size)]
        self.protos = [self._read_proto(i) for i in range(self.proto_ids_size)]
        self.fields = [self._read_field_id(i) for i in range(self.field_ids_size)]
        self.methods = [self._read_method_id(i) for i in range(self.method_ids_size)]
        self.method_defs = self._read_method_defs()

    def _read_string(self, index: int) -> str:
        string_data_off = unpack_from("<I", self.data, self.string_ids_off + index * 4)[0]
        _utf16_size, pos = read_uleb128(self.data, string_data_off)
        end = self.data.find(b"\0", pos)
        if end < 0:
            raise DexError(f"unterminated string at 0x{string_data_off:x}")
        return self.data[pos:end].decode("utf-8", "replace")

    def _read_proto(self, index: int) -> str:
        _shorty_idx, return_type_idx, parameters_off = unpack_from(
            "<III", self.data, self.proto_ids_off + index * 12
        )
        params: list[str] = []
        if parameters_off:
            size = unpack_from("<I", self.data, parameters_off)[0]
            for i in range(size):
                type_idx = unpack_from("<H", self.data, parameters_off + 4 + i * 2)[0]
                params.append(self.types[type_idx])
        return f"({''.join(params)}){self.types[return_type_idx]}"

    def _read_field_id(self, index: int) -> tuple[str, str, str]:
        class_idx, type_idx, name_idx = unpack_from(
            "<HHI", self.data, self.field_ids_off + index * 8
        )
        return self.types[class_idx], self.types[type_idx], self.strings[name_idx]

    def _read_method_id(self, index: int) -> tuple[str, str, str]:
        class_idx, proto_idx, name_idx = unpack_from(
            "<HHI", self.data, self.method_ids_off + index * 8
        )
        return self.types[class_idx], self.strings[name_idx], self.protos[proto_idx]

    def _read_method_defs(self) -> dict[int, MethodDef]:
        defs: dict[int, MethodDef] = {}
        for i in range(self.class_defs_size):
            off = self.class_defs_off + i * 32
            class_idx = unpack_from("<I", self.data, off)[0]
            class_descriptor = self.types[class_idx]
            class_data_off = unpack_from("<I", self.data, off + 24)[0]
            if not class_data_off:
                continue
            pos = class_data_off
            static_fields_size, pos = read_uleb128(self.data, pos)
            instance_fields_size, pos = read_uleb128(self.data, pos)
            direct_methods_size, pos = read_uleb128(self.data, pos)
            virtual_methods_size, pos = read_uleb128(self.data, pos)
            for _ in range(static_fields_size + instance_fields_size):
                _field_idx_diff, pos = read_uleb128(self.data, pos)
                _access_flags, pos = read_uleb128(self.data, pos)
            for method_list_size in (direct_methods_size, virtual_methods_size):
                running_idx = 0
                for _ in range(method_list_size):
                    method_idx_diff, pos = read_uleb128(self.data, pos)
                    access_flags, pos = read_uleb128(self.data, pos)
                    code_off, pos = read_uleb128(self.data, pos)
                    running_idx += method_idx_diff
                    m_class, m_name, m_proto = self.methods[running_idx]
                    defs[running_idx] = MethodDef(
                        method_idx=running_idx,
                        access_flags=access_flags,
                        code_off=code_off,
                        class_descriptor=class_descriptor or m_class,
                        name=m_name,
                        proto=m_proto,
                    )
        return defs

    def iter_code(self, method: MethodDef) -> list[int]:
        if method.code_off == 0:
            return []
        insns_size = unpack_from("<I", self.data, method.code_off + 12)[0]
        insns_off = method.code_off + 16
        return [
            unpack_from("<H", self.data, insns_off + i * 2)[0]
            for i in range(insns_size)
        ]

    def scan_method(self, method: MethodDef) -> dict[str, list[int]]:
        refs = {"invokes": [], "strings": [], "fields": [], "types": []}
        insns = self.iter_code(method)
        i = 0
        while i < len(insns):
            op = insns[i] & 0xFF
            width = instruction_width(insns, i)
            if op in range(0x6E, 0x73) or op in range(0x74, 0x79):
                if i + 1 < len(insns):
                    refs["invokes"].append(insns[i + 1])
            elif op in range(0x52, 0x6E):
                if i + 1 < len(insns):
                    refs["fields"].append(insns[i + 1])
            elif op == 0x1A:
                if i + 1 < len(insns):
                    refs["strings"].append(insns[i + 1])
            elif op == 0x1B:
                if i + 2 < len(insns):
                    refs["strings"].append(insns[i + 1] | (insns[i + 2] << 16))
            elif op in {0x1C, 0x1F, 0x20, 0x22}:
                if i + 1 < len(insns):
                    refs["types"].append(insns[i + 1])
            i += max(width, 1)
        return refs

    def method_json(self, idx: int) -> dict:
        cls, name, proto = self.methods[idx]
        return {"method_idx": idx, "class": cls, "name": name, "proto": proto}

    def field_json(self, idx: int) -> dict:
        cls, typ, name = self.fields[idx]
        return {"field_idx": idx, "class": cls, "type": typ, "name": name}


def method_sig(dex: DexFile, idx: int) -> str:
    cls, name, proto = dex.methods[idx]
    return f"{cls}->{name}{proto}"


def field_sig(dex: DexFile, idx: int) -> str:
    cls, typ, name = dex.fields[idx]
    return f"{cls}->{name}:{typ}"


def matches_terms(value: str, terms: list[str]) -> bool:
    lower = value.lower()
    return any(term.lower() in lower for term in terms)


def analyze(roots: list[Path], output: Path, terms: list[str]) -> dict:
    dex_paths: list[Path] = []
    for root in roots:
        if root.is_file() and root.suffix == ".dex":
            dex_paths.append(root)
        elif root.exists():
            dex_paths.extend(sorted(root.rglob("classes*.dex")))

    payload = {
        "terms": terms,
        "dex_files": [],
        "matching_methods": [],
        "matching_fields": [],
        "matching_strings": [],
        "referrer_methods": [],
    }

    for dex_path in sorted(set(dex_paths)):
        dex = DexFile(dex_path)
        rel = str(dex_path).replace("\\", "/")
        payload["dex_files"].append(rel)

        target_methods = {
            idx
            for idx, (cls, name, proto) in enumerate(dex.methods)
            if matches_terms(f"{cls}->{name}{proto}", terms)
        }
        target_fields = {
            idx
            for idx, (cls, typ, name) in enumerate(dex.fields)
            if matches_terms(f"{cls}->{name}:{typ}", terms)
        }
        target_strings = {
            idx
            for idx, value in enumerate(dex.strings)
            if matches_terms(value, terms)
        }

        for idx in sorted(target_methods):
            item = dex.method_json(idx)
            item["dex"] = rel
            item["defined"] = idx in dex.method_defs
            item["code_off_hex"] = hex(dex.method_defs[idx].code_off) if idx in dex.method_defs else None
            payload["matching_methods"].append(item)

        for idx in sorted(target_fields):
            item = dex.field_json(idx)
            item["dex"] = rel
            payload["matching_fields"].append(item)

        for idx in sorted(target_strings):
            payload["matching_strings"].append({"dex": rel, "string_idx": idx, "value": dex.strings[idx]})

        target_any = target_methods | set()
        for method in dex.method_defs.values():
            refs = dex.scan_method(method)
            invoke_hits = sorted(target_methods.intersection(refs["invokes"]))
            field_hits = sorted(target_fields.intersection(refs["fields"]))
            string_hits = sorted(target_strings.intersection(refs["strings"]))
            if not invoke_hits and not field_hits and not string_hits:
                continue
            payload["referrer_methods"].append(
                {
                    "dex": rel,
                    "caller_method_idx": method.method_idx,
                    "caller": method.signature,
                    "code_off_hex": hex(method.code_off),
                    "invoke_hits": [method_sig(dex, idx) for idx in invoke_hits],
                    "field_hits": [field_sig(dex, idx) for idx in field_hits],
                    "string_hits": [dex.strings[idx] for idx in string_hits[:80]],
                }
            )

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return payload


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "roots",
        nargs="*",
        type=Path,
        default=[Path(r"artifacts\phone_firmware\mi13p_os3_0_313\apk_extract\MiLinkOS3Cn")],
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(r"artifacts\phone_firmware\mi13p_os3_0_313\phone_source_ref_identity_trace.json"),
    )
    parser.add_argument(
        "--terms",
        nargs="*",
        default=[
            "ref_channel",
            "ref_function",
            "ref_content",
            "getRef_channel",
            "setRef_channel",
            "getRefChannel",
            "setPlaySource",
            "ontrackDataToJson",
            "setRefContent",
            "activeMiplayStats",
            "startCommandChannel",
            "cmdSessionSuccess",
            "CmdSessionControl",
            "AppInfo",
            "ServiceName",
            "signature",
            "package",
        ],
    )
    args = parser.parse_args()
    payload = analyze(args.roots, args.output, args.terms)
    print(f"wrote {args.output}")
    print(f"dex_files={len(payload['dex_files'])}")
    print(f"matching_methods={len(payload['matching_methods'])}")
    print(f"matching_fields={len(payload['matching_fields'])}")
    print(f"matching_strings={len(payload['matching_strings'])}")
    print(f"referrer_methods={len(payload['referrer_methods'])}")
    for item in payload["referrer_methods"][:120]:
        print(json.dumps(item, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())