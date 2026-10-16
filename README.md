# SkyBrowser

A Windows desktop sky atlas for your own FITS and XISF photographs, built with **.NET 10, C# 14 and WPF**. The observer stays at the center of a celestial sphere. Photographs are mapped onto textured 3D meshes using their astrometric solutions, while an offline star and constellation map provides orientation.

Start with the illustrated [user manual](docs/SkyBrowser-User-Manual.pdf) for a beginner's tour and step-by-step instructions. The PDF is also copied beside the executable on every publish. Its editable [Markdown source](docs/user-manual.md) and [rebuild instructions](docs/manual/README.md) are included in the repository.

## Run

Requires Windows and a .NET SDK with C# 14 support. The app targets `net10.0-windows`; the build's framework-dependent executable needs the .NET 10 Desktop Runtime. `global.json` permits a newer SDK when a .NET 10 SDK is unavailable.

The default library reference is your adjacent checkout at `D:\Code\XISFLib.Net\XISFLib\XISFLib.csproj`. Source files in that checkout are not modified.

In Rider, open `SkyBrowser.slnx` and run the **SkyBrowser.Desktop** .NET project configuration. The solution includes XISFLib under **Dependencies**, so Rider builds the current library before compiling the application. Normal IDE Debug builds need no custom command-line arguments.

```powershell
dotnet build SkyBrowser.slnx -c Release --artifacts-path .\artifacts
.\artifacts\bin\SkyBrowser.Desktop\release\SkyBrowser.exe
```

Or run directly:

```powershell
dotnet run --project src\SkyBrowser.Desktop -c Release --artifacts-path .\artifacts
```

You can pass image paths, a folder, or a `.sky.json` session after `--`. For another library location, update the dependency path in `SkyBrowser.slnx` and pass `-p:XisfLibraryRoot=D:\Your\XISFLib.Net` to the build command. The explicit artifacts path also keeps dependency build outputs inside this repository.

## First launch

When there is no saved settings file or recovery backup, a **Welcome to SkyBrowser** window appears before any images supplied at launch are opened. Choose a default stretch, with **Automatic · dark sky (fast)** recommended, and optionally use **Choose on map…** to select an observing location. The map defaults to the current system time. A selected location starts the sky facing south; leaving it empty uses the celestial atlas. Short descriptions explain each stretch, including the longer first load for dark-sky cleanup.

**Save & start** saves your choices and applies them before loading startup images. **Use defaults** saves the factory defaults without a location, including the 10 GB preview cache. Both dismiss setup on later starts. **Not now**, Escape or closing the welcome window continues without saving; setup is offered again on the next launch if settings are still absent. A failed save keeps the window open with an explanation and the option to defer. Existing saved options and recoverable backups skip setup, and a legacy observer location is prefilled when available. All choices remain editable in **Options**.

## Explore your images

1. Choose **Browse folder…** to show a folder's images, replacing the current collection. **Recent folders…** switches between the last 12 folders, including after restarting the application. **Add images**, **Add…**, and dropped files/folders append to the collection. Folder imports include supported files directly inside that folder, without recursion. Formats: `.fits`, `.fit`, `.fts`, `.xisf`, and distributed `.xish` with its data files present.
2. Plate-solved images appear at their stored coordinates. Double-click a collection entry or choose **Go to image** to frame it. **Double-click a photograph in the sky view** to open its original FITS/XISF file in the Windows default application. With overlapping images, the foremost image under the pointer opens. Missing files or absent file associations are reported in the status bar and **Details**. Images without a supported solution remain in the collection with **Needs placement**.
3. Drag the sky to pan and scroll to zoom. Arrow keys navigate when the sky has focus. **Home** restores the initial view (south when an observer is configured), **F** frames the selection, and **+ / −** change zoom. The object picker and RA/Dec inputs jump to a sky position.
4. Toggle layers, set opacity, and use the arrows to reorder them. With **Blend overlaps** enabled, larger sky fields form the background and smaller overlapping images appear above them at 60% of their chosen opacity. Hover an inset to temporarily show it at full opacity; move away to restore the blend and your chosen opacity. Turning **Blend overlaps** off uses collection order, with later entries drawn on top. Grid, constellation lines, labels and image outlines can be toggled separately.
   Drag the horizontal divider between the collection and **Selected image** to give either panel more space. You can also focus the divider and use Up/Down arrows. The chosen split is retained while switching selections or folders, and both panels scroll independently.
