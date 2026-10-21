# Guide for AI agents working on SkyBrowser

This file applies to the whole SkyBrowser repository. Keep it current when the
architecture, workflows or important behavior changes. Read the relevant code
before editing; this is a guide to the current implementation, not a replacement
for it.

## Project and dependencies

SkyBrowser is a Windows desktop sky atlas for the user's astronomical photographs.
The observer is at the center of a celestial sphere; FITS/XISF previews are placed
using their astrometry over an offline star, constellation and deep-sky map.

- Use **.NET 10, C# 14 and WPF**. The application is not Avalonia. The external
  XISFLib repository has an Avalonia viewer whose finishing pipeline inspired ours.
- Open `SkyBrowser.slnx` in Rider and run the **SkyBrowser.Desktop** project.
  Ordinary IDE Debug builds must continue to work without special arguments.
- `Directory.Build.props` sets language version, nullable references, implicit
  usings, deterministic builds and repository-local `artifacts/` output.
- `Version.props` is the application's single semantic version. The desktop header
  and title use its generated assembly metadata; keep `CHANGELOG.md` in step when
  bumping it. Release tags and packaged executable metadata must match this file.
- `global.json` permits a newer SDK to build the .NET 10 targets. A newer selected
  SDK is not a reason to change the target framework.
- XISFLib is a source project reference in the pinned Git submodule
  `dependencies/XISFLib.Net/XISFLib/XISFLib.csproj`. `XisfLibraryRoot` overrides the root for
  project builds; the solution also has an explicit dependency path to update if
  the submodule path changes. Do not replace this with an unrelated package.
- The user owns XISFLib and permits changes there when necessary. Before editing
  that separate repository, read its own `AGENTS.md` and inspect its working tree.
  It can contain unrelated changes; preserve them and respect filesystem access
  boundaries. Prefer keeping application-specific behavior here.

## Where things live

