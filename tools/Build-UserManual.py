"""Build the illustrated manual from Markdown and render every PDF page for review.

The checked-in PDF is published as content, so normal .NET publishing does not need
Python. Install tools/manual-requirements.txt only when regenerating the manual.
"""

from __future__ import annotations

import argparse
from html import escape
import hashlib
import json
import os
from pathlib import Path
import re
import shlex

from PIL import Image as PillowImage
from pypdf import PdfReader
import pypdfium2
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    BaseDocTemplate,
    Flowable,
    Frame,
    KeepTogether,
    PageBreak,
    PageTemplate,
    Paragraph,
    Spacer,
    Table,
    TableStyle,
)


ROOT = Path(__file__).resolve().parents[1]
PAGE_WIDTH, PAGE_HEIGHT = A4
MARGIN = 42
WIDTH = PAGE_WIDTH - MARGIN * 2
INK = colors.HexColor("#17263A")
MUTED = colors.HexColor("#52647A")
ACCENT = colors.HexColor("#137A65")
MINT = colors.HexColor("#8BE2C3")
PALE = colors.HexColor("#EFF7F4")
RULE = colors.HexColor("#D8E2E8")


def check_private_text(text: str, label: str) -> None:
    """Reject host account names and profile paths without storing those values."""
    username = os.environ.get("USERNAME", "")
    ordinary_words = {"user", "admin", "administrator", "system", "root", "public", "default", "demo", "skybrowser"}
    profile_path = re.search(r"\b[A-Z]:[\\/]+Users[\\/]+", text, re.IGNORECASE)
    account_name = len(username) >= 3 and username.casefold() not in ordinary_words and re.search(
        rf"(?<!\w){re.escape(username)}(?!\w)", text, re.IGNORECASE
    )
    if profile_path or account_name:
        raise SystemExit(f"Personal account information found in {label}. Use anonymous examples.")


def validate_screenshots(source: Path) -> None:
    """Only embed reviewed, anonymous captures, including their off-crop pixels."""
    directory = ROOT / "docs" / "manual" / "screenshots"
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    if manifest.get("Version") != 1 or manifest.get("Dataset") != "anonymous-demonstration":
        raise SystemExit("Regenerate screenshots with tools/ManualScreenshots before building the manual.")
    observer = manifest.get("Observer", {})
    if (observer.get("Latitude"), observer.get("Longitude")) != (0, 0):
        raise SystemExit("Manual screenshots must use the neutral demonstration location.")
    if observer.get("TimeUtc") != "2000-01-01T12:00:00+00:00":
        raise SystemExit("Manual screenshots must use the fixed UTC demonstration time.")
    files = manifest["Files"]
    if set(files) != {path.name for path in directory.glob("*.png")}:
        raise SystemExit("Unreviewed screenshots found in the manual assets.")
    for filename, expected in files.items():
        if Path(filename).name != filename:
            raise SystemExit("Screenshot manifest contains an invalid filename.")
        image_path = directory / filename
        if hashlib.sha256(image_path.read_bytes()).hexdigest().upper() != expected:
            raise SystemExit(f"Unreviewed screenshot change: {filename}. Regenerate anonymous captures.")
        with PillowImage.open(image_path) as picture:
            if set(picture.info) - {"dpi", "gamma", "srgb", "chromaticity", "transparency"}:
                raise SystemExit(f"Unexpected screenshot metadata in {filename}.")
    for path in (source, ROOT / "docs" / "manual" / "README.md", directory / "manifest.json"):
        check_private_text(path.read_text(encoding="utf-8"), path.relative_to(ROOT).as_posix())


def register_fonts(font_directory: Path) -> None:
    """Embed fonts so the PDF remains readable on another computer."""
    for name, filename in (
        ("Manual", "segoeui.ttf"),
        ("Manual-Bold", "segoeuib.ttf"),
        ("Manual-Italic", "segoeuii.ttf"),
    ):
        pdfmetrics.registerFont(TTFont(name, str(font_directory / filename)))
    pdfmetrics.registerFontFamily(
        "Manual",
        normal="Manual",
        bold="Manual-Bold",
        italic="Manual-Italic",
        boldItalic="Manual-Bold",
    )


