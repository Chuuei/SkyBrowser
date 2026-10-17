This reference documents detailed behavior and optional probes. Start with the [README](../README.md) and [development setup](development.md) for installation and building.

# Technical reference

A Windows desktop sky atlas for your own FITS and XISF photographs, built with **.NET 10, C# 14 and WPF**. The observer stays at the center of a celestial sphere. Photographs are mapped onto textured 3D meshes using their astrometric solutions, while an offline star and constellation map provides orientation.

Start with the illustrated [user manual](SkyBrowser-User-Manual.pdf) for a beginner's tour and step-by-step instructions. The PDF is also copied beside the executable on every publish. Its editable [Markdown source](user-manual.md) and [rebuild instructions](manual/README.md) are included in the repository.

## Comparison and collection organization

Ctrl/Shift-select loaded images and choose **Compare…**. Every selected image must
have a placement. The comparison window snapshots the current preview and manual
placement into independent layers at full opacity. Two `SkyViewport` instances share
sky center and horizontal field of view, including seam/pole navigation. Side-by-side
panes have equal dimensions; blink and swipe use overlapping, identically sized
viewports. Swipe composites frozen, full-view 2D frames and clips only the top
bitmap, avoiding partial 3D redraw artifacts during slow divider motion. Frames
refresh after camera, image-pair or size changes; moving the device-pixel-aligned
divider reuses them without resizing or rerendering the projection. Mode exit and
window close release frames and pending capture callbacks.
Blink timing is configurable and stops while inactive or closed. Closing clears layer
subscriptions and timer handlers. Current source processing and atlas state are unchanged.

Collection thumbnails reuse the frozen preview bitmap; no source is opened. Collection
sorting affects only its `ListCollectionView`, preserving scene draw order. Structured
filters read retained FITS/XISF fields for object, filter, telescope, camera or ISO
observation date; they combine with exact case-insensitive tag/group matching,
favorites and the existing normalized metadata search. Unknown fields do not match.

`ImageLibrary` adds an independent `organization` table keyed by canonical source path
and image index. Favorites, normalized comma-separated tags and one group survive
source revisions, preview eviction and root removal. Batch annotation writes use a
single transaction, preserving unspecified fields; empty provided strings clear them.
Annotations do not relocate automatically with moved files. Database work stays off
the UI thread. Batch display work uses the existing serial loader, preferences,
operation/version-guarded progress and cancellation. Cancellation keeps committed
organization and completed previews; errors are reported per image and remaining jobs
continue. Visibility/opacity are transient scene choices.

## Surveys, visibility planning and annotations

`SkyToolsWindow` owns independent cancellable survey/planning operations. Opening it
does not download or decode sources. CDS survey requests are explicit, square TAN
cutouts with a centered ICRS WCS, fixed allow-listed survey IDs and HTTPS endpoint.
Downloads are bounded to 32 MB and validated for FITS geometry/placement before
commit. Scientific decoding and SQLite work stay off the UI thread. The separate
`SurveyCache/surveys-v1.sqlite3` beside settings retains up to 512 MB of FITS payloads
using transactional LRU eviction; SQLite/WAL overhead is additional. Saved regions
reopen offline, corrupt entries are rejected, and cache clearing retains the displayed
frames. Panning never starts a download. The saved `ShowCachedSurveyImages` option
(off by default) restores all cached regions at startup or when enabled in Options,
without network access or camera changes. Restoration skips missing/damaged frames,
does not touch LRU order, and uses cancellation plus scene revisions to preserve
newer manual choices. Disabling removes the automatically restored set unless it
has since been changed manually. New cutouts join existing backgrounds; newest wins
where they overlap. Each survey mesh has its own `Viewport3D` depth buffer, composited
oldest to newest below the personal-photo viewport with the same perspective camera.
This prevents differently sampled spherical facets from intersecting; small radial
offsets alone do not provide a reliable draw order. Survey regions stay excluded
from collection selection/hit actions. Pixel regressions cover overlapping rotated
wide fields, panning/zooming, source-over opacity, foreground photos and exports.
Credits remain visible in atlas/exports independently of Labels. The returned FITS
WCS determines orientation; WCSLIB's solved zenithal LATPOLE=reference declination
is equivalent to its omitted/default pole-selection preference for these projections.

Visibility planning retains J2000 target coordinates, rotates them with ObserverFrame,
and obtains topocentric J2000 Sun/Moon positions and lunar illumination from pinned
Astronomy Engine. One UTC day has 721 two-minute samples. Target extrema refine
geometric rise/set and upper transit; sampled darkness and imaging-criterion changes
are bisected to half-second brackets. Sub-two-minute grazing windows may be missed.
Criteria combine target altitude, Sun altitude and Moon separation, optionally
ignoring separation below the geometric lunar horizon. No refraction, elevation,
terrain or weather model is applied. Polar day/night and circumpolar/no-rise targets
remain explicit. Planning location/time never overwrite startup observer options.

Bookmarks and notes live in a path-independent `bookmarks` table in the durable
library, keyed by GUID, and load at startup without source decoding. They retain
J2000 position and field of view. Async callbacks are guarded by lifetime/revision;
closing cancels work and detaches handlers. Measurements are transient, use robust
vector/great-circle geometry and support seams/poles/antipodes. Optional positive,
finite distances from the observer to A/B in light-years give their Euclidean 3D
separation: `sqrt((dA-dB)^2 + 4*dA*dB*sin(theta/2)^2)`. The implementation scales
before computing the hypotenuse to retain tiny angles and avoid intermediate
overflow. Distances are manually supplied or selected from the offline catalogue,
stay transient, clear for a new
measurement and appear in atlas/export labels only when both are known. This is
an estimate from the supplied distances, without uncertainty propagation or a
cosmological distance model. Scale bars solve the
angular distance at their actual screen row rather than assuming constant scale.
Celestial N/E arrows follow the projected J2000 tangent frame and are hidden at an
exact celestial pole. These scene overlays and survey attribution carry into sky
exports; comparison snapshots remain independent.