| Path | Responsibility |
| --- | --- |
| `src/SkyBrowser.Core/Astronomy/` | Coordinates, camera, observer frame, placement, spherical footprints and catalogues |
| `src/SkyBrowser.Core/Imaging/` | XISFLib loading, metadata snapshots, stretches and cosmetic finishing |
| `src/SkyBrowser.Core/Caching/` | Source validation, persistent SQLite previews and cached loading |
| `src/SkyBrowser.Core/Library/` | Durable archive index, recursive scans, search and source-bound ASTAP placements |
| `src/SkyBrowser.Core/Astrometry/` | ASTAP process, temporary scientific solver inputs and validated TAN results |
| `src/SkyBrowser.Desktop/LibraryWindow.xaml` | Staged folder management and confirmed recursive scene loading |
| `src/SkyBrowser.Core/Imaging/ScientificImageInspection.cs` | On-demand full-resolution display, original samples, histogram/statistics and pixel-center coordinates |
| `src/SkyBrowser.Core/Astronomy/FitsSkySolution.cs` | Exact forward FITS models, SIP/TPV distortion and persisted cache coefficients |
| `src/SkyBrowser.Desktop/Services/ImageExport.cs` | Metadata-free PNG/JPEG encoding, alpha flattening and atomic export |
| `src/SkyBrowser.Desktop/ExportWindow.xaml` | Sky/preview export settings and validation |
| `src/SkyBrowser.Desktop/ImageInspectionWindow.xaml` | Independent full-width fit/100% inspection, panning and sample/coordinate readouts below the image |
| `src/SkyBrowser.Core/Configuration/ApplicationOptions.cs` | Validated defaults, atomic settings persistence and recovery |
| `src/SkyBrowser.Desktop/MainWindow.xaml.cs` | Application composition, shared operation lifetime, options and window coordination |
| `src/SkyBrowser.Desktop/MainWindow.Loading.cs` | Startup/import/folder workflows and guarded processing progress |
| `src/SkyBrowser.Desktop/MainWindow.Inspector.cs` | Selected-image inspection, display changes and manual placement |
| `src/SkyBrowser.Desktop/MainWindow.Navigation.cs` | Target search, observer orientation, timeline and camera commands |
| `src/SkyBrowser.Desktop/MainWindow.Export.cs` | Display exports, ASTAP solving and solved-copy workflows |
| `src/SkyBrowser.Desktop/Controls/SkyViewport.cs` | WPF textured sky, overlays, batching, overlap rendering and hit testing |
| `src/SkyBrowser.Desktop/Controls/SkyViewport.Drawing.cs` | Sky/foreground drawing, projected annotations and bounded text layouts |
| `src/SkyBrowser.Desktop/Controls/ObserverMap.cs` | Offline location picker |
| `src/SkyBrowser.Desktop/Controls/SceneTimeline.xaml` | Scene time slider, day steps and live-clock controls |
| `src/SkyBrowser.Desktop/Styles/Controls.xaml` | Shared dark control palette/templates, merged by App.xaml |
| `src/SkyBrowser.Desktop/Models/` | Layer state and normalized collection search |
| `src/SkyBrowser.Desktop/ComparisonWindow.xaml` | Independent aligned blink/swipe/linked sky views |
| `src/SkyBrowser.Desktop/MainWindow.Collection.cs` | Collection sorting/filtering and cancellable batch actions |
| `src/SkyBrowser.Core/Library/ImageOrganization.cs` | Durable per-path/image tags, favorites and groups |
| `src/SkyBrowser.Core/Astronomy/SurveyImages.cs` | Opt-in CDS cutouts, validation and separate offline LRU cache |
| `src/SkyBrowser.Core/Astronomy/TargetVisibility.cs` | UTC planning, geometric events, Sun/Moon and imaging windows |
| `src/SkyBrowser.Core/Astronomy/LunarEphemeris.cs` | J2000 Moon position, observer parallax, phase and Sun-directed disk illumination |
| `src/SkyBrowser.Desktop/Controls/SkyViewport.Moon.cs` | Scene-time lunar state, bounded graphic cache and hover readout |
| `src/SkyBrowser.Desktop/Controls/MoonGlyph.cs` | Procedural illustrative lunar disk with phase shading and decorative texture |
| `src/SkyBrowser.Core/Astronomy/CameraFraming.cs` | Equipment fields, sourced smart-telescope presets, spherical mosaic geometry and CSV coordinates |
| `src/SkyBrowser.Core/Configuration/EquipmentProfileStore.cs` | Bounded, atomic custom equipment persistence and backup recovery |
| `src/SkyBrowser.Core/Astronomy/SkyGeometry.cs` | Great-circle geometry and bookmark/measurement records |
| `src/SkyBrowser.Core/Astronomy/TargetDistances.cs` | Offline sourced distance estimates, alias lookup and target-pair measurements |
| `src/SkyBrowser.Core/Astronomy/DistanceSpaceCamera.cs` | Linear light-year positions, perspective projection and independent 3D camera |
| `src/SkyBrowser.Core/Astronomy/DistanceSpaceScene.cs` | Transient primary/comparison selection, cached pair measurement and navigation rules without WPF |
| `src/SkyBrowser.Core/Astronomy/ImageComparisonGeometry.cs` | Spherical overlap eligibility and shared-field comparison framing |
| `src/SkyBrowser.Desktop/DistanceSpaceWindow.xaml` | Search, scale presets, sourced two-target measurements and navigation for the 3D distance map |
| `src/SkyBrowser.Desktop/Controls/DistanceSpaceView.cs` | WPF input/picking and adaptation of the Core distance scene to existing control events |
| `src/SkyBrowser.Desktop/Controls/DistanceSpaceView.Drawing.cs` | Depth-sorted point cloud, equatorial rings, labels and pair overlays |
| `tools/Build-TargetDistances.py` | Rebuilds the attributed NASA/CDS distance snapshot from cached sources |
| `tools/TargetDistanceSources.py` | Source identity, position, method, units and provenance validation |
| `src/SkyBrowser.Desktop/SkyToolsWindow.xaml` | Survey, planning and measurement controls |
| `src/SkyBrowser.Desktop/SkyToolsWindow.*.cs` | Feature-specific Surveys, Visibility, Measurements, Distances and Framing handlers; the shell owns shared lifetime |
| `src/SkyBrowser.Desktop/Controls/VisibilityChart.cs` | Offline altitude chart, darkness/window shading and readouts |
| `src/SkyBrowser.Desktop/Services/` | Window appearance and observer preferences |
| `tests/SkyBrowser.Tests/` | xUnit scientific, cache, settings and real WPF integration tests |
| `tests/SkyBrowser.CacheProbe/` | Separate-process persistence checks and performance probes |
| `examples/` | User-supplied FITS/XISF images, including overlapping Horsehead fields |
| `tools/Build-TargetCatalog.ps1` | Regenerates bundled deep-sky data from pinned sources |
| `tools/Build-ApplicationIcon.ps1` | Renders the header mark into the multi-resolution Windows application icon |
| `tools/ManualScreenshots/` | Privacy-safe WPF screenshot generator; separate project outside the solution |
| `tools/Build-UserManual.py` | PDF generation, privacy validation and page rendering |
| `docs/user-manual.md` | Editable beginner manual |
| `docs/manual/README.md` | Manual regeneration and screenshot privacy instructions |
| `docs/performance-review.md` | Measurements, implemented optimizations, limits and reproduction commands |

Start with `README.md` for detailed behavior and supported formats. For changes to
a particular feature, read its existing tests as well as its implementation.
See `docs/architecture.md` for state ownership and feature boundaries. Partial
window files share one WPF window and its operation/lifetime guards; moving a
handler to another partial must not duplicate those state owners. Keep distance
scene rules in Core rather than reintroducing them into rendering or input code.

## Build, run and verify

Run these PowerShell commands from the repository root on Windows:

```powershell
dotnet build SkyBrowser.slnx -c Release --artifacts-path .\artifacts
dotnet run --project src\SkyBrowser.Desktop -c Release --artifacts-path .\artifacts
```

The build executable is
`artifacts/bin/SkyBrowser.Desktop/release/SkyBrowser.exe`. Framework-dependent
builds require the .NET 10 Desktop Runtime. Startup arguments after `--` can be
image paths or a folder; saved library folders also load automatically.

