# Development and reproducible builds

Use Windows, PowerShell 7 and the .NET 10 SDK. The application uses WPF and C# 14.
`global.json` permits newer SDKs; application targets remain .NET 10. The current
local verification environment is Windows x64. CI also validates on Windows x64.

## Clone

The source dependency is pinned as `dependencies/XISFLib.Net`. Git's normal
Download ZIP does not include submodule contents. Use Git for development.
Large FITS/XISF fixtures in the dependency are optional Git LFS objects. Skip their
download during checkout; no LFS installation or sample download is needed to build.

```powershell
git clone https://github.com/Chuuei/SkyBrowser.git
cd SkyBrowser
./tools/Initialize-Dependencies.ps1
dotnet build SkyBrowser.slnx -c Release --artifacts-path ./artifacts
dotnet test tests/SkyBrowser.Tests -c Release --artifacts-path ./artifacts
dotnet run --project src/SkyBrowser.Desktop -c Release --artifacts-path ./artifacts
```

Open `SkyBrowser.slnx` in Rider/Visual Studio and run `SkyBrowser.Desktop`; ordinary
Debug builds work after initializing dependencies. Python is only needed to
regenerate the manual, not for normal builds or application publishing.

`XisfLibraryRoot` can override the root for project builds. The solution explicitly
includes the pinned dependency path; use the default layout for solution builds.
Do not silently update the submodule to a moving branch or substitute a different package.

## Optional real images

See [examples](../examples/README.md) for external inputs. Normal tests generate
small deterministic fixtures and do not need external photographs. Enable optional
checks only after providing suitable inputs:

```powershell
$env:SKYBROWSER_SOLVED_SAMPLES = (Resolve-Path ./examples).Path
$env:SKYBROWSER_SCREENSHOTS = Join-Path $PWD 'artifacts/screenshots'
dotnet test tests/SkyBrowser.Tests -c Release --artifacts-path ./artifacts
```

Keep these captures private. They are not approved manual assets. A user of the
external library corpus can additionally set `SKYBROWSER_SAMPLE_DIRECTORY` to its
local fixture directory. Source images and large corpus files stay outside commits.

## Releases

Builds and PRs run the Windows CI workflow with read-only repository permissions.
The release job alone can publish assets. A `vX.Y.Z` tag publishes a release only
after build/tests/package validation succeed; a hyphenated tag is a prerelease.
The version is passed to MSBuild from the validated tag. Local packaging:

```powershell
./tools/Build-Release.ps1 -Version 0.1.1
```

The ZIP, checksums, notices and source-compliance materials are written under
`artifacts/release`. Consult [licensing](licensing.md) before distributing binaries.
The self-contained application does not require a separately installed .NET runtime.
The Windows publish profile pins the reviewed runtime patch independently of the
SDK selected for building. Update its version and the redistribution inventory
together when reviewing a runtime security update.
Keep the PDF, license/NOTICE files and `Data` directory alongside the executable.

For detailed scientific behavior and probes see the
[technical reference](technical-reference.md) and [performance review](performance-review.md).
