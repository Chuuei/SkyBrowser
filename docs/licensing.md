# Licensing and redistribution

SkyBrowser-authored code is [Apache-2.0](../LICENSE), with attribution in
[NOTICE](../NOTICE). It allows commercial use and modification. Preserve relevant
copyright/license notices and the attribution notice when redistributing derivative
works; no on-screen badge is required. Changes to upstream-authored files must be
identified as required by the license. XISFLib.Net is independently Apache-2.0.

## Third-party boundaries

| Component | License / redistribution handling |
| --- | --- |
| Astronomy Engine | MIT; full copyright/permission text in `Data/LICENSE-AstronomyEngine.txt` |
| d3-celestial sky data | BSD-3-Clause; retain its notice and catalogue provenance |
| OpenNGC-derived catalogue | CC-BY-SA-4.0; attribution, full license and adapted JSON remain supplied separately |
| Natural Earth map | Public domain; provenance retained |
| XISFLib.Net | Apache-2.0; its LICENSE, NOTICE and third-party guide are supplied under `Data/` |
| K4os LZ4, ZstdSharp | MIT; full texts retained, plus original LZ4 BSD-2-Clause and Zstandard BSD-3-Clause notices |
| Microsoft.Data.Sqlite, System.Security.Cryptography, .NET/Windows Desktop | MIT and bundled third-party notices; preserve package/runtime notice texts |
| SQLitePCLRaw | Apache-2.0; native SQLite is public domain |
| Magick.NET | Apache-2.0 for the managed wrapper; full upstream multi-license `Notice.txt` supplied |
| ImageMagick/native delegates | ImageMagick license and multiple delegate licenses, including LGPL/source-availability terms; matching source/build bundle supplied with releases |

The application license does not relicense these components. The catalogue is a
separate dataset aggregated with the program: adaptations of that dataset must
retain CC-BY-SA-4.0, attribution and change descriptions. Keep its JSON and license
files accessible in distributions. External photographs are not project code and
are neither relicensed nor bundled.

## Native redistribution

Magick.NET's NuGet license expression describes its wrapper, not every native
delegate. The complete upstream notice identifies those delegates. Releases include
`XISFLib.NativeSources-Magick.NET-15.0.0.zip` containing exact, SHA-256-locked source
archives, native build scripts and a rebuild/relink guide, available from the same
release as the executable. Application sources include the pinned library source.
Retain these source assets alongside any redistributed binary, and supply modified
native sources if you change them. Read the included `REBUILD.md` for replacing
the native library and republishing an unpacked application.

The existing .NET-managed code, MIT/BSD/Apache dependencies and the separate
CC-BY-SA dataset can be distributed with these preserved terms. Native copyleft
requirements remain applicable to their components; they are not waived by an
Apache license at the repository root. Dependency updates require a new review.

## Release checks

`tools/Build-Release.ps1` verifies the full package, current package inventory,
license/NOTICE files, PDF, catalogue attribution, absence of scientific images and
debug/XML files, and the native-source lock. It generates SHA-256 checksums and an
application source ZIP that includes the submodule's actual tracked files.
Do not use GitHub's automatic source ZIP as the only application source bundle:
it omits Git submodule contents.

License references: [Apache redistribution](https://www.apache.org/licenses/LICENSE-2.0),
[GNU LGPL source/relinking guidance](https://www.gnu.org/licenses/gpl-faq.html#LGPLStaticVsDynamic),
and [Creative Commons BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/).