For code changes, run the relevant tests; use the full suite for changes spanning
image processing, astrometry, caching or desktop workflows:

```powershell
$env:SKYBROWSER_SOLVED_SAMPLES = Join-Path $PWD 'examples'
$env:SKYBROWSER_SCREENSHOTS = Join-Path $PWD 'artifacts\screenshots'
# Optional additional source images, if the adjacent checkout provides them:
$env:SKYBROWSER_SAMPLE_DIRECTORY = [IO.Path]::GetFullPath((Join-Path $PWD 'dependencies\XISFLib.Net\Examples'))
dotnet test tests\SkyBrowser.Tests -c Release --artifacts-path .\artifacts
```

- Real-image checks depend on these optional sample directories. Set the solved
  examples path when verifying image quality, placement or blending.
- Desktop tests use a real WPF window on an STA dispatcher. Keep their shared
  `Application` lifetime: WPF permits only one `Application` per process.
- Tests cover cancellation and late callbacks as well as successful results.
  Preserve numerical/pixel comparisons, source preservation and separate-process
  cache checks when changing the associated implementation.
- Performance probes and their limitations are documented in
  `docs/performance-review.md` and `README.md`. Measure Release builds on
  representative inputs; do not add machine-dependent timing assertions to tests.
- Documentation-only edits do not require an application build. Check referenced
  paths and formatting. Manual edits additionally require PDF generation and
  visual review as described below.

## Scientific image and coordinate contracts

- **Never rewrite the user's source images** to stretch, clean, rotate or cache
  them. Preserve scientific sample precision, metadata, checksums and validation
  in the reader. Do not weaken malformed-input or decompression bounds checks.
- Supported extensions are `.fits`, `.fit`, `.fts`, `.xisf` and distributed
  `.xish` with its sidecars. Support is for 2D grayscale/color and FITS RGB/RGBA
  third axes; unsupported cubes/complex samples are rejected. Recognized 2 x 2
  Bayer mosaics are automatically debayered for previews before stretch/resizing;
  unsupported CFA remains gray. ASTAP is an optional external blind solver.
- Native XISF Revision 1 solutions take precedence, followed by legacy linear
  `PCL:AstrometricSolution:*` metadata and FITS WCS. Legacy opaque/spline models
  are rejected with a warning rather than approximated as linear. FITS supports
  TAN/SIP/TPV and SIN/STG/ZEA/ARC in degrees, ICRS or FK5/J2000, CD, PC/CDELT,
  CROTA and swapped RA/Dec axes. SIP applies before CD to pixel offsets; TPV
  applies after CD in degrees, with reversed monomial arguments for PV2. Retain
  order/index/finite checks, mixed-model/lookup rejection and exact-pole defaults.
  For these zenithal projections, CRVAL/LONPOLE uniquely fix the rotation;
  any finite LATPOLE preference in [-90, 90], including Draco's zero, is equivalent.
  `tools/Build-AstrometryReferences.py` generates synthetic Astropy 8.0.2/WCSLIB
  reference coordinates; normal tests need only its checked-in JSON. Undecoded
  projections or frames require warnings/manual placement.
- Cached FITS placements retain exact coefficients alongside the 33 x 33 display
  mesh. Do not reconstruct a nonlinear scientific readout from mesh interpolation.
  Never retain an entire scientific image through an astrometry closure.
- FITS reference pixels, the library's zero-based pixel API and native XISF
  half-pixel centers have different conventions. Keep their tests and conversions.
  Use spherical/vector geometry around the RA seam and poles.
- Map the original stored pixel rows to the solution. Display-only XISF rotation
  and reflection must not move a texture away from its astrometry. Preview
  processing preserves the full frame, orientation and alpha; do not crop it for
  appearance. Manual placement uses degrees, north up/east left at zero rotation,
  with positive rotation counterclockwise and optional horizontal mirroring.
- Previews have at most 2048 pixels on their longest side. Average scientific
  samples before stretch/quantization to retain faint detail. Full source decoding
  still occurs; do not assume the preview size limits peak source memory.
- `ImagePlacement` is immutable and retains its evaluated 33 x 33 sample grid.
  `CaptureSampleGrid` returns a defensive copy; input grids must also be copied.

## Display processing

- `SkyImageDebayer` detects native XISF CFA (takes precedence) or FITS `BAYERPAT`
  with `XBAYROFF`/`YBAYROFF` (also `XBAYEROFF`/`YBAYEROFF`, including HIERARCH).
  Explicit `ROWORDER=BOTTOM-UP` adjusts tile parity for the source height; absent
  row order uses the tile at stored (0,0). Debayering preserves stored coordinates,
  sample type/bounds and original metadata snapshots. Bilinear interpolation uses
  available neighbors at borders, exact wide integer means and row cancellation.
  Gray display functions are linked across reconstructed RGB. Invalid masks stay
  gray with a message and skip cosmetic finishing; RGB/alpha data are not debayered.
- **Automatic · dark sky (fast)** is the default: stretch only, initially targeting
  a 10% background. It uses a stored display function when available and otherwise
  handles dark/high-pedestal linear samples adaptively, retaining developed images
  when appropriate. It must not perform gradient correction, color balancing,
  denoising, sharpening or color enhancement, even if cleanup options are saved.
