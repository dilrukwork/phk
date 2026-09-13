# Project Overview
**Name**: Dhamma Books Publishing
**Stack**: Python
**Description**: Extract text from raw source PDFs (scanned books) and convert to Markdown format (`-ocr.md`). Then convert the Markdown format to EPUB maintaining the look and feel of the original PDF.

---

## Architecture Rules
1. All tools must be opensource or runnable within the environment.
2. The raw source documents are mainly in the **Sinhala language**, but there are Pali language verses and words in various places. No difference in styles between Sinhala and Pali.
3. Styles are provided in a separate stylesheet file `src/ocr/style.css`. Styles and fonts must be embedded into the EPUB.
4. Reference file for unclear or difficult-to-translate words is located at `src/ocr/glossary.json`.
5. OCR and EPUB publishing are two distinct, separate steps. Manually verify/review the OCR Markdown file before publishing to EPUB.
6. The primary font is **NotoSerifSinhala-Regular.ttf** located at `src/fonts/NotoSerifSinhala-Regular.ttf`.
7. EPUB output must be compatible with common e-readers including Kindle (`sendtokindle` / MOBI).
8. Books are based on audio sermons. Audio references in `src/rawpdf/` can be used to clarify ambiguous words in OCR.
9. In Sinhala, when two letters are attached (conjuncts), when separated the second letter should receive a 'hal' mark (e.g., අ and න‍ attached -> අන‍්තරා).
10. Graphics on original cover pages should be omitted/ignored.

---

## File Structure
```
src/
├── fonts/        # Font files (NotoSerifSinhala-Regular.ttf)
├── rawpdf/       # Original PDF scanned documents and audio references
├── ocr/          # OCR markdown files (-ocr.md), style.css, and glossary.json
├── resources/    # Common assets (dhammawheel.png)
└── published/    # Output EPUB files
```

---

## Coding & Publishing Conventions
1. Python is used as the scripting language (`build_epub.py`, `build_correction_map.py`, `pdf_to_pages.py`, `ocr_draft.py`).
2. Consistent file naming convention:
   `src/rawpdf/[original].pdf` -> `src/ocr/[original]-ocr.md` -> `src/published/[original].epub`
3. To build/publish an EPUB:
   ```bash
   python build_epub.py <name>
   ```
   Example: `python build_epub.py pahankanuwa_03` generates `src/published/pahankanuwa_03.epub`.

---

## Formatting Instructions
1. Ignore page breaks from the original print document, but preserve section breaks, paragraphs, and verses.
2. Sinhala paragraphs start with a first-line indent (`text-indent: 2em` in CSS).
3. Cover pages are automatically rasterized to an embedded JPEG image (`images/cover.jpg`) so Kindle library thumbnail previews render Sinhala titles properly without tofu/blank text.
