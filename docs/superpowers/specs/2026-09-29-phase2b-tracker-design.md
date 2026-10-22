# Phase 2b: Shortlist and Application Tracker

**Status:** Draft for review · **Date:** 2026-09-29 · **Builds on:** [Phase 2a spec](2026-09-28-phase2a-dashboard-design.md)

## Context

Phase 2a gave the owner a local, read-only dashboard over the market data, which is rebuilt from the archive every Tuesday and Friday (TRUNCATE + COPY). Phase 2b adds the owner's own records: postings they shortlist and every application they make, through to an offer or a rejection. Phase 2c (tailoring the CV per posting) builds on these records next.

**Decisions made with the owner:**
- **What a record holds:** status and dates, follow-ups, contacts and notes, the documents sent, and company details (office location, work model, team size, working hours, annual leave and so on).
- **Jobs the collector never saw** (LinkedIn, bank career sites, referrals) are pasted into the dashboard. That creates the entry immediately and also saves the posting to `data/inbox/`, so the next rebuild counts it in the market data.
- **Main screen:** an "Up next" strip of follow-ups above a sortable table, not a Kanban board.
- **Backup:** an automatic JSON copy after every change, with an automatic restore if the tables are ever found empty.
- **Storage:** approach A. The API owns separate tables in the same Postgres database, and the rebuild never touches them.

**The API is no longer read-only.** It writes only its own tables and files under `data/tracker/` and `data/inbox/`. The market tables stay read-only for it, as in 2a.

## Goals

- Keep every shortlisted posting and application in one place with its status, timeline and next action, so nothing gets lost and no follow-up is missed.
- The owner's records survive every market rebuild and can be recovered if the Docker volume is lost. They are the only data here that can't be recollected.
- Adding an entry takes one click from a market posting, or a short paste form.
- **Learning value:**
  - EF Core writes, migrations and transactions;
  - validation and optimistic concurrency;
  - file uploads;
  - designing user data that lives next to a destructive rebuild.

## Non-goals

- CV tailoring and generating documents. That is 2c; 2b only stores the documents that were sent.
- Reminders outside the dashboard (email, Windows notifications), access from other devices, and authentication. Everything stays on this PC.
- Contacts shared across applications, and a CRM for recruiters.
- Changing the market data or the rebuild, apart from the pasted inbox files the rebuild already reads.

## Data and storage

### Separate context and tables

- A new EF Core context, `ApplicationsDb`, lives in `src/Api/Applications/`. It has its own migrations and its own history table, `__applications_migrations`.
- Its tables sit in the tracker's Postgres database but are **not** in `TrackerTables.All`, so the rebuild's TRUNCATE never touches them.
- The Processor doesn't reference them.
- The API applies this context's migrations itself, on the first tracker request (see Errors).

| Table | Columns (besides `id` uuid, `created_at` and `updated_at`) |
|---|---|
| `companies` | `name` (display), `name_key` (trimmed, lower-case, unique), `website`, `office_location`, `company_size`, `annual_leave`, `benefits`, `notes` |
| `applications` | Link to the company and to the market posting (`posting_id`; nullable, a stable market id such as `greenhouse:101` or `inbox:<file>`). The application's own copy of `title`, `url`, `location` and `posting_text` (the pasted text; empty for market postings, whose text is read from the market tables). `status`, `origin` (`market` / `pasted`) and `found_via`. Job details: `work_model` (`remote` / `hybrid` / `onsite` / unknown), `office_days`, `team_size`, `working_hours`. Salaries: `salary_posted`, `salary_asked`, `salary_offered`. `notes`. `xmin` is the concurrency token. |
| `application_events` | `application_id`, `on_date` (date), `from_status`, `to_status`, `note` |
| `follow_ups` | `application_id`, `action`, `due_on` (date), `done_on` (date, nullable) |
| `contacts` | `application_id`, `name`, `role`, `email`, `phone`, `linkedin`, `notes` |
| `application_documents` | `application_id`, `kind` (`cv` / `cover_letter` / `other`), `label`, `file_name`, `content_type`, `size_bytes`, `sent_on` (date) |

- **Text fields:** salaries, team size, working hours and annual leave are free text ("CAD 130–150k", "8–10 people", "20 days + 5 personal"). Postings and people phrase them in too many ways to force numbers.
- **Dates:** the date the owner enters (`DateOnly`), not converted between time zones.
- **Deleting an application** removes its events, follow-ups, contacts, documents and files. Companies stay.

### Statuses