def make_styles() -> dict[str, ParagraphStyle]:
    base = getSampleStyleSheet()["BodyText"]
    body = ParagraphStyle(
        "ManualBody", parent=base, fontName="Manual", fontSize=10.3,
        leading=14.5, textColor=INK, spaceAfter=8,
    )
    return {
        "body": body,
        "cover": ParagraphStyle(
            "CoverTitle", parent=body, fontName="Manual-Bold", fontSize=35,
            leading=42, spaceBefore=2, spaceAfter=4,
        ),
        "subtitle": ParagraphStyle(
            "CoverSubtitle", parent=body, fontSize=19, leading=26,
            textColor=ACCENT, spaceAfter=13,
        ),
        "title": ParagraphStyle(
            "ChapterTitle", parent=body, fontName="Manual-Bold", fontSize=24,
            leading=31, spaceBefore=0, spaceAfter=13,
        ),
        "heading": ParagraphStyle(
            "ManualHeading", parent=body, fontName="Manual-Bold", fontSize=13,
            leading=18, textColor=ACCENT, spaceBefore=7, spaceAfter=6,
            keepWithNext=True,
        ),
        "list": ParagraphStyle(
            "ManualList", parent=body, leftIndent=16, firstLineIndent=0,
            bulletIndent=1, bulletFontName="Manual-Bold", bulletFontSize=10,
            spaceAfter=5,
        ),
        "caption": ParagraphStyle(
            "ManualCaption", parent=body, fontSize=8.6, leading=11.8,
            textColor=MUTED, spaceBefore=5, spaceAfter=9,
        ),
        "cell": ParagraphStyle(
            "ManualCell", parent=body, fontSize=9.5, leading=13,
            spaceAfter=0,
        ),
        "header": ParagraphStyle(
            "ManualTableHeader", parent=body, fontName="Manual-Bold",
            fontSize=9.5, leading=13, textColor=colors.white, spaceAfter=0,
        ),
        "tip": ParagraphStyle(
            "ManualTip", parent=body, fontSize=9.8, leading=13.8, spaceAfter=0,
        ),
    }


def inline(text: str) -> str:
    text = escape(text, quote=False)
    return re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", text)


class Screenshot(Flowable):
    """Clip a screenshot on the PDF canvas; the original PNG stays untouched."""

    def __init__(self, path: Path, settings: dict[str, str], max_width: float = WIDTH):
        super().__init__()
        self.path = path
        with PillowImage.open(path) as picture:
            self.original_width, self.original_height = picture.size
        self.crop = tuple(map(float, settings.get(
            "crop", f"0,0,{self.original_width},{self.original_height}"
        ).split(",")))
        left, top, right, bottom = self.crop
        if not (0 <= left < right <= self.original_width and
                0 <= top < bottom <= self.original_height):
            raise ValueError(f"Invalid screenshot crop for {path.name}: {self.crop}")
        self.scale = min(max_width / (right - left), float(settings.get("height", 300)) / (bottom - top))
        self.width = (right - left) * self.scale
        self.height = (bottom - top) * self.scale
        self.markers = settings.get("markers", "")
        self.hAlign = "CENTER"

    def draw(self) -> None:
        canvas = self.canv
        left, _, _, bottom = self.crop
        canvas.saveState()
        clipping = canvas.beginPath()
        clipping.rect(0, 0, self.width, self.height)
        canvas.clipPath(clipping, stroke=0, fill=0)
        canvas.drawImage(
            str(self.path), -left * self.scale,
            -(self.original_height - bottom) * self.scale,
            width=self.original_width * self.scale,
            height=self.original_height * self.scale, mask="auto",
        )
        canvas.restoreState()
        canvas.setStrokeColor(RULE)
        canvas.setLineWidth(0.5)
        canvas.rect(0, 0, self.width, self.height, stroke=1, fill=0)
        for marker in filter(None, self.markers.split(";")):
            label, x, y = marker.split(":")
            center_x = float(x) * self.width
            center_y = (1 - float(y)) * self.height
            canvas.setFillColor(MINT)
            canvas.setStrokeColor(INK)
            canvas.circle(center_x, center_y, 8.5, fill=1, stroke=1)
            canvas.setFillColor(INK)
            canvas.setFont("Manual-Bold", 9)
            canvas.drawCentredString(center_x, center_y - 3, label)


