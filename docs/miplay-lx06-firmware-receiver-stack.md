# MiPlay LX06 firmware receiver-stack evidence

Date: 2026-07-21

Scope: offline static analysis only. No S12 network operation, shell access,
firmware flashing, playback request, RTSP request, media transfer, or protocol
probe was performed.

## Artifact boundary

Analyzed local artifact:

`artifacts/firmware/mico_lx06_1.74.1`

Source image identity supplied for this extraction:

- file: `mico_all_b9cbb_1.74.1.bin`
- SHA-256:
  `73058C64CBED0CFC915A0E7F162FEF21F01DCA28B477377DA6285B115083624C`
- header: `HDR1` / `LX06` / ROM `1.74.1`
- build date: `2021-04-25`

Correction: LX06 current firmware should be treated as `1.94.13`; the previous
`2.1.x` runtime assumption came from a non-LX06 context and is discarded.
This `1.74.1` image is still older than the current receiver stack. It can
provide receiver-side platform context, but it cannot prove the current
SafetyAuth/post-auth state machine. The nearer `1.88.51` LX06 image contains
the real `mpas`/`mpap` MiPlay receiver evidence and is documented separately in
`docs/miplay-lx06-mpas-receiver-evidence.md`.

## 1. Dynamic receiver injection / overlay evidence

The boot image selects one of two rootfs slots:

- `rootfs-extracted/init:52-63` reads `boot_part` and identifies LX06/L06A
  hardware.
- `rootfs-extracted/init:98-120` maps `boot0` to `/dev/mtdblock4`, otherwise to
  `/dev/mtdblock5`, and mounts the selected squashfs root at `/mnt`.
- `rootfs-extracted/init:130-138` switch-roots into `/mnt/sbin/init`.

The rootfs mounts persistent `/data`:

- `rootfs-extracted/etc/init.d/boot:185-193` attaches `/dev/mtd6` as UBI and
  mounts `/dev/ubi0_0` at `/data`; if the mount fails, it recreates the `data`
  volume.
- `rootfs-extracted/etc/init.d/boot:56-76` copies `mico` configuration into
  `/data/mico` and bind-mounts selected `/data/mico/*` files over
  `/usr/share/mico/*`.
- `rootfs-extracted/etc/init.d/mediaplayer:15-17` and `:33-43` use
  `/data/player` for persisted player configuration symlinks.

The upgrade path can replace rootfs state and preserve overlay/config files:

- `rootfs-extracted/sbin/sysupgrade:100-131` enumerates config and overlay
  files from `/overlay/upper` or `/overlay`.
- `rootfs-extracted/sbin/sysupgrade:184-200` copies the upgrade image to
  `/tmp/sysupgrade.img` when needed.
- `rootfs-extracted/sbin/sysupgrade:248-260` invokes
  `ubus call system sysupgrade`.
- `rootfs-extracted/bin/flash.sh` and `rootfs-extracted/bin/boardupgrade.sh`
  contain the board upgrade flow; `rootfs-extracted/bin/set_upgrade_status`
  writes OTA state to `/data/status/ota`.

Negative injection evidence from this image:

- The enabled init links include services such as `S70mediaplayer`,
  `S90dlnainit`, and `S99mdplay`, but service binaries still resolve to
  rootfs paths like `/usr/bin/mediaplayer`, `/usr/bin/dlna`, and
  `/usr/bin/mdplay`.
- Targeted searches did not find an init-script command pattern equivalent to
  `procd_set_param command /data...`.
- `/data` is proven as persistent state/config storage. A direct `/data`
  receiver-binary autostart path is not proven in this `1.74.1` image.

Conclusion: a newer receiver can plausibly arrive through OTA/rootfs replacement
or a component absent from this old image. This extraction does not prove that
the current MiPlay receiver is injected by a generic `/data` service loader; the
`1.88.51` image instead proves a rootfs `etc/init.d/miplay` service that starts
`/usr/bin/mpas`.

## 2. Reusable playback bridge evidence

The current `1.74.1` rootfs does expose useful playback-control surfaces:

### mediaplayer

