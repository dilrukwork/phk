"""Render each page of a source PDF to a PNG image for OCR.

Usage:
    python pdf_to_pages.py <name>

    <name> is the PDF's basename without extension, expected at
    src/rawpdf/<name>.pdf. Output pages go to
    src/ocr/<name>/pages/page_NNNN.png

Re-running is safe: existing page images are skipped.
"""

import sys
from pathlib import Path

import pymupdf as fitz

DPI = 300

REPO_ROOT = Path(__file__).resolve().parent
RAWPDF_DIR = REPO_ROOT / "src" / "rawpdf"
OCR_DIR = REPO_ROOT / "src" / "ocr"


def render_pages(name: str) -> None:
    pdf_path = RAWPDF_DIR / f"{name}.pdf"
    if not pdf_path.exists():
        raise FileNotFoundError(f"No such PDF: {pdf_path}")

    pages_dir = OCR_DIR / name / "pages"
    pages_dir.mkdir(parents=True, exist_ok=True)

    doc = fitz.open(pdf_path)
    zoom = DPI / 72
    matrix = fitz.Matrix(zoom, zoom)
    total_pages = doc.page_count

    rendered, skipped = 0, 0
    for page_index in range(total_pages):
        page_number = page_index + 1
        out_path = pages_dir / f"page_{page_number:04d}.png"
        if out_path.exists():
            skipped += 1
            continue
        page = doc.load_page(page_index)
        pix = page.get_pixmap(matrix=matrix)
        pix.save(out_path)
        rendered += 1

    doc.close()
    print(f"{name}: {rendered} page(s) rendered, {skipped} already existed, "
          f"{total_pages} total -> {pages_dir}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print("Usage: python pdf_to_pages.py <name>")
        sys.exit(1)
    render_pages(sys.argv[1])
