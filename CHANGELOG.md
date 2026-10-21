# Changelog

## 1.0.0 - 2026-10-21

- Add **Forget ASTAP solution** for the selected image. Remove its saved local solve across restarts, restore file astrometry or show Needs placement, and preserve manual placement, previews and original files.

- Guide ASTAP with valid image position and equipment metadata before falling back to a full-sky search. Use a larger star set for the quick nearby attempt, retain one overall time limit and preserve original files.

- Make Selected image collapsible, with an always-visible image name and right-hand chevron. Start collapsed, return space to the collection when closed, and restore the adjustable split when expanded.

- Add an optional illustrated Moon overlay at its calculated J2000 position, with observer parallax, a Sun-oriented phase graphic, illumination readout, hover details and Go to Moon. Follow scene time/location or live UTC in atlas mode; retain the choice in Options → Sky and include it in sky exports.

- Keep compatible cached previews across unrelated Core rebuilds, output paths and Debug/Release/publish configurations by using an explicit processing revision and stable dependency identities. Preserve source/settings validation; old build-specific entries need one initial refresh. Count cached images accurately when refreshing an already loaded folder.

- Add camera framing and mosaics to Sky tools, with rotatable native fields, 1–10 rows/columns, overlap, numbered panel outlines, spherical panel coordinates and CSV export.
- Include 29 manufacturer-sourced smart-telescope configurations across ZWO, DWARFLAB, Vaonis, Unistellar and Celestron, plus a sensor/focal-length calculator and saved custom profiles.
- Connect mosaic panels to the target visibility planner; clear stale reports and cancel calculations when planning inputs change.
- Produce only the compact Windows application ZIP, retaining source/compliance assets and checksums. Remove workspace recovery (#8) from the roadmap.

## 0.9.0 - 2026-10-20

- Enable Compare only for a valid overlapping selection, updating immediately after selection or placement changes and explaining disabled states in its tooltip.
- Open image comparisons in Swipe divider mode, require overlapping sky coverage, frame both images through their shared field, and add Fit both while preventing self-comparison.
- Clear image editing selection and collapse its controls when navigating to sky targets or working with measurements; support deselection with empty-sky clicks or Escape while retaining image visibility.
- Replace image-list visibility checkboxes with eye and crossed-out-eye buttons, with action-specific tooltips and accessible labels.
- Centre the 3D reference plane, scale rings and guide lines on the focused object, with matching north/south colours and an updated origin caption.
- Cap the Objects in 3D zoom-out scale at a 13.8-billion-light-year vertical focus span across wheel/keyboard zoom, presets and framing, with feedback at the limit.
- Combine sky-point measurements and catalogue target distances in one Measurements tab, retaining synchronized distances, source details and 3D exploration.
- Remove bookmarks from Sky tools and atlas overlays, retaining a dedicated Measurements tab and existing library data.
- Show displayed RGB as #RRGGBB alongside separately labelled original scientific samples in full-resolution inspection.
- Give full-resolution inspection the full image width by removing its histogram and statistics sidebar; retain sample and coordinate readouts below the image.
- Accept valid zenithal LATPOLE preferences, including zero in Draco TAN-SIP FITS exports, so their embedded plate solutions place images automatically in the sky view while retaining SIP distortion.

## 0.8.0 - 2026-10-17

- Refactor desktop workflows into feature-specific files, separate sky drawing from control input, and move 3D selection/measurement/navigation rules into a WPF-independent scene model with direct state-transition tests. Preserve existing UI behaviour and scientific/image-processing contracts.

## 0.7.0 - 2026-10-17

- Add a compact self-contained Windows release ZIP with three files: the executable, PDF manual and a companion archive preserving all licences, notices and catalogue data. Validate both package formats during CI and generate checksums for both release downloads.

## 0.6.0 - 2026-10-17

- Measure between two objects directly in the 3D map with Shift-click or a second-target selection mode, a labelled connecting line, source details, Frame pair and Clear measurement controls.
- Centre 3D navigation immediately on a selected object, retain that centre when changing scale, and add explicit Recenter on target and Reset to Sun actions.
- Add an offline 3D object map centred on the Solar System, with linear light-year positions, perspective orbit/pan/zoom, keyboard travel, scale presets, catalogue filters, searchable object focus and source details.
- Expand offline distances to 3,283 unique objects across Messier (107/110), Caldwell (109/109), NGC (2,554/8,098) and IC (824/5,505), with NASA/CDS provenance, literature references, methods and reported object errors.
- Fix jagged artifacts where restored DSS2 regions overlap by compositing survey viewports with separate depth buffers and a shared camera.
- Add a saved option to restore all cached DSS2 regions behind photographs on startup, without downloads or changing the camera.
- Extend two-point measurements with optional object distances in light-years and a straight-line 3D separation in the atlas and sky exports.
- Add Sky tools with opt-in CDS DSS2 colour/red HiPS cutouts, a bounded offline survey cache, catalogue positions and retained survey credits in atlas/export views.
- Add 24-hour UTC target visibility charts, geometric rise/set/transit, Moon phase/separation, darkness and configurable imaging windows, including polar/circumpolar cases.
- Save named sky bookmarks and notes in the durable library; add great-circle measurements, angular scale bars and celestial north/east indicators.
- Recognize the solved zenithal LATPOLE metadata written by WCSLIB/CDS while retaining coordinate-frame and pole-orientation validation.
- Prevent vertical black-column artifacts during slow swipe-divider movement by compositing full-view 2D frames instead of clipping live 3D meshes.
- Add aligned preview blinking, a draggable swipe divider and linked side-by-side sky views, with independent comparison snapshots and shared coordinate navigation.
- Add collection thumbnails, sorting, field-specific metadata filters, tags, favorites and groups retained in the durable library.
- Add multiple selection and batch visibility, opacity, organization and display changes with progress, cancellation and per-image failure reporting.
- Extend the manual with comparison and collection workflows and anonymous demonstration screenshots.

## 0.5.0 - 2026-10-17

- Evaluate FITS SIP and TPV distortion, additional zenithal projections, swapped celestial axes and legacy CROTA rotation; retain exact FITS forward models in cached previews.
- Recognize legacy linear PixInsight astrometry, keeping native Revision 1 precedence and explicit warnings for undecoded legacy spline models.
- Add PNG/JPEG sky-view and selected-preview export with resolution, label, outline, coordinate-grid and caption choices; preserve source files, sidecars and live scene state.
- Verify coordinates against synthetic Astropy/WCSLIB references and refresh the illustrated manual.

## 0.4.0 - 2026-10-17

- Double-click sky images to inspect them internally; move opening originals in the default Windows application to a small corner button on the hovered or selected image.
- Add internal full-resolution inspection with fit/100% zoom, panning, pixel-center sky coordinates and exact scientific sample readouts.
- Show per-channel original-sample histograms, finite/invalid counts, minimum, maximum, mean and population standard deviation; keep alpha and Bayer mosaic measurements separate from display processing.
- Load inspection data on demand with cancellation, source/sidecar validation and source-bound ASTAP coordinates, preserving original files and atlas previews.

## 0.3.0 - 2026-10-16

- Add a persistent folder-based image library with staged additions, multi-selection removal, clearing, cancellation and recursive loading on confirmation.
- Automatically reopen saved folders at startup, using valid cached previews first and loading new, changed or uncached images from their source files.
- Remove session-file opening and saving in favor of saved folders, cached per-image display choices and startup defaults.
- Add configurable ASTAP blind solving with cancellation, time limits, scientific temporary inputs and persistent source-validated placements.
- Show fitted field size and source pixel scale, and handle ASTAP's initial-scale advisory after validating the fitted solution.
- Save solved scientific copies as FITS or XISF with embedded astrometry while preserving original files and source sample precision.
- Automatically debayer recognized XISF/FITS Bayer metadata into color previews before display processing; preserve original files, metadata and sky placement.
- Respect Bayer offsets and explicit FITS row order; keep unsupported masks grayscale with an explanation.
- Update the illustrated manual and record future work in `docs/roadmap.md`.

## 0.2.0 - 2026-10-16

- Display the application version beside the SkyBrowser name and in the window title.
- Show the full build identity, including the Git commit when available, in the version tooltip.
- Keep assembly metadata, Windows file versions and release tags consistent with `Version.props`.
- Reject mismatched release tags and incorrectly versioned application packages.

## 0.1.1 - 2026-10-16

- Preserve the original LZ4 codec's BSD attribution alongside the K4os wrapper's MIT notice.
- Require that additional notice in validated application packages. No image-processing behavior changed.

## 0.1.0 - 2026-10-16

Initial public release of the Windows x64 sky atlas.

- Display FITS/XISF photographs using retained astrometry or manual placement.
- Explore offline stars, constellations and deep-sky targets; blend overlapping fields.
- Choose automatic stretching or explicit dark-sky cleanup while preserving originals.
- Save sessions, cache previews, and optionally explore an observer's location/time.
- Include the illustrated manual and complete redistribution notices.

Known limits include no built-in plate solving/debayering, full source decoding
before preview generation, and the projection/frame limits documented in the manual.
The executable is unsigned. Review release notes and checksums before running it;
do not disable Windows security features globally.
