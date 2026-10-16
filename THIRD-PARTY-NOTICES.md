# Third-party notices

## Observer orientation and offline map

Observer rotations use [Astronomy Engine](https://github.com/cosinekitty/astronomy), **CosineKitty.AstronomyEngine 2.1.19**, by Don Cross, under the MIT license. The library converts between J2000 equatorial coordinates and the observer's horizontal frame, including precession, nutation and Earth rotation. Its license is included as `Data/LICENSE-AstronomyEngine.txt`.

The embedded `world-countries.geojson` is the unmodified [Natural Earth 1:110m country dataset](https://github.com/nvkelso/natural-earth-vector/blob/master/geojson/ne_110m_admin_0_countries.geojson), downloaded on 2026-10-15. Natural Earth map data are public domain. Credits and terms are included as `Data/LICENSE-Natural-Earth.txt`. Map boundaries are for orientation and do not determine observer time zones.

## Offline sky background

The embedded, unmodified `stars.6.json`, `constellations.lines.json` and `constellations.json` files are from [d3-celestial](https://github.com/ofrohn/d3-celestial), by Olaf Frohn. Downloaded from its `master` branch on 2026-10-15. They contain 5,044 stars and constellation line/name features. RA is stored as GeoJSON longitude (−180° to +180°); SkyBrowser normalizes it to 0° to 360° when reading.

Upstream identifies the bright-star data as Hipparcos (ESA, 1997) and constellation information as IAU names with positions and line modifications by Olaf Frohn. Positions are at epoch J2000. Source catalogue provenance is documented in the [upstream README](https://github.com/ofrohn/d3-celestial#data).

The upstream BSD-3-Clause license is reproduced in [LICENSE-d3-celestial.txt](src/SkyBrowser.Core/Data/LICENSE-d3-celestial.txt). Its full text is also copied into build/publish outputs.

## Deep-sky target catalogue

`Data/deep-sky.json` is adapted from [OpenNGC](https://github.com/mattiaverga/OpenNGC), by Mattia Verga and contributors Patrick Chevalley, Christian Dersch, Jean-Sebastien Dieu and Sebastian Godelet. It uses `NGC.csv` and `addendum.csv` at commit [`4e2398a263de663d83f7b86be80c368c34335764`](https://github.com/mattiaverga/OpenNGC/tree/4e2398a263de663d83f7b86be80c368c34335764), downloaded on 2026-10-15. OpenNGC data and this adapted dataset are licensed under **Creative Commons Attribution-ShareAlike 4.0 International**. This data license applies to the catalogue, independently of the application code.

Changes include selecting fields, converting J2000 sexagesimal coordinates to degrees, omitting entries without positions, merging duplicate identifiers, and adding Caldwell cross-identifications from the [Astronomical League object list](https://www.astroleague.org/caldwell-program-object-list/). M102 is associated with NGC5866 and named Spindle Galaxy, following [NASA's Hubble Messier catalogue](https://science.nasa.gov/mission/hubble/science/explore-the-night-sky/hubble-messier-catalog/messier-102/), with a note retaining the historical identification dispute. IC434 is named Horsehead background nebula, reflecting [NASA's description of the Horsehead and its background](https://science.nasa.gov/asset/hubble/the-horsehead-nebula/). NGC253 uses Sculptor Galaxy and Silver Dollar Galaxy as common names. C33 and C49 identify representative nebular components, NGC6992 and NGC2237 respectively.

The full license is reproduced in [LICENSE-OpenNGC.txt](src/SkyBrowser.Core/Data/LICENSE-OpenNGC.txt); attribution, source references and upstream authorship are retained in [LICENSE-OpenNGC-ATTRIBUTION.txt](src/SkyBrowser.Core/Data/LICENSE-OpenNGC-ATTRIBUTION.txt). The adapted JSON and both notice files are copied into build/publish outputs. `tools/Build-TargetCatalog.ps1` and `tools/caldwell-identifiers.json` reproduce the adaptation from the pinned source snapshot.

## XISFLib.Net

SkyBrowser references the user's separate [XISFLib.Net checkout](../XISFLib.Net) for scientific image reading, rendering, stretching and astrometry. Its transitive packages include K4os.Compression.LZ4, ZstdSharp.Port, and System.Security.Cryptography.Xml. Their licenses and redistribution conditions remain applicable when distributing the program. No astronomy images from that checkout are bundled with SkyBrowser.

## Image finishing

`SkyImageFinisher` adapts the dark-background stretch, clipped broad-gradient fit, sample normalization and cosmetic filter choices from the user's `XISFLib.Viewer.Core/ViewerFinishService` implementation. It preserves the full frame for astrometric overlays and processes bounded display previews. It does not modify the separate library checkout or reference the Avalonia UI.

Cosmetic filters use **Magick.NET-Q16-AnyCPU 15.0.0**, the same version as the Avalonia viewer. The package's complete notices, covering Magick.NET, ImageMagick and bundled components, are copied into the output as `Data/Magick.NET-Notice.txt`.

## Persistent preview database

The cache uses **Microsoft.Data.Sqlite 10.0.13** and its Core package (MIT, .NET Foundation and Contributors), together with **SQLitePCLRaw 2.1.13** (Apache-2.0, Copyright 2014–2024 SourceGear, LLC). Their license texts are included in the output as `Data/LICENSE-Microsoft.Data.Sqlite.txt` and `Data/LICENSE-SQLitePCLRaw.txt`.

The SQLitePCLRaw bundle supplies the native SQLite runtime. SQLite's deliverable code is dedicated to the public domain; see the [SQLite copyright statement](https://www.sqlite.org/copyright.html). The database is created locally at runtime and contains user-generated previews; no example photographs are bundled.
