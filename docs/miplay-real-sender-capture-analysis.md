# MiPlay official sender capture path: 192.168.10.20:42509

This note records the current offline boundary for using bytes captured from a
real Xiaomi/MiPlay sender instead of guessing post-auth SafetyData state.

## Current endpoint evidence

- Confirmed real sender/source endpoint: `192.168.10.20:42509`.
- Analyzer host observed by `ipconfig`: `192.168.10.9`.
- Broad conversation filter: `ip.addr == 192.168.10.20 && tcp.port == 42509`.
- Sender-to-receiver payload filter: `ip.src == 192.168.10.20 && tcp.srcport == 42509`.
- Receiver-to-sender response filter: `ip.dst == 192.168.10.20 && tcp.dstport == 42509`.
- This sender endpoint is treated as an evidence source/filter, not as a destination
  for generated Probe packets.
- No replay, guessed business commands, playback commands, RTSP, media, or audio
  frames are justified by this evidence.

The analyzer host is not the sender endpoint. On ordinary Wi-Fi, a third host may
not see phone-to-speaker unicast traffic unless the capture point is on the
sender, the speaker/AP/router, monitor mode, or a mirrored path. A local Windows
capture from `192.168.10.9` can therefore be a negative capture without proving
that no MiPlay traffic exists.

## Minimum useful artifact

One of the following is enough to begin byte-level comparison:

1. `pcapng`/`pcap` filtered to the official sender and S12 control connection,
   with TCP payload bytes intact.
2. Direction-preserving TCP payload hex exported from the capture, preferably
   `192.168.10.20:42509 -> speaker:8899` and `speaker:8899 -> 192.168.10.20:42509`
   as separate streams.

The artifact may contain replayable authentication/session material or media
metadata. Keep it offline-only; do not paste it into live Probe send paths.

## Offline decoder now available

`MiPlayCapturedCommandStreamDecoder` accepts raw TCP payload bytes or common
hex-dump text and performs only structural analysis:

- resynchronizes on the MiPlay command magic byte `$` (`0x24`);
- decodes the outer command header as `cmd u16 BE`, `seq u16 BE`, `payload length u32 BE`;
- reports capture offset, command id, sequence, payload length, payload SHA-256,
  and a bounded payload hex prefix;
- recognizes SafetyData v1 header metadata (`flags`, padding length field,
  integrity field, ciphertext length) without decrypting it;
- stops on incomplete trailing frames without inventing payload bytes.

This gives us a safe oracle for comparing official sender bytes against Probe
dry-run bytes:

1. First align outer `cmd/seq/len`.
2. Then compare SafetyData header shape and ciphertext length/hash.
3. Only after the official sender vector proves the post-auth cipher phase should
   any new device-side validation be considered.

## Decision boundary

`192.168.10.20:42509` alone is not enough for byte-level comparison. The next
actionable evidence item is the actual pcap/pcapng or exported TCP payload hex
for that endpoint. Until then, this path remains offline and non-replayable.