class StartupDiagram(Flowable):
    def __init__(self):
        super().__init__()
        self.width = WIDTH
        self.height = 86

    def draw(self) -> None:
        canvas = self.canv
        gap = 28
        card_width = (WIDTH - gap * 2) / 3
        for index, (title, detail) in enumerate((
            ("Saved folders", "Your image library"),
            ("Cached previews", "Read sources if needed"),
            ("Images in the sky", "Loaded on application start"),
        )):
            x = index * (card_width + gap)
            canvas.setFillColor(PALE)
            canvas.setStrokeColor(RULE)
            canvas.roundRect(x, 13, card_width, 60, 6, fill=1, stroke=1)
            canvas.setFillColor(INK)
            canvas.setFont("Manual-Bold", 10)
            canvas.drawCentredString(x + card_width / 2, 49, title)
            canvas.setFont("Manual", 8.8)
            canvas.drawCentredString(x + card_width / 2, 31, detail)
        canvas.setFillColor(ACCENT)
        canvas.setFont("Manual-Bold", 16)
        canvas.drawCentredString(card_width + gap / 2, 38, "→")
        start = 2 * card_width + gap + 5
        end = start + gap - 10
        canvas.setStrokeColor(ACCENT)
        canvas.setLineWidth(1.5)
        canvas.line(start, 43, end, 43)
        canvas.line(end - 4, 47, end, 43)
        canvas.line(end - 4, 39, end, 43)


class ManualDocument(BaseDocTemplate):
    def __init__(self, path: Path):
        super().__init__(
            str(path), pagesize=A4, leftMargin=MARGIN, rightMargin=MARGIN,
            topMargin=48, bottomMargin=54,
            title="SkyBrowser - Beginner's User Manual", author="SkyBrowser",
            subject="Getting started with the Windows desktop sky and image browser",
            creator="SkyBrowser manual generator", producer="SkyBrowser documentation",
            lang="en-GB", pageCompression=1, invariant=1,
        )
        self.section_pages: list[tuple[str, int]] = []
        self.layout: list[dict] = []
        frame = Frame(MARGIN, 54, WIDTH, PAGE_HEIGHT - 102, leftPadding=0,
                      rightPadding=0, topPadding=0, bottomPadding=0)
        self.addPageTemplates(PageTemplate(id="Manual", frames=frame, onPage=self.decorate))

    @staticmethod
    def decorate(canvas, document) -> None:
        canvas.saveState()
        canvas.bookmarkPage(f"page-{document.page}")
        if document.page > 1:
            canvas.setFont("Manual-Bold", 8)
            canvas.setFillColor(MUTED)
            canvas.drawString(MARGIN, PAGE_HEIGHT - 26, "SKYBROWSER  /  USER MANUAL")
            canvas.setStrokeColor(RULE)
            canvas.line(MARGIN, PAGE_HEIGHT - 34, PAGE_WIDTH - MARGIN, PAGE_HEIGHT - 34)
        canvas.setStrokeColor(RULE)
        canvas.line(MARGIN, 29, PAGE_WIDTH - MARGIN, 29)
        canvas.setFillColor(MUTED)
        canvas.setFont("Manual", 8)
        canvas.drawString(MARGIN, 16, "Windows desktop edition  |  October 2026")
        canvas.drawRightString(PAGE_WIDTH - MARGIN, 16, str(document.page))
        canvas.restoreState()

    def afterFlowable(self, flowable) -> None:
        if hasattr(self, "frame") and not isinstance(flowable, PageBreak):
            self.layout.append({
                "page": self.page, "type": type(flowable).__name__, "bottom": self.frame._y,
                "text": flowable.getPlainText()[:60] if isinstance(flowable, Paragraph) else "",
            })
        if isinstance(flowable, Paragraph) and flowable.style.name in ("CoverTitle", "ChapterTitle"):
            heading = flowable.getPlainText()
            key = f"section-{len(self.section_pages)}"
            self.canv.bookmarkPage(key)
            self.canv.addOutlineEntry(heading, key, level=0)
            self.section_pages.append((heading, self.page))


