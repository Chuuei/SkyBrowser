# SkyBrowser

**Development note:** SkyBrowser was created with the help of OpenAI Codex.

A Windows desktop sky atlas for your FITS and XISF photographs. Explore an offline
star map, place solved images on the sky, blend overlapping fields, and save scenes.
Display processing preserves your original scientific files.

[![Build and test](https://github.com/Chuuei/SkyBrowser/actions/workflows/ci.yml/badge.svg)](https://github.com/Chuuei/SkyBrowser/actions/workflows/ci.yml)
[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

![SkyBrowser displaying overlapping Horsehead images.](docs/manual/screenshots/main-window.png)

## Download and start

1. Download **SkyBrowser-0.1.1-win-x64.zip** from the
   [latest release](https://github.com/Chuuei/SkyBrowser/releases/latest).
   Choose the application ZIP, rather than GitHub's automatically generated source ZIP.
2. Extract the complete ZIP into a folder you can write to.
3. Run **SkyBrowser.exe**. The Windows x64 package includes its .NET runtime.
4. Keep **Automatic · dark sky (fast)** on the welcome screen. An observing location
   is optional; leaving it empty gives you a celestial atlas.
5. Choose **Browse folder** or **Add images**, then double-click a collection entry
   to frame it. Drag to pan and scroll to zoom.

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
images need external plate solving or manual placement; CFA data are displayed gray.
There is no built-in plate solver or debayering.

No photographs are included in the source repository or application. See the
[example-image guide](examples/README.md) for external sample downloads and their
limitations. Check the provider's terms before sharing downloaded photographs.

Features include overlap blending, collection metadata search, an offline deep-sky
finder, preview caching, sessions, and optional observer location/time controls.
Large files are fully decoded before their display previews are reduced; allow
enough memory. See the [technical reference](docs/technical-reference.md) for exact
coordinate, file-format and processing contracts.

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
