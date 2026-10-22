# Phase 2c: CV Tailoring per Posting

**Status:** Draft for review · **Date:** 2026-09-29 · **Builds on:** [Phase 2b spec](2026-09-29-phase2b-tracker-design.md)

## Context

Phase 2b gave every shortlisted or applied job an entry in the tracker, holding the job's text (pasted, or read from the market tables) and the documents sent. Each application deserves a CV that puts forward what that posting asks for. Doing that by hand in Word takes too long to repeat for every application. Phase 2c adds a **Tailor CV** page to each entry. The page proposes a CV built from the owner's own true bullets, lets the owner adjust it, and saves the Word file on the entry.

**Decisions made with the owner:**
- **What tailoring does:** it picks and reorders. A master CV holds every true bullet, tagged with skills. For a posting, the tool pre-selects and orders the bullets and skills that match and shows what the posting asks for that the CV doesn't cover. The owner ticks and unticks bullets and edits the headline and summary, then generates the file. There is no AI, and nothing leaves the PC.
- **Output:** a Word `.docx`. It can be edited before sending, applicant-tracking systems read it well, and Word's "Save as PDF" covers portals that want a PDF.
- **Master CV:** a file, `config/cv.json`, edited by hand and gitignored like `profile.json`.
- **Approach A:** the tailoring logic lives in the API, there is an interactive Tailor page per entry, and a code-built single-column `.docx` is saved on the entry.
- **The Canadian-format CV rewrite is the same piece of work.** The real `cv.json` holds the rewritten content, and a **Download full CV** link produces the untailored version.

## Goals

- For any entry, a proposed tailored CV in one click and a finished `.docx` a few minutes later.
- **Honest by construction.** The tool only selects, orders and emphasises bullets the owner wrote. It can never add a claim, and the page says where the owner's profile claims a skill that the CV doesn't show yet.
- One source of truth for skills. The Tailor page finds a posting's skills with the same function and catalog that the market data uses, so the two never disagree.
- Learning value: generating Office Open XML with Microsoft's SDK, validating documents, and deterministic ranking with tests.

## Non-goals

- Cover letters.
- AI rewording or summarising. That belongs to phase 3 (Azure OpenAI).
- PDF export. Word's "Save as PDF" covers it.
- A master-CV editor in the dashboard.
- Tailoring a market posting without an entry. Shortlisting it first is one click.
- Changing what the Processor computes. Moving the notice-stripping into a shared function must leave the Processor's output unchanged.

## The master CV (`config/cv.json`)

The file is gitignored: `.gitignore` gets `config/cv.json` next to `config/profile.json`. The committed `config/cv.example.json` describes a made-up person and documents the format. Tests load only the example file or files they write themselves. The file is JSON with comments and trailing commas allowed, read the same way as `profile.json`.

```jsonc
{
  "person": {
    "name": "Alex Example",
    "location": "Toronto, ON",
    "status": "Canadian Permanent Resident",   // optional; shown on the contact line
    "phone": "+1 416 555 0100",
    "email": "alex@example.com",
    "links": [ { "label": "GitHub", "url": "https://github.com/alex-example" } ]
  },
  "headline": "Senior Full Stack .NET Developer",
  "summaries": [
    { "id": "dotnet", "families": ["dotnet", "fullstack", "software_engineer"], "text": "..." },
    { "id": "lead", "families": ["lead_manager"], "text": "..." }
  ],
  "skillGroups": [
    { "name": "Backend", "items": [ { "text": "C# / .NET 8-10", "skills": ["C#", ".NET"] }, { "text": "ASP.NET Core" } ] }
  ],
  "roles": [
    {
      "id": "acme", "title": "Senior Developer", "company": "Acme Corp", "location": "Toronto, ON",
      "start": "2023-10", "end": null, "maxBullets": 6,
      "bullets": [ { "id": "acme-cicd", "text": "...", "skills": ["Azure DevOps"], "always": true } ]
    }
  ],
  "projects": [ /* same shape as roles; company and location optional */ ],
  "education": [ { "degree": "BSc Computer Engineering", "school": "Example University", "years": "2012-2016" } ],
  "certifications": [ { "name": "Microsoft Certified: Azure Developer Associate (AZ-204)", "year": "2026", "skills": ["Azure"] } ]
}
```

