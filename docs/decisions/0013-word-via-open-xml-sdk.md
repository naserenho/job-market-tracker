# 0013 — Word files via the Open XML SDK, one column for applicant-tracking systems

**Status:** Accepted (2026-09-29)

## Context
Canadian employers mostly take CVs through applicant-tracking systems (Workday, Greenhouse, Lever), which read the file's text in document order. Tables, text boxes and headers often come out scrambled or missing. The owner's current CV uses text boxes, and its extracted text is out of order.

## Decision
- Generate `.docx` with Microsoft's Open XML SDK (`DocumentFormat.OpenXml`, MIT licence), built in code:
  - one column on US Letter;
  - Word's built-in Title and Heading styles, and real bullets from a numbering definition;
  - no tables, text boxes, images, headers or footers;
  - contact details in the body.
- `CvLayout` produces the paragraphs in page order and `CvDocx` only formats them, so the text an applicant-tracking system reads is the text the tests check.
- Tests run `OpenXmlValidator` with zero errors allowed, because Word refuses some files the SDK happily writes. They also read the text back in order.

**Alternatives considered:**
- **PDF:** fixed layout, but not editable before sending. Word's "Save as PDF" covers portals that want PDF.
- **Filling a Word template (.dotx):** keeps a hand-made design, but templates lean on tables and text boxes, and repeating sections through content controls is fragile.

## Consequences
- The look is plain and consistent, which is what tracking systems and Canadian recruiters expect.
- Page count is only estimated on the page (450 words a page). Word has the final say.
- A generated file is saved on the entry as "generated". If it's edited in Word before sending, the uploaded final file is the one marked "sent".
