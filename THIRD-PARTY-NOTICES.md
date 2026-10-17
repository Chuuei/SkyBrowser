# Third-party notices

## Observer orientation and offline map

Observer rotations and visibility planning use [Astronomy Engine](https://github.com/cosinekitty/astronomy), **CosineKitty.AstronomyEngine 2.1.19**, by Don Cross, under the MIT license. It supplies horizon rotations and Sun/Moon ephemerides, including precession, nutation and Earth rotation. Its license is included as `Data/LICENSE-AstronomyEngine.txt`.

## Optional DSS survey backgrounds

Sky tools requests FITS cutouts from the public [CDS HiPS2FITS service](https://alasky.cds.unistra.fr/hips-image-services/hips2fits). DSS imagery is credited to STScI/NASA; colour composition and HEALPix products are by CDS, CNRS/Unistra. The provider identifies its HiPS products as ODbL-1.0. Downloaded survey imagery retains these terms and is independent of the application's license. Raw survey photographs are not bundled with the application; manual figures include public M31 cutouts with retained attribution.

Complete provider acknowledgments, source-property URLs and data-license references are in [LICENSE-DSS-SURVEYS.txt](src/SkyBrowser.Core/Data/LICENSE-DSS-SURVEYS.txt), copied into `Data/` on build/publish. Sky backgrounds and sky exports retain visible survey attribution even when catalogue labels are disabled.

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

SkyBrowser pins [XISFLib.Net](https://github.com/Chuuei/XISFLib.Net) as the source submodule at `dependencies/XISFLib.Net`. Authored library code is Apache-2.0. Its LICENSE, NOTICE and redistribution guide are copied to `Data/XISFLib-*`. Its compression and cryptography dependencies retain their own terms; no library example photographs are bundled.

## Image finishing

`SkyImageFinisher` adapts the dark-background stretch, clipped broad-gradient fit, sample normalization and cosmetic filter choices from the user's `XISFLib.Viewer.Core/ViewerFinishService` implementation. It preserves the full frame for astrometric overlays and processes bounded display previews. It does not modify the separate library checkout or reference the Avalonia UI.

`SkyImageDebayer` adapts bilinear Bayer interpolation from `XISFLib.Viewer.Core/ViewerDebayerService`, Copyright 2026 Chuuei and contributors, Apache-2.0. Changes add automatic preview detection, offset aliases, explicit row-order handling, parallel rows and linked grayscale display functions while retaining stored coordinates.

Cosmetic filters use **Magick.NET-Q16-AnyCPU 15.0.0**, the same version as the Avalonia viewer. The package's complete notices, covering Magick.NET, ImageMagick and bundled components, are copied into the output as `Data/Magick.NET-Notice.txt`.

## Persistent preview database

The cache uses **Microsoft.Data.Sqlite 10.0.13** and its Core package (MIT, .NET Foundation and Contributors), together with **SQLitePCLRaw 2.1.13** (Apache-2.0, Copyright 2014–2024 SourceGear, LLC). Their license texts are included in the output as `Data/LICENSE-Microsoft.Data.Sqlite.txt` and `Data/LICENSE-SQLitePCLRaw.txt`.

The SQLitePCLRaw bundle supplies the native SQLite runtime. SQLite's deliverable code is dedicated to the public domain; see the [SQLite copyright statement](https://www.sqlite.org/copyright.html). The database is created locally at runtime and contains user-generated previews; no example photographs are bundled.

## Application license and complete distribution

The bundled `target-distances.json` contains 3,283 numerical estimates cross-identified
through the position catalogue. Sources include NASA/Hubble's
[Messier](https://science.nasa.gov/mission/hubble/science/explore-the-night-sky/hubble-messier-catalog/)
and [Caldwell](https://science.nasa.gov/mission/hubble/science/explore-the-night-sky/hubble-caldwell-catalog/)
catalogues, three reviewed NASA/APOD facts and published literature distances from
[SIMBAD / CDS](https://simbad.cds.unistra.fr/simbad/). Each estimate retains a source
URL, original value/units, date and source hash; SIMBAD records also retain their
method, literature bibcode and reported errors where available. No website images
or article prose are bundled. Provenance and use notes are supplied as
`Data/LICENSE-NASA-DISTANCES.txt` and `Data/LICENSE-SIMBAD-DISTANCES.txt`. This product
has made use of the SIMBAD database, operated at CDS, Strasbourg, France.

SkyBrowser-authored code is Apache-2.0. Preserve the relevant attribution in
`NOTICE`. `SkyImageFinisher` identifies its adaptation from XISFLib.Net and its
full-frame/bounded-preview changes. Third-party code and datasets retain their
own licenses; the application license does not relicense them.

K4os.Compression.LZ4 1.3.8 (Copyright 2017 Milosz Krajewski) and ZstdSharp.Port 0.8.8
(Copyright 2021 Oleg Stepanischev) are MIT. Their full texts are supplied as
`Data/LICENSE-K4os.Compression.LZ4.txt` and `Data/LICENSE-ZstdSharp.txt`, together with
`Data/LICENSE-Zstandard.txt` for the original BSD-3-Clause Zstandard code.
The original LZ4 codec attribution (Copyright 2011-2020 Yann Collet, BSD-2-Clause)
is retained in `Data/LICENSE-LZ4.txt` alongside the K4os wrapper's MIT notice.
Microsoft cryptography/runtime components are MIT, with additional runtime notices
in `Data/DotNet-THIRD-PARTY-NOTICES.txt` and
`Data/WindowsDesktop-THIRD-PARTY-NOTICES.txt`.

The managed Magick.NET wrapper's Apache-2.0 license does not cover every native
ImageMagick delegate. Preserve the complete `Data/Magick.NET-Notice.txt`, including
its native license texts and copyrights. Releases additionally supply the exact
Magick.NET 15.0.0 native sources, dependency sources and rebuild materials in
`XISFLib.NativeSources-Magick.NET-15.0.0.zip`, at the same release location as the
binary. Read [licensing and source/relinking instructions](docs/licensing.md).
