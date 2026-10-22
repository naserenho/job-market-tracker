# 0012 — Tailor a CV by selecting true bullets, not generating text

**Status:** Accepted (2026-09-29)

## Context
During a job search every application needs a CV that puts forward what that posting asks for. Rewriting it in Word each time is slow. A language model could reword the CV per posting, but it can also invent experience, and a CV with one false claim is worse than a generic one.

## Decision
- The owner keeps every true bullet in one master file, `config/cv.json`, tagged with skills. It is gitignored like `profile.json`.
- For a posting, the API finds its skills with the same function and catalog as the market data. Skills in the title weigh double.
- It scores each bullet by the posting skills it shows, keeps `always` bullets, and fills each role up to its `maxBullets` with the best scorers and then the rest in file order.
- The owner ticks, unticks, reorders and edits the headline and summary, then generates. **The tool never writes a sentence**: everything in the file comes from `cv.json` or from the owner's own edits on the page.
- Skills the posting wants that `cv.json` doesn't show are listed as "not in your CV". When `profile.json` says the owner knows one, the page says to add a true bullet; otherwise it says "gap".

**Alternatives considered:**
- **AI rewording now:** it can invent claims, it sends the CV to a cloud service, and its output can't be pinned by tests. That belongs in phase 3, and only as suggestions the owner copies into `cv.json`.
- **Advice only, editing Word by hand:** smallest to build, but the slow step stays slow.

## Consequences
- The same inputs always give the same CV, so the rules are unit-tested.
- A CV is only as good as `cv.json`: the Tailor page shows where it falls short.
- Saved choices point at bullet ids. Rewording a bullet keeps its id; deleting one is reported as "no longer in cv.json".
