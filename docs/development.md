# Development and reproducible builds

Use Windows, PowerShell 7 and the .NET 10 SDK. The application uses WPF and C# 14.
`global.json` permits newer SDKs; application targets remain .NET 10. The current
local verification environment is Windows x64. CI also validates on Windows x64.

Read [architecture and state ownership](architecture.md) before extending a desktop
workflow or adding scene rules. It maps the feature files and their lifetime,
threading and dependency boundaries.

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

`Version.props` is the single source of the application's semantic version. Use
`major.minor.patch`: increase minor for new features, patch for compatible fixes,
and major for breaking changes after 1.0. Before 1.0, minor releases may change
behavior. An optional suffix such as `-rc.1` marks a prerelease.

The app shows this version beside its name and in its window title. Hover over
the version to see the full build identity, including the Git commit when available.
MSBuild generates assembly and Windows file versions from the same value;
prerelease labels remain in the displayed/product version. No Git installation
or network access is needed when running the app.

Builds and PRs run the Windows CI workflow with read-only repository permissions.
The release job alone can publish assets. A `vX.Y.Z` tag publishes a release only
after build/tests/package validation succeed; a hyphenated tag is a prerelease.
CI rejects a tag that differs from `Version.props`, and package validation checks
the executable's product and file versions. To release, update `Version.props`
and `CHANGELOG.md` together, merge the reviewed change, then tag that commit with
the exact version (for example `v0.3.0`). Never move a published version tag.
Local packaging uses the committed version automatically:

```powershell
./tools/Get-Version.ps1
./tools/Build-Release.ps1
```

The ZIP, checksums, notices and source-compliance materials are written under
`artifacts/release/<version>`. An optional `-Version` argument is a consistency
check and must match `Version.props`; it cannot relabel a build.
Consult [licensing](licensing.md) before distributing binaries.
The self-contained application does not require a separately installed .NET runtime.
The Windows publish profile pins the reviewed runtime patch independently of the
SDK selected for building. Update its version and the redistribution inventory
together when reviewing a runtime security update.
The regular ZIP keeps the PDF, license/NOTICE files and `Data` directory alongside
the executable. `Build-Release.ps1` also produces a **win-x64-compact.zip** with
exactly three files: the same executable, the PDF and `SkyBrowser-Notices.zip`.
The latter preserves every supporting file and its relative path, including the
adapted catalogue JSON. Keep this archive with the executable when redistributing.

Compact packaging does not change executable bytes or runtime behaviour. The
runtime/native dependencies are already bundled by `WinX64.pubxml`; compact
packaging moves only documents, notices and the duplicate catalogue copy into a
ZIP. The catalogue used at runtime is embedded in the Core assembly. The packager
rejects unexpected external runtime files rather than silently archiving them,
then verifies every file hash from the finished outer and inner ZIPs. CI validates
both formats. To package an existing validated publish directory:

```powershell
./tools/New-CompactPackage.ps1 -PublishDirectory ./artifacts/publish/SkyBrowser.Desktop/release_win-x64 -OutputPath ./artifacts/compact/SkyBrowser-win-x64-compact.zip
```

`Test-Publish.ps1` and the compact packager accept `-DependencyManifest` for a
`SkyBrowser.deps.json` produced in a non-default artifacts directory. It is used to
validate the bundled runtime/package inventory, not included in the distribution.

Keep `PublishTrimmed=false`: Microsoft currently disables trimming for WPF because
its runtime reflection cannot be analysed safely. Native AOT requires trimming and
is therefore not a supported deployment option for this WPF app. These are separate
from self-contained/single-file deployment, which is already enabled. See
[WPF trimming incompatibilities](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities#wpf)
and [Native AOT limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#limitations-of-native-aot-deployment).

For detailed scientific behavior and probes see the
[technical reference](technical-reference.md) and [performance review](performance-review.md).