**Rules:**
- **Required:**
  - `person.name`;
  - `person.email`;
  - `headline`;
  - at least one summary;
  - at least one role;
  - for each role, `id`, `title`, `company`, `start` and at least one bullet;
  - for each project, `id` and `title`;
  - for each bullet, `id` and `text`.
- **Optional fields and defaults:**
  - `end: null` means the role is current.
  - `maxBullets` defaults to 5.
  - `always` defaults to false.
  - `skills` defaults to empty.
  - `projects`, `skillGroups`, `education` and `certifications` default to empty.
- **Dates:** `start` and `end` are `yyyy-MM` and are rendered as "Oct 2023" and "Present".
- **Ids:**
  - Role, project, summary and bullet ids are unique across the whole file.
  - They are used only as references and never shown.
  - Saved choices point at ids, not positions, so editing the file never re-points them.
- **Skills:**
  - Every name in a `skills` list must be a `name` in `config/skills.json`.
  - `families` entries must be `dotnet`, `fullstack`, `software_engineer`, `lead_manager` or `other`.
- **What counts as "the skills of" a bullet, a skill item or a certification:** its `skills` tags, plus every skill the catalog finds in its text. A bullet that says "Next.js" needs no tag.
- **When the file is read:** on every request that needs it, so edits apply without a restart. It is a small file.
- **Problems:**
  - Validation collects every problem, each with its path, for example `roles[0].bullets[2].id: "x-1" is used twice`.
  - A missing file is one problem: "config/cv.json is missing. Copy config/cv.example.json to config/cv.json and fill in your CV."
  - Invalid JSON is one problem, naming the line.
- **Privacy:** the API never logs the file's contents, and telemetry never carries CV text.

## Posting text and skills

**Which text is used.** Today the entry detail uses `entry.PostingText` for pasted entries and the market description for market entries. 2c changes the rule:
- A non-blank `entry.PostingText` wins for every entry.
- Otherwise a market entry falls back to the market description, as before.
- A pasted entry with blank text has none.

The same rule serves the entry page and the Tailor page, and the proposal reports the source as `entry`, `market` or `none`.

**Pasting the full text onto an entry.**
- `PUT /applications/{id}/posting-text` sets `entry.PostingText`.
- It does not go through `InboxWriter`, and it never creates or changes an inbox file. The next rebuild therefore never counts the job twice, and a pasted entry's inbox file keeps the text first pasted.
- Blank text clears it, and a market entry then falls back to the market text.
- The request is versioned like other entry edits, so a stale version gets a 409 and **Reload**.
- The limit is `Rules.MaxPostingText` (100,000 characters).

**Thin text.** The proposal flags the text as thin when either holds:
- fewer than 3 skills are found;
- the text is the market description of a posting that isn't full text (an Adzuna snippet).

Most .NET roles are Adzuna snippets, so the page puts the paste box in front of the owner when the text is thin.

**One skill-finding function.**
- `Posting.Interpret` currently strips hiring notices (the Ontario AI-in-hiring disclosure and accommodation statements) before matching.
- That moves to a public static class in Tracker.Core: `Tracker.Interpreting.SkillText`.
- Its method `ForMatching(string title, string description)` returns the text to match: the title, a newline, then the description with the notices removed.
- `Posting.Interpret` and the tailoring code both call `catalog.Match(SkillText.ForMatching(title, text))`.
- The Processor's output must not change, and the existing Tracker.Core tests are the guard.

**Weights.**
- A posting skill that the catalog also finds in the entry's title weighs 2. Every other posting skill weighs 1.
- The title is the entry's own title, which the owner can edit.

## Proposal and pre-selection

**Sections.** Each role and each project is a section. Roles are listed first, then projects, both in file order, and the tool never reorders sections.

**Bullet score.** A bullet's score is the sum of the weights of the posting skills it shows.

