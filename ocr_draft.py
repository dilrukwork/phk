"""Run a Tesseract OCR draft pass over rendered page images.

Usage:
    python ocr_draft.py <name>

    Reads src/ocr/<name>/pages/page_NNNN.png and writes a matching
    page_NNNN.draft.txt next to it, using the Sinhala ('sin') language
    model. This is a fast, rough first pass -- accuracy issues here are
    expected and get corrected in the Claude review step.

Re-running is safe: pages with an existing .draft.txt are skipped.
"""

import sys
from pathlib import Path

import pytesseract
from PIL import Image

TESSERACT_CMD = r"C:\Program Files\Tesseract-OCR\tesseract.exe"
LANG = "sin"

REPO_ROOT = Path(__file__).resolve().parent
OCR_DIR = REPO_ROOT / "src" / "ocr"


def run_draft(name: str) -> None:
    pytesseract.pytesseract.tesseract_cmd = TESSERACT_CMD

    pages_dir = OCR_DIR / name / "pages"
    if not pages_dir.exists():
        raise FileNotFoundError(
            f"No pages dir: {pages_dir} (run pdf_to_pages.py first)")

    page_images = sorted(pages_dir.glob("page_*.png"))
    if not page_images:
        raise FileNotFoundError(f"No page images found in {pages_dir}")

    drafted, skipped = 0, 0
    for image_path in page_images:
        out_path = image_path.with_suffix("").with_suffix(".draft.txt")
        if out_path.exists():
            skipped += 1
            continue
        text = pytesseract.image_to_string(Image.open(image_path), lang=LANG)
        out_path.write_text(text, encoding="utf-8")
        drafted += 1

    print(f"{name}: {drafted} page(s) drafted, {skipped} already existed, "
          f"{len(page_images)} total -> {pages_dir}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print("Usage: python ocr_draft.py <name>")
        sys.exit(1)
    run_draft(sys.argv[1])