5. **Save session** stores source paths, layer order, visibility, opacity, display modes, finishing settings, manual placements, added catalogue targets, observer/time settings and the sky view. Paths are relative to the session file where possible. Reopening reuses saved previews when available and validates that the sources still exist and are unchanged. Missing images are reported through **Details**; the remaining images load normally.

The solved FITS images and `IC434_20260122.xisf` in your `examples` folder can be opened together using **Browse folder…**. The narrower XISF Horsehead image blends into the wider FITS Horsehead field automatically.

Longer imports, folder visits, session restores and display changes open a separate **loading progress window** after a brief delay. It shows completed/total files, how many remain, the current filename and processing step, and counts of ready, cached, failed and already-loaded images. Imports count source files, since one FITS/XISF document can contain multiple images; session restores count their individual image entries. The overall bar tracks completed entries. A second bar shows progress within the current step when available, otherwise it pulses. Fast cached loads finish without flashing a window.

Choose **Cancel loading**, press **Escape** in the progress window, or close that window to request cancellation. The status-bar Cancel button remains available too. The window shows **Stopping…** until the worker acknowledges the request; some decoding and cleanup filters must finish their current step first. Remaining files are not started. Images already added by an import stay available, while a cancelled display change keeps its previous preview and a cancelled session restore keeps the current sky. The window closes automatically when work completes, fails or is cancelled. Folder browsing remains available; choosing another folder cancels the old request and replaces its progress window.

Each collection entry has a small **ⓘ** button. It opens **Image details**, showing the loaded preview, original and preview dimensions, channel/sample information, source path, file size and modification time, display settings and sky placement. The searchable table lists image attributes, FITS keywords (including repeated history entries and comments) and XISF image properties. Select a row to read and copy its value; search also matches values and comments. Details are retained in the preview cache, so opening this window does not decode or stretch the scientific image again. File size and modification time are read separately in the background; if the source is unavailable, the loaded metadata remains accessible. Large property values, arrays and metadata lists are shortened with a visible notice to keep cached details compact.

Use the **Search names & metadata** field above the collection to filter loaded images by filename, object name, or retained metadata keys, values and comments. Matching ignores case, accents and separators: **`ic434`**, **`IC 434`**, **`IC-434`** and **`ic_434`** find the same target. Spaced catalogue prefixes such as `M 31`, `NGC 7000` and `Sh2 240` are also recognized. Multiple terms must all match, including across different metadata fields; for example, `IC 434 H-alpha` narrows the collection to matching hydrogen-alpha images. Search uses normalized text from the loaded image metadata.

The count shows matching/total images while filtering, and an empty result displays guidance. **Ctrl+F** focuses search; **Esc** or **×** clears it. The query stays active while importing or switching folders, with new matching entries appearing as they load. Filtering affects the collection list; layer visibility, blending, sky direction and saved sessions retain the loaded images. Selecting an image in the sky clears the filter when necessary to reveal that entry. A normalized search index is built once per loaded image on a worker thread; typing does not read or reprocess source files. Search covers the metadata retained for display, including its limits on unusually large values and lists.

Blending compares angular sky coverage and intersecting footprints, rather than pixel dimensions or object names. It works with rotated images, the RA seam and polar fields; images elsewhere in the sky retain their normal opacity. Nested insets are supported, and hovering reveals the nearest inset under the pointer. Hiding a background makes its remaining inset opaque when no larger overlapping field remains. The toggle is stored in sessions; existing sessions default to enabled. Hover changes only scene opacity, so it does not stretch images again or modify cached previews or source files.

### Find in the sky