`TargetDistances` embeds 3,283 unique sourced estimates across Messier, Caldwell,
NGC and IC, with original distance labels, URLs, a dated snapshot and source SHA-256
hashes. Coverage is 107/110 Messier, 109/109 Caldwell, 2,554/8,098 NGC and 824/5,505 IC
objects; alias/component counts follow the supported position catalogue and overlap.
Canonical
identity/coordinates come from `DeepSkyCatalog`; aliases and common-name searches
share its cross-identifications. No runtime lookup or redshift inference occurs.
M24, M40, M73 and objects without usable distance facts are absent, never zero.
`tools/Build-TargetDistances.py --as-of YYYY-MM-DD` regenerates the data using cached
source HTML under `artifacts/distance-sources` (`--offline` reuses it; `--refresh`
fetches updated pages). Only numeric facts are retained, with provenance in
`Data/LICENSE-NASA-DISTANCES.txt` and `Data/LICENSE-SIMBAD-DISTANCES.txt`.

Source precedence is NASA Messier, NASA Caldwell (including the joint C60/C61 page),
three reviewed NASA/APOD facts, then the latest eligible SIMBAD literature measurement.
The generator requires exact cross-identified catalogue IDs and coordinate agreement
within the catalogue angular extent, with a 1.2 arcminute floor and one degree cap.
It rejects member-star/component names absent from the position catalogue, uncertain
flags, invalid units/values and redshift/flow/kinematic methods. Unspecified methods
are accepted only for nearby non-galaxy objects (within 150 kpc). Supported pc/kpc/Mpc
values and asymmetric errors convert to Julian light-years at 3.261563777167433 ly/pc.
Ties in publication year use reported fractional error, then stable bibliographic
order. No measurements are averaged and no distances are inferred from redshift.
VOTable query status and the row cap are checked before publishing a snapshot;
`python -m unittest discover -s tools/tests -p test_target_distance_sources.py`
checks ingestion without network access.

Sky tools' Target distances tab independently filters/selects A and B. Both selected
objects supply their J2000 catalogue centres and distances to the existing sky
measurement. Frame selected pair is explicit; Use two marked targets requires
exactly two marked objects with bundled estimates. Catalogue provenance remains in
the transient measurement and export snapshot, and results display an approximate
marker with at most two significant figures. This is a presentation limit, not an
uncertainty estimate: NASA's fact tables do not provide error bars; SIMBAD's reported
object errors, methods and references are retained in hover source details without
propagating them to separation errors. Rounding can dominate close-pair separation.
List virtualization keeps the larger selection lists responsive. Manual overrides detach source identities, while
new/cleared measurements clear selectors. Arbitrary sky clicks never guess identities.

### Three-dimensional distance map

Target distances opens a separate modeless `DistanceSpaceWindow`; a single child is
reused while open and closes with Sky tools. It has no persistent settings, observer
changes or network requests except explicit source links. The independent camera
starts centred on the Solar System. Each object centre is its J2000 direction vector
multiplied by its published distance in light-years. Axes follow `SkyCoordinate`:
Y north, X at RA 0, negative Z at RA 90. The reference plane is equatorial, not the
Galactic plane. All distances are linear; no logarithmic compression or marker-size
claims are made. The Euclidean snapshot inherits source uncertainty and does not
model cosmology, travel times, object motion or physical extents.

`DistanceSpaceCamera` provides 55-degree vertical perspective, an orbit pivot and
camera-relative translation. Zoom changes eye-to-pivot distance; panning and WASD/QE
translate both. Relative near/far clipping at 0.00001/8 times camera distance keeps
the point cloud usable across scales; zooming out reveals more distant objects.
Pitch is bounded away from poles; zoom is bounded to 10 through 1e12 light-years.
Home and Reset to Sun clear the selection and restore the initial Solar System view.
Selecting a point or search result immediately frames its position as the navigation
pivot. Recenter on target restores that pivot after panning. Presets change only the
scale around the current pivot, retaining orientation and object distance records. Catalogue filters affect points and search; text search affects only the list.
Render points from far to near, pick nearby screen symbols with depth tie-breaking,
and reserve label space for the Solar System and selection before other labels.
Guides drop labelled points onto the equatorial plane. Labels are bounded and points
are screen-sized. Tests cover independent axis/projection expectations, separation,
scale invariance, navigation/clipping and the shared WPF desktop lifecycle.

Shift-click on the map or search list adds a second measurement target without
changing the primary target or camera. A checkbox provides the same selection mode
without a modifier. Shift-drag continues to pan; only a completed click picks an
endpoint. Selecting B before A establishes A; selecting A as B clears B rather
than producing a self-measurement. Empty Shift-clicks retain the existing pair.
`DistanceSpaceScene.SelectTarget` centralizes these rules in Core, while `ComparisonChanged` keeps the
measurement presentation independent of navigation selection. New primary targets,
catalogue changes and Reset to Sun clear the pair. Escape/Clear measurement remove
only B. Search filtering and camera movement retain the pair.

The result reuses `TargetDistances.Measure` and its approximate two-significant-
figure formatter. Both endpoints are highlighted and labelled A/B, with a dashed
gold line and a projected distance label when in view. The persistent result panel
also shows both names and source details (including reported errors) in its tooltip,
even if the endpoints leave the visible volume. Frame pair uses their Cartesian
midpoint and separation to fit both in perspective. Distance errors are not
propagated. Tests cover M81/M82's known separation, stable navigation when adding B,
search selection, framing, invalid/self selections and clearing.

The scene owns the camera, primary/comparison targets and immutable pair result.
It recalculates the result when the pair changes. The WPF control forwards its
existing selection/comparison/view events and maps mouse/keyboard input to scene
operations. Rendering lives in `DistanceSpaceView.Drawing.cs`; it reads scene state
without owning selection transitions. See [architecture](architecture.md) for
the desktop workflow and rendering boundaries.

## Run

Requires Windows and a .NET SDK with C# 14 support. The app targets `net10.0-windows`; the build's framework-dependent executable needs the .NET 10 Desktop Runtime. `global.json` permits a newer SDK when a .NET 10 SDK is unavailable.

The default library reference is the pinned source submodule at `dependencies\XISFLib.Net\XISFLib\XISFLib.csproj`. Source files in that checkout are not modified.

