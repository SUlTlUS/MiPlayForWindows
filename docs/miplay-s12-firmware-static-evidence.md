# MiPlay S12 firmware static evidence

Date: 2026-07-21

Scope: offline static analysis only. No S12/device network operation was
performed.

## Artifact

Local firmware image:

`D:/download/mico_all_b9cbb_1.74.1.bin`

The original path on the workstation is `D:/下载/mico_all_b9cbb_1.74.1.bin`.

Firmware metadata recovered from the `HDR1` header:

- SHA-256:
  `73058C64CBED0CFC915A0E7F162FEF21F01DCA28B477377DA6285B115083624C`
- hardware: `LX06`
- ROM: `1.74.1`
- channel: `release`
- build time: `Sun, 25 Apr 2021 23:11:23 +0800`
- git tag: `commit b9e9b6640c2491c7a77a22612e47790e6c8c0356`

## Extraction

The firmware contains a little-endian SquashFS v4/XZ root filesystem at
offset `0x2b8`.

Rootfs superblock/extraction facts:

- bytes used: `36,993,414`
- inodes: `1,996`
- extracted records: `1,996`
- directories: `111`
- files: `1,245`
- symlinks: `639`
- extraction warnings: `0`

The firmware also contains one valid Android boot image at offset `36,994,184`.
Its gzip ramdisk expands to a `newc` cpio archive:

- records: `139`
- files: `31`
- warnings: `0`

The decompressed kernel and boot ramdisk were searched for MiPlay/SafetyAuth
terms.

## Positive receiver-side evidence

The rootfs contains receiver-side DLNA and mdplay/iotdcm components:

- `etc/init.d/dlnainit` starts `/usr/bin/dlna`, gated by
  `/data/etc/dlnaswitch.cfg`.
- `etc/init.d/mdplay` starts `/usr/bin/mdplay`.
- `usr/bin/mdplay` imports `libmdplay.so` and `libiotdcm_mdplay.so`.
- `usr/bin/mdplay` contains `GetDeviceInfo` plus platform identity helpers:
  `MdplayGetDeviceId`, `MdplayGetAppid`, `MdplayGetTokenId`,
  `MdplayGetUserId`, and `MdplayGetDeviceName`.
- `usr/bin/mdplay` formats MIOT credentials as
  `Authorization: MIOT-TOKEN-V1 app_id:%s,token:%s,session_id:%s`.
- `usr/bin/mdplay` starts iotdcm with
  `iotdcm_create user_id:%lld app_id:%s dev_id:%s token:%s udp_cb:%p`.
- `usr/lib/libiotdcm.so` contains `securityKey` and `service_key` token
  manager strings.

This is useful evidence for the device-side identity prerequisites behind
mdplay/iotdcm multiroom behavior.

## Negative 8899/SafetyAuth evidence

Static string searches over the extracted rootfs, boot ramdisk, and decompressed
kernel did not find:

- `SafetyAuth`
- `SafetyData`
- `MiPlay` / `miplay`
- `8899`
- `0x1400` / `0x1401` / `0x1402` / `0x1403`
- `1400` / `1401` / `1402` / `1403` as protocol strings tied to MiPlay

The rootfs does contain a receiver-side `GetDeviceInfo` string in `mdplay`, but
that should not be conflated with the APK-side legacy TCP 8899
`0x001e -> 0x001f` getDeviceInfo command path.

## Current conclusion

This firmware is worth keeping as receiver-side context, especially for
`mdplay/iotdcm` identity fields. It does not currently justify another S12
network reprobe and does not prove a legacy TCP 8899 SafetyAuth receiver
implementation.

The next useful offline target remains a business/client-side Android artifact
that proves the MiPlay static discovery/listener/channel context, or a receiver
firmware/module image that actually contains 8899/SafetyAuth strings or code.
