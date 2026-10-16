# Changelog

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