In Rider, open `SkyBrowser.slnx` and run the **SkyBrowser.Desktop** .NET project configuration. The solution includes XISFLib under **Dependencies**, so Rider builds the current library before compiling the application. Normal IDE Debug builds need no custom command-line arguments.

```powershell
dotnet build SkyBrowser.slnx -c Release --artifacts-path .\artifacts
.\artifacts\bin\SkyBrowser.Desktop\release\SkyBrowser.exe
```

Or run directly:

```powershell
dotnet run --project src\SkyBrowser.Desktop -c Release --artifacts-path .\artifacts
```

You can pass image paths or a folder after `--`. For another library location, update the dependency path in `SkyBrowser.slnx` and pass `-p:XisfLibraryRoot=D:\Your\XISFLib.Net` to the build command. The explicit artifacts path also keeps dependency build outputs inside this repository.

## First launch

When there is no saved settings file or recovery backup, a **Welcome to SkyBrowser** window appears before any images supplied at launch are opened. Choose a default stretch, with **Automatic · dark sky (fast)** recommended, and optionally use **Choose on map…** to select an observing location. The map defaults to the current system time. A selected location starts the sky facing south; leaving it empty uses the celestial atlas. Short descriptions explain each stretch, including the longer first load for dark-sky cleanup.

**Save & start** saves your choices and applies them before loading startup images. **Use defaults** saves the factory defaults without a location, including the 10 GB preview cache. Both dismiss setup on later starts. **Not now**, Escape or closing the welcome window continues without saving; setup is offered again on the next launch if settings are still absent. A failed save keeps the window open with an explanation and the option to defer. Existing saved options and recoverable backups skip setup, and a legacy observer location is prefilled when available. All choices remain editable in **Options**.

## Explore your images

1. Choose **Image library → Add folder…** to build a list of image folders, then **Load folders** to save it and load all supported files recursively. **Remove selected** (Ctrl/Shift for several) and **Remove all** edit the pending list; **Cancel** discards edits. **Add images** opens individual documents directly. Formats: `.fits`, `.fit`, `.fts`, `.xisf`, and distributed `.xish` with its data files present.
2. Plate-solved images appear at their stored coordinates. Double-click a collection entry or choose **Go to image** to frame it. **Double-click a photograph in the sky view** to open internal full-resolution inspection for the foremost image under the pointer. A small corner arrow button on the hovered or selected image opens its original FITS/XISF file in the Windows default application. The button follows projected image corners and stays inside an unobscured image area; zoomed images use a visible viewport edge. It is hidden for tiny or fully obscured fields without room for the control. This action supports keyboard focus/activation and works with outlines or labels hidden. Missing files or absent file associations are reported in the status bar and **Details**. Images without a supported solution remain in the collection with **Needs placement**.
3. Drag the sky to pan and scroll to zoom. Arrow keys navigate when the sky has focus. **Home** restores the initial view (south when an observer is configured), **F** frames the selection, and **+ / −** change zoom. The object picker and RA/Dec inputs jump to a sky position.
4. Toggle layers, set opacity, and use the arrows to reorder them. With **Blend overlaps** enabled, larger sky fields form the background and smaller overlapping images appear above them at 60% of their chosen opacity. Hover an inset to temporarily show it at full opacity; move away to restore the blend and your chosen opacity. Turning **Blend overlaps** off uses collection order, with later entries drawn on top. Grid, constellation lines, labels and image outlines can be toggled separately.
   Drag the horizontal divider between the collection and **Selected image** to give either panel more space. You can also focus the divider and use Up/Down arrows. The chosen split is retained while switching selections or folders, and both panels scroll independently.
5. Confirm the desired folders in **Image library** once. Subsequent application starts load those folders automatically, prioritizing valid cached previews and decoding source files when no current preview is available. Per-image rendering preferences and ASTAP solutions are retained locally; **Options** supplies the startup observer, sky and display defaults.

Optional local plate-solved Horsehead FITS/XISF images can be opened together from **Image library** or **Add images**. Photographs are not bundled; see the [example-image guide](../examples/README.md).

Longer image imports, saved-folder startup loads and display changes open a separate **loading progress window** after a brief delay. It shows completed/total files, how many remain, the current filename and processing step, and counts of ready, cached, failed and already-loaded images. Imports count source files, since one FITS/XISF document can contain multiple images. The overall bar tracks completed entries. A second bar shows progress within the current step when available, otherwise it pulses. Fast cached loads finish without flashing a window.

Choose **Cancel loading**, press **Escape** in the progress window, or close that window to request cancellation. The status-bar Cancel button remains available too. The window shows **Stopping…** until the worker acknowledges the request; some decoding and cleanup filters must finish their current step first. Remaining files are not started. Images already added by an import stay available, while a cancelled display change keeps its previous preview. The window closes automatically when work completes, fails or is cancelled. Folder browsing remains available; choosing another folder cancels the old request and replaces its progress window.

Each collection entry has a small **ⓘ** button. It opens **Image details**, showing the loaded preview, original and preview dimensions, channel/sample information, source path, file size and modification time, display settings and sky placement. The searchable table lists image attributes, FITS keywords (including repeated history entries and comments) and XISF image properties. Select a row to read and copy its value; search also matches values and comments. Details are retained in the preview cache, so opening this window does not decode or stretch the scientific image again. File size and modification time are read separately in the background; if the source is unavailable, the loaded metadata remains accessible. Large property values, arrays and metadata lists are shortened with a visible notice to keep cached details compact.

Use the **Search names & metadata** field above the collection to filter loaded images by filename, object name, or retained metadata keys, values and comments. Matching ignores case, accents and separators: **`ic434`**, **`IC 434`**, **`IC-434`** and **`ic_434`** find the same target. Spaced catalogue prefixes such as `M 31`, `NGC 7000` and `Sh2 240` are also recognized. Multiple terms must all match, including across different metadata fields; for example, `IC 434 H-alpha` narrows the collection to matching hydrogen-alpha images. Search uses normalized text from the loaded image metadata.

The count shows matching/total images while filtering, and an empty result displays guidance. **Ctrl+F** focuses search; **Esc** or **×** clears it. The query stays active while importing or switching folders, with new matching entries appearing as they load. Filtering affects the collection list; layer visibility, blending, sky direction and the loaded images remain unchanged. Selecting an image in the sky clears the filter when necessary to reveal that entry. A normalized search index is built once per loaded image on a worker thread; typing does not read or reprocess source files. Search covers the metadata retained for display, including its limits on unusually large values and lists.

