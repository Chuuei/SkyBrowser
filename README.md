# SkyBrowser

**Development note:** SkyBrowser was created with the help of OpenAI Codex.

A Windows desktop sky atlas for your FITS and XISF photographs. Explore an offline
star map, place solved images on the sky, blend overlapping fields, and save scenes.
Display processing preserves your original scientific files.

[![Build and test](https://github.com/Chuuei/SkyBrowser/actions/workflows/ci.yml/badge.svg)](https://github.com/Chuuei/SkyBrowser/actions/workflows/ci.yml)
[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

![SkyBrowser displaying overlapping Horsehead images.](docs/manual/screenshots/main-window.png)

## Download and start

1. Download **SkyBrowser-0.4.0-win-x64.zip** from the
   [latest release](https://github.com/Chuuei/SkyBrowser/releases/latest).
   Choose the application ZIP, rather than GitHub's automatically generated source ZIP.
2. Extract the complete ZIP into a folder you can write to.
3. Run **SkyBrowser.exe**. The Windows x64 package includes its .NET runtime.
4. Keep **Automatic · dark sky (fast)** on the welcome screen. An observing location
   is optional; leaving it empty gives you a celestial atlas.
5. Choose **Image library → Add folder** to build your folder list, then choose
   **Load folders** to load their images, including subfolders. Use **Remove selected**
   or **Remove all** to manage the list, or **Add images** for individual files.
   Double-click a collection entry to frame it.
   Drag to pan and scroll to zoom.
   Double-click a photograph in the sky to inspect its full resolution. Use its
   small corner arrow button to open the original in the default application.

Saved library folders reopen automatically on startup. Valid cached previews load
first; new images and missing or stale previews load from the source files.

SkyBrowser targets Windows x64 and .NET 10/WPF. The initial release is an early
0.x release, verified locally on Windows x64 and built/tested by Windows CI.
The executable is unsigned; verify the release's SHA-256 checksum if Windows asks
you to assess the download. Keep the PDF and notice/data files beside the executable.

Read the illustrated [PDF manual](docs/SkyBrowser-User-Manual.pdf), also supplied
in the application ZIP, or the [online manual](docs/user-manual.md).
See [release notes](CHANGELOG.md) for current limits.

## Bring your photographs

Supported files are `.fits`, `.fit`, `.fts`, `.xisf`, and distributed `.xish` with
its sidecars. Suitable stored astrometry places images automatically. Unsolved
images can be blind solved with a configured ASTAP installation or placed manually. Recognized Bayer metadata
automatically produces color previews; unsupported CFA patterns remain grayscale.
Configure **Options → Plate solving** with the path to `astap.exe` or `astap_cli.exe`
and an installed ASTAP star database, then choose **Blind solve & place** for an
image. Solutions are saved locally; original scientific files remain unchanged.

No photographs are included in the source repository or application. See the
[example-image guide](examples/README.md) for external sample downloads and their
limitations. Check the provider's terms before sharing downloaded photographs.

Features include a persistent folder library, recursive image loading, ASTAP
blind solving, overlap blending, collection metadata search, an offline deep-sky
finder, automatic saved-folder startup, preview caching, and optional observer location/time controls.
Choose **Inspect full resolution…** under Selected image or in Image details for
100% inspection, original sample and sky-coordinate readouts, histograms and statistics.
Large files are fully decoded before their display previews are reduced; allow
enough memory. See the [technical reference](docs/technical-reference.md) for exact
coordinate, file-format and processing contracts.

Future work is recorded in the [feature roadmap](docs/roadmap.md).

## Build from source

Use Windows, PowerShell 7 and a .NET 10 SDK with C# 14 support. XISFLib is a pinned
source submodule. Initialize it before opening the solution:

```powershell
git clone https://github.com/Chuuei/SkyBrowser.git
cd SkyBrowser
./tools/Initialize-Dependencies.ps1
dotnet build SkyBrowser.slnx -c Release --artifacts-path ./artifacts
dotnet test tests/SkyBrowser.Tests -c Release --artifacts-path ./artifacts
dotnet run --project src/SkyBrowser.Desktop -c Release --artifacts-path ./artifacts
```

The initializer skips large optional Git LFS images. Normal tests generate their
own small fixtures. Open `SkyBrowser.slnx` in Rider/Visual Studio and run
`SkyBrowser.Desktop`; ordinary IDE Debug builds need no special arguments.
See [development and release instructions](docs/development.md) for dependency
updates, optional image checks and publishing. Python is needed only to regenerate
the manual.

## Help and contribute

The app version appears beside the SkyBrowser name and in the window title.
Include it when reporting a problem; hovering over it shows the full build identity.

- Ask questions in [Discussions](https://github.com/Chuuei/SkyBrowser/discussions).
- Report bugs or propose features through [Issues](https://github.com/Chuuei/SkyBrowser/issues/new/choose).
- Follow [CONTRIBUTING.md](CONTRIBUTING.md) and the [code of conduct](CODE_OF_CONDUCT.md).
- Report vulnerabilities [privately](SECURITY.md). Sanitize paths, metadata and
  observing locations before posting screenshots, logs or image headers.

## License and attribution

SkyBrowser code is licensed under [Apache-2.0](LICENSE). You may use it commercially,
modify it and redistribute it under that license's terms. Preserve the relevant
attribution in [NOTICE](NOTICE) and license/copyright notices when redistributing.
Contributions use the same license; an on-screen badge is not required.

XISFLib.Net retains its own Apache-2.0 license and notices. Third-party libraries,
runtime components and datasets keep their own terms: the adapted OpenNGC catalogue
is **CC-BY-SA-4.0**, independently of application code. Read
[third-party notices](THIRD-PARTY-NOTICES.md) and the
[redistribution guide](docs/licensing.md) before packaging a derivative.
