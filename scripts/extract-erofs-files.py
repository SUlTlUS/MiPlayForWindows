#!/usr/bin/env python3
"""
Narrow read-only extractor for selected files from the Mi13P phone firmware
EROFS partitions already indexed in artifacts/phone_firmware.

This is intentionally not a general-purpose EROFS implementation.  It supports
the compact-inode, layout=3, compacted-index, LZ4-block files encountered in
the supplied product_a.img MirrorOS3/MiLinkOS3Cn candidates.  Unsupported
features fail closed so protocol evidence cannot silently drift.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import mmap
import os
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
from struct import unpack_from
from typing import Iterable


BLOCK_SIZE = 4096
BLOCK_BITS = 12

EROFS_INODE_FLAT_PLAIN = 0
EROFS_INODE_COMPRESSED_FULL = 1
EROFS_INODE_FLAT_INLINE = 2
EROFS_INODE_COMPRESSED_COMPACT = 3

Z_EROFS_ADVISE_COMPACTED_2B = 0x0001
Z_EROFS_ADVISE_BIG_PCLUSTER_1 = 0x0002
Z_EROFS_ADVISE_BIG_PCLUSTER_2 = 0x0004
Z_EROFS_ADVISE_INLINE_PCLUSTER = 0x0008
Z_EROFS_ADVISE_INTERLACED_PCLUSTER = 0x0010
Z_EROFS_ADVISE_FRAGMENT_PCLUSTER = 0x0020

Z_EROFS_LCLUSTER_TYPE_PLAIN = 0
Z_EROFS_LCLUSTER_TYPE_HEAD1 = 1
Z_EROFS_LCLUSTER_TYPE_NONHEAD = 2
Z_EROFS_LCLUSTER_TYPE_HEAD2 = 3

Z_EROFS_LI_D0_CBLKCNT = 1 << 11


class ExtractionError(RuntimeError):
    pass


@dataclass(frozen=True)
class IndexedPath:
    partition: str
    path: str
    nid: int
    size: int
    layout: int
    inode_off: int


@dataclass(frozen=True)
class Inode:
    offset: int
    version: int
    datalayout: int
    mode: int
    size: int
    blocks: int
    inode_size: int
    xattr_size: int
    map_header_start: int
    map_header_end: int
    z_advise: int
    algorithmtype: int
    clusterbits_raw: int
    lclusterbits: int


@dataclass(frozen=True)
class LCluster:
    lcn: int
    kind: int
    clusterofs: int
    pblk: int | None

    @property
    def logical_offset(self) -> int:
        return (self.lcn << self.lclusterbits_for_property) | self.clusterofs

    # Set after creation by replacing the generated attribute in helper code.
    lclusterbits_for_property: int = BLOCK_BITS


@dataclass(frozen=True)
class ExtractionSummary:
    partition: str
    source_path: str
    output_path: str
    size: int
    sha256: str
    magic_hex: str
    layout: int
    lclusterbits: int
    data_pclusters: int
    skip_histogram: dict[str, int]


def align(value: int, alignment: int) -> int:
    return (value + alignment - 1) & ~(alignment - 1)


def round_down(value: int, alignment: int) -> int:
    return value & ~(alignment - 1)


def load_index(index_path: Path) -> dict[tuple[str, str], IndexedPath]:
    items: dict[tuple[str, str], IndexedPath] = {}
    with index_path.open("r", encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            entry = json.loads(line)
            if entry.get("type") != "file":
                continue
            path = entry["path"]
            partition = entry["partition"]
            items[(partition, path)] = IndexedPath(
                partition=partition,
                path=path,
                nid=int(entry["nid"]),
                size=int(entry["size"]),
                layout=int(entry["layout"]),
                inode_off=int(entry["inode_off_hex"], 16),
            )
    return items


def xattr_ibody_size(i_xattr_icount: int) -> int:
    if i_xattr_icount == 0:
        return 0
    return 12 + (i_xattr_icount - 1) * 4


def parse_inode(image: mmap.mmap, inode_offset: int) -> Inode:
    i_format, i_xattr_icount, i_mode, i_nb = unpack_from("<HHHH", image, inode_offset)
    version = i_format & 0x01
    datalayout = (i_format >> 1) & 0x07
    if version == 0:
        inode_size = 32
        i_size = unpack_from("<I", image, inode_offset + 8)[0]
        i_u = unpack_from("<I", image, inode_offset + 16)[0]
        blocks = i_u
    elif version == 1:
        inode_size = 64
        i_size = unpack_from("<Q", image, inode_offset + 8)[0]
        i_u = unpack_from("<I", image, inode_offset + 16)[0]
        blocks_hi = i_nb
        blocks = (blocks_hi << 32) | i_u
    else:
        raise ExtractionError(f"unsupported inode version {version} at 0x{inode_offset:x}")

    xattr_size = xattr_ibody_size(i_xattr_icount)
    header_start = align(inode_offset + inode_size + xattr_size, 8)
    header_end = header_start + 8
    z_advise = 0
    algorithmtype = 0
    clusterbits_raw = 0
    lclusterbits = BLOCK_BITS

    if datalayout in (EROFS_INODE_COMPRESSED_FULL, EROFS_INODE_COMPRESSED_COMPACT):
        _h_first, z_advise, algorithmtype, clusterbits_raw = unpack_from(
            "<I H B B", image, header_start
        )
        lclusterbits = BLOCK_BITS + (clusterbits_raw & 0x0F)

    return Inode(
        offset=inode_offset,
        version=version,
        datalayout=datalayout,
        mode=i_mode,
        size=i_size,
        blocks=blocks,
        inode_size=inode_size,
        xattr_size=xattr_size,
        map_header_start=header_start,
        map_header_end=header_end,
        z_advise=z_advise,
        algorithmtype=algorithmtype,
        clusterbits_raw=clusterbits_raw,
        lclusterbits=lclusterbits,
    )


def decode_compacted_bits(lobits: int, buf: bytes | mmap.mmap, bit_pos: int) -> tuple[int, int]:
    byte_pos = bit_pos // 8
    shift = bit_pos & 7
    value = unpack_from("<I", buf, byte_pos)[0] >> shift
    lo = value & ((1 << lobits) - 1)
    kind = (value >> lobits) & 0x03
    return lo, kind


def load_compact_lcluster(image: mmap.mmap, inode: Inode, lcn: int, totalidx: int) -> LCluster:
    ebase = inode.map_header_end
    compacted_4b_initial = ((32 - ebase % 32) // 4) & 7
    compacted_2b = 0
    if (
        inode.z_advise & Z_EROFS_ADVISE_COMPACTED_2B
        and compacted_4b_initial < totalidx
    ):
        compacted_2b = round_down(totalidx - compacted_4b_initial, 16)

    adjusted_lcn = lcn
    pos = ebase
    amortizedshift = 2
    if adjusted_lcn >= compacted_4b_initial:
        pos += compacted_4b_initial * 4
        adjusted_lcn -= compacted_4b_initial
        if adjusted_lcn < compacted_2b:
            amortizedshift = 1
        else:
            pos += compacted_2b * 2
            adjusted_lcn -= compacted_2b

    pos += adjusted_lcn * (1 << amortizedshift)

    if (1 << amortizedshift) == 4 and inode.lclusterbits <= 14:
        vcnt = 2
    elif (1 << amortizedshift) == 2 and inode.lclusterbits <= 12:
        vcnt = 16
    else:
        raise ExtractionError(
            f"unsupported compact index pack: amortizedshift={amortizedshift}, "
            f"lclusterbits={inode.lclusterbits}"
        )

    pack_size = vcnt << amortizedshift
    bytes_into_pack = pos & (pack_size - 1)
    pack_start = pos - bytes_into_pack
    pack = image[pack_start : pack_start + pack_size]
    i = bytes_into_pack >> amortizedshift

    lobits = max(inode.lclusterbits, Z_EROFS_LI_D0_CBLKCNT.bit_length())
    encodebits = (pack_size - 4) * 8 // vcnt
    lo, kind = decode_compacted_bits(lobits, pack, encodebits * i)

    if kind == Z_EROFS_LCLUSTER_TYPE_NONHEAD:
        return LCluster(
            lcn=lcn,
            kind=kind,
            clusterofs=1 << inode.lclusterbits,
            pblk=None,
            lclusterbits_for_property=inode.lclusterbits,
        )

    if inode.z_advise & (Z_EROFS_ADVISE_BIG_PCLUSTER_1 | Z_EROFS_ADVISE_BIG_PCLUSTER_2):
        raise ExtractionError(
            f"big-pcluster compressed inode is not supported at 0x{inode.offset:x}"
        )

    nblk = 1
    j = i
    while j > 0:
        j -= 1
        prior_lo, prior_kind = decode_compacted_bits(lobits, pack, encodebits * j)
        if prior_kind == Z_EROFS_LCLUSTER_TYPE_NONHEAD:
            j -= prior_lo
        if j >= 0:
            nblk += 1

    pblk_base = unpack_from("<I", pack, pack_size - 4)[0]
    return LCluster(
        lcn=lcn,
        kind=kind,
        clusterofs=lo,
        pblk=pblk_base + nblk,
        lclusterbits_for_property=inode.lclusterbits,
    )


def lz4_decompress_block(src: bytes | memoryview, expected_size: int) -> bytes:
    out = bytearray()
    i = 0
    src_len = len(src)
    while i < src_len and len(out) < expected_size:
        token = src[i]
        i += 1

        literal_length = token >> 4
        if literal_length == 15:
            while True:
                if i >= src_len:
                    raise ExtractionError("truncated LZ4 literal length")
                value = src[i]
                i += 1
                literal_length += value
                if value != 255:
                    break

        if i + literal_length > src_len:
            raise ExtractionError("truncated LZ4 literal data")
        out.extend(src[i : i + literal_length])
        i += literal_length

        if len(out) == expected_size:
            break

        if i + 2 > src_len:
            raise ExtractionError("truncated LZ4 match offset")
        offset = src[i] | (src[i + 1] << 8)
        i += 2
        if offset == 0 or offset > len(out):
            raise ExtractionError(f"bad LZ4 offset {offset} at output {len(out)}")

        match_length = token & 0x0F
        if match_length == 15:
            while True:
                if i >= src_len:
                    raise ExtractionError("truncated LZ4 match length")
                value = src[i]
                i += 1
                match_length += value
                if value != 255:
                    break
        match_length += 4

        for _ in range(match_length):
            out.append(out[-offset])
            if len(out) > expected_size:
                raise ExtractionError("LZ4 block expanded beyond expected size")

    if len(out) != expected_size:
        raise ExtractionError(
            f"LZ4 block expanded to {len(out)} bytes, expected {expected_size}"
        )
    return bytes(out)


def read_plain(image: mmap.mmap, pblk: int, expected_size: int) -> bytes:
    start = pblk * BLOCK_SIZE
    end = start + expected_size
    if end > len(image):
        raise ExtractionError(f"plain extent outside image: 0x{start:x}..0x{end:x}")
    return bytes(image[start:end])


def decompress_lz4_pcluster(
    image: mmap.mmap, pblk: int, expected_size: int
) -> tuple[bytes, int]:
    start = pblk * BLOCK_SIZE
    end = start + BLOCK_SIZE
    if end > len(image):
        raise ExtractionError(f"compressed extent outside image: 0x{start:x}..0x{end:x}")
    block = image[start:end]
    if all(value == 0 for value in block):
        return bytes(expected_size), -2
    leading_zeroes = 0
    while leading_zeroes < len(block) and block[leading_zeroes] == 0:
        leading_zeroes += 1
    skip_candidates = []
    for candidate in [0, *range(1, 65), leading_zeroes]:
        if 0 <= candidate < len(block) and candidate not in skip_candidates:
            skip_candidates.append(candidate)
    errors: list[str] = []
    for skip in skip_candidates:
        try:
            return lz4_decompress_block(block[skip:], expected_size), skip
        except ExtractionError as exc:
            if len(errors) < 6:
                errors.append(f"skip={skip}: {exc}")
    raise ExtractionError(
        f"could not decompress pblk {pblk} to {expected_size} bytes; "
        + "; ".join(errors)
    )


def extract_compressed_compact(image: mmap.mmap, inode: Inode) -> tuple[bytes, Counter[int], int]:
    unsupported_advise = inode.z_advise & (
        Z_EROFS_ADVISE_INLINE_PCLUSTER
        | Z_EROFS_ADVISE_INTERLACED_PCLUSTER
        | Z_EROFS_ADVISE_FRAGMENT_PCLUSTER
    )
    if unsupported_advise:
        raise ExtractionError(
            f"unsupported compact compressed advise flags 0x{unsupported_advise:04x}"
        )

    totalidx = (inode.size + (1 << inode.lclusterbits) - 1) >> inode.lclusterbits
    records = [
        load_compact_lcluster(image, inode, lcn, totalidx)
        for lcn in range(totalidx)
    ]
    heads = [
        record
        for record in records
        if record.kind != Z_EROFS_LCLUSTER_TYPE_NONHEAD
    ]
    if not heads:
        raise ExtractionError("compressed file has no head lclusters")

    heads.sort(key=lambda item: item.logical_offset)
    output = bytearray(inode.size)
    skip_histogram: Counter[int] = Counter()
    copied = 0

    for index, head in enumerate(heads):
        if head.pblk is None:
            raise ExtractionError(f"head lcluster {head.lcn} has no pblk")
        logical = head.logical_offset
        next_logical = (
            heads[index + 1].logical_offset if index + 1 < len(heads) else inode.size
        )
        if logical > inode.size:
            raise ExtractionError(
                f"head lcluster {head.lcn} starts beyond EOF: {logical} > {inode.size}"
            )
        if next_logical <= logical:
            if next_logical == logical:
                continue
            raise ExtractionError(
                f"non-increasing logical extents around lcluster {head.lcn}"
            )
        expected = next_logical - logical

        if head.kind == Z_EROFS_LCLUSTER_TYPE_PLAIN:
            data = read_plain(image, head.pblk, expected)
            skip_histogram[-1] += 1
        elif head.kind in (Z_EROFS_LCLUSTER_TYPE_HEAD1, Z_EROFS_LCLUSTER_TYPE_HEAD2):
            data, skip = decompress_lz4_pcluster(image, head.pblk, expected)
            skip_histogram[skip] += 1
        else:
            raise ExtractionError(f"unsupported lcluster type {head.kind}")

        output[logical : logical + expected] = data
        copied += expected

    if copied != inode.size:
        raise ExtractionError(f"copied {copied} bytes, expected file size {inode.size}")
    return bytes(output), skip_histogram, len(heads)


def extract_one(image: mmap.mmap, indexed: IndexedPath) -> tuple[bytes, Inode, Counter[int], int]:
    inode = parse_inode(image, indexed.inode_off)
    if inode.size != indexed.size:
        raise ExtractionError(
            f"index size {indexed.size} differs from inode size {inode.size} for {indexed.path}"
        )
    if inode.datalayout != indexed.layout:
        raise ExtractionError(
            f"index layout {indexed.layout} differs from inode layout {inode.datalayout}"
        )

    if inode.datalayout == EROFS_INODE_COMPRESSED_COMPACT:
        data, skip_histogram, pclusters = extract_compressed_compact(image, inode)
        return data, inode, skip_histogram, pclusters

    raise ExtractionError(
        f"unsupported datalayout {inode.datalayout} for {indexed.path}; "
        "this tool currently only extracts layout=3 compact compressed files"
    )


def sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def default_targets() -> list[str]:
    return [
        "/priv-app/MirrorOS3/MirrorOS3.apk",
        "/priv-app/MirrorOS3/oat/arm64/MirrorOS3.vdex",
        "/priv-app/MirrorOS3/oat/arm64/MirrorOS3.odex",
        "/app/MiLinkOS3Cn/MiLinkOS3Cn.apk",
        "/app/MiLinkOS3Cn/oat/arm64/MiLinkOS3Cn.vdex",
        "/app/MiLinkOS3Cn/oat/arm64/MiLinkOS3Cn.odex",
    ]


def safe_output_path(output_root: Path, partition: str, source_path: str) -> Path:
    relative = source_path.lstrip("/").replace("/", os.sep)
    output = output_root / partition / relative
    resolved_root = output_root.resolve()
    resolved_parent = output.parent.resolve()
    if not str(resolved_parent).lower().startswith(str(resolved_root).lower()):
        raise ExtractionError(f"refusing to write outside output root: {output}")
    return output


def extract_targets(
    artifact_dir: Path,
    partition: str,
    targets: Iterable[str],
    output_root: Path,
) -> list[ExtractionSummary]:
    index = load_index(artifact_dir / "erofs_path_index.jsonl")
    partition_image = artifact_dir / "partitions" / f"{partition}.img"
    if not partition_image.exists():
        raise ExtractionError(f"missing partition image: {partition_image}")

    summaries: list[ExtractionSummary] = []
    with partition_image.open("rb") as handle:
        with mmap.mmap(handle.fileno(), 0, access=mmap.ACCESS_READ) as image:
            for source_path in targets:
                key = (partition, source_path)
                if key not in index:
                    raise ExtractionError(f"path not found in index: {partition}:{source_path}")
                data, inode, skip_histogram, pclusters = extract_one(image, index[key])
                output_path = safe_output_path(output_root, partition, source_path)
                output_path.parent.mkdir(parents=True, exist_ok=True)
                output_path.write_bytes(data)
                summaries.append(
                    ExtractionSummary(
                        partition=partition,
                        source_path=source_path,
                        output_path=str(output_path),
                        size=len(data),
                        sha256=sha256_hex(data),
                        magic_hex=data[:16].hex(),
                        layout=inode.datalayout,
                        lclusterbits=inode.lclusterbits,
                        data_pclusters=pclusters,
                        skip_histogram={
                            str(key): value
                            for key, value in sorted(skip_histogram.items())
                        },
                    )
                )
    return summaries


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--artifact-dir",
        default=r"artifacts\phone_firmware\mi13p_os3_0_313",
        type=Path,
        help="artifact directory containing erofs_path_index.jsonl and partitions/",
    )
    parser.add_argument(
        "--partition",
        default="product_a",
        help="logical partition name without .img suffix",
    )
    parser.add_argument(
        "--output-root",
        default=r"artifacts\phone_firmware\mi13p_os3_0_313\extracted_files",
        type=Path,
        help="workspace output root",
    )
    parser.add_argument(
        "--path",
        action="append",
        dest="paths",
        help="absolute EROFS path to extract; may be repeated",
    )
    parser.add_argument(
        "--summary-json",
        type=Path,
        default=r"artifacts\phone_firmware\mi13p_os3_0_313\erofs_extraction_summary.json",
        help="write a machine-readable extraction summary",
    )
    args = parser.parse_args()

    targets = args.paths if args.paths else default_targets()
    summaries = extract_targets(
        artifact_dir=args.artifact_dir,
        partition=args.partition,
        targets=targets,
        output_root=args.output_root,
    )

    summary_payload = [summary.__dict__ for summary in summaries]
    args.summary_json.parent.mkdir(parents=True, exist_ok=True)
    args.summary_json.write_text(
        json.dumps(summary_payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )

    for summary in summaries:
        print(
            f"{summary.partition}:{summary.source_path} -> {summary.output_path} "
            f"size={summary.size} sha256={summary.sha256[:16]}... "
            f"magic={summary.magic_hex} skips={summary.skip_histogram}"
        )
    print(f"summary={args.summary_json}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
