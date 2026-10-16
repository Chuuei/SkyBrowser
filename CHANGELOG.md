# Changelog

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