- **Dark sky + cleanup** explicitly runs `SkyImageFinisher` cosmetics as well as
  stretching. Retain the darker Horsehead result and conservative processing
  based on the external viewer. Do not merge it with Automatic.
- Linear, stored-display-function and library auto-stretch variants remain
  distinct. Per-image choices take precedence over global
  defaults; saving Options does not reprocess all currently loaded images.
- Row processing uses bounded parallelism, disjoint writes and unchanged sample
  accumulation order. Preserve deterministic pixels, alpha and monotonic progress.
  Cancellation is checked between stages/rows; native decoder/filter calls may
  have to finish before cancellation takes effect.

## Persistence, loading and responsiveness

- The durable library is `library-v1.sqlite3` beside the options file, separate
  from the disposable/movable preview cache. Ordinary imports register source
  metadata; folder discovery is recursive and skips reparse points. The library
  dialog lists folders, with additions/removals staged until **Load folders**.
  Cancel discards edits. Opening/editing the dialog never decodes images, and the
  desktop does not start background indexing. Confirmation atomically saves roots
  and loads all supported images, deduplicating overlapping roots and documents.
  Imports and solution application validate sampled source hashes. Folder removal
  keeps indexed metadata, saved solutions and original files.
- `CachedSkyImageLoader.Library` overlays valid externally solved placements after
  reading/storing raw previews. Never bake a source-specific solution into an old
  preview variant. Solutions bind to source/sidecar signatures and image index,
  remain after cache clearing/moves, and are invalidated by source changes.
- **Forget ASTAP solution** deletes only the selected path/image-index solution
  and updates the library's placement flag in one transaction. `LoadedSkyImage`
  retains file astrometry separately from the ASTAP overlay (`HasAstapSolution`),
  so forgetting restores file placement/warnings without decoding pixels. Keep
  manual placement, other images, organization, display preferences and previews.
  The preview cache stores file astrometry, never an applied ASTAP overlay.
- ASTAP configuration belongs in Options → Plate solving. Use `ArgumentList`,
  `UseShellExecute=false`, hidden process windows, bounded output, cancellation
  that kills/waits for the process tree, and a configurable timeout. Valid source
  pointing metadata starts a nearby search (`-r 5`, 1,000 stars), optionally using
  field height estimated from stored rows/pixel size/focal length. User FOV wins.
  Nearby failure/timeout retries full-sky (`-r 180 -ra 0 -spd 90`, 500 stars) without
  metadata position/scale; retain user FOV or automatic 0. The nearby budget is at
  most 20 seconds or one quarter of the shared total deadline. Use independent
  output bases and `-sip n` for supported linear TAN. Parse degree versus hour RA
  and XISF metre versus FITS millimetre focal lengths explicitly. Keep hint parsing
  in AstapPlateSolver.Hints.cs and search/process lifetime in its Search partial.
  Export a full-frame, linear Float32 Gray FITS copy into a unique temporary
  directory, retaining stored rows and using decoded/debayered scientific data.
  Never pass source paths or `-update` to ASTAP. Validate INI coordinates/matrix,
  evaluate the full mesh, recheck source signatures, then commit the placement.
  Use the fitted matrix and full source dimensions for sizing. Dismiss only ASTAP's
  recognized initial-scale advisory after validation; retain other diagnostics.
  The inspector's center pixel scale comes from the placement and source dimensions,
  including restored meshes, rather than rounded warning values or preview sizes.
- **Save solved copy** exports source-bound ASTAP results as separate FITS/XISF
  documents, preserving scientific samples and stored geometry. It recovers a
  centered TAN matrix from exact saved grid vertices and validates the full mesh.
  Retain all images; update only the selected image's astrometry. FITS exports use
  standard WCS; XISF exports also carry native Revision 1 properties. Never export
  stretched previews. Preserve same-format metadata; report cross-format omissions.
  Refuse source/sidecar destinations and stale solutions. Stage output, revalidate
  signatures and cancellation, then commit without changing the scene or original.
- User options are JSON at `%LOCALAPPDATA%\SkyBrowser\settings.json`, separate
  from the disposable preview database. Keep validation, atomic writes and `.bak`
  recovery. Do not use or change the user's actual settings for automated tests.
- The default cache is
  `%LOCALAPPDATA%\SkyBrowser\Cache\previews-v1.sqlite3`, with a **10 GB** limit
  measured as 10 x 1024^3 bytes. It must survive program restarts and skip decoding
  and finishing on a valid hit. It retains pixels, metadata, placement grids,
  per-image render preferences and recent folders.
- Cache keys include rendering settings, an explicit preview pipeline revision and
  dependency informational versions/source revisions. Never use assembly MVIDs or
  the app/Core build version: unrelated code edits, output paths, publishing and
  Debug/Release builds must reuse compatible previews. Bump `PreviewPipelineVersion`
  in `ProcessedImageCache` when application pixels, astrometry, metadata/schema or
  locally modified reader behavior change incompatibly. Source
  signatures include size/times and sampled hashes at the start, middle and end,
  including distributed XISF sidecars. These are change detectors, not full-file
  integrity hashes. Keep corruption recovery, validation and transactional LRU
  eviction. A changed processing contract or dependency may invalidate old variants;
  ordinary application rebuilds must not.