**Pre-selection within a section,** where `max` is `maxBullets`:
1. Select every `always` bullet. If there are more than `max`, all of them are still kept.
2. While fewer than `max` are selected, add the unselected bullet with the highest score above 0. Ties go to the one earlier in the file.
3. While fewer than `max` are selected, add unselected bullets in file order.
4. Order the selected bullets by score, highest first, with ties in file order.

**Skills section.**
- Every group and every item is always shown, and groups keep file order.
- Within a group, items whose skills include a posting skill come first. Both halves keep file order.

**Summary.**
- The suggestion is the first summary whose `families` include `TitleRules.Family(entry.Title)`. If none does, it is the first summary.
- The headline suggestion is `headline`.
- The owner may pick another summary variant or edit the text freely. What is saved is the text.

**Education and certifications** are always shown in full and in file order.

**Saved choices.**
- If the entry has generated CVs, the page starts from the choices of the newest one: its headline, summary and ordered bullet ids.
- Ids no longer in `cv.json` are dropped. The proposal lists them, and the page says "N bullets from your last CV are no longer in cv.json".
- **Reset to suggestion** replaces the page state with the fresh suggestion.
- The order of saved bullet ids gives the order within each section. A bullet always renders in the section `cv.json` puts it in.

**Coverage.** Each posting skill is in one of four states. The browser recomputes them live from the proposal data as the owner ticks.

| State | Meaning |
|---|---|
| **In a bullet** | A selected bullet shows it. |
| **Listed** | Only the skills section or a certification shows it. |
| **Available** | Only unselected bullets show it. Clicking the chip highlights those bullets. |
| **Not in your CV** | Nothing in `cv.json` shows it. The chip carries the owner's profile level from `profile.json`. **Strong** or **working** reads "your profile says you know this: add a true bullet to cv.json". **Learning** or **none** reads "gap". |

**Length.** The page counts the words of everything the file will contain and shows "about N pages" at 450 words a page, to one decimal place. It warns above 2.0. This is an estimate: only Word knows the real page count.

## The Word file

**Library.** Microsoft's Open XML SDK (`DocumentFormat.OpenXml`, MIT licence). The version is pinned in the plan after checking NuGet.

**Layout** (built for applicant-tracking systems):
- US Letter, 0.7 inch margins, one column.
- Calibri 10.5 pt body text, with the name at 20 pt.
- Word's built-in Title, Heading 1 and Heading 2 styles, and bullets from a real numbering definition.
- No tables, text boxes, drawings or images, and no content in headers or footers. Applicant-tracking systems often skip headers and footers.

**Order:**
1. **Name** (Title style).
2. **Contact line**, as one paragraph with parts joined by " · ": location, status, phone, email, then each link as a hyperlink showing its URL.
3. **Headline.**
4. **Summary** heading and paragraph.
5. **Skills** heading, then one paragraph per group: the group name in bold, then its items joined by ", ".
6. **Experience** heading. For each role, a Heading 2 of "Title — Company", then a line of "Location | Oct 2023 – Present", then the selected bullets.
7. **Projects**, the same as roles. The company, location and dates are shown only when present.
8. **Education**, as "Degree — School (years)".
9. **Certifications**, as "Name (year)".

**Empty sections** are left out entirely:
- no Summary when the summary is empty;
- no Skills, Projects, Education or Certifications when `cv.json` has none of them.

Every role and project keeps its title line even with no bullets selected, so the work history stays complete. Roles are required, so the Experience heading is always there.

**Never included:** photo, date of birth, age, nationality, marital status or gender. The format has no fields for them.

**File name.** `<person.name> - CV - <company>.docx`, or `<person.name> - CV.docx` for the full CV. Characters that are invalid in Windows file names are removed, runs of spaces are collapsed, and the name is kept to 120 characters.

**Valid, not just openable.**
- Tests run `OpenXmlValidator` and expect zero errors.
- Tests read the file back and check that the text of the body paragraphs, in document order, equals the order the page shows. That order is what an applicant-tracking system reads.

## Saving: generated is not sent

