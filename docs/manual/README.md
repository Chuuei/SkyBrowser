# Maintaining the user manual

The manual's text is in [user-manual.md](../user-manual.md). The generated
[SkyBrowser-User-Manual.pdf](../SkyBrowser-User-Manual.pdf) is checked in with the
source so normal .NET publishing needs no Python installation. The desktop project
copies it to the publish root and excludes it from the single-file bundle, leaving
an accessible PDF next to SkyBrowser.exe.

## Rebuild and review

Use Python 3.12 or newer on Windows:

Initialize the pinned library with `./tools/Initialize-Dependencies.ps1` first.
Photographs are external inputs, not repository assets. Screenshot regeneration
requires a wider plate-solved Horsehead `.fits` (filename includes `horsehead`) and
a smaller overlapping IC434 `.xisf` (filename includes `434`). Pass their directory
after `--`, or set `SKYBROWSER_SOLVED_SAMPLES`. With missing inputs the helper exits
with a clear error before opening a window or changing approved screenshots.
The committed screenshots/PDF allow normal builds and publishing without these inputs.

```powershell
dotnet run --project tools/ManualScreenshots/SkyBrowser.ManualScreenshots.csproj -c Release
python -m venv artifacts/manual-tools
artifacts/manual-tools/Scripts/python.exe -m pip install -r tools/manual-requirements.txt
artifacts/manual-tools/Scripts/python.exe tools/Build-UserManual.py
```

For external samples, use a neutral demonstration path, for example:

```powershell
dotnet run --project tools/ManualScreenshots/SkyBrowser.ManualScreenshots.csproj -c Release -- C:\SkyBrowserDemo\Samples
```

To refresh Image details, inspection, its sky corner action and export illustrations, pass one supported
source image; this does not require a solved overlapping pair or alter the other
approved screenshots:

```powershell
dotnet run --project tools/ManualScreenshots/SkyBrowser.ManualScreenshots.csproj -c Release -- --inspection-only C:\SkyBrowserDemo\Samples\example.xisf
```

This mode retains the other manifest entries and refreshes these five illustrations.
Inspect all five complete PNGs before rebuilding the PDF. The inspection
example writes a separate anonymous UInt8 image under `artifacts/` from display
pixels, with fixed manual demonstration coordinates; its readouts never expose
the original photograph's scientific samples or metadata. Its source path is not
displayed. The details window still uses neutral paths, facts and metadata.

The builder uses ReportLab and embeds the Windows Segoe UI fonts. Use
`--font-directory` if they are installed elsewhere. It renders every page through
PDFium into `artifacts/manual-preview` and writes `validation.json` with chapter
pages, text lengths and layout positions. Review every rendered page before
publishing. It fails if a chapter overflows its allotted page or if a page is empty.
If you add or reorder chapters, also update the contents table and page references.
The builder checks the screenshot manifest and rejects changed or unreviewed images,
text containing the current account name or a Windows profile path, unexpected PNG
metadata, and PDF attachments. Its PDF timestamps and metadata are deterministic,
so they do not identify the authoring computer or its time zone.

Publish normally after rebuilding the PDF:

```powershell
dotnet publish src/SkyBrowser.Desktop -c Release -p:PublishProfile=WinX64 --artifacts-path artifacts
```

## Source format and screenshots

Refresh the survey, planning and annotation illustrations independently:

```powershell
dotnet run --project tools/ManualScreenshots/SkyBrowser.ManualScreenshots.csproj -c Release -- --sky-tools-only
```

This mode needs no private sample images. It requests one public M31 DSS2 cutout
from CDS (or reuses its local survey cache), retains provider credits, and uses a
separate demonstration library under `artifacts/`. All planning locations are 0/0
and planning dates are fixed to 1 January 2000. The target-distance illustration
uses the bundled, public M81/M82 distance estimates and shows their source snapshot
date. The same mode captures the offline 3D map as `distance-space.png`, using only
bundled public coordinates/distances. It also captures the public M81/M82 pair as
`distance-space-pair.png`. Inspect all seven complete PNGs before rebuilding the PDF.
It merges their hashes into the existing manifest.

Refresh comparison and batch-organization illustrations while retaining the other
approved figures with:

```powershell
dotnet run --project tools/ManualScreenshots/SkyBrowser.ManualScreenshots.csproj -c Release -- --collection-only C:\SkyBrowserDemo\Samples
```

This mode compares two display-brightness variants of one anonymous preview with
identical geometry. Missing astrometry receives fixed demonstration coordinates;
source files are untouched. Inspect all three complete new PNGs before rebuilding.

The builder supports the subset used by this manual: headings, paragraphs, bold
text, lists, tables, callouts and local images. A horizontal rule starts a new page.
The small HTML comments before images specify PDF-only clipping, height and numbered
tour markers. A `pair` comment places two figures side by side; `workflow` adds the
automatic startup diagram. These comments do not alter the original screenshot files.

The screenshots show real WPF windows populated by the anonymous demonstration model
in `tools/ManualScreenshots`. The helper reads only preview pixels and sky geometry
from the example photographs. It replaces filenames, source paths, descriptions,
metadata, file facts and cache statistics before they reach a window. It does not
read the user's saved options or observing location. All paths use `C:\SkyBrowserDemo`;
the observer example uses latitude 0, longitude 0 and a fixed UTC time in the year
2000. Timeline examples also replace the computer-local clock readouts with UTC.
It writes the SHA-256 manifest after capturing all images.

The library screenshot uses explicit `LibraryFolder` demonstration rows and a
separate empty test database, never a user's archive. ASTAP options use the neutral
example executable path `C:\Program Files\ASTAP\astap.exe`; no solver is run for
screenshots. Do not substitute actual library paths or index records.

Do not copy raw desktop-test screenshots into this directory. Cropping or drawing a
box over a screenshot in the PDF would leave its original pixels recoverable from
the embedded image. The complete source PNGs must be anonymous before PDF generation.

When refreshing screenshots, run the helper from the repository root, then inspect
every complete PNG as well as the rendered PDF. Keep the current control labels and
complete action buttons visible. The PDF builder handles clipping and scaling.
The image and cache defaults should show Automatic and 10 GB so the beginner's
instructions agree with the figures.
