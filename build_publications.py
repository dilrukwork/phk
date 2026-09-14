import argparse
import io
import re
import sys
import uuid
from pathlib import Path

from ebooklib import epub
from PIL import Image, ImageDraw, ImageFont
from bs4 import BeautifulSoup
from weasyprint import HTML, CSS
import subprocess

sys.stdout.reconfigure(encoding="utf-8")

REPO_ROOT = Path(__file__).resolve().parent
OCR_DIR = REPO_ROOT / "src" / "ocr"
FONTS_DIR = REPO_ROOT / "src" / "fonts"
RESOURCES_DIR = REPO_ROOT / "src" / "resources"
PUBLISHED_DIR = REPO_ROOT / "src" / "published"

FONT_FILE = "NotoSerifSinhala-Regular.ttf"
FONT_FAMILY = "Noto Serif Sinhala"
BOOK_AUTHOR = "කටුකුරුන්දේ ඤාණානන්ද භික්ෂු"
BOOK_LANGUAGE = "si"

DHAMMAWHEEL_FILE = "dhammawheel.png"
DHAMMAWHEEL_MAX_DIM = 300

TRAILING_NOTES_RE = re.compile(r"^<!--\s*=+\s*-->\s*$")
MYEDIT_LINE_RE = re.compile(r'^"(.*?)"\s*:\s*"(.*?)"\s*$')
GLOSSARY_MARKER_RE = re.compile(r"\[\[\?([^\]]*)\]\]")
TOC_ENTRY_RE = re.compile(r'<div class="toc-entry">.*?</div>')
CHAPTER_TITLE_RE = re.compile(r'<h2 class="chapter-title">(.*?)</h2>')
SERMON_DIVIDER_RE = re.compile(r'<div class="sermon-divider">(.*?)</div>')
BOOKTITLE_RE = re.compile(r'<div class="booktitle">(.*?)</div>')
BOOKSUBTITLE_RE = re.compile(r'<div class="booksubtitle">(.*?)</div>')
BOOKAUTHOR_RE = re.compile(r'<div class="bookauthor">(.*?)</div>')
DHAMMACAKKA_RE = re.compile(r'<div class="dhammacakka">.*?</div>')


def load_myedit(name: str) -> list[tuple[str, str]]:
    myedit_path = OCR_DIR / "myedit.md"
    if not myedit_path.exists():
        return []
    corrections = []
    for line in myedit_path.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if not stripped:
            continue
        normalized = stripped.translate({0x201C: '"', 0x201D: '"', 0x2018: "'", 0x2019: "'"})
        match = MYEDIT_LINE_RE.match(normalized)
        if match:
            corrections.append((match.group(1), match.group(2)))
    return corrections


def extract_single_range(lines: list[str], start_line: int | None, end_line: int | None) -> tuple[int, int]:
    if start_line is not None:
        start_idx = start_line - 1
    else:
        start_idx = 0
        while start_idx < len(lines):
            stripped = lines[start_idx].strip()
            if stripped == "" or (
                stripped.startswith("<!--") and not stripped.startswith("<!-- src:")
            ):
                start_idx += 1
            else:
                break

    if end_line is not None:
        end_idx = end_line
    else:
        end_idx = len(lines)
        for i, line in enumerate(lines):
            if TRAILING_NOTES_RE.match(line.strip()):
                end_idx = i
                break

    return start_idx, end_idx


def extract_body(text: str, start_line: int | None, end_line: int | None,
                  ranges: list[str] | None) -> list[str]:
    lines = text.splitlines()

    if ranges:
        result = []
        for r in ranges:
            start_s, end_s = r.split(":")
            start_idx, end_idx = int(start_s) - 1, int(end_s)
            result.extend(lines[start_idx:end_idx])
        return result

    start_idx, end_idx = extract_single_range(lines, start_line, end_line)
    return lines[start_idx:end_idx]


_WORD_CHAR_RE = re.compile(r"[\w඀-෿‍]")


def _is_word_char(ch: str) -> bool:
    return bool(_WORD_CHAR_RE.match(ch))


