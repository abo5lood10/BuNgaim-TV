# BuNgaim TV

A small Windows IPTV player (C# WinForms, no installer) built around mpv, with a dark-green glass UI.

- GPU decoding (d3d11va) and optional AI upscaling (FSRCNNX / ArtCNN GLSL shaders) via mpv
- Quality presets: Ultra (24 fps) / High / Balance / Original, with automatic step-down when frames drop
- Video plays inside the app window, volume up to 200%, fullscreen, quick lists (BEIN, 4K, FHD, ...)
- Sign in with your own Xtream-codes subscription (server URL, username, password). Nothing is bundled.

## Build

Needs only Windows (uses the built-in .NET Framework C# compiler):

    powershell -ExecutionPolicy Bypass -File src\build.ps1

This produces `NEXT-Boost.exe` next to `src`. Then put an mpv build in `mpv\` (e.g. shinchiro's Windows builds) with the shaders in `mpv\portable_config\shaders`.
The UI font is not included (licence); the app falls back to Bahnschrift.

## Privacy

Credentials are stored only in `account.ini` on your own PC and are git-ignored. Never commit it.

Third-party: mpv, FSRCNNX (igv), ArtCNN (Artoriuz), uosc, each under its own licence.