The finder starts collapsed to leave more of the sky visible. Click **Find in the sky** or its arrow to expand or collapse it. Collapsing keeps your filter, selected target and added sky markers.

The offline finder contains **13,372 distinct targets**, covering all **110 Messier** and **109 Caldwell** entries, together with positioned NGC and IC entries and their components. Duplicate catalogue designations resolve to the same object. Nonexistent entries without a sky position are omitted. Data come from [OpenNGC](https://github.com/mattiaverga/OpenNGC), with Caldwell cross-identifications from the [Astronomical League](https://www.astroleague.org/caldwell-program-object-list/); see the [third-party notices](THIRD-PARTY-NOTICES.md).

Type a designation or common name in the filter, for example `IC434`, `IC 434`, `M31`, `Caldwell 20` or `Horsehead`. Matching ignores case, accents and separators, accepts multiple terms, and puts an exact catalogue designation first. The catalogue selector restricts results to Messier, Caldwell, NGC or IC. Hover an entry for its coordinates, type, angular size, magnitude and catalogue notes where available. The dropdown loads visible rows as needed to stay responsive with the full catalogue.

Choose a result to face it, add a named marker to the sky and fill the RA/Dec fields. **Enter** selects an exact designation or a single remaining match. The single **Go** button beside RA/Dec returns to the selected object and restores its framing when its displayed coordinates still match. Edit either coordinate to navigate to that position while retaining the current zoom; Go also works without a selected catalogue target. Approximate angular outlines appear when size data are available. The background initially labels **30 prominent targets**; chosen targets remain added while browsing or changing the filter. Aliases of an already added object share one marker. **Clear added** removes these extra markers, and sessions save and restore them. These markers identify catalogue positions; your loaded photographs continue to appear at their own astrometric coordinates. Turning labels off hides captions while retaining the added position markers.

### Observer location and time

Choose **Options → Observer → Choose on map…** to open a separate setup window. Click the offline world map to place a marker, drag to pan and scroll to zoom. Latitude and longitude can also be entered precisely; negative values mean south and west. Keyboard arrows move the marker when the map has focus. **Apply & face south** stages the location in Options; **Save options** applies it to the sky. Cancelling Options keeps the current view.

**Use current system time** is enabled by default. The local sky then updates every second while preserving the direction you are looking. Uncheck it to choose a fixed date and 24-hour time, either in your **computer's local time zone** or **UTC**. The map does not infer a time zone; the sky's corner clock displays computer-local time with its UTC offset. Ambiguous or nonexistent local times during daylight-saving transitions are rejected with guidance to use UTC.

Applying an observer faces **south, 20° above the horizon**, at a 90° field of view. The horizon stays level, compass labels appear along it, and the corner readout shows azimuth, altitude and observing time. **Alt/Az grid** is fixed to the local horizon so the stars move against it as time changes; atlas mode retains **RA/Dec grid**. With labels enabled, the actual **Zenith (90° altitude)** and **Polaris with its calculated altitude** are marked when visible. Polaris is approximately as high above the northern horizon as the observer's latitude; the zenith coincides with a celestial pole only at a geographic pole. Stars and image footprints remain aligned in J2000; Astronomy Engine supplies the observer rotation, including precession and nutation. Panning, zooming, object searches, image framing, blending and opening originals remain available. Imports preserve the local compass view; explicitly frame an image to jump to it.

Saving a location retains it and its live/fixed time choice in the user options. Subsequent starts face south at that location, using the current system time when the live clock is selected. A session retains its own location and time mode without replacing the startup defaults. **Clear location** in Options (or **Use atlas view** on the map), followed by **Save options**, clears the saved location and returns to the original view. Without a location, the previous atlas behavior and initial view are retained. The map is never shown in the main sky view or opened automatically.

With a location active, **Scene time** appears below the sky. Drag its slider backward or forward up to 12 hours from its center; the first movement pauses the live clock. **−1 day** and **+1 day** move 24 hours and recenter the slider, allowing longer trips through the calendar. Arrow keys move one minute and Page Up/Down move one hour while the slider has focus. **Live now** returns to the current system clock. The displayed date/time includes the computer-local UTC offset; stepping uses elapsed UTC time safely across daylight-saving transitions. Limits are clamped to 1900–2100. The compass direction, zoom, image placements and processed previews are retained; this does not reload or stretch images. Sessions retain the selected fixed/live time, while saved startup defaults remain unchanged. The timeline is hidden in atlas mode.

The country map is bundled and requires no network requests or location permissions. Its boundaries are approximate; use numerical coordinates for precision. The horizon is geometric at sea level: no atmospheric refraction, terrain masking, daylight simulation or stellar proper-motion updates are applied. Dates from 1900 through 2100 are supported. Objects below the horizon remain browsable.

### Persistent options

Choose **Options** in the top bar to configure:

- **Images:** the default stretch variant, sky background target and cleanup options. These apply to newly loaded images without a saved per-image rendering choice. Existing image choices and session settings take precedence. Saving does not reprocess the collection currently on screen. **Reset defaults** in the selected-image panel applies your saved image defaults.
- **Observer:** a startup location selected on the separate world map, with current system time or a fixed observing time. Clear the location to use the original atlas view.
- **Cache:** the cache folder and maximum preview storage, initially **10 GB**, with currently used space and preview count shown beside the limit. Usage is read from the active cache when the dialog opens; a folder change takes effect when saved. GB uses 1,024³ bytes. Space is allocated as previews accumulate. Saving a smaller limit immediately evicts older previews. Switching folders merges previews, per-image choices and recent folders into the destination, preserving destination-only entries. The original cache files remain in their old folder; remove those separately if you no longer need them.
- **Sky:** startup defaults for the coordinate grid, constellation lines, labels, image outlines and overlap blending. Saving also applies these choices to the current view.

**Save options** applies and saves changes; **Cancel** leaves your settings unchanged. **Restore defaults** stages the factory defaults until saved. Invalid input or a failed save keeps the dialog open so you can correct it. The options file is `%LOCALAPPDATA%\SkyBrowser\settings.json`, separate from the disposable cache. Writes are atomic, and a `.bak` file retains the previous valid version for recovery. A location saved by the earlier version is imported automatically. Options persist across exits and restarts; moving or clearing the cache does not remove them.

### Persistent preview cache

Processed previews are stored in a local **SQLite database**, shared by folder browsing, image imports and session restoration. The cache survives application exits, restarts and reboots. On a cache hit, scientific decoding, stretching and cosmetic processing are skipped; the saved pixels, metadata and sky mesh are restored directly.

- Default location: `%LOCALAPPDATA%\SkyBrowser\Cache\previews-v1.sqlite3`. No database server or account is needed.
- Rendering modes and finishing settings are remembered for each source image. Changing the settings creates a separate preview variant; returning to an existing variant reuses it. Sessions retain their explicitly saved settings.
- Source signatures include file size, modification/creation times and SHA-256 samples from the beginning, middle and end of each file. Distributed XISF data files are tracked too. These are fast change detectors, not full-file integrity hashes: an edit that deliberately preserves timestamps and only changes unsampled bytes can evade detection. Rendering pipeline/library versions also invalidate older previews.
- Stored preview data defaults to **10 GiB**, configurable in **Options → Cache**, with least recently used previews evicted automatically. SQLite pages, journals and small preference/history records add some disk overhead. The Cache tab shows usage and the cache folder. **Clear saved previews** releases previews from the active cache immediately while retaining rendering preferences, recent folders and loaded images.
- Cached files load first, and images appear progressively. Switching folders cancels the preceding request. Warm cache hits can load while a previous cold image finishes decoding; cold jobs are serialized to bound memory use. Image processing and database work run off the UI thread.

The first visit to an image, or a previously unused finish, still requires processing. Corrupt previews are regenerated, and an unavailable cache falls back to normal processing. Source files remain unchanged; the cache is disposable and does not replace the originals or saved sessions.

### Display stretching

Scientific reading and stretch functions use **XISFLib**, including FITS physical-sample conversion, color handling, compression/checksums, stored display functions and median/MAD adaptive stretching. Processing runs off the UI thread. Source files are never rewritten.

**Automatic · dark sky (fast)** is the default and performs **stretching only**. It uses a stored display function when available. Otherwise it inspects normalized scientific samples: dark data and narrow high-pedestal distributions receive an adaptive stretch to the chosen sky-background target, initially 10%. High-pedestal data are recentered as part of the stretch. Automatic skips background color balancing, gradient reduction, noise smoothing, sharpening and color enhancement, even when those cleanup choices are enabled in Options. Developed images with broad, brighter distributions retain their existing exposure. This detection is a heuristic; **Linear / already stretched** always bypasses stretching and cleanup.

**Dark sky + cleanup** runs the full finishing pipeline, adapting `XISFLib.Viewer.Core/ViewerFinishService` from your Avalonia viewer:

- A default **10% sky background**, replacing the old 25% screen-stretch target. It uses `XisfDisplayFunctions.CreateAdaptive` with linked RGB parameters and −2.8 clipping. High-pedestal linear data is recentered before stretching.
- Optional background color balancing and a conservative, two-sided clipped broad-gradient plane. The plane requires spatial support in every quadrant and removes 80% of a reliable slope. It is an aesthetic estimate; turn it off when a nebula filling the frame is affected.
- Optional Magick.NET selective background smoothing, thresholded detail enhancement and a subtle color boost, using the viewer's filter settings.
- The full image frame, row order, reflection and alpha coverage are retained. Unlike the sharing-image workflow, this does **not** crop edges or rotate pixels; the original WCS remains applicable. It does not include the viewer's multiscale wavelet denoising, local star deconvolution or spike attenuation.

Select an image, expand **Sky background & cleanup**, adjust the background slider (2–25%), choose cleanup options, and click **Apply finish**. This selects **Dark sky + cleanup** for that image. Automatic retains these choices for a later switch to cleanup but uses only the background target. **Reset defaults** applies the image defaults saved in **Options** (initially Automatic and 10%). Existing sessions without finishing settings use the factory 10% settings when opened. Older processed previews are regenerated for the updated pipeline; image preferences and saved sessions remain available.

**Library auto stretch (25%)** remains available for comparison with the old screen stretch. **Stored display function** uses the file's display metadata or produces a linear display when that metadata is absent. Finishing controls do not change opacity. Source pixels and files remain intact, and processing can be cancelled between stages; progress is shown in the loading window and status bar.

### Positioning

- Native XISF Revision 1 solutions are evaluated through the library, including its supported projection, projective and distortion layers. These take precedence over FITS keywords.
- FITS automatic placement supports **RA/Dec TAN** WCS in degrees with `CD`, or `PC`/`CDELT` matrices, in ICRS or FK5/J2000. FITS one-based reference pixels and the library's zero-based pixel API are handled separately from native XISF half-pixel centers.
- Unsupported FITS projections, SIP/TPV distortion and non-J2000 coordinate frames require manual placement. Acquisition RA/Dec alone is insufficient to infer scale, rotation and reflection.
- Manual placement takes center RA/Dec in **decimal degrees**, angular width, rotation and optional horizontal mirroring. Zero rotation means north up, east left; positive rotation is counterclockwise on the sky. Placement is an adjustable tangent-plane approximation, not a plate solve.

The display maps the original stored rows to their astrometric coordinates. XISF display-only rotation/reflection metadata is deliberately bypassed so it cannot rotate the texture away from its solution.

## Current scope

- A J2000/ICRS celestial atlas with optional observer-relative horizon orientation and live/fixed observing time. No atmospheric effects, terrain, daylight simulation, planetary ephemerides or stellar proper-motion updates.
- No built-in plate solver. Solve images externally or use manual placement. Legacy PixInsight `PCL:AstrometricSolution:*` distortion models are not the new native XISF solution format.
- Two-dimensional grayscale/color images and FITS RGB/RGBA cubes. A three/four-plane FITS third axis is explicitly interpreted as color. Other scientific cubes and complex samples are rejected. CFA images are shown as grayscale; debayer externally for color.
- Display previews have at most **2048 pixels** on their longest side, using box averaging. Finishing averages linear scientific samples before stretching/quantization, then applies cosmetics at preview resolution. A 32 × 32 mesh approximates each footprint. Full-resolution source decoding still occurs, so large files need adequate memory. Raw sample buffers are released after import; layers retain their preview and astrometry.
- Cancellation is checked between file, decode and render stages; library codec calls are not interrupted mid-operation. Malformed files and unavailable sessions are explained in **Details**.

## Build and verify

See the [performance review](docs/performance-review.md) for measured rendering/loading improvements, validation, remaining costs and repeatable CPU/allocation probes.

```powershell
dotnet build SkyBrowser.slnx -c Release --artifacts-path .\artifacts
dotnet test tests\SkyBrowser.Tests -c Release --artifacts-path .\artifacts
```

Tests cover celestial projection and hemisphere/seam behavior, pixel origins, manual transforms, unsupported WCS handling, XISF/FITS imports, dark-sky finishing, high-pedestal inputs, transparency, source preservation, orientation, overlapping image brightness, object-picker interactions, session restoration and missing files. Overlap checks cover sky-area ordering, actual rendered alpha values, rotated and nested hover hit tests, background visibility, manual opacity, touching/disjoint fields, RA seams and poles. Cache checks include a new program process with scientific processing disabled, exact pixel/mesh restoration, preference persistence, source and XISF-sidecar invalidation, partial multi-image entries, corruption recovery, eviction, cancellation, concurrent warm/cold loading and rapid folder switches. The desktop test creates a real WPF window on a dedicated STA thread and closes it afterwards. The optional solved-example check compares the Horsehead's old and finished previews, verifies unchanged WCS coordinates, and renders the FITS/XISF pair blended and hovered.

Loading-progress checks hold a processing step open while verifying actual file totals, remaining counts, cached/failed/skipped results and per-step progress. They exercise Cancel, window-close and Escape cancellation, preserve partial imports and existing previews/sessions, reject late progress callbacks and verify that a subsequent import works. Screenshots include `image-loading-progress.png` and `image-loading-cancelling.png`.

Automatic-stretch checks compare its pixels to a stretch with all cleanup disabled, verify that no cleanup stages run even with every cleanup option saved as enabled, and exercise both dark and high-pedestal linear images with transparency. Explicit cleanup still runs its full set of stages, while the original file and the user's cleanup choices are preserved.

First-run checks choose a stretch and an optional map position, verify that startup images use the saved stretch, and restart with the saved preferences. They also exercise deferring setup, using defaults without a location, backup recovery and a failed first save. Screenshots include `first-run-setup.png` and `first-run-location.png`.

Observer tests verify horizon orientation across both hemispheres and the poles, the J2000 Greenwich sidereal reference, live/fixed clocks, DST conversions, saved locations and observer sessions. The desktop check exercises the actual map/time dialog, cancellation, returning to atlas view and importing without losing the compass direction. Screenshots include `observer-location.png`, `observer-custom-time.png` and `observer-south.png`.

Options checks cover a new program process, complete setting restoration, invalid data, backup recovery, legacy-location migration, cache copying and size reduction, and the precedence of image preferences over global defaults. The WPF check opens all four Options tabs, stages a map selection, cancels changes, recovers from an unusable cache folder, saves to a new cache location, and opens another window using the saved options and migrated previews. It also clears saved previews through Options, checks the refreshed usage and verifies that loaded images remain available. Screenshots are saved as `options-images.png`, `options-observer.png`, `options-cache.png`, `options-cache-cleared.png` and `options-sky.png`.

Image-detail checks verify FITS structural keywords, duplicate history and property comments, metadata for individual images in a multi-image document, bounded large values, and details restored by a separate process without scientific processing. The desktop check clicks the actual collection info buttons, filters and selects metadata, reads file sizes, handles a missing source, and opens details for the real XISF example. Screenshots include `image-details.png`, `image-details-missing.png` and `image-details-xisf.png`.

Collection-search checks cover filename-only and metadata-only matches, catalogue spacing, punctuation, accents, comments, combined terms and unrelated-field boundaries. The WPF check verifies matching counts, empty results, unchanged sky layers, live imports, removing a filtered entry, folder switches and clearing search. Screenshots include `collection-search.png` and `collection-no-results.png`.

Target-catalogue checks verify every Messier and Caldwell designation, NGC/IC coverage, coordinates, aliases, common names, catalogue filters and exact-match ranking. The WPF check opens the actual virtualized dropdown, chooses entries, verifies a rendered marker, tests alias deduplication and the Go button, and restores added markers from a session. Screenshots include `target-picker-dropdown.png` and `target-picker-selected.png`.

To regenerate the bundled deep-sky data, run `pwsh -File .\tools\Build-TargetCatalog.ps1` with PowerShell 7. The script downloads the pinned OpenNGC CSV snapshot into `artifacts/catalog-sources` when absent, applies the checked-in Caldwell cross-reference, and writes `src/SkyBrowser.Core/Data/deep-sky.json`. This adapted dataset and its attribution and license are also copied into build/publish outputs.

Optional checks against your large examples and rendered screenshots:

```powershell
$env:SKYBROWSER_SAMPLE_DIRECTORY = 'D:\Code\XISFLib.Net\Examples'
$env:SKYBROWSER_SOLVED_SAMPLES = 'D:\Code\SkyBrowser\examples'
$env:SKYBROWSER_SCREENSHOTS = 'D:\Code\SkyBrowser\artifacts\screenshots'
dotnet test tests\SkyBrowser.Tests -c Release --artifacts-path .\artifacts
```

To measure cold versus cached loading of the examples, run these phases in separate processes after building the solution. Use a new cache directory for each cold measurement. The warm phase throws if scientific processing runs, and each report includes preview hashes for comparison. Timings include cache initialization and loading, but exclude WPF scene creation and process startup.

```powershell
dotnet artifacts\bin\SkyBrowser.CacheProbe\release\SkyBrowser.CacheProbe.dll --measure cold artifacts\cache-measurement examples artifacts\cache-cold.json
dotnet artifacts\bin\SkyBrowser.CacheProbe\release\SkyBrowser.CacheProbe.dll --measure warm artifacts\cache-measurement examples artifacts\cache-warm.json
```

`src/SkyBrowser.Core` contains coordinate math, catalogues, astrometry adapters and imports. `src/SkyBrowser.Desktop` owns the WPF scene, controls and session UI. C# 14 field-backed properties, immutable records, source-generated JSON, spans in the underlying library, frozen WPF assets and cancellable worker operations keep the implementation small and explicit.

The repository's `.editorconfig` defines four-space C# indentation and two-space XML/XAML indentation. Use explicit braces for control flow, keep statements on separate lines, and wrap long calls and queries at logical boundaries. Simple properties and short expressions can stay on one line. Comments explain coordinate conventions, buffer ownership, cache validity and asynchronous lifetimes; keep those constraints beside the code they describe. Bundled catalogue data and build-generated files keep their existing generated format.

To compare Automatic and Dark sky + cleanup processing on an individual image without the preview cache, run:

```powershell
dotnet artifacts\bin\SkyBrowser.CacheProbe\release\SkyBrowser.CacheProbe.dll --compare-stretches examples\horsehead_164x2m_hyperstar_unfiltered_poseidon-cpro.fits artifacts\stretch-comparison.json
```

The report includes decoding, metadata and preview processing time for each mode, excluding cache and scene creation. Automatic is measured first; filesystem caching and machine load affect the timings.

Publish a self-contained Windows x64 build:

```powershell
dotnet publish src\SkyBrowser.Desktop -c Release -p:PublishProfile=WinX64 --artifacts-path .\artifacts
```

The output is under `artifacts/publish/SkyBrowser.Desktop/release_win-x64`. See [third-party notices](THIRD-PARTY-NOTICES.md) for the offline sky catalogue's sources and license.