- Cache failures fall back to normal processing. Cold jobs are bounded to avoid
  overlapping large source allocations; warm hits must not wait for an unrelated
  cold decode. Keep processing/database/search-index work off the UI thread.
- Cache migration preserves the old folder and merges destination records.
  **Clear saved previews** acts immediately, even if Options is later cancelled;
  it retains rendering preferences, recent folders and already loaded images.
  Options shows used space and preview count beside the limit.
- Image library is the primary browsing workflow. **Load folders** loads every
  listed root, including roots hidden by its folder-name/path filter. **Remove
  selected** and **Remove all** edit only the pending list. Confirmed removals take
  covered layers out of the scene unless a retained root still covers them; unrelated
  scene layers remain. Retained layers keep their state, and repeat confirmations
  discover new files and missing image indices without duplicates. Add/drop imports
  retain metadata but do not add folder roots. Legacy direct-folder APIs still
  replace/append and retain recent folders for test compatibility. Discovery
  and loading run off the UI thread with the existing serial cold-loader gate.
- Progress opens after a short delay (currently 350 ms), with totals, remaining
  items, current stage and cached/failed/skipped counts. Cancellation keeps partial
  imports and startup loads, and the old display during a cancelled reprocess.
- Guard asynchronous updates by operation identity and item version. Old-folder
  progress and finishing callbacks must not alter a newer operation or layer.
- Batch scene changes through `SkyViewport.DeferLayerUpdates`; nested scopes and
  repeated disposal are supported. Detach events from removed/reset layers. Keep
  reusable camera state, frozen mesh topology and bounded drawing caches.
- Startup reads the saved folder list after first-run setup, recursively discovers
  its files and loads valid cached previews first. Missing/stale/corrupt previews
  fall back to source processing with the existing signature checks and preferences.
  Startup does not rewrite the saved roots; explicit image/folder arguments append.
  Do not restore the removed session-file UI or serializer. Per-image rendering and
  ASTAP results persist; transient layer/camera/target changes are not autosaved.

## UI behavior to preserve

- Sky tools must not download images on open, pan, startup or scene-time changes.
  Survey downloads are explicit allow-listed HTTPS CDS requests with bounded bodies,
  cancellation and validated FITS WCS/dimensions. Survey cache payloads have a separate
  512 MB LRU limit, with offline region selection and retained credits. Survey meshes
  stay behind personal photos and outside collection hit testing. Preserve provider
  acknowledgments in Data/LICENSE-DSS-SURVEYS.txt and visible atlas/export attribution.
  ShowCachedSurveyImages restores all cached regions offline on startup/options enable,
  preserving cache LRU order and the camera. Skip damaged entries, guard late restores
  with survey revisions, and keep manual choices when disabling this startup default.
  Composite overlapping surveys oldest to newest in independent Viewport3D depth
  buffers below the personal-photo viewport, sharing its camera. Small radial
  offsets cannot prevent intersections between differently sampled spherical meshes.
  Retain pixel regressions for rotated wide fields, opacity, pan/zoom and export.
- Planning uses the existing pinned Astronomy Engine, J2000 target coordinates,
  topocentric Sun/Moon positions and an explicit UTC day. Keep geometric altitudes,
  polar/circumpolar cases, refined crossings and documented two-minute detection
  limits. Temporary planning input cannot change startup options. Avoid DefineStar's
  shared mutable slots; concurrent reports must be independent. Closing cancels jobs.
- Sky tools has Survey background, Target visibility and Measurements tabs.
  Measurements combines sky-point/manual distances and catalogue target pairs in
  one scrollable page with bounded, virtualized target lists. Bookmarks are removed
  from the desktop UI and atlas overlays;
  legacy bookmark storage remains for compatibility, without loading it at startup.
  Measurements use great-circle distances and remain transient.
  The Objects in 3D camera caps its vertical focus span at 13.8 billion light-years;
  zoom, presets and framing share this bound. Show feedback at the zoom-out limit.
  Its reference plane, scale rings, guide drops and north/south colours use the
  primary focused object's position as origin, retaining J2000 orientation.
  Pair framing and panning retain that reference; Reset to Sun restores the Sun.
  Optional positive object distances from the observer, in light-years, add a scaled
  half-angle cosine-rule 3D separation; retain small-angle accuracy, validation and
  export labels. Starting/clearing a measurement clears its distance inputs.
  Target distances offers 3,283 bundled NASA/CDS estimates cross-identified through
  DeepSkyCatalog. Selecting a pair measures catalogue object centres and supplies
  distances without network access or automatic camera changes. Preserve source
  URLs/date/hashes, uncertainty wording and approximate (two significant figure)
  atlas/export labels. Show coverage for Messier/Caldwell/NGC/IC and retain reported
  asymmetric object errors in source details without claiming separation error bars.
  Never infer distances from nearby clicks or redshift, use
  zero for unknown values, or guess distances for missing objects. Manual edits
  detach catalogue provenance; clearing/new picks clear selected target identities.
  Regenerate Data/target-distances.json with tools/Build-TargetDistances.py; source
  HTML/VOTable data stays under ignored artifacts/distance-sources. Preserve NASA and
  SIMBAD/CDS provenance notices. Source priority is NASA Messier, NASA Caldwell,
  reviewed NASA APOD facts, then latest eligible SIMBAD literature value. Exclude
  redshift/flow/kinematic/unknown galaxy methods, uncertain flags, member-star IDs,
  nonfinite values and identity/position disagreements. Check exact catalogue aliases
  plus a bounded angular extent; never cross-match to a nearby object as a fallback.
  The generator rejects capped/overflowed TAP responses. For ingestion changes run
  `python -m unittest discover -s tools/tests -p test_target_distance_sources.py`.
  Scale bars use the actual projected row; N/E indicates celestial directions,
  not observer azimuth.
  Keep seam/pole/antipode tests, export fidelity, and main-scene preservation.