- The flow is `Shortlisted → Applied → Screening → Interviewing → Offer → Accepted`, and `Rejected` or `Withdrawn` close an application.
- Any change is allowed, including skipping steps or reopening a closed application.
- Every change adds an `application_events` row (the timeline). A new entry starts with one event: "created as `<status>`".

### Pasted jobs

- `POST /api/tracker/applications/paste` needs a title, a company and the job text; the link and location are optional.
- It writes `data/inbox/<yyyy-MM-dd>-<company-slug>-<title-slug>.txt` in the existing inbox format (`URL:`, `Company:`, `Title:`, `Location:`, `Saved:` headers, a blank line, then the text).
  - The file name gets a numeric suffix if one already exists.
  - Before writing, the content is parsed with `InboxReader.Parse`, and the entry is only saved if that succeeds. This proves the next rebuild will read it.
- The application stores `posting_id = "inbox:<file name>"`. After the next rebuild the posting exists in the market data, and the entry links to it automatically.
  - A pasted job outside the GTA / remote-Canada scope stays unlinked. The page shows "not in the market data (outside the tracked area)".

### JSON mirror and restore

- After every committed write, the API serialises all six tables to `data/tracker/tracker.json`. It writes a temp file in the same folder, then replaces the old file.
- Uploaded files live in `data/tracker/files/<application id>/<document id>-<safe name>` and are written before the database row is committed.
- **Restore:** after the migration check, if all six tables are empty and `tracker.json` has at least one company or application, the API restores everything in one transaction, keeping ids, and logs "Restored N applications from data/tracker/tracker.json".
- Because the mirror is rewritten after every change, deleting the last entry also empties the mirror, so a deliberate "delete everything" never comes back.
- **If the mirror write fails** (disk full, file locked): the change itself stays saved. The error is logged, and `GET /api/tracker/status` reports `lastBackupError` until a later write succeeds; the dashboard shows a banner meanwhile.
- `data/` is gitignored, so none of this is committed.

## API (under `/api/tracker`)

| Endpoint | Does |
|---|---|
| `GET status` | `{ migrated, lastBackupAt, lastBackupError, counts by status, overdue }` |
| `GET applications` | Table rows, filtered by `status` (comma list), `q` (title / company contains), and `sort`: `next` (the default) puts entries with an open follow-up first, by due date, then the rest by last update; `updated` sorts by last update, newest first; `company` sorts by company name, then title. Each row has id, title, company, status, the next open follow-up (action and due date), `updatedAt`, `postingId`, and `live` (the posting is in the current market snapshot and was seen in the latest run) |
| `GET up-next` | Open follow-ups that are overdue or due within 7 days, soonest first, with each application's title and company |
| `GET index` | `{ postingId: { id, status } }` for every entry with a posting id, used to mark market rows |
| `GET companies` | Id and name, for suggestions |
| `POST applications` `{ postingId, status? }` | Shortlist (default) or record a market posting, copying title, company, url and location from the market snapshot. 404 for an unknown posting; 409 with `{ id }` if it is already tracked |
| `POST applications/paste` | See Pasted jobs. 201 with the new id |
| `GET applications/{id}` | The full entry: company, timeline, follow-ups, contacts, documents, and the posting text (pasted text, or the market `postings.description` when linked). For a linked posting, also the market facts: match score, live, and first and last seen |
| `PATCH applications/{id}` / `PATCH companies/{id}` | Edit fields. The body carries `version` (xmin); a stale version gives 409 |
| `POST applications/{id}/status` `{ status, on?, note? }` | Change status and add a timeline event (`on` defaults to today) |
| `POST` / `PATCH` / `DELETE` follow-ups and contacts | Under `applications/{id}/follow-ups/{fid}` and `applications/{id}/contacts/{cid}` |
| `POST applications/{id}/documents` | Multipart upload: `file`, `kind`, `label` and `sentOn` |
| `GET` / `DELETE` `documents/{docId}` | Download and delete |
| `DELETE applications/{id}` | Removes the entry and its files |

## Errors and edge cases

**Problem details, as in 2a:**
- **400** names the field. Examples: a missing title, an unknown status, a due date that isn't a date, a file over 10 MB or not PDF, DOCX, DOC, TXT or MD.
- **404** for an unknown id.
- **409** for a duplicate shortlist (with the existing id) or a stale version ("This entry changed in another tab. Reload to see the latest.").

**Database unavailable:** every tracker endpoint answers 503 with the same "start Docker" message as 2a. There's no stale mode; the dashboard must never show an edit as saved when it wasn't.

