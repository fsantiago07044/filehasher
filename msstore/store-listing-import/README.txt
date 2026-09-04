Microsoft Store listing import folder
=====================================

Upload THIS WHOLE FOLDER at Store listing > Import listing. The folder must
contain exactly one .csv plus every asset the csv references, which is the
case here.

BEFORE YOU IMPORT, READ THIS
----------------------------
Microsoft's documentation says to fill in THEIR exported template, and several
documented import errors are exactly "you added or removed a field, export the
listing again". Two things in StoreListing.csv are therefore best-effort:

  1. The language column heading. This file uses "en-us". Partner Center may
     emit something else, such as "English (United States)".
  2. The exact set and order of Field rows, taken from the published field
     mapping rather than from a real export.

If the import fails, click Export listing on the Store listing page, and the
csv it produces is authoritative. Send it over and it can be refilled exactly;
all the content below is already settled, so that is a mechanical step.

WHAT IS FILLED IN
-----------------
  ProductName             FileHasher - Checksum Utility
  ShortDescription        one line, 102 chars
  Description             2325 chars
  WhatsNew                the 0.3.1 user-facing summary
  ProductFeatures1-11     11 features, longest 89 chars (limit 200)
  Screenshots1-6          in deliberate order, strongest first:
                            1 completed hash run
                            2 sidecar verification, all four verdicts
                            3 idle main window
                            4 results right-click menu
                            5 inner-MSI scan
                            6 help window
  StoreLogos1             1:1 box art, 1080x1080
  SearchTerms1-7          14 of the 21 permitted words
  Copyright               Copyright (c) 2026 FSP Productions, LLC
  Applicable license      MIT, with the LICENSE URL
  DevelopedBy             FSP Productions, LLC

DELIBERATELY LEFT EMPTY
-----------------------
  StoreLogos2             2:3 poster art. Optional, and mainly used for games.
  RequirementsMinimum1-11
  RequirementsRecommended1-11
                          The hardware checklist. Blank on purpose: anything
                          marked required is published as required hardware and
                          stops customers on a device lacking it from rating or
                          reviewing the app. The real requirements (64-bit
                          Windows 10 1809+, ~150 MB) are prose in Description.

NOT COVERED BY THE IMPORT
-------------------------
These are separate pages in Partner Center and the csv cannot carry them:
  - Packages (the Wasabi package URL, x64, en-us, silent-mode checkbox)
  - Properties (category, privacy policy URL, support contact, product
    declarations, system requirements)
  - Age ratings questionnaire
  - Notes for certification
  - The 300x300 app tile icon, if Partner Center offers it separately; the
    import format only exposes StoreLogos1 and StoreLogos2.
All of those values are in ../listing.md.