def figure(lines: list[str], index: int, styles, max_width=WIDTH):
    settings = {}
    if lines[index].startswith("<!-- figure "):
        settings = dict(item.split("=", 1) for item in shlex.split(lines[index][12:-4].strip()))
        index += 1
    match = re.fullmatch(r"!\[(.+)]\((.+)\)", lines[index])
    if match is None:
        raise ValueError(f"Expected screenshot at source line {index + 1}")
    caption, relative = match.groups()
    image = Screenshot(ROOT / "docs" / relative, settings, max_width)
    return [image, Paragraph(inline(caption), styles["caption"])], index + 1


def markdown_story(source: Path, styles) -> list[Flowable]:
    lines = source.read_text(encoding="utf-8").splitlines()
    story = []
    index = 0
    first_title = True
    while index < len(lines):
        line = lines[index].strip()
        if not line:
            index += 1
            continue
        if line == "---":
            story.append(PageBreak())
        elif line == "<!-- workflow -->":
            story.append(StartupDiagram())
        elif line == "<!-- pair -->":
            first, index = figure(lines, index + 1, styles, (WIDTH - 16) / 2)
            second, index = figure(lines, index, styles, (WIDTH - 16) / 2)
            table = Table([[first, second]], colWidths=[WIDTH / 2, WIDTH / 2])
            table.setStyle(TableStyle([
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (0, 0), 0),
                ("RIGHTPADDING", (-1, 0), (-1, 0), 0),
            ]))
            story.append(KeepTogether([table, Spacer(1, 7)]))
            continue
        elif line.startswith("<!-- figure ") or line.startswith("!["):
            content, index = figure(lines, index, styles)
            story.append(KeepTogether(content))
            continue
        elif line.startswith("# "):
            story.append(Paragraph(inline(line[2:]), styles["cover" if first_title else "title"]))
            first_title = False
        elif line.startswith("## "):
            is_subtitle = line[3:] == "Beginner's user manual"
            story.append(Paragraph(inline(line[3:]), styles["subtitle" if is_subtitle else "heading"]))
        elif line.startswith("|"):
            rows = []
            while index < len(lines) and lines[index].startswith("|"):
                cells = [cell.strip() for cell in lines[index].strip("|").split("|")]
                if not all(re.fullmatch(r":?-+:?", cell) for cell in cells):
                    rows.append(cells)
                index += 1
            widths = [WIDTH * fraction for fraction in ([0.40, 0.08, 0.44, 0.08] if len(rows[0]) == 4 else [0.32, 0.68])]
            contents = []
            for row_index, row in enumerate(rows):
                cells = []
                for value in row:
                    rendered = inline(value)
                    if len(row) == 4 and value.isdigit():
                        rendered = f'<link href="#page-{value}" color="#137A65">{value}</link>'
                    cells.append(Paragraph(rendered, styles["header" if row_index == 0 else "cell"]))
                contents.append(cells)
            table = Table(contents, colWidths=widths, repeatRows=1, hAlign="LEFT", spaceBefore=3, spaceAfter=10)
            table.setStyle(TableStyle([
                ("BACKGROUND", (0, 0), (-1, 0), INK),
                ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#F2F5F7")]),
                ("LINEBELOW", (0, 0), (-1, -1), 0.4, RULE),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (-1, -1), 8),
                ("RIGHTPADDING", (0, 0), (-1, -1), 8),
                ("TOPPADDING", (0, 0), (-1, -1), 6),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
            ]))
            story.append(table)
            continue
        elif line.startswith("> "):
            box = Table([[Paragraph(inline(line[2:]), styles["tip"])]], colWidths=[WIDTH], spaceBefore=3, spaceAfter=9)
            box.setStyle(TableStyle([
                ("BACKGROUND", (0, 0), (-1, -1), PALE),
                ("BOX", (0, 0), (-1, -1), 0.5, colors.HexColor("#BFDACF")),
                ("LEFTPADDING", (0, 0), (-1, -1), 10),
                ("RIGHTPADDING", (0, 0), (-1, -1), 10),
                ("TOPPADDING", (0, 0), (-1, -1), 8),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 8),
            ]))
            story.append(box)
        elif line.startswith("- ") or re.match(r"\d+\. ", line):
            match = re.match(r"(-|\d+\.) (.+)", line)
            bullet = "\u2022" if match[1] == "-" else match[1]
            story.append(Paragraph(inline(match[2]), styles["list"], bulletText=bullet))
        else:
            paragraph = [line]
            while index + 1 < len(lines) and lines[index + 1].strip() and not lines[index + 1].startswith(("#", "|", "<!--", "![", ">", "- ")):
                index += 1
                paragraph.append(lines[index].strip())
            story.append(Paragraph(inline(" ".join(paragraph)), styles["body"]))
        index += 1
    return story


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "docs" / "SkyBrowser-User-Manual.pdf")
    parser.add_argument("--render-dir", type=Path, default=ROOT / "artifacts" / "manual-preview")
    parser.add_argument("--font-directory", type=Path, default=Path("C:/Windows/Fonts"))
    arguments = parser.parse_args()
    validate_screenshots(ROOT / "docs" / "user-manual.md")
    register_fonts(arguments.font_directory)
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    document = ManualDocument(arguments.output)
    document.build(markdown_story(ROOT / "docs" / "user-manual.md", make_styles()))

    # Render every page for visual review. PDFium is available as a Windows wheel,
    # avoiding a separate system Poppler installation on documentation machines.
    arguments.render_dir.mkdir(parents=True, exist_ok=True)
    with pypdfium2.PdfDocument(str(arguments.output)) as pdf:
        for number, page in enumerate(pdf, 1):
            bitmap = page.render(scale=1.5)
            bitmap.to_pil().save(arguments.render_dir / f"manual-{number:02}.png")
            bitmap.close()
            page.close()

    reader = PdfReader(arguments.output)
    # A fresh, deterministic PDF avoids local-time-zone timestamps and host metadata.
    check_private_text("\n".join(page.extract_text() for page in reader.pages), "PDF text")
    check_private_text(str(reader.metadata), "PDF metadata")
    if reader.attachments:
        raise SystemExit("The manual must not contain embedded source-file attachments.")
    report = {
        "pdf": arguments.output.name,
        "privacy": "anonymous captures verified; deterministic PDF metadata",
        "pages": len(reader.pages),
        "sections": [{"title": title, "page": page} for title, page in document.section_pages],
        "page_text_lengths": [len(page.extract_text()) for page in reader.pages],
        "bytes": arguments.output.stat().st_size,
        "flowable_bottoms": document.layout,
    }
    (arguments.render_dir / "validation.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key != "flowable_bottoms"}, indent=2))
    if len(reader.pages) != len(document.section_pages) or any(
        page != expected for expected, (_, page) in enumerate(document.section_pages, 1)
    ):
        raise SystemExit("A section overflowed its page. Review the rendered pages and adjust the layout.")
    if any(not page.extract_text().strip() for page in reader.pages):
        raise SystemExit("Unexpected empty manual page.")


if __name__ == "__main__":
    main()