- Collection thumbnails reuse loaded frozen bitmaps. Multiple selection supports
  Ctrl/Shift and Select all matching. Sorting/filtering changes only the collection
  view, never scene layer order or visibility. Structured metadata matching stays
  within its selected FITS/XISF field; tag/group matching is exact and ignores case.
  Organization lives in the library's separate path/image-index table and survives
  preview clearing and root removal. Batch annotation edits are transactional and
  preserve unspecified fields; checked empty strings clear tags/groups. Batch
  display changes retain per-image finish settings, continue after individual
  failures and retain completed previews/committed annotations on cancellation.
  Create progress reporters on the UI thread before passing them to workers.
- Compare requires at least two placed selections whose sky footprints all overlap
  one another. Enforce this in the main command and comparison constructor.
  Keep Compare disabled unless the current selection is eligible, refresh it when
  selected placements change, and explain the disabled state in its tooltip.
  Start in Swipe divider mode, frame both images through shared sky on opening/pair/mode
  changes, and offer Fit both. B excludes the current A to prevent self-comparison.
  Geometry belongs in Core and must handle rotated fields, insets, RA seams and poles.
  It uses independent full-opacity
  preview/placement snapshots and two synchronized sky projections. Swipe clips a
  full-size, frozen 2D renderings of the views rather than the live 3D meshes;
  changing the clip on a live mesh can leave black columns during small divider
  movements. Refresh swipe frames after camera/pair/size changes, coalesce layout
  updates, retain them during divider motion, and release them on mode exit/close.
  Keep the divider boundary on device pixels. Blink stops on deactivation/close;
  closing detaches timer/layer handlers. Comparison cannot change the atlas's
  camera, visibility, opacity or preview settings. Keep real pixel-render checks for
  all modes and linked camera checks around the RA seam and celestial poles.

- **Export…** in the header saves sky views or existing selected previews as PNG
  or JPEG. Sky capture uses an independent viewport at the chosen resolution,
  preserving camera, observer instant, layers/opacity and target state without
  touching the live view or showing action buttons/hover emphasis. Labels,
  outlines, grid and center-coordinate caption are independent choices. Retain
  PNG alpha; flatten JPEG onto the dark sky color. Encoder output carries no
  source metadata. Refuse source/sidecar destinations, stage output and recheck
  cancellation/protection before replacing a destination. Detached export viewports
  must unsubscribe from all layers, including when rendering fails.
- Keep control styling in the shared `Styles/Controls.xaml` dictionary so sliders,
  scrollbars, dropdowns, text fields, checks, expanders and progress indicators
  match in the main window and dialogs. Preserve WPF template parts/commands and
  content scrolling/virtualization. ComboBox selection uses SelectionBoxItem,
  avoiding the old crash from reparenting a selected item's visual. Keep visible
  keyboard-focus, hover, selection and disabled states. TextBox already forwards
  Padding to PART_ContentHost; adding a host margin would double the spacing.
- The header logo, SkyBrowser name and tagline share one horizontal row. Keep
  them vertically centered and clear of the action buttons at the minimum width.
- Native title bars match the dark header through `Services/WindowAppearance.cs`
  and the shared Window style. Preserve standard Windows resizing, snapping and
  caption controls. High contrast uses system caption colors; older Windows can
  fall back gracefully when DWM color attributes are unsupported.
  Derived windows explicitly reference the shared Window style; implicit base-type
  styles do not apply automatically. Icon resource URIs include the assembly name
  so dialogs also work when hosted by tests or the screenshot generator.
- The application and window icon is `src/SkyBrowser.Desktop/Assets/SkyBrowser.ico`,
  generated from the existing header geometry in `MainWindow.xaml`. If the mark
  changes, regenerate it with `pwsh -STA -File tools/Build-ApplicationIcon.ps1`.
  The committed icon needs no generation step during normal builds or publishing.
