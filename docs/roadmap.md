# SkyBrowser feature roadmap

Updated on 2026-10-20 for version 0.9.0. This records completed work and the
remaining ideas from the product review on 2026-10-16. Future priorities are a
backlog, not a promise that every feature will ship in this order.

## Released in 0.3.0

1. **Persistent image library:** manage a saved folder list, stage additions and
   removals, confirm recursive loading and reopen those folders automatically at
   startup. Reuse valid cached previews and process new or changed source files.
2. **ASTAP plate solving:** configure an executable path, blind solve without
   requiring approximate coordinates, report progress/failures, support cancellation,
   and retain validated placements across restarts without rewriting originals.

The library uses separate durable SQLite metadata and source-bound placements.
The folder dialog does not decode images before confirmation. Session files have
been replaced by automatic startup loading from saved folders. ASTAP executable,
database paths, timeout and optional field-height hint are configurable. Solving
searches the whole sky, applies the fitted scale and supports solved FITS/XISF
copies. Recognized Bayer data produces color previews without changing original
samples or astrometry. Tags, thumbnails and sorting followed in 0.6.0; archive
relocation and more efficient initial decoding remain future work.

## Released in 0.4.0

3. **Full-resolution inspection:** internal 100% zoom, pixel/sky coordinates and
   scientific sample readout.

Available from Selected image and Image details. Inspection opens source pixels on
demand, with fit/100% zoom, wheel zoom and drag panning. The original histogram
and statistics sidebar was removed in 0.9.0 to give the image more room.
Measurements use unstretched original samples (FITS physical values); Bayer
readouts retain the original mosaic. Source files and atlas previews stay unchanged.

## Released in 0.5.0

4. **Astrometry compatibility:** FITS SIP/TPV distortion and more existing solutions,
   including legacy PixInsight metadata, with physically accurate evaluation.
5. **Export and sharing:** PNG/JPEG sky-view exports with optional labels, outlines
   and coordinates; selected-image preview export without changing sources.

FITS placement now evaluates SIP (orders 2–9), TPV (all 40 coefficients), TAN,
SIN, STG, ZEA and ARC, with swapped axes and CROTA support. Legacy linear
PixInsight metadata is recognized; opaque legacy spline formats still require a
new plate solution. Exact FITS models survive preview-cache reloads.

**Export…** saves an independent sky rendering with resolution and overlay choices,
or the selected image's existing display preview. PNG retains preview alpha;
JPEG composites onto a dark background. Sources and XISF sidecars are protected.

## Released in 0.6.0

6. **Comparison tools:** aligned blinking, swipe divider and linked side-by-side
   views for filters, processing versions and observing dates.
7. **Collection organization:** thumbnails, sorting, structured metadata filters,
   tags, favorites, groups and batch visibility/display operations.
9. **Survey background:** optional DSS/HiPS imagery and catalogue overlays behind
   personal photographs, with cached/offline behavior and retained attribution.
11. **Target visibility:** altitude charts, transit/rise/set times, darkness windows,
    Moon phase/separation and useful imaging windows.
12. **Measurements and sky guides:** angular distances, scale bars and north/east
    indicators.

Select placed collection entries with Ctrl/Shift and choose **Compare…** for linked
sky cameras, timed blinking or a draggable swipe divider. Version 0.9.0 additionally
requires overlapping fields and starts in swipe mode. Comparison uses independent
display snapshots and the existing astrometric projection. **Sort & filter** narrows
the loaded collection without changing sky layers; **Organize…** saves tags, favorites
and a group per source/image in the durable library and applies batch scene/display
changes with progress and cancellation. Source files remain unchanged.

**Sky tools…** adds explicit CDS HiPS2FITS downloads of DSS2 colour/red fields,
a separate 512 MB offline cache, catalogue overlays and retained data credits.
An optional startup default restores all cached DSS2 regions without downloads.
Visibility planning evaluates a 24-hour UTC period with the existing Astronomy
Engine, target/Sun/Moon curves, geometric horizon events, transit and configurable
imaging windows. Great-circle measurements, angular scales and celestial direction
guides apply to the scene.
Known, manually supplied object distances can also give a light-year 3D separation.
The catalogue section in Measurements additionally supplies 3,283 attributed NASA/CDS estimates
across Messier, Caldwell, NGC and IC through searchable object selectors, with
coverage counts, source/error details and approximate separation labels.

**Objects in 3D** provides an independent offline map of these catalogue objects,
with linear light-year positions, orbit/pan/zoom, object search and focus, scale
presets, source details and two-object measurements. It leaves the atlas unchanged.

## Released in 0.7.0

- **Compact Windows distribution:** a self-contained executable, PDF manual and
  notices archive in a three-file ZIP, alongside the regular distribution.
  Both packages preserve the same executable and supporting files, with validated
  contents, checksums and complete source/compliance assets.

## Released in 0.8.0

- **Desktop workflow organization:** feature-specific window files share operation
  and lifetime guards. The independent 3D scene owns navigation and selection in
  Core, with direct transition tests and WPF rendering checks.

## Completed for 0.9.0

- **More existing plate solutions:** accept valid zenithal LATPOLE preferences,
  including zero in Draco TAN-SIP FITS exports, while retaining exact distortion
  evaluation and scientific coordinate readouts.
- **Simpler image inspection:** remove the histogram/statistics sidebar. Keep the
  image across the available width, with coordinates, display RGB as #RRGGBB and
  separately labelled full-precision original samples below it.
- **Clearer image interaction:** eye/crossed-out-eye icons control visibility.
  Choosing sky targets or measuring clears image editing selection and collapses
  its controls. Empty-sky clicks and Escape also deselect; clicking an image returns
  to its controls without changing visibility.
- **Meaningful comparisons:** start in Swipe divider mode, require all selected
  sky footprints to overlap, keep A and B distinct and frame both through shared
  sky. Fit both restores that view. Compare enables only for an eligible selection,
  updates after placement changes and explains disabled states in its tooltip.
- **One Measurements tab:** combine sky-point/manual distances and catalogue
  target distances, retaining sources, shared measurements and 3D exploration.
  Remove the bookmark feature from Sky tools and atlas overlays; preserve legacy
  stored records for compatibility.
- **3D scale and focus:** cap the vertical focus span at 13.8 billion light-years
  across zoom, presets and framing, with feedback at the limit. Move the reference
  plane, rings, guide drops and north/south colours to the focused object. Panning
  and pair framing retain that origin; Reset to Sun restores the Solar System.
- **Current documentation:** update the beginner manual, anonymous screenshots,
  technical reference and regression coverage for these workflows.

## Next priorities

8. **Full workspace recovery:** restore camera/layer choices, undo/redo, locate
   missing files and remap moved archive folders, beyond the saved folder list.

## Astronomy and planning

10. **Camera framing and mosaics:** equipment profiles, rotatable field-of-view
    rectangles, overlap/panel planning and coordinate export.
13. **Processed-image imports:** TIFF/PNG/JPEG with solving or saved placement.

Current product focus: a browsable atlas of the user's photographs. Library,
placement and sharing workflows take priority over telescope/camera control.