def _replace_whole_word(content: str, old: str, new: str) -> tuple[str, int]:
    if not old:
        return content, 0
    parts = []
    count = 0
    i = 0
    while True:
        idx = content.find(old, i)
        if idx == -1:
            parts.append(content[i:])
            break
        before_ok = idx == 0 or not _is_word_char(content[idx - 1])
        after_idx = idx + len(old)
        after_ok = after_idx >= len(content) or not _is_word_char(content[after_idx])
        if before_ok and after_ok:
            parts.append(content[i:idx])
            parts.append(new)
            count += 1
            i = after_idx
        else:
            parts.append(content[i:idx + 1])
            i = idx + 1
    return "".join(parts), count


def apply_corrections(content: str, corrections: list[tuple[str, str]]) -> tuple[str, list[tuple[str, str, int]]]:
    applied = []
    for old, new in corrections:
        content, count = _replace_whole_word(content, old, new)
        if count:
            applied.append((old, new, count))
    return content, applied


def strip_glossary_markers(content: str) -> tuple[str, list[str]]:
    stripped = []

    def _replace(match: re.Match) -> str:
        word = match.group(1)
        context = content[max(0, match.start() - 20): match.end() + 20]
        stripped.append(f"{word}  (context: ...{context}...)")
        return word

    content = GLOSSARY_MARKER_RE.sub(_replace, content)
    return content, stripped


def trim_toc(content: str, toc_limit: int | None, toc_keep: list[int] | None) -> str:
    if toc_limit is None and toc_keep is None:
        return content

    def _replace(match: re.Match) -> str:
        entries = TOC_ENTRY_RE.findall(match.group(0))
        if toc_keep is not None:
            kept = "\n".join(entries[i - 1] for i in toc_keep if 0 < i <= len(entries))
        else:
            kept = "\n".join(entries[:toc_limit])
        return f'<div class="toc-list">\n{kept}\n</div>'

    return re.sub(
        r'<div class="toc-list">.*?\n</div>', _replace, content, flags=re.S, count=1
    )


def _fit_font(draw: ImageDraw.ImageDraw, text: str, font_path: Path,
              max_width: int, start_size: int, min_size: int = 20) -> tuple[ImageFont.FreeTypeFont, int]:
    size = start_size
    while size > min_size:
        font = ImageFont.truetype(str(font_path), size)
        left, top, right, bottom = draw.textbbox((0, 0), text, font=font)
        width = right - left
        if width <= max_width:
            return font, width
        size -= 2
    font = ImageFont.truetype(str(font_path), min_size)
    left, top, right, bottom = draw.textbbox((0, 0), text, font=font)
    return font, right - left


def generate_cover_image(title: str, subtitle: str, author: str) -> bytes:
    width, height = 1600, 2071
    img = Image.new("RGB", (width, height), "white")
    draw = ImageDraw.Draw(img)
    font_path = FONTS_DIR / FONT_FILE
    max_width = width - 240

    title_font, title_w = _fit_font(draw, title, font_path, max_width, 120)
    subtitle_font, subtitle_w = _fit_font(draw, subtitle, font_path, max_width, 76)
    author_font, author_w = _fit_font(draw, author, font_path, max_width, 56)

    y = height * 0.30
    draw.text(((width - title_w) / 2, y), title, font=title_font, fill="black")
    _, top, _, bottom = draw.textbbox((0, 0), title, font=title_font)
    y += (bottom - top) * 1.6

    draw.text(((width - subtitle_w) / 2, y), subtitle, font=subtitle_font, fill="black")

    y_author = height * 0.85
    draw.text(((width - author_w) / 2, y_author), author, font=author_font, fill="black")

    buf = io.BytesIO()
    img.save(buf, format="JPEG", quality=92)
    return buf.getvalue()