- The collection and selected-image panels have a user-adjustable divider.
  The selected-image region starts collapsed, with the image name and a right-hand
  chevron always visible while selected. Expanding reveals a scrollable body below
  the fixed header and restores the saved divider proportions. Collapsing hides
  the divider and returns space to the list; keep this separate from deselection.
  Each image's visibility toggle uses an eye/crossed-out-eye icon, with a tooltip
  and accessible action/state labels; retain keyboard toggle support.
  Image editing selection clears on catalogue/manual sky navigation, survey-region
  navigation and measurement actions, collapsing the inspector without hiding images.
  Empty-sky clicks and Escape in the atlas/image list also deselect. Panning, zooming
  and merely opening tools retain selection. Selecting an image cancels unfinished
  sky-point picking; completed measurements can remain as guides.
  Image details must use retained metadata/previews rather than decode again.
  The circled info icon uses one geometry coordinate space for circle, stem and
  dot; font glyph substitution previously caused misalignment.
- **Inspect full resolution…** is available in Selected image and Image details.
  Opening details alone still never decodes scientific data. Inspection loads on a
  worker with a serial allocation gate, revalidates source/sidecar signatures and
  reads source-bound ASTAP placement without writing library records. Closing
  cancels and releases data; late progress/results cannot update a closed window.
  Preserve true source dimensions and 100% mapping to device pixels across DPI.
  The image uses the full available width, with sample/coordinate readouts below;
  do not add a histogram/statistics sidebar.
  Show display RGB as #RRGGBB from the full-resolution processed BGRA pixels,
  separately labelled from the original scientific samples.
  Readouts use stored pixel centers (`(x+.5)/width`, `(y+.5)/height`), original XISF
  values or FITS physical values with BLANK as NaN. Bayer statistics/readouts use
  the mosaic; alpha stays separate. UInt64 readouts must not round through double.
  Histograms/statistics cover all source samples before display processing, count
  non-finite values separately and use double precision for aggregate statistics.
- Collection search covers filenames and retained metadata, normalizing case,
  accents, spacing and punctuation (`IC434` equals `IC 434`). All query terms must
  match. Filtering the list does not hide sky layers.
- With overlap blending enabled, larger angular footprints are behind smaller
  ones; insets use 60% of the selected opacity and become fully opaque
  on hover without changing the slider. Hover changes scene opacity only. With blending off, later collection
  entries are on top. Handle rotated fields, nested overlaps, poles and the seam.
- Double-clicking a collection entry frames it. Double-clicking a sky image opens
  internal full-resolution inspection for the foremost hit. The hovered or selected
  image has a small corner button to open its original in the default Windows app.
  Keep the button inside a visible, foremost part of the image across rotation,
  mirroring, overlap, zoom, RA seams and poles; hide it when no suitable space is
  visible. It must not initiate sky dragging or inspection. Preserve native button
  keyboard access, focus styling, tooltip and automation name.
- **Find in the sky** starts collapsed, has filters and one shared **Go** button.
  Selecting a target adds its marker, fills coordinates and frames it; manual
  coordinate navigation retains zoom. The bundled catalogue covers Messier,
  Caldwell, NGC and IC, while only prominent targets appear by default. Preserve
  alias deduplication and dropdown virtualization.
- Location/time and cache controls belong in **Options**, not extra toolbar
  buttons. The map is a separate window. Without a location, keep atlas behavior.
  With one, default to south, altitude 20 degrees, 90-degree field of view and the
  current system time. Fixed UTC/computer-local times are supported; the map does
  not infer a time zone. Keep J2000 alignment and supported 1900-2100 dates.
- Atlas mode uses a RA/Dec grid; observer mode uses an Alt/Az grid fixed to the
  horizon. Equatorial-grid convergence marks a celestial pole; the local zenith
  is straight up (coincident with a celestial pole at a geographic pole).
  Observer labels distinguish Zenith (90 degrees altitude) from Polaris and its
  calculated altitude. Polaris is approximately as high as the observer's latitude.
  Preserve the independent hour-angle, Polaris-altitude and circular-motion tests
  in ObserverTests, plus the real north-facing timeline scenario in DesktopTests;
  self-consistent coordinate round trips alone do not establish physical accuracy.
- When an observer is active, the scene timeline appears below the sky. Scrubbing
  pauses the clock over a fixed +/-12-hour anchor; day buttons move 24 hours and
  recenter. Live now resumes system time. Preserve compass direction, zoom and
  image textures; clamp dates to 1900-2100 using UTC arithmetic across DST changes.
  Scene-time edits do not overwrite startup options. In atlas
  mode the timeline is hidden. Anonymous manual captures replace both visible
  computer-local clock readouts with fixed UTC demonstration values.
- First-run setup appears only if neither settings nor backup exists, before
  startup imports. Offer a stretch and optional location. Save & start / Use
  defaults persist; Not now / Escape / close defer without saving. Failed saves
  keep the dialog open. Startup uses the saved observer defaults.

## Manual, privacy and publishing

The user explicitly requires that documentation and screenshots reveal **no
personal username, profile paths, observing location or private image metadata**.
This also applies to PDF metadata, embedded images and other published artifacts.

- The editable source is `docs/user-manual.md`; the generated
  `docs/SkyBrowser-User-Manual.pdf` is a repository source artifact. Normal .NET
  publishing must not need Python. Keep the PDF current with user-facing changes.