Blending compares angular sky coverage and intersecting footprints, rather than pixel dimensions or object names. It works with rotated images, the RA seam and polar fields; images elsewhere in the sky retain their normal opacity. Nested insets are supported, and hovering reveals the nearest inset under the pointer. Hiding a background makes its remaining inset opaque when no larger overlapping field remains. The startup toggle is configured in Options. Hover changes only scene opacity, so it does not stretch images again or modify cached previews or source files.

### Full-resolution inspection

Double-clicking an image in the sky, or choosing **Inspect full resolution…** in Selected image or Image details, opens a separate
window. It decodes the selected document image on a worker, outside the preview
cache, and renders the complete stored frame with the selected display mode and
cleanup settings. Opening Image details alone still uses retained metadata only.
Inspection jobs are serialized to bound overlapping full-resolution allocations;
closing cancels queued work and suppresses late progress/results. Native decoding
and filters may finish before cancellation is observed. Closing releases the image.

Fit, 100%, wheel zoom (1%-1600%) and drag panning operate independently of the sky.
100% maps one source pixel to one screen pixel, including Windows display scaling.
Pointer readouts use zero-based stored columns/rows and also show one-based FITS
pixel centers. Click holds/releases a readout for copying. Sky coordinates evaluate
the pixel center through the current file solution, source-validated saved ASTAP
mesh or active manual placement; manual and cached-mesh coordinates retain those
placements' accuracy limits. A missing solution leaves scientific readouts available.

Readouts and statistics use original XISF samples or FITS physical values after
BSCALE/BZERO, with BLANK represented as NaN. They precede stretching, cosmetics and
debayering; alpha is a separate channel and Bayer values remain mosaic samples.
UInt64 readouts retain all integer bits. Each channel's 256-bin histogram spans its
finite minimum/maximum with a linear count axis. All source samples contribute;
non-finite values are counted separately. Statistics show minimum, maximum, mean
and population standard deviation using double precision (large integers can round).
Constant channels occupy the first bin; all-invalid channels have empty bins and
undefined statistics. No scientific samples or full-resolution bitmaps are persisted.
Source/sidecar signatures are checked before and after loading; unavailable,
changed-dimension or concurrently modified sources produce an error in this window.

### Find in the sky

The finder starts collapsed to leave more of the sky visible. Click **Find in the sky** or its arrow to expand or collapse it. Collapsing keeps your filter, selected target and added sky markers.