- `rootfs-extracted/etc/init.d/mediaplayer:47-54` starts
  `/usr/bin/mediaplayer` through procd.
- `rootfs-extracted/usr/bin/mediaplayer` contains `miplayer_create` and ubus
  method strings:
  - `player_play_url`
  - `player_play_music`
  - `player_play_operation`
  - `player_get_play_status`
  - `player_get_context`
  - `player_set_volume`
  - `player_set_continuous_volume`
  - `get_media_volume`
  - `notify_mdplay_status`
  - `player_wakeup`
- `rootfs-extracted/usr/bin/mphelper:12-18` calls
  `mediaplayer player_play_operation`.
- `rootfs-extracted/usr/bin/mphelper:95-98` calls
  `mediaplayer player_play_url`.
- `rootfs-extracted/usr/bin/mphelper:123-166` calls
  `player_get_context`, `player_set_volume`, and
  `player_set_continuous_volume`.

### local miplayer wrapper

- `rootfs-extracted/etc/init.d/wireless:112-116` defines a shell function named
  `miplay()`, but it only runs `miplayer -f $1` around LED handling.
- `rootfs-extracted/etc/init.d/wireless:1664-1679` and `:1711-1713` use that
  local wrapper for boot/config prompt sounds.

This `miplay()` name is a local prompt helper, not static evidence of the
private MiPlay receiver protocol.

### DLNA/QPlay bridge

- `rootfs-extracted/etc/init.d/dlnainit:36-50` starts `/usr/bin/dlna` only when
  `/data/etc/dlnaswitch.cfg` enables DLNA, and maintains
  `/data/dlna/device.xml`.
- `rootfs-extracted/usr/bin/dlna` contains `QPlayAuth`,
  `SetAVTransportURI`, `player_play_music`, `player_play_operation`, and
  `player_get_play_status` strings.

### mdplay / multiroom FIFO bridge

- `rootfs-extracted/etc/init.d/mdplay:7-13` starts `/usr/bin/mdplay`.
- `rootfs-extracted/etc/init.d/mdplay:16-18` notifies
  `mediaplayer notify_mdplay_status` and removes `/tmp/multiroom.fifo` on stop.
- `rootfs-extracted/usr/bin/mdplay` contains mediaplayer ubus method strings,
  including `player_play_url`, `player_play_music`, `player_play_operation`,
  `player_get_play_status`, `player_get_context`, `player_set_volume`, and
  `player_wakeup`.
- `rootfs-extracted/usr/lib/libmdplay.so` contains `/tmp/multiroom.fifo` and
  `pipe:///tmp/multiroom.fifo?name=Radio`.
- `rootfs-extracted/usr/lib/libxiaomimediaplayerlite.so` contains
  `audiofifo-file` and `/tmp/multiroom.fifo`.

Conclusion: the reusable bridge for this firmware is local
`mediaplayer`/`ubus`/`miplayer`/DLNA/mdplay/FIFO playback control. That is
valuable receiver-side context, but it is not evidence of the low-latency
SafetyAuth MiPlay receiver.

## 3. Minimum missing evidence

Directly reconstructing the current modern S12 SafetyAuth receiver should not
proceed from this `1.74.1` image alone. However, this is no longer the main
implementation blocker for basic functionality: the newer `1.88.51` LX06 image
already provides positive `mpas`/`mpap` receiver evidence for a bounded
old-version/basic route.

The remaining split is:

1. use `1.88.51` for legacy/basic reconstruction around `8899`, `0x0028/0x0029`,
   `0x001e/0x001f`, `Cmd_Open 0x0000`, and the `mpap` audio receiver bridge;
2. reserve a matching `1.94.13` OTA/rootfs delta or read-only device file/process
   map for exact current modern `0x1400..0x1403` SafetyAuth compatibility only.

Safe conclusion:

- keep the current SafetyAuth/post-auth probe boundary unchanged;
- do not send media, playback, RTSP, `openDevice`, or `0x0058`;
- use `1.74.1` only to model reusable platform bridges, and use `1.88.51` as the
  primary old-version/basic receiver evidence set.

The test-backed representation is in
`MiPlayLx06FirmwareReceiverStackEvidence`.