- Read `docs/manual/README.md` before refreshing it. Use the anonymous model in
  `tools/ManualScreenshots`, not raw desktop-test screenshots or real user options.
  It replaces paths, filenames, metadata, file facts and cache statistics. Only
  preview pixels/sky geometry are reused from the examples.
  App.OnStartup reuses an explicitly provided MainWindow so tests and screenshot
  generation never open an additional window with the real user's options/cache.
- Treat raw examples, sessions, settings, logs and diagnostic captures as
  potentially private. Do not add them to the distributed application or manual.
- Demo paths use `C:\SkyBrowserDemo`; demo observer latitude/longitude are both 0
  with fixed time `2000-01-01T12:00:00+00:00`. Never substitute a real location,
  account name, system timestamp or private source history.
- **An overlay, blur or PDF crop is not sufficient redaction.** The complete PNG
  must already be anonymous: full source images remain recoverable from a PDF.
  Inspect every source PNG, including regions clipped out of the printed page.
- The capture helper writes SHA-256 hashes to
  `docs/manual/screenshots/manifest.json`. The PDF builder checks the manifest,
  private paths/account text, PNG metadata and PDF attachments. Keep these guards
  and deterministic neutral PDF metadata; do not bypass validation by merely
  updating hashes for an unreviewed screenshot.

Regenerate from the root (Windows, Python 3.12+; reuse the venv if it exists):

```powershell
dotnet run --project tools\ManualScreenshots\SkyBrowser.ManualScreenshots.csproj -c Release
python -m venv artifacts\manual-tools
artifacts\manual-tools\Scripts\python.exe -m pip install -r tools\manual-requirements.txt
artifacts\manual-tools\Scripts\python.exe tools\Build-UserManual.py
```

Review every full screenshot and every rendered PDF page under
`artifacts/manual-preview`, as well as its `validation.json`. Ensure captions,
contents links and current UI labels agree. Use the available PDF skill when
authoring or reviewing PDF layout. Private test captures under `artifacts/` are
not approved documentation assets; use the maintained screenshot helper.

Publish the self-contained Windows x64 application:

```powershell
dotnet publish src\SkyBrowser.Desktop -c Release -p:PublishProfile=WinX64 --artifacts-path .\artifacts
```

Output: `artifacts/publish/SkyBrowser.Desktop/release_win-x64/`.
Keep `SkyBrowser-User-Manual.pdf` accessible beside `SkyBrowser.exe`, outside the
single-file bundle. The publish profile uses embedded debug information; the
desktop project excludes **all dependency/application `.pdb` and XML documentation
files** from publish output. Preserve normal build debugging. Verify these output
requirements after changing publish configuration, and retain required notices,
licenses and catalogue data.

## Editing conventions and repository care

- Follow `.editorconfig`: UTF-8, CRLF, four spaces for C#, two for XML/XAML/JSON,
  explicit control-flow braces and statements on separate lines. Keep code easy
  to read; short properties/expressions can remain concise. Comments should explain
  coordinate conventions, ownership, validity and asynchronous lifetimes.
- Use modern C#/.NET features where they improve clarity. Keep Core independent
  of WPF. Preserve existing source-generated JSON and immutable data contracts.
- `SkyImageLayer.ReplaceDisplay` owns coherent image/bitmap/placement replacement,
  including manual geometry after source dimension changes. Batch its viewport
  updates instead of reapplying manual placement in individual callers. Isolate
  damaged preference/survey rows so other valid cached records remain usable.
- For semantic renames/moves/signature changes, use the available Rider
  refactoring skill when applicable. Avoid unrelated restructuring or broad
  reformatting, especially in generated data and the external library.
- Bundled sky/world datasets have attribution and license requirements. Keep
  `THIRD-PARTY-NOTICES.md` and `Data/LICENSE-*` files. Regenerate deep-sky data with
  `tools/Build-TargetCatalog.ps1` (PowerShell 7; pinned download cached in
  `artifacts/catalog-sources`) rather than manually reformatting generated JSON.
- Preserve the user's images, sessions, settings and unrelated working-tree
  changes. Do not clean/reset repositories, delete example files, or stage/commit
  work unless requested. Much of this application may still be untracked; check
  `git status --short` rather than assuming `git diff` shows all source changes.
- Keep temporary caches, captures, probes and tool environments under ignored
  `artifacts/`. Do not depend on local benchmark reports being present later.
- Report what changed, what was verified and any remaining limitations. Do not
  claim a build, test run, privacy review or performance gain without evidence.

## Public repository workflow

- Run tools/Initialize-Dependencies.ps1 after cloning; it skips optional LFS images.
- README.md is the short user entry point; docs/technical-reference.md retains detailed behavior.
- Apache-2.0 and NOTICE cover authored code. Preserve separate third-party/data licenses.
- Build-release tooling verifies notices, package contents and source-compliance assets before publication.
- Build-Release creates only the compact Windows application ZIP with the self-contained
  executable, loose PDF and SkyBrowser-Notices.zip. New-CompactPackage.ps1 must retain
  all supporting files byte-for-byte, reject external runtime dependencies and
  validate both ZIP layers. Do not trim WPF or enable Native AOT as a size shortcut.
- Windows CI checks builds/tests and version tags publish validated release assets. Keep PR jobs read-only.
