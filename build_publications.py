import argparse
import io
import re
import sys
import uuid
import base64
import subprocess
from pathlib import Path

from ebooklib import epub
from PIL import Image, ImageDraw, ImageFont
from bs4 import BeautifulSoup
from weasyprint import HTML, CSS
from docx import Document
from docx.shared import Pt, Inches

sys.stdout.reconfigure(encoding="utf-8")

REPO_ROOT = Path(__file__).resolve().parent
OCR_DIR = REPO_ROOT / "src" / "ocr"
FONTS_DIR = REPO_ROOT / "src" / "fonts"
RESOURCES_DIR = REPO_ROOT / "src" / "resources"
PUBLISHED_DIR = REPO_ROOT / "src" / "published"

FONT_FILE = "NotoSerifSinhala-Regular.ttf"
FONT_FAMILY = "Noto Serif Sinhala"
LATIN_FONT_PATH = Path("/usr/share/fonts/truetype/liberation/LiberationSerif-Regular.ttf")

BOOK_AUTHOR_SINHALA = "කටුකුරුන්දේ ඤාණානන්ද භික්ෂු"
BOOK_AUTHOR_ENGLISH = "Ven. Katukurunde Nanananda Thero"
BOOK_LANGUAGE = "en-US"

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


def _get_token_font(token: str, sinhala_font_path: Path, latin_font_path: Path, size: int) -> ImageFont.FreeTypeFont:
    if any(c.isdigit() or c in "-\u2013\u2014" for c in token):
        if latin_font_path.exists():
            return ImageFont.truetype(str(latin_font_path), size)
    return ImageFont.truetype(str(sinhala_font_path), size)


def _get_mixed_text_width(text: str, font_path: Path, latin_font_path: Path, size: int) -> int:
    tokens = re.split(r'([\d\-\u2013\u2014]+)', text)
    total_w = 0
    for token in tokens:
        if not token:
            continue
        f = _get_token_font(token, font_path, latin_font_path, size)
        left, top, right, bottom = f.getbbox(token)
        total_w += (right - left)
    return total_w


def _draw_mixed_text(draw: ImageDraw.ImageDraw, x: int, y: int, text: str,
                     font_path: Path, latin_font_path: Path, size: int, fill: str = "black") -> int:
    tokens = re.split(r'([\d\-\u2013\u2014]+)', text)
    curr_x = x
    max_h = 0
    for token in tokens:
        if not token:
            continue
        f = _get_token_font(token, font_path, latin_font_path, size)
        left, top, right, bottom = f.getbbox(token)
        draw.text((curr_x, y), token, font=f, fill=fill)
        curr_x += (right - left)
        if (bottom - top) > max_h:
            max_h = bottom - top
    return max_h


def _fit_mixed_font_size(text: str, font_path: Path, latin_font_path: Path,
                         max_width: int, start_size: int, min_size: int = 20) -> tuple[int, int]:
    size = start_size
    while size > min_size:
        w = _get_mixed_text_width(text, font_path, latin_font_path, size)
        if w <= max_width:
            return size, w
        size -= 2
    w = _get_mixed_text_width(text, font_path, latin_font_path, min_size)
    return min_size, w


def generate_cover_image(title: str, subtitle: str, author: str) -> bytes:
    width, height = 1600, 2071
    img = Image.new("RGB", (width, height), "white")
    draw = ImageDraw.Draw(img)
    font_path = FONTS_DIR / FONT_FILE
    latin_font_path = LATIN_FONT_PATH
    max_width = width - 240

    title_size, title_w = _fit_mixed_font_size(title, font_path, latin_font_path, max_width, 120)
    subtitle_size, subtitle_w = _fit_mixed_font_size(subtitle, font_path, latin_font_path, max_width, 76)
    author_size, author_w = _fit_mixed_font_size(author, font_path, latin_font_path, max_width, 56)

    y = height * 0.30
    h_title = _draw_mixed_text(draw, (width - title_w) / 2, y, title, font_path, latin_font_path, title_size)
    y += max(h_title, title_size) * 1.6

    _draw_mixed_text(draw, (width - subtitle_w) / 2, y, subtitle, font_path, latin_font_path, subtitle_size)

    y_author = height * 0.85
    _draw_mixed_text(draw, (width - author_w) / 2, y_author, author, font_path, latin_font_path, author_size)

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