The offline finder contains **13,372 distinct targets**, covering all **110 Messier** and **109 Caldwell** entries, together with positioned NGC and IC entries and their components. Duplicate catalogue designations resolve to the same object. Nonexistent entries without a sky position are omitted. Data come from [OpenNGC](https://github.com/mattiaverga/OpenNGC), with Caldwell cross-identifications from the [Astronomical League](https://www.astroleague.org/caldwell-program-object-list/); see the [third-party notices](../THIRD-PARTY-NOTICES.md).

Type a designation or common name in the filter, for example `IC434`, `IC 434`, `M31`, `Caldwell 20` or `Horsehead`. Matching ignores case, accents and separators, accepts multiple terms, and puts an exact catalogue designation first. The catalogue selector restricts results to Messier, Caldwell, NGC or IC. Hover an entry for its coordinates, type, angular size, magnitude and catalogue notes where available. The dropdown loads visible rows as needed to stay responsive with the full catalogue.

Choose a result to face it, add a named marker to the sky and fill the RA/Dec fields. **Enter** selects an exact designation or a single remaining match. The single **Go** button beside RA/Dec returns to the selected object and restores its framing when its displayed coordinates still match. Edit either coordinate to navigate to that position while retaining the current zoom; Go also works without a selected catalogue target. Approximate angular outlines appear when size data are available. The background initially labels **30 prominent targets**; chosen targets remain added while browsing or changing the filter. Aliases of an already added object share one marker. **Clear added** removes these extra markers. These markers identify catalogue positions; your loaded photographs continue to appear at their own astrometric coordinates. Turning labels off hides captions while retaining the added position markers.

### Observer location and time

Choose **Options → Observer → Choose on map…** to open a separate setup window. Click the offline world map to place a marker, drag to pan and scroll to zoom. Latitude and longitude can also be entered precisely; negative values mean south and west. Keyboard arrows move the marker when the map has focus. **Apply & face south** stages the location in Options; **Save options** applies it to the sky. Cancelling Options keeps the current view.

**Use current system time** is enabled by default. The local sky then updates every second while preserving the direction you are looking. Uncheck it to choose a fixed date and 24-hour time, either in your **computer's local time zone** or **UTC**. The map does not infer a time zone; the sky's corner clock displays computer-local time with its UTC offset. Ambiguous or nonexistent local times during daylight-saving transitions are rejected with guidance to use UTC.

Applying an observer faces **south, 20° above the horizon**, at a 90° field of view. The horizon stays level, compass labels appear along it, and the corner readout shows azimuth, altitude and observing time. **Alt/Az grid** is fixed to the local horizon so the stars move against it as time changes; atlas mode retains **RA/Dec grid**. With labels enabled, the actual **Zenith (90° altitude)** and **Polaris with its calculated altitude** are marked when visible. Polaris is approximately as high above the northern horizon as the observer's latitude; the zenith coincides with a celestial pole only at a geographic pole. Stars and image footprints remain aligned in J2000; Astronomy Engine supplies the observer rotation, including precession and nutation. Panning, zooming, object searches, image framing, blending and opening originals remain available. Imports preserve the local compass view; explicitly frame an image to jump to it.

Saving a location retains it and its live/fixed time choice in the user options. Subsequent starts face south at that location, using the current system time when the live clock is selected. **Clear location** in Options (or **Use atlas view** on the map), followed by **Save options**, clears the saved location and returns to the original view. Without a location, the previous atlas behavior and initial view are retained. The map is never shown in the main sky view or opened automatically.

With a location active, **Scene time** appears below the sky. Drag its slider backward or forward up to 12 hours from its center; the first movement pauses the live clock. **−1 day** and **+1 day** move 24 hours and recenter the slider, allowing longer trips through the calendar. Arrow keys move one minute and Page Up/Down move one hour while the slider has focus. **Live now** returns to the current system clock. The displayed date/time includes the computer-local UTC offset; stepping uses elapsed UTC time safely across daylight-saving transitions. Limits are clamped to 1900–2100. The compass direction, zoom, image placements and processed previews are retained; this does not reload or stretch images. Scene-time edits leave saved startup defaults unchanged. The timeline is hidden in atlas mode.

The country map is bundled and requires no network requests or location permissions. Its boundaries are approximate; use numerical coordinates for precision. The horizon is geometric at sea level: no atmospheric refraction, terrain masking, daylight simulation or stellar proper-motion updates are applied. Dates from 1900 through 2100 are supported. Objects below the horizon remain browsable.

### Persistent options

Choose **Options** in the top bar to configure:

- **Images:** the default stretch variant, sky background target and cleanup options. These apply to newly loaded images without a saved per-image rendering choice. Existing per-image choices take precedence. Saving does not reprocess the collection currently on screen. **Reset defaults** in the selected-image panel applies your saved image defaults.
- **Observer:** a startup location selected on the separate world map, with current system time or a fixed observing time. Clear the location to use the original atlas view.
- **Cache:** the cache folder and maximum preview storage, initially **10 GB**, with currently used space and preview count shown beside the limit. Usage is read from the active cache when the dialog opens; a folder change takes effect when saved. GB uses 1,024³ bytes. Space is allocated as previews accumulate. Saving a smaller limit immediately evicts older previews. Switching folders merges previews, per-image choices and recent folders into the destination, preserving destination-only entries. The original cache files remain in their old folder; remove those separately if you no longer need them.
- **Sky:** startup defaults for the coordinate grid, constellation lines, labels, image outlines and overlap blending. Saving also applies these choices to the current view.

**Save options** applies and saves changes; **Cancel** leaves your settings unchanged. **Restore defaults** stages the factory defaults until saved. Invalid input or a failed save keeps the dialog open so you can correct it. The options file is `%LOCALAPPDATA%\SkyBrowser\settings.json`, separate from the disposable cache. Writes are atomic, and a `.bak` file retains the previous valid version for recovery. A location saved by the earlier version is imported automatically. Options persist across exits and restarts; moving or clearing the cache does not remove them.

### Persistent preview cache

Processed previews are stored in a local **SQLite database**, shared by folder browsing, image imports and saved-folder startup loading. The cache survives application exits, restarts and reboots. On a cache hit, scientific decoding, stretching and cosmetic processing are skipped; the saved pixels, metadata and sky mesh are restored directly.

- Default location: `%LOCALAPPDATA%\SkyBrowser\Cache\previews-v1.sqlite3`. No database server or account is needed.
- Rendering modes and finishing settings are remembered for each source image. Changing the settings creates a separate preview variant; returning to an existing variant reuses it.
- Source signatures include file size, modification/creation times and SHA-256 samples from the beginning, middle and end of each file. Distributed XISF data files are tracked too. These are fast change detectors, not full-file integrity hashes: an edit that deliberately preserves timestamps and only changes unsampled bytes can evade detection. Rendering pipeline/library versions also invalidate older previews.
- Stored preview data defaults to **10 GiB**, configurable in **Options → Cache**, with least recently used previews evicted automatically. SQLite pages, journals and small preference/history records add some disk overhead. The Cache tab shows usage and the cache folder. **Clear saved previews** releases previews from the active cache immediately while retaining rendering preferences, recent folders and loaded images.
- Cached files load first, and images appear progressively. Switching folders cancels the preceding request. Warm cache hits can load while a previous cold image finishes decoding; cold jobs are serialized to bound memory use. Image processing and database work run off the UI thread.

The first visit to an image, or a previously unused finish, still requires processing. Corrupt previews are regenerated, and an unavailable cache falls back to normal processing. Source files remain unchanged; the cache is disposable and does not replace the originals.

### Display stretching

Scientific reading and stretch functions use **XISFLib**, including FITS physical-sample conversion, color handling, compression/checksums, stored display functions and median/MAD adaptive stretching. Processing runs off the UI thread. Source files are never rewritten.

**Automatic · dark sky (fast)** is the default and performs **stretching only**. It uses a stored display function when available. Otherwise it inspects normalized scientific samples: dark data and narrow high-pedestal distributions receive an adaptive stretch to the chosen sky-background target, initially 10%. High-pedestal data are recentered as part of the stretch. Automatic skips background color balancing, gradient reduction, noise smoothing, sharpening and color enhancement, even when those cleanup choices are enabled in Options. Developed images with broad, brighter distributions retain their existing exposure. This detection is a heuristic; **Linear / already stretched** always bypasses stretching and cleanup.

**Dark sky + cleanup** runs the full finishing pipeline, adapting `XISFLib.Viewer.Core/ViewerFinishService` from your Avalonia viewer:

- A default **10% sky background**, replacing the old 25% screen-stretch target. It uses `XisfDisplayFunctions.CreateAdaptive` with linked RGB parameters and −2.8 clipping. High-pedestal linear data is recentered before stretching.
- Optional background color balancing and a conservative, two-sided clipped broad-gradient plane. The plane requires spatial support in every quadrant and removes 80% of a reliable slope. It is an aesthetic estimate; turn it off when a nebula filling the frame is affected.
- Optional Magick.NET selective background smoothing, thresholded detail enhancement and a subtle color boost, using the viewer's filter settings.
- The full image frame, row order, reflection and alpha coverage are retained. Unlike the sharing-image workflow, this does **not** crop edges or rotate pixels; the original WCS remains applicable. It does not include the viewer's multiscale wavelet denoising, local star deconvolution or spike attenuation.

Select an image, expand **Sky background & cleanup**, adjust the background slider (2–25%), choose cleanup options, and click **Apply finish**. This selects **Dark sky + cleanup** for that image. Automatic retains these choices for a later switch to cleanup but uses only the background target. **Reset defaults** applies the image defaults saved in **Options** (initially Automatic and 10%). Older processed previews are regenerated for the updated pipeline; image preferences remain available.

**Library auto stretch (25%)** remains available for comparison with the old screen stretch. **Stored display function** uses the file's display metadata or produces a linear display when that metadata is absent. Finishing controls do not change opacity. Source pixels and files remain intact, and processing can be cancelled between stages; progress is shown in the loading window and status bar.

### Persistent archive library

`library-v1.sqlite3` lives beside `settings.json`, independently of the disposable preview cache. It retains folder roots, imported document/image metadata, source/dependency signatures and source-bound ASTAP solutions. The library dialog lists folders, never individual image records. Folder additions, multi-selection removal and clearing the list are staged in memory; Cancel/close leaves the database and scene unchanged. Opening the dialog checks only folder availability, without scientific decoding or background indexing. After first-run setup, application startup reads these saved roots and loads their supported files automatically, using the same cached loader as folder confirmation. Cached source paths are prioritized; each preview is validated against its source and sidecars, and a missing/stale/corrupt entry falls back to source processing. Recursive discovery also includes new files. Startup does not rewrite the folder list. Explicit startup image/folder arguments append after the saved folders. Ordinary imports do not add new roots. Folder search filters names/paths and does not change the complete list to be loaded.

**Load folders** atomically replaces the root list, discovers supported files recursively without following reparse points, and loads them with the existing cache, progress and cancellation pipeline. Overlapping roots and multi-image documents are deduplicated. All listed roots load, including roots hidden by the folder filter. Confirmed root removals remove covered scene layers unless another retained root covers them; unrelated scene layers and original files are preserved. Existing layer state is retained, and repeating confirmation discovers newly added files and missing image indices. Unavailable roots stay saved and are reported. Cancelling subsequent image loading keeps the already-confirmed folder list and partial imports. Folder removal and preview-cache operations retain indexed metadata and ASTAP solutions. The core scan/monitor APIs remain available for explicit tooling but the desktop no longer starts an automatic monitor.

### ASTAP blind solving

Configure **Options → Plate solving** with an absolute `astap.exe` or `astap_cli.exe` path. ASTAP and a star database must be installed separately; an optional database folder can be set. The default timeout is 300 seconds (10–3600 supported). Image height defaults to 0 for automatic scale search; an explicit degree value can help unusually wide/narrow fields. See [ASTAP's command-line documentation](https://www.hnsky.org/astap.htm#astap_command_line) for its automatic scale-search range and database requirements.

**Blind solve & place** decodes the selected image into a unique temporary full-frame, linear Float32 Gray FITS copy. RGB is averaged, alpha is masked, supported Bayer data is reconstructed first, and display stretch/orientation is not applied. ASTAP never receives the original path. Process arguments use full-sky search (`-r 180 -ra 0 -spd 90`), auto downsampling (`-z 0`), configured/automatic field height (`-fov`), a temporary output base (`-o`) and `-sip n` to request supported linear TAN. No `-update` flag is used. Standard output/error are drained concurrently with bounded retained diagnostics; cancellation/timeout terminates the process tree and waits for exit.

Successful INI reports must identify a solved image and contain finite reference pixels, celestial coordinates and a nonsingular CD matrix. The complete 33 x 33 mesh is evaluated before publication, and source/sidecar signatures are revalidated before committing. Placements persist separately from original metadata and disposable previews, keyed by source signature and image index. They apply on imports, reprocessing, cache hits and startup restoration; changed sources invalidate them. An explicit successful solve replaces manual placement. Original files remain byte-for-byte unchanged. A scene transferred to another computer still requires its sources and that computer's library solution (or another solve); solutions are local archive records.

Image sizing uses the fitted TAN matrix and full source dimensions, including rotation and separate X/Y scales. The selected-image inspector shows field width/height and the angular size of a source pixel at the image center, independent of preview resolution. ASTAP's recognized "inexact scale! Set FOV=... or scale=..." advisory compares its starting search scale with the fitted result. After validating the solution, that advisory is dismissed because the fitted scale is already applied; other diagnostics are retained. Rounded numbers in the advisory never replace the precise matrix or change global field-height hints.

**Save solved copy** is enabled for an ASTAP placement with no manual override. It writes a separate `.fits`/`.fit`/`.fts` or monolithic `.xisf`, using full-resolution source samples without display stretch, cleanup, rotation or debayering. All document images are retained and only the selected image receives the new solution. FITS-to-FITS retains original image storage and HDUs; XISF-to-XISF retains document metadata. Cross-format omissions are reported in the status/details log; XISF is preferred for retaining XISF-specific properties. Recognized native Bayer tiles are represented by FITS `BAYERPAT` without altering samples.

Export revalidates the saved source signature, recovers the centered TAN matrix from the exact retained mesh vertices and verifies all 1089 vertices within 1e-8 degrees before writing. FITS gets standard ICRS TAN WCS; XISF gets native Revision 1 linear astrometry plus equivalent FITS keywords. Stale competing WCS/distortion entries are removed from the selected image. Export refuses to replace the source or its sidecars, stages output beside the destination, and checks cancellation and source signatures before committing. Reopening a solved copy requires no local library. A changed source must be solved again before export.

### Positioning

- Native XISF Revision 1 solutions are evaluated through the library, including its supported projection, projective and distortion layers. These take precedence over FITS keywords.
- FITS automatic placement supports TAN, TAN-SIP, TPV, SIN, STG, ZEA and ARC in degrees, in ICRS or FK5/J2000. CD, PC/CDELT, legacy CROTA rotation and swapped celestial axes are supported. FITS one-based reference pixels remain distinct from native XISF half-pixel centers. SIP forward polynomials (orders 2–9) operate on pixel offsets before CD; TPV uses all 40 registered terms after CD in projection-plane degrees, including radial terms and the reversed PV2 arguments. Missing TPV linear terms use the registered identity defaults. Legacy TAN/PV headers require coefficients for both axes to distinguish them from unrelated native-pole parameters. Mixed SIP/TPV, malformed coefficients, lookup-table distortion, custom pole orientations and non-J2000 frames are rejected with an explanation. Exact north-pole default orientation is handled separately.
- Legacy linear `PCL:AstrometricSolution:*` projection/reference/matrix properties precede FITS WCS. Their native half-pixel geometry is retained. Opaque serialized and spline legacy models remain unsupported and are never reduced to their linear approximation. Acquisition RA/Dec alone is insufficient to infer scale, rotation and reflection.

The FITS model is evaluated directly for coordinates and persisted beside the display mesh in the preview cache. Cache hits retain polynomial precision between mesh vertices. Swapped celestial axes are supported for undistorted and SIP solutions; TPV requires the registered RA-first/Dec-second ordering. Independent synthetic reference cases cover SIP order 9, every TPV term, additional projections, axis ordering, CROTA, the RA seam and poles. Regenerate with `tools/Build-AstrometryReferences.py` using Astropy 8.0.2; runtime and normal tests do not require Python. The reference generator explicitly supplies TPV identity coefficients because WCSLIB's omitted-coefficient behavior differs from the registered convention.

The mathematical conventions are documented by the [registered SIP specification](https://fits.gsfc.nasa.gov/registry/sip/SIP_distortion_v1_0.pdf) and [TPV registry](https://fits.gsfc.nasa.gov/registry/tpvwcs/tpv.html). Legacy property semantics are described by the publisher's [PCL WCS metadata implementation](https://gitlab.com/pixinsight/PCL/-/raw/master/src/pcl/WCSKeywords.cpp); SkyBrowser uses its own adapter and does not import PCL code.

### PNG/JPEG export

**Export…** in the header offers sky view or selected-image preview. Sky dimensions
are 128–8192 pixels per side with a 32-million-pixel limit. The center and horizontal
field remain fixed; changing aspect ratio changes the vertical view. A separate
viewport copies the camera, exact observer instant, visible image state and targets.
Labels, outlines, coordinate grid and center-coordinate caption are optional. No
application controls, image action buttons or transient hover opacity are exported.
Closing the temporary renderer detaches all layer subscriptions.

Preview export uses the existing processed pixels at their stored dimensions,
without re-decoding sources. PNG preserves alpha; JPEG composites against RGB
(8,14,22), with quality from 1 to 100. New encoder frames omit original metadata.
Files are staged beside the destination, cancellation and source/sidecar protection
are checked again, then output is committed atomically. Export does not replace a
scientific source or distributed XISF sidecar. If an unavailable XISF header prevents
sidecar validation, choose a new destination filename. These display exports do not
carry scientific samples or embedded WCS; **Save solved copy** remains the separate
scientific export workflow.
- Manual placement takes center RA/Dec in **decimal degrees**, angular width, rotation and optional horizontal mirroring. Zero rotation means north up, east left; positive rotation is counterclockwise on the sky. Placement is an adjustable tangent-plane approximation, not a plate solve.

The display maps the original stored rows to their astrometric coordinates. XISF display-only rotation/reflection metadata is deliberately bypassed so it cannot rotate the texture away from its solution.

## Current scope

- A J2000/ICRS celestial atlas with optional observer-relative horizon orientation and live/fixed observing time. No atmospheric effects, terrain, daylight simulation, planetary ephemerides or stellar proper-motion updates.
- ASTAP integration requires a separately installed executable and star database. It can blind solve without a coordinate hint. Legacy PixInsight `PCL:AstrometricSolution:*` distortion models are not the new native XISF solution format.
- Two-dimensional grayscale/color images and FITS RGB/RGBA cubes. A three/four-plane FITS third axis is explicitly interpreted as color. Other scientific cubes and complex samples are rejected. Single-channel Bayer mosaics with RGGB, BGGR, GRBG or GBRG metadata are automatically reconstructed to RGB using bilinear interpolation before preview stretching/resizing. Native XISF 2 x 2 CFA takes precedence over FITS BAYERPAT. FITS offsets XBAYROFF/YBAYROFF (or XBAYEROFF/YBAYEROFF, including HIERARCH) select the effective stored tile; explicit ROWORDER=BOTTOM-UP adjusts row parity for image height without flipping pixels. Without ROWORDER the pattern starts at stored (0,0). Unsupported/invalid CFA metadata remains grayscale with an explanation, skipping cosmetic cleanup. Existing RGB/alpha data are not debayered. Originals and astrometry are unchanged; source metadata is retained. Reconstruction allocates full-resolution RGB data in addition to the decoded mosaic.
- Display previews have at most **2048 pixels** on their longest side, using box averaging. Finishing averages linear scientific samples before stretching/quantization, then applies cosmetics at preview resolution. A 32 × 32 mesh approximates each footprint. Full-resolution source decoding still occurs, so large files need adequate memory. Raw sample buffers are released after import; layers retain their preview and astrometry.
- Cancellation is checked between file, decode and render stages; library codec calls are not interrupted mid-operation. Malformed files and unavailable sources are explained in **Details**.

## Build and verify

See the [performance review](performance-review.md) for measured rendering/loading improvements, validation, remaining costs and repeatable CPU/allocation probes.

```powershell
dotnet build SkyBrowser.slnx -c Release --artifacts-path .\artifacts
dotnet test tests\SkyBrowser.Tests -c Release --artifacts-path .\artifacts
```

Tests cover celestial projection and hemisphere/seam behavior, pixel origins, manual transforms, unsupported WCS handling, XISF/FITS imports, dark-sky finishing, high-pedestal inputs, transparency, source preservation, orientation, overlapping image brightness, object-picker interactions, saved-folder startup, mixed cache hits/misses, stale sources and missing files. Overlap checks cover sky-area ordering, actual rendered alpha values, rotated and nested hover hit tests, background visibility, manual opacity, touching/disjoint fields, RA seams and poles. Cache checks include a new program process with scientific processing disabled, exact pixel/mesh restoration, preference persistence, source and XISF-sidecar invalidation, partial multi-image entries, corruption recovery, eviction, cancellation, concurrent warm/cold loading and rapid folder switches. The desktop test creates a real WPF window on a dedicated STA thread and closes it afterwards. The optional solved-example check compares the Horsehead's old and finished previews, verifies unchanged WCS coordinates, and renders the FITS/XISF pair blended and hovered.

Loading-progress checks hold a processing step open while verifying actual file totals, remaining counts, cached/failed/skipped results and per-step progress. They exercise Cancel, window-close and Escape cancellation, preserve partial imports and existing previews, reject late progress callbacks and verify that a subsequent import works. Screenshots include `image-loading-progress.png` and `image-loading-cancelling.png`.

Automatic-stretch checks compare its pixels to a stretch with all cleanup disabled, verify that no cleanup stages run even with every cleanup option saved as enabled, and exercise both dark and high-pedestal linear images with transparency. Explicit cleanup still runs its full set of stages, while the original file and the user's cleanup choices are preserved.

First-run checks choose a stretch and an optional map position, verify that startup images use the saved stretch, and restart with the saved preferences. They also exercise deferring setup, using defaults without a location, backup recovery and a failed first save. Screenshots include `first-run-setup.png` and `first-run-location.png`.

Observer tests verify horizon orientation across both hemispheres and the poles, the J2000 Greenwich sidereal reference, live/fixed clocks, DST conversions, saved locations and startup observer defaults. The desktop check exercises the actual map/time dialog, cancellation, returning to atlas view and importing without losing the compass direction. Screenshots include `observer-location.png`, `observer-custom-time.png` and `observer-south.png`.

Options checks cover a new program process, complete setting restoration (including ASTAP paths, timeout and field height), invalid data, backup recovery, legacy-location migration, cache copying and size reduction, and the precedence of image preferences over global defaults. WPF checks exercise the Options workflow, location selection, cancellation, unavailable cache folders and migrated previews. Library/solver checks cover recursive indexing, new-process search, cache-independent retention, missing/restored sources, malformed files, staged folder edits, cancellation, overlapping roots, confirmed removals, changed-file refresh and complete multi-image loading, solver failures/cancellation/timeout, source/sidecar changes and solved startup restoration. The process fixture validates blind-search arguments and temporary scientific inputs. Actual installed-ASTAP checks are available through the CacheProbe `--astap-solve source executable report-directory` command and never write to the source image.

Image-detail checks verify FITS structural keywords, duplicate history and property comments, metadata for individual images in a multi-image document, bounded large values, and details restored by a separate process without scientific processing. The desktop check clicks the actual collection info buttons, filters and selects metadata, reads file sizes, handles a missing source, and opens details for the real XISF example. Screenshots include `image-details.png`, `image-details-missing.png` and `image-details-xisf.png`.

Collection-search checks cover filename-only and metadata-only matches, catalogue spacing, punctuation, accents, comments, combined terms and unrelated-field boundaries. The WPF check verifies matching counts, empty results, unchanged sky layers, live imports, removing a filtered entry, folder switches and clearing search. Screenshots include `collection-search.png` and `collection-no-results.png`.

Target-catalogue checks verify every Messier and Caldwell designation, NGC/IC coverage, coordinates, aliases, common names, catalogue filters and exact-match ranking. The WPF check opens the actual virtualized dropdown, chooses entries, verifies a rendered marker, tests alias deduplication and the Go button. Screenshots include `target-picker-dropdown.png` and `target-picker-selected.png`.

To regenerate the bundled deep-sky data, run `pwsh -File .\tools\Build-TargetCatalog.ps1` with PowerShell 7. The script downloads the pinned OpenNGC CSV snapshot into `artifacts/catalog-sources` when absent, applies the checked-in Caldwell cross-reference, and writes `src/SkyBrowser.Core/Data/deep-sky.json`. This adapted dataset and its attribution and license are also copied into build/publish outputs.

Optional checks against your large examples and rendered screenshots:

```powershell
$env:SKYBROWSER_SAMPLE_DIRECTORY = 'C:\SkyBrowserDemo\LibrarySamples'
$env:SKYBROWSER_SOLVED_SAMPLES = 'C:\SkyBrowserDemo\Examples'
$env:SKYBROWSER_SCREENSHOTS = 'C:\SkyBrowserDemo\Screenshots'
dotnet test tests\SkyBrowser.Tests -c Release --artifacts-path .\artifacts
```

To measure cold versus cached loading of the examples, run these phases in separate processes after building the solution. Use a new cache directory for each cold measurement. The warm phase throws if scientific processing runs, and each report includes preview hashes for comparison. Timings include cache initialization and loading, but exclude WPF scene creation and process startup.

```powershell
dotnet artifacts\bin\SkyBrowser.CacheProbe\release\SkyBrowser.CacheProbe.dll --measure cold artifacts\cache-measurement examples artifacts\cache-cold.json
dotnet artifacts\bin\SkyBrowser.CacheProbe\release\SkyBrowser.CacheProbe.dll --measure warm artifacts\cache-measurement examples artifacts\cache-warm.json
```

`src/SkyBrowser.Core` contains coordinate math, catalogues, astrometry adapters and imports. `src/SkyBrowser.Desktop` owns the WPF scene, controls and automatic folder startup. C# 14 field-backed properties, immutable records, source-generated JSON, spans in the underlying library, frozen WPF assets and cancellable worker operations keep the implementation small and explicit.

The repository's `.editorconfig` defines four-space C# indentation and two-space XML/XAML indentation. Use explicit braces for control flow, keep statements on separate lines, and wrap long calls and queries at logical boundaries. Simple properties and short expressions can stay on one line. Comments explain coordinate conventions, buffer ownership, cache validity and asynchronous lifetimes; keep those constraints beside the code they describe. Bundled catalogue data and build-generated files keep their existing generated format.

To compare Automatic and Dark sky + cleanup processing on an individual image without the preview cache, run:

```powershell
dotnet artifacts\bin\SkyBrowser.CacheProbe\release\SkyBrowser.CacheProbe.dll --compare-stretches examples\horsehead-wide.fits artifacts\stretch-comparison.json
```

The report includes decoding, metadata and preview processing time for each mode, excluding cache and scene creation. Automatic is measured first; filesystem caching and machine load affect the timings.

Publish a self-contained Windows x64 build:

```powershell
dotnet publish src\SkyBrowser.Desktop -c Release -p:PublishProfile=WinX64 --artifacts-path .\artifacts
```

The output is under `artifacts/publish/SkyBrowser.Desktop/release_win-x64`. See [third-party notices](../THIRD-PARTY-NOTICES.md) for the offline sky catalogue's sources and license.