- **Generate** creates an `ApplicationDocument`:
  - kind `Cv`, label "Tailored CV";
  - `SentOn` set to today;
  - the content type for `.docx`;
  - the file under `data/tracker/files/<entry>/<document>-<file name>`, like uploads.
  It also stores the choices in a new nullable column, `ApplicationDocument.CvChoices` (`cv_choices`, text): JSON of `{ headline, summary, bulletIds }`.
- A document with `CvChoices` is a **generated** document:
  - the documents view gains `generated: true`;
  - the list shows a "generated" badge and "Generated 12 Mar" instead of "Sent 12 Mar".
- If the owner edits the file in Word before sending, they upload the final file as usual, and that upload is the **sent** copy. They can delete a generated copy they didn't use.
- **Migration:** one new migration in `ApplicationsDb` adds the nullable column. Existing rows get null, so older documents are "sent" documents.
- **Backup:**
  - `tracker.json` gains the optional field through the entity.
  - `MirrorFile.CurrentFormat` stays 1, because the field is additive.
  - A format-1 file without the field restores with nulls.
- **Write pipeline:** generation goes through the same pipeline as every 2b write:
  - the `X-Tracker-Client` header check;
  - the gate that stays closed on a corrupt backup;
  - `Tracking.CommitAsync` with the mirror update;
  - the same file-then-row ordering and cleanup as uploads, so a failed commit leaves no stray file;
  - the existing delete-entry cleanup, which removes the entry's files folder.

## API (under `/api/tracker`)

| Method and path | Body | Returns | Errors |
|---|---|---|---|
| `GET /cv` | | `{ ready, problems[] }` for the master CV | |
| `GET /cv/full` | | The untailored full CV as `.docx`: every bullet in file order, `headline` and the first summary | 400 `field: "cv"` when the master CV has problems |
| `GET /applications/{id}/cv` | | The proposal (below) | 404 when the entry is missing; 400 `field: "cv"` |
| `PUT /applications/{id}/posting-text` | `{ postingText, version }` | 204 | 400 `field: "postingText"` when too long; 404; 409 when stale |
| `POST /applications/{id}/cv` | `{ headline, summary, bulletIds[] }` | 201 `{ documentId, fileName }` | 400 `field: "headline"`, `"summary"` or `"bulletIds"`; 400 `field: "cv"`; 404 |

**The proposal** carries:
- the entry's id, title and company;
- the text source (`entry`, `market` or `none`), `thin`, and the entry's version (for the posting-text form);
- the posting skills, each with its weight and profile level;
- `headline` and every summary as `{ id, families, text }`, plus `suggestedSummaryId`;
- the skill groups, whose items carry their skills and whether they match;
- the sections (`{ id, kind: role|project, title, company, location, dates, maxBullets, bullets[{ id, text, skills[], score, always }] }`) in file order;
- the certifications with their skills;
- `suggestion` as `{ headline, summary, bulletIds }`;
- `saved` as `{ generatedOn, headline, summary, bulletIds, droppedIds[] }`, or null.

**Input rules for generate:**
- `headline` is required, up to `Rules.MaxText`.
- `summary` may be empty, up to 2,000 characters.
- `bulletIds` holds up to 300 ids. They must be unique and every one must exist in `cv.json`. Each bullet renders in its own section, and the list's order gives the order within each section.
- Text containing a NUL character gets a 400 for its field. This applies to `headline`, `summary` and `postingText`. 2b's own text fields still give a 500 for NUL, and that is on the fixes list.

## Dashboard

- **Entry page, Documents panel:**
  - a **Tailor CV** link to `/tracker/:id/cv`;
  - generated documents show the "generated" badge and "Generated <date>".
- **Tracker page:**
  - a **Download full CV** link, shown when `GET /cv` is ready;
  - otherwise a hint naming `config/cv.example.json`.