def convert_to_txt(raw_text: str, out_path: Path) -> None:
    soup = BeautifulSoup(raw_text, "html.parser")
    paragraphs = []
    for p_elem in soup.find_all(['p', 'h1', 'h2', 'h3', 'div']):
        text = p_elem.get_text().strip()
        if text:
            paragraphs.append(text)
    plain_text = "\n\n".join(paragraphs)
    plain_text = re.sub(r"<!--.*?-->", "", plain_text, flags=re.S)
    plain_text = re.sub(r"\n{3,}", "\n\n", plain_text)
    out_path.write_text(plain_text.strip(), encoding="utf-8")


def convert_to_docx(raw_text: str, out_path: Path) -> None:
    doc = Document()
    soup = BeautifulSoup(raw_text, "html.parser")

    style = doc.styles['Normal']
    style.font.name = 'Noto Serif Sinhala'
    style.font.size = Pt(12)

    for p_elem in soup.find_all(['p', 'h1', 'h2', 'h3', 'div']):
        text = p_elem.get_text().strip()
        if not text:
            continue
        p = doc.add_paragraph(text)
        p.paragraph_format.space_after = Pt(6)
        p.paragraph_format.line_spacing = 1.25

    doc.save(out_path)


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

    if '<div class="booktitle">' in chunks[0]:
        chunks[0] = '<div class="cover-image-container" style="text-align: center;"><img src="images/cover.jpg" alt="Cover" style="max-width: 100%; height: auto;"/></div>'

    if page_numbers:
        chunks = [f'{chunk}\n<div class="page-number">{i + 1}</div>' for i, chunk in enumerate(chunks)]

    font_bytes = (FONTS_DIR / FONT_FILE).read_bytes()
    b64_font = base64.b64encode(font_bytes).decode("utf-8")

    style_css = (OCR_DIR / "style.css").read_text(encoding="utf-8")
    font_css = (
        f"@font-face {{\n"
        f'  font-family: "{FONT_FAMILY}";\n'
        f'  font-style: normal;\n'
        f'  font-weight: normal;\n'
        f'  src: url("../fonts/{FONT_FILE}") format("truetype");\n'
        f"}}\n\n"
        f"@font-face {{\n"
        f'  font-family: "{FONT_FAMILY}";\n'
        f'  font-style: italic;\n'
        f'  font-weight: normal;\n'
        f'  src: url("../fonts/{FONT_FILE}") format("truetype");\n'
        f"}}\n\n"
        f"@font-face {{\n"
        f'  font-family: "{FONT_FAMILY}";\n'
        f'  font-style: normal;\n'
        f'  font-weight: bold;\n'
        f'  src: url("../fonts/{FONT_FILE}") format("truetype");\n'
        f"}}\n\n"
        f"@font-face {{\n"
        f'  font-family: "{FONT_FAMILY}";\n'
        f'  font-style: italic;\n'
        f'  font-weight: bold;\n'
        f'  src: url("../fonts/{FONT_FILE}") format("truetype");\n'
        f"}}\n\n"
        f"body, p, div, h1, h2, h3, h4, span, li, a {{\n"
        f'  font-family: "{FONT_FAMILY}", serif;\n'
        f"}}\n\n"
        f"body {{\n"
        f"  font-size: 1em;\n"
        f"  line-height: 1.6;\n"
        f"}}\n"
    )

    book = epub.EpubBook()
    book.set_identifier(f"dhammabooks-{name}{suffix}-{uuid.uuid4()}")

    if name in ("phk1", "pahankanuwa_01-ocr"):
        english_title = "Pahankanuwa Sermons - 1"
    else:
        num = name.replace("phk", "").replace("pahankanuwa_", "").replace("-ocr", "")
        english_title = f"Pahankanuwa Sermons - {num}"

    english_author = BOOK_AUTHOR_ENGLISH

    book.set_title(english_title)
    book.set_language(BOOK_LANGUAGE)
    book.add_author(english_author, file_as=english_author, role="aut")
    book.add_metadata(None, "meta", "", {"name": "author", "content": english_author})

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
        media_type="font/ttf",
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
            lang="si",
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

    # Ensure system font cache has Noto Serif Sinhala installed for Calibre
    user_fonts = Path.home() / ".local" / "share" / "fonts"
    user_fonts.mkdir(parents=True, exist_ok=True)
    target_font = user_fonts / FONT_FILE
    if not target_font.exists():
        import shutil
        shutil.copy(FONTS_DIR / FONT_FILE, target_font)
        subprocess.run(["fc-cache", "-f"], capture_output=True)

    # 1. Convert to MOBI via ebook-convert (with KF8 support and embedded Sinhala font family)
    mobi_path = PUBLISHED_DIR / f"{name}{suffix}.mobi"
    res = subprocess.run(
        [
            "ebook-convert",
            str(epub_path),
            str(mobi_path),
            "--mobi-file-type=both",
            f"--embed-font-family={FONT_FAMILY}",
            "--language=en",
        ],
        capture_output=True,
        text=True,
    )
    if res.returncode == 0:
        print(f"[MOBI] {name} -> {mobi_path} ({mobi_path.stat().st_size / 1024:.1f} KB)")
    else:
        print(f"[MOBI Error] {res.stderr}")

    # 1b. Convert to AZW3 (KF8) for direct USB sideloading on modern Kindle e-readers
    azw3_path = PUBLISHED_DIR / f"{name}{suffix}.azw3"
    res_azw = subprocess.run(
        [
            "ebook-convert",
            str(epub_path),
            str(azw3_path),
            f"--embed-font-family={FONT_FAMILY}",
            "--language=en",
        ],
        capture_output=True,
        text=True,
    )
    if res_azw.returncode == 0:
        print(f"[AZW3] {name} -> {azw3_path} ({azw3_path.stat().st_size / 1024:.1f} KB)")
    else:
        print(f"[AZW3 Error] {res_azw.stderr}")

    # Save generated cover image to resources directory for local HTML PDF rendering
    cover_img_path = RESOURCES_DIR / "cover.jpg"
    if cover_bytes:
        cover_img_path.write_bytes(cover_bytes)

    # 1. Convert to PDF via WeasyPrint with embedded Sinhala font
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
        chunk_html = chunk_html.replace('images/cover.jpg', f'file://{RESOURCES_DIR / "cover.jpg"}')
        html_pieces.append(f'<div class="section-chunk">{chunk_html}</div>')

    html_pieces.append('</body></html>')
    full_html = '\n'.join(html_pieces)

    pdf_path = PUBLISHED_DIR / f"{name}{suffix}.pdf"
    HTML(string=full_html, base_url=str(REPO_ROOT)).write_pdf(pdf_path)
    print(f"[PDF]  {name} -> {pdf_path} ({pdf_path.stat().st_size / 1024:.1f} KB)")

    # 2. Convert to TXT
    txt_path = PUBLISHED_DIR / f"{name}{suffix}.txt"
    convert_to_txt(content, txt_path)
    print(f"[TXT]  {name} -> {txt_path} ({txt_path.stat().st_size / 1024:.1f} KB)")

    # 3. Convert to DOCX
    docx_path = PUBLISHED_DIR / f"{name}{suffix}.docx"
    convert_to_docx(content, docx_path)
    print(f"[DOCX] {name} -> {docx_path} ({docx_path.stat().st_size / 1024:.1f} KB)")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("name")
    args = parser.parse_args()
    build_publications(args.name)
