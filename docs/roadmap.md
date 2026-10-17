# SkyBrowser feature roadmap

Recorded from the product review on 2026-10-16. This is a backlog, not a promise
that every feature is implemented or will ship in this order.

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
samples or astrometry. Future library work includes tags, thumbnails, sorting,
relocation and more efficient initial decoding.

## Released in 0.4.0

3. **Full-resolution inspection:** internal 100% zoom, pixel/sky coordinates,
   scientific sample readout, histogram and image statistics.

Available from Selected image and Image details. Inspection opens source pixels on
demand, with fit/100% zoom, wheel zoom, drag panning and per-channel statistics.
Measurements use unstretched original samples (FITS physical values); Bayer
readouts retain the original mosaic. Source files and atlas previews stay unchanged.

## Next priorities

4. **Astrometry compatibility:** FITS SIP/TPV distortion and more existing solutions,
   including legacy PixInsight metadata, with physically accurate evaluation.
5. **Export and sharing:** PNG/JPEG sky-view exports with optional labels, outlines
   and coordinates; selected-image preview export without changing sources.
6. **Comparison tools:** aligned blinking, swipe divider and linked side-by-side
   views for filters, processing versions and observing dates.
7. **Collection organization:** thumbnails, sorting, structured metadata filters,
   tags, favorites, groups and batch visibility/display operations.
8. **Full workspace recovery:** restore camera/layer choices, undo/redo, locate
   missing files and remap moved archive folders, beyond the saved folder list.

## Astronomy and planning

9. **Survey background:** optional DSS/HiPS imagery and catalogue overlays behind
   personal photographs, with cached/offline behavior and retained attribution.
10. **Camera framing and mosaics:** equipment profiles, rotatable field-of-view
    rectangles, overlap/panel planning and coordinate export.
11. **Target visibility:** altitude charts, transit/rise/set times, darkness windows,
    Moon phase/separation and useful imaging windows.
12. **Annotations and measurements:** named bookmarks, notes, angular distances,
    scale bars and north/east indicators.
13. **Processed-image imports:** TIFF/PNG/JPEG with solving or saved placement.

Current product focus: a browsable atlas of the user's photographs. Library,
placement and sharing workflows take priority over telescope/camera control.