def load_dhammawheel_image() -> bytes:
    img_path = RESOURCES_DIR / DHAMMAWHEEL_FILE
    img = Image.open(img_path)
    img.thumbnail((DHAMMAWHEEL_MAX_DIM, DHAMMAWHEEL_MAX_DIM), Image.LANCZOS)
    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def split_chapters(content: str) -> list[str]:
    chunks = re.split(r'<div class="page-break"></div>', content)
    return [c.strip() for c in chunks if c.strip()]


def chapter_label(chunk: str, index: int) -> str | None:
    if index == 0 and '<div class="booktitle">' in chunk:
        return "Cover"
    match = CHAPTER_TITLE_RE.search(chunk)
    if match:
        return re.sub(r"<[^>]+>", "", match.group(1))
    match = SERMON_DIVIDER_RE.search(chunk)
    if match:
        return re.sub(r"<[^>]+>", "", match.group(1))
    return None


def build_publications(name: str, start_line: int | None = None, end_line: int | None = None,
                        ranges: list[str] | None = None, suffix: str = "", label: str = "",
                        toc_limit: int | None = None, toc_keep: list[int] | None = None,
                        cover_image: bool = True, page_numbers: bool = False) -> None:
    src_path = OCR_DIR / f"{name}.md"
    if not src_path.exists():
        src_path = OCR_DIR / f"{name}-ocr.md"
    if not src_path.exists():
        raise FileNotFoundError(f"No such OCR file for '{name}' in {OCR_DIR}")

    raw_text = src_path.read_text(encoding="utf-8")
    body_lines = extract_body(raw_text, start_line, end_line, ranges)
    content = "\n".join(body_lines)

    corrections = load_myedit(name)
    content, applied = apply_corrections(content, corrections)
    content, stripped_markers = strip_glossary_markers(content)
    content = trim_toc(content, toc_limit, toc_keep)
    content = content.replace("&nbsp;", "&#160;")

    dhammawheel_count = len(DHAMMACAKKA_RE.findall(content))
    if dhammawheel_count:
        content = DHAMMACAKKA_RE.sub(
            '<img class="dhammacakka" src="images/dhammawheel.png" alt="Dhamma wheel"/>',
            content,
        )

    cover_bytes = None
    if cover_image:
        title_m = BOOKTITLE_RE.search(content)
        subtitle_m = BOOKSUBTITLE_RE.search(content)
        author_m = BOOKAUTHOR_RE.search(content)
        if title_m and subtitle_m and author_m:
            cover_bytes = generate_cover_image(title_m.group(1), subtitle_m.group(1), author_m.group(1))

    chunks = split_chapters(content)
    if not chunks:
        raise ValueError("No content left after slicing")

    if page_numbers:
        chunks = [f'{chunk}\n<div class="page-number">{i + 1}</div>' for i, chunk in enumerate(chunks)]

    style_css = (OCR_DIR / "style.css").read_text(encoding="utf-8")
    font_css = (
        f"@font-face {{\n"
        f'  font-family: "{FONT_FAMILY}";\n'
        f"  src: url(\"../fonts/{FONT_FILE}\");\n"
        f"  font-weight: normal;\n"
        f"  font-style: normal;\n"
        f"}}\n\n"
        f"html, body {{\n"
        f'  font-family: "{FONT_FAMILY}", serif;\n'
        f"}}\n"
    )

    book = epub.EpubBook()
    book.set_identifier(f"dhammabooks-{name}{suffix}-{uuid.uuid4()}")
    title = f"පහන් කණුව ධම් දේශනා 1 - වෙළුම{f' ({label})' if label else ''}"
    book.set_title(title)
    book.set_language(BOOK_LANGUAGE)
    book.add_author(BOOK_AUTHOR)

    if cover_bytes is not None:
        book.set_cover("images/cover.jpg", cover_bytes)

    css_item = epub.EpubItem(
        uid="style", file_name="style/style.css", media_type="text/css", content=style_css
    )
    font_css_item = epub.EpubItem(
        uid="font_style", file_name="style/fonts.css", media_type="text/css", content=font_css
    )
    font_item = epub.EpubItem(
        uid="sinhala_font",
        file_name=f"fonts/{FONT_FILE}",
        media_type="application/x-font-ttf",
        content=(FONTS_DIR / FONT_FILE).read_bytes(),
    )
    book.add_item(css_item)
    book.add_item(font_css_item)
    book.add_item(font_item)

    if dhammawheel_count:
        dhammawheel_item = epub.EpubItem(
            uid="dhammawheel",
            file_name="images/dhammawheel.png",
            media_type="image/png",
            content=load_dhammawheel_image(),
        )
        book.add_item(dhammawheel_item)

    chapters = []
    nav_points = []
    for i, chunk in enumerate(chunks):
        xhtml_body = chunk
        c = epub.EpubHtml(
            title=f"Section {i + 1}",
            file_name=f"chap_{i + 1:03d}.xhtml",
            lang=BOOK_LANGUAGE,
        )
        c.content = (
            f"<html xmlns=\"http://www.w3.org/1999/xhtml\">"
            f"<head><title>Section {i + 1}</title>"
            f'<link rel="stylesheet" href="style/style.css" type="text/css"/>'
            f'<link rel="stylesheet" href="style/fonts.css" type="text/css"/>'
            f"</head><body>{xhtml_body}</body></html>"
        )
        c.add_item(css_item)
        c.add_item(font_css_item)
        book.add_item(c)
        chapters.append(c)

        label = chapter_label(chunk, i)
        if label:
            nav_points.append(epub.Link(c.file_name, label, f"nav_{i + 1}"))

    book.toc = nav_points
    book.add_item(epub.EpubNcx())
    book.add_item(epub.EpubNav())
    book.spine = chapters

    PUBLISHED_DIR.mkdir(parents=True, exist_ok=True)
    epub_path = PUBLISHED_DIR / f"{name}{suffix}.epub"
    epub.write_epub(epub_path, book)
    print(f"[EPUB] {name}: {len(chunks)} section(s) -> {epub_path} ({epub_path.stat().st_size / 1024:.1f} KB)")

    # 1. Convert to MOBI via ebook-convert
    mobi_path = PUBLISHED_DIR / f"{name}{suffix}.mobi"
    res = subprocess.run(["ebook-convert", str(epub_path), str(mobi_path)], capture_output=True, text=True)
    if res.returncode == 0:
        print(f"[MOBI] {name} -> {mobi_path} ({mobi_path.stat().st_size / 1024:.1f} KB)")
    else:
        print(f"[MOBI Error] {res.stderr}")

    # 2. Convert to PDF via WeasyPrint with embedded Sinhala font
    html_pieces = [f'''<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8"/>
<style>
@font-face {{
    font-family: 'Noto Serif Sinhala';
    src: url('file://{FONTS_DIR / FONT_FILE}');
    font-weight: normal;
    font-style: normal;
}}
body {{
    font-family: 'Noto Serif Sinhala', serif;
}}
{style_css}
@page {{
    size: A4;
    margin: 20mm;
    @bottom-center {{
        content: counter(page);
        font-family: 'Noto Serif Sinhala', serif;
        font-size: 10pt;
    }}
}}
.section-chunk + .section-chunk {{
    break-before: page;
}}
img.dhammacakka {{
    display: block;
    margin: 1em auto;
    max-width: 150px;
}}
</style>
</head>
<body>
''']

    for i, chunk in enumerate(chunks):
        chunk_html = chunk.replace('images/dhammawheel.png', f'file://{RESOURCES_DIR / DHAMMAWHEEL_FILE}')
        html_pieces.append(f'<div class="section-chunk">{chunk_html}</div>')

    html_pieces.append('</body></html>')
    full_html = '\n'.join(html_pieces)

    pdf_path = PUBLISHED_DIR / f"{name}{suffix}.pdf"
    HTML(string=full_html, base_url=str(REPO_ROOT)).write_pdf(pdf_path)
    print(f"[PDF]  {name} -> {pdf_path} ({pdf_path.stat().st_size / 1024:.1f} KB)")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("name")
    args = parser.parse_args()
    build_publications(args.name)