**Migration timing:**
- The first tracker request migrates `ApplicationsDb` under a lock, then runs the restore check.
- If the database is down, the request answers 503 and the next one retries.
- Market endpoints never wait on this.

**Other cases:**
- **Two tabs editing the same entry:** optimistic concurrency via xmin. A save carrying an old version is refused, not merged.
- **Upload failure:** a file written without its database row (or the reverse) is cleaned up. The file is written first, the row second, and the file is deleted if the row fails.
- **File names:** stored with only letters, digits, `-`, `_` and `.`; the original name is kept as the label default.
- **Company names:** "RBC", "rbc " and "Rbc" are one company (`name_key`). Renaming a company updates `name` only when the new key doesn't belong to another company (409 otherwise).
- **Unexpected errors:** 500 without details, as in 2a.

## Dashboard

**Navigation**
- A fifth link, **Tracker**, with a red count of overdue follow-ups.
- The market filter bar is hidden on tracker pages.

**`/tracker`**
- Status counts across the top, and a **Paste a job** button.
- **Up next:** overdue items in red, then today, then this week, each with **Done**.
- **Table:** role, company, status chip, next action with its date, last update, and a live dot.
  - Filter by status, search, and sort; these stay in the URL.
  - The table scrolls sideways at phone width.
- Empty state: "Nothing tracked yet. Shortlist postings from the Postings page, or paste a job."

**`/tracker/new`**
- The paste form: title, company (suggests existing companies), link, location, starting status (Shortlisted or Applied) and the job text.
- Saving opens the entry.

**`/tracker/:id`**
- **Header:** title, company, posting link, and (if linked) match score and live/expired.
- **Status:** a control with an optional date and note.
- **Sections:**
  - timeline;
  - follow-ups (add, mark done);
  - job details;
  - company ("shared by all your <company> applications");
  - contacts;
  - documents (upload, download, delete);
  - notes;
  - the posting text (collapsed).
- **Editing:** explicit **Save** buttons, errors next to their fields, and **Reload** on a conflict.
- **Delete:** a confirmation step inside the page.

**Market pages**
- The Postings table, skill-detail examples and the Overview's best matches get a **Shortlist** button per row.
- Once an entry exists, the button becomes a status chip that links to it, using `GET index`.

**Backup warning:** a banner on tracker pages while `lastBackupError` is set.

## Observability

- An ActivitySource `Tracker.Applications` creates a span per write (named after the operation) and one for each backup write.
- A counter `tracker.applications.changes` counts writes, tagged by operation.
- A log line records each restore and each backup failure.

## Testing

1. **Unit tests** (Api.Tests, no database):
   - status changes add events;
   - validation of every input rule;
   - company-key matching;
   - the inbox file writer (its output passes through `InboxReader.Parse`, and existing names get a suffix);
   - the mirror (it serialises and restores identically, and a failed temp write leaves the old file intact);
   - safe file names.
2. **Integration tests** (WebApplicationFactory + Testcontainers):
   - **Survival:** tracker rows survive a full `RebuildService` run.
   - **Pasted job:** a pasted job's inbox file becomes a posting after a rebuild, and the entry is then linked and live.
   - **Duplicate shortlist:** returns 409 with the existing id.
   - **Stale edit:** a stale `version` returns 409.
   - **Restore:** empty tables plus a mirror file restore on the first request.
   - **Database down:** tracker endpoints return 503 when the database is unreachable.
   - **Documents:** upload, download and delete work, and deleting an application removes its files.
   - **Mirror:** it is updated after each write.
3. **Dashboard (Vitest):**
   - the tracker table (filters and sort in the URL, empty state);
   - **Done** on an Up next item;
   - paste-form validation;
   - a status change;
   - the Shortlist button turning into a chip (including the 409 case);
   - the conflict message with **Reload**;
   - the overdue count in the nav;
   - the backup banner.
4. **Live check** on the owner's setup:
   - Shortlist a real posting and paste a test job.
   - Run a real Processor rebuild. The collector is never run.
   - Confirm both entries survived and the pasted job is now linked.
   - Delete the test entries and the test inbox file, and confirm `tracker.json` is empty again.

## Decision notes to add

- **0010:** the owner's data lives beside a destructive rebuild. It covers:
  - the separate context, migrations history and tables;
  - the stable posting ids plus snapshot copies;
  - the JSON mirror and automatic restore.
  - **Alternatives:** JSON files only, or tables in the Processor's context.
- **0011:** optimistic concurrency with Postgres `xmin`, instead of last-writer-wins or pessimistic locks.
