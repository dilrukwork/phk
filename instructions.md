Project Overview
Name: Dhamma Books Publishing
Stack: Python
Description: Extract text from the raw source PDFs (scanned books) and convert to md format.
Then convert the md format to epub maintaining the look and feel of the original pdf.

Architecture Rules
1. All the tools must be opensource or within my Claude subscription credits
2. The raw source documents are mainly in "Sinahala language", but there're pali lanugage verses and words in various places. This is provided for accurate translation. No difference in styles.
3. Provide styles in a separate stylesheet file for md. Styles and fonts should be embedded to ebup
4. Create a seperate reference file for the words that are not clear or difficult to translate. I'll manually correcdt to re-apply. call it /ocr/glossary.json
5. OCR and epub publishing should be two different steps. I need manually check the OCT md file before go to the next step.
6. The font will be NotoSerifSinhala-Regular
7. The epub version should be compatible with all the common e-readers like Kindle. For kindle I use sendtokindle and mobi versions.
8. The books were based on audio sermens. One book contain several sermons. I have provided the avaialble audio sermon as well for referene if they can be used. This is if word is not clear in the OCR process or verification purpose.
9. In sinahal language, when two letters are attached to each other means when separate, the second letter should be 'hal'.
e.g  ![alt text](image.png) (අ and න‍ attached) should be  අන‍්තරා

10. Ignore graphics in the original cover

File Structure
src/
├── fonts/           # fonts files
├── rawpdf/        # Orignal PDF scanned documents and original audio reference
├── ocr/           # Where the OCR markdown files and mapping documents are stored
├── published/    # where the epub files will go

Coding Conventions
1. Use python as the scriping language
2. Use avaialble APIs or plugins
3. Name the files in a consistant manner:
`   [original].pdf -> [original]-ocr.md -> [original].epub

Other instructions
1. Provide an implementation plan prior to doing any executions

Formatting instructions
1. Do not consider page breaks in the original document, but do consider section breaks, paragrahps, verses
2. In sinhala language, paragram starts with 2 leading spaces in the first sentence of a paragraph. Include this in the published version.
3. Convert the cover page to a image and embed, otherwise kindle can't preview the sinhala text in the thubmnail.