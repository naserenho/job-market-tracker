# 0011 — Optimistic concurrency with Postgres xmin

**Status:** Accepted (2026-09-29)

## Context
The dashboard can be open in two tabs. Without a check, the second of two edits to the same entry silently overwrites the first.

## Decision
- Entries and companies map Postgres's system column `xmin` as a row version (`uint Version` with `IsRowVersion()`), which changes on every update.
- The dashboard reads the version with the entry and sends it back with each edit.
- The API uses it as the original value. EF's `UPDATE … WHERE id = @id AND xmin = @version` then matches nothing if someone saved in between.
- The resulting `DbUpdateConcurrencyException` becomes a 409: "This entry changed in another tab. Reload to see the latest." The page offers **Reload**.

**Alternatives considered:**
- **Last writer wins:** silent data loss.
- **Pessimistic locks:** a lock held across a form that might sit open for an hour.
- **Our own version column:** works, but `xmin` is free and can't be forgotten in an update.

## Consequences
- No extra column or bookkeeping.
- Follow-ups, contacts, documents and status changes also move the entry's version, because they update its "last updated" time. Those writes aren't version-checked themselves; they don't overwrite anyone's fields.
- **The forms must therefore tell a real conflict from a refresh.** The detail and company forms keep unsaved edits when the entry is refetched (`useEditableForm`):
  - with no unsaved changes, they take the new values;
  - with unsaved changes, they adopt the new version only when their own fields are unchanged on the server (the refresh came from a follow-up or a status change);
  - otherwise they keep the old version, so **Save** gets the 409 and **Reload**.
- The final review caught the first version of this, which remounted the forms on every version change and silently dropped unsaved edits.
