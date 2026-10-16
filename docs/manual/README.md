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

The builder supports the subset used by this manual: headings, paragraphs, bold
text, lists, tables, callouts and local images. A horizontal rule starts a new page.
The small HTML comments before images specify PDF-only clipping, height and numbered
tour markers. A `pair` comment places two figures side by side; `workflow` adds the
session diagram. These comments do not alter the original screenshot files.

The screenshots show real WPF windows populated by the anonymous demonstration model
in `tools/ManualScreenshots`. The helper reads only preview pixels and sky geometry
from the example photographs. It replaces filenames, source paths, descriptions,
metadata, file facts and cache statistics before they reach a window. It does not
read the user's saved options or observing location. All paths use `C:\SkyBrowserDemo`;
the observer example uses latitude 0, longitude 0 and a fixed UTC time in the year
2000. Timeline examples also replace the computer-local clock readouts with UTC.
It writes the SHA-256 manifest after capturing all images.

Do not copy raw desktop-test screenshots into this directory. Cropping or drawing a
box over a screenshot in the PDF would leave its original pixels recoverable from
the embedded image. The complete source PNGs must be anonymous before PDF generation.

When refreshing screenshots, run the helper from the repository root, then inspect
every complete PNG as well as the rendered PDF. Keep the current control labels and
complete action buttons visible. The PDF builder handles clipping and scaling.
The image and cache defaults should show Automatic and 10 GB so the beginner's
instructions agree with the figures.
