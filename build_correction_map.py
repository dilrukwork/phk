"""Diff the raw per-page Tesseract OCR draft against its final reviewed
version to build a reusable word-correction mapping sheet for future books.

Usage:
    python build_correction_map.py <name> [--final-suffix TEXT]

    Reads src/ocr/<name>/pages/page_*.draft.txt (the raw, pre-review
    Tesseract output written by ocr_draft.py) and src/ocr/<name><final-suffix>.md
    (the manually corrected reference), strips markup/comments down to plain
    running text, tokenizes into words, and diffs the two token streams.
    Every word-for-word (or short phrase-for-phrase) substitution found is
    counted and written to src/ocr/<name>-corrections.json, sorted by
    frequency.

    This is a review aid, not something build_epub.py consumes -- it is
    meant to be skimmed (or grepped) during the OCR review pass on the next
    book, as a list of Tesseract Sinhala misreadings this reviewer has
    already had to fix once before.

    Pure insertions/deletions (sentences added or removed during review,
    not OCR misreadings) are excluded from the mapping and only counted in
    the summary, since they aren't word corrections.
"""

import argparse
import json
import re
import sys
from collections import Counter
from difflib import SequenceMatcher
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")

REPO_ROOT = Path(__file__).resolve().parents[2]
OCR_DIR = REPO_ROOT / "src" / "ocr"

HTML_COMMENT_RE = re.compile(r"<!--.*?-->", re.S)
TAG_RE = re.compile(r"<[^>]+>")
# Same widened "word character" set as build_epub.py: the Sinhala Unicode
# block plus ZWJ, so combining vowel signs/virama stay attached to their
# base consonant instead of splitting mid-word.
WORD_RE = re.compile(r"[\w඀-෿‍]+")

# Longer runs of replaced tokens are usually a rewritten sentence, not a
# handful of OCR misreadings -- capping keeps the map to genuine word/short
# -phrase corrections instead of dumping whole rewritten passages into it.
MAX_PHRASE_LEN = 4


def extract_words(text: str) -> list[str]:
    text = HTML_COMMENT_RE.sub(" ", text)
    text = TAG_RE.sub(" ", text)
    text = text.replace("&nbsp;", " ").replace("&#160;", " ")
    return WORD_RE.findall(text)


def build_map(name: str, final_suffix: str) -> None:
    pages_dir = OCR_DIR / name / "pages"
    final_path = OCR_DIR / f"{name}{final_suffix}.md"
    if not pages_dir.exists():
        raise FileNotFoundError(f"No pages dir: {pages_dir}")
    page_files = sorted(pages_dir.glob("page_*.draft.txt"))
    if not page_files:
        raise FileNotFoundError(f"No page_*.draft.txt files found in {pages_dir}")
    if not final_path.exists():
        raise FileNotFoundError(f"No such file: {final_path}")

    original_text = "\n".join(p.read_text(encoding="utf-8") for p in page_files)
    old_words = extract_words(original_text)
    new_words = extract_words(final_path.read_text(encoding="utf-8"))

    matcher = SequenceMatcher(a=old_words, b=new_words, autojunk=False)

    corrections: Counter[tuple[str, str]] = Counter()
    skipped_long = 0
    inserted = deleted = 0

    for tag, a1, a2, b1, b2 in matcher.get_opcodes():
        if tag == "equal":
            continue
        if tag == "insert":
            inserted += b2 - b1
            continue
        if tag == "delete":
            deleted += a2 - a1
            continue
        # tag == "replace"
        old_span = old_words[a1:a2]
        new_span = new_words[b1:b2]
        if len(old_span) > MAX_PHRASE_LEN or len(new_span) > MAX_PHRASE_LEN:
            skipped_long += 1
            continue
        corrections[(" ".join(old_span), " ".join(new_span))] += 1

    entries = [
        {"from": old, "to": new, "count": count}
        for (old, new), count in corrections.items()
    ]
    entries.sort(key=lambda e: (-e["count"], e["from"]))

    out_path = OCR_DIR / f"{name}-corrections.json"
    out_path.write_text(
        json.dumps(entries, ensure_ascii=False, indent=2), encoding="utf-8"
    )

    print(f"{name}: {len(old_words)} words ({len(page_files)} draft page(s)) vs "
          f"{len(new_words)} words (final)")
    print(f"{len(entries)} distinct correction(s), {sum(corrections.values())} occurrence(s) "
          f"-> {out_path}")
    print(f"{inserted} word(s) inserted, {deleted} word(s) deleted during review "
          f"(not included in the map)")
    if skipped_long:
        print(f"{skipped_long} replaced span(s) longer than {MAX_PHRASE_LEN} words skipped "
              f"(rewritten passages, not word corrections)")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("name")
    parser.add_argument("--final-suffix", default="-ocr")
    args = parser.parse_args()
    build_map(args.name, args.final_suffix)
