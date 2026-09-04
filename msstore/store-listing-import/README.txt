Microsoft Store listing import folder
=====================================

Upload THIS WHOLE FOLDER at Store listing > Import listing.

Built from the real template exported from Partner Center on 2026-09-04, not
from the published field mapping. That mattered: the live template's language
column is headed "English (United States)" (not "en-us"), it orders fields
differently from the documentation, and it carries two rows the docs omit
(HeroArts, Trailers). Field names, their order, and the Type column are
reproduced byte-for-byte from the export; only the value column is filled.
Saved as UTF-8 with BOM, as the docs require.

Assets sit beside the csv, so the relative paths are bare filenames.

FILLED: 32 of 70 rows
  ProductName, Description (2325 chars), WhatsNew, ShortDescription,
  ProductFeatures1-11, SearchTerms1-7, Applicable license terms, Copyright,
  DevelopedBy, StoreLogos1, Screenshots1-6.

Screenshot order is deliberate, strongest first:
  1 completed hash run          4 results right-click menu
  2 sidecar verification        5 inner-MSI scan
  3 idle main window            6 help window

LEFT BLANK ON PURPOSE: 38 rows
  ProductFeatures12-20, StoreLogos2 (2:3 poster art, optional and mostly for
  games), Screenshots7-10, HeroArts, Trailers, and
  RequirementsMinimum1-11 / RequirementsRecommended1-11.

  The Requirements rows are the hardware checklist. Blank is correct: anything
  marked required is published as required hardware and prevents customers on a
  device lacking it from rating or reviewing the app. The real requirements
  (64-bit Windows 10 1809 or newer, about 150 MB) are prose inside Description.

NOT COVERED BY THE IMPORT
  Separate pages in Partner Center; values are in ../listing.md.
  - Packages: the Wasabi package URL, x64, en-us, and the "runs in silent mode
    but does not require switches" checkbox
  - Properties: category Utilities + tools > File managers, privacy policy URL,
    support contact, product declarations (all No), system requirements (blank)
  - Age ratings questionnaire
  - Notes for certification
  - The 300x300 app tile icon, if Partner Center asks for it separately; the
    import format exposes only StoreLogos1 and StoreLogos2. It is at
    ../assets/store-app-tile-icon-300.png

IF THE IMPORT FAILS
  Download the error report; it names the offending field. The likeliest
  culprit is Description, which contains blank lines and is therefore a
  quoted multi-line CSV value. If that is rejected, the fix is to flatten it to
  single-spaced paragraphs and re-import.
