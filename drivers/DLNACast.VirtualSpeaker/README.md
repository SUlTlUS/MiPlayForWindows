# DLNA Cast Virtual Speaker

This is the dedicated render-only WaveRT endpoint used by DLNA Cast when
"speaker-only playback" is enabled. Windows routes the selected audio into
`DLNA Cast Virtual Speaker`; the app captures the endpoint's standard WASAPI
loopback stream and restores the previous route after casting stops.

The driver is derived from MikeTheTech/Virtual-Audio-Driver and Microsoft's
SysVAD sample. See `LICENSE` and `THIRD_PARTY_NOTICES.md`.

## Build prerequisites

- Visual Studio 2026 with the Windows Driver Kit component
- Windows SDK and WDK 10.0.28000.0
- x64 MSBuild (`MSBuild\Current\Bin\amd64\MSBuild.exe`)

The current package is test-signed. Installing it requires Windows test-signing
mode and a reboot unless a production-signed package is supplied.