- **Tailor page** (`/tracker/:id/cv`): two columns on a wide window and one column on a narrow one.
  - **Left:**
    - the coverage chips, grouped by state;
    - the thin-text warning with a **Paste the full job text** form, which saves and then refetches the proposal;
    - the word count and "about N pages", with the warning above 2.0.
  - **Right:**
    - headline input;
    - summary variant picker plus a text box;
    - a read-only preview of the skills section;
    - each section with a tickbox per bullet, where the bullet's skills are chips and matching ones are highlighted, ↑/↓ buttons to reorder the selected bullets, and "4 of 6". Going over `maxBullets` warns but doesn't block;
    - **Reset to suggestion** and **Generate .docx**.
  - Generate saves, downloads through the existing document download, and refreshes the tracker queries.
  - Unsaved ticks live in the page. Generating is what saves them.
  - When `cv.json` is missing or invalid, a banner lists the problems and the rest of the page is hidden.
  - **Accessibility:** tickboxes are labelled by their bullet text, and the ↑/↓ buttons have labels such as "Move up: <first words>".

## Observability

- Generation runs in an activity, `tracker.cv.generate`, tagged with the entry id, the bullet count and the file size.
- Master CV problems are logged at warning level as a problem count only.
- No CV or posting text appears in logs or traces.

## Testing

Everything is test-first, as in 2a and 2b.

- **Tracker.Core:**
  - `SkillText.ForMatching` strips both notice kinds and keeps the title.
  - `Posting.Interpret` finds the same skills as before. All 240 existing tests stay green.
- **Api.Tests, unit tests** (no database):
  - **`MasterCv`:**
    - the example file loads;
    - duplicate ids across sections;
    - an unknown skill tag;
    - an unknown family;
    - missing required fields;
    - a bad `yyyy-MM` date;
    - a missing file;
    - invalid JSON.
    Every problem is reported with its path, and all problems are reported together.
  - **`CvTailor`:**
    - scoring, with title skills double;
    - `always` kept even beyond `max`;
    - the fill in file order;
    - tie order;
    - the summary chosen by role family and the fallback;
    - skill items reordered within groups;
    - saved choices applied, with dropped ids reported;
    - the thin-text rule.
  - **`CvDocx`:**
    - zero `OpenXmlValidator` errors;
    - body text in the expected order;
    - empty sections left out;
    - a role title kept even with no bullets;
    - the contact line;
    - no header or footer parts and no drawings;
    - the file-name cleanup.
- **Api.Tests, integration tests** (Testcontainers):
  - the proposal for a market entry and for a pasted entry;
  - entry text winning over market text;
  - posting-text saves creating no inbox file;
  - a stale posting-text save getting a 409;
  - generate saving the document, the file and the choices, and updating the mirror;
  - a second proposal starting from the saved choices;
  - CSRF header required on PUT and POST;
  - the `cv` problems as a 400;
  - unknown and duplicate bullet ids as a 400;
  - the new migration upgrading a database that holds 2b data;
  - a format-1 `tracker.json` without `cvChoices` still restoring;
  - deleting an entry removing its generated files.
- **Vitest:**
  - coverage states updating as bullets are ticked;
  - clicking an "available" chip highlighting its bullets;
  - reordering;
  - reset;
  - the over-max warning;
  - the paste-text flow;
  - generate then download;
  - the problems banner;
  - the generated badge on the entry page;
  - the full-CV link and hint.

## Decision notes to add

- **ADR 0012: tailor by selecting true bullets, not by generating text.**
  - Honesty: the tool can't invent a claim.
  - Repeatability: the same inputs give the same CV, so it can be tested.
  - Nothing leaves the PC.
  - AI rewording can build on this in phase 3, as suggestions the owner accepts into `cv.json`.
- **ADR 0013: Word via the Open XML SDK, with a single-column layout for applicant-tracking systems.**
  - Why `.docx` over PDF.
  - Why code-built rather than a Word template: templates tend to use tables and text boxes, and filling their content controls is fragile.
  - Why validation is part of the tests.
- **README:** a "CV tailoring" section covering setup (`cv.json`), tailoring, generating, and uploading the final file.

## After the build (not code)

- Claude drafts the owner's real `config/cv.json` from their existing CV and their answers about recent work.
- Every conflict or uncertain claim goes on a list for the owner to decide rather than being resolved silently, for
  example where the existing CV and the owner's answers disagree, or which email and phone number to use.
- Once the owner approves it, the full-CV download is the Canadian-format rewrite.
