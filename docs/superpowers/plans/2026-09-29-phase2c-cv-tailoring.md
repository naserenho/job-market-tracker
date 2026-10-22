# Phase 2c CV Tailoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a **Tailor CV** page to each tracker entry. It proposes a CV built only from the owner's own true bullets in `config/cv.json`, lets the owner adjust it, and saves a single-column Word `.docx` on the entry.

**Architecture:**
- **Tracker.Core:** one shared function finds the text a posting's skills are matched in. The Processor and tailoring both use it.
- **API (`src/Api/Cv/`):** pure units with no database or HTTP, each tested alone:
  - `MasterCvFile` reads and validates `cv.json`;
  - `CvLayout` turns a CV and the owner's choices into paragraphs in page order;
  - `CvTailor` scores bullets and proposes a selection;
  - `CvDocx` writes the paragraphs as Word XML.
- **Endpoints:** under `/api/tracker`, so they inherit the gate, the CSRF header and the mirror.
- **Storage:** a generated file is an ordinary `ApplicationDocument` with one new nullable column, `cv_choices`.
- **Dashboard:** a new route `/tracker/:id/cv`, plus links from the entry page and the tracker page.

**Tech Stack:**
- Back end: .NET 10, ASP.NET Core minimal APIs, EF Core 10 with Npgsql, `DocumentFormat.OpenXml` 3.x (Open XML SDK, MIT licence), xUnit, Testcontainers.
- Front end: React 19, TanStack Query 5, React Router 7, Vitest with Testing Library.

**Spec:** `docs/superpowers/specs/2026-09-29-phase2c-cv-tailoring-design.md`

## Global Constraints

**Naming and placement**
- New API code goes in namespace `Api.Cv` (`src/Api/Cv/`), with tests in `Api.Tests.Cv` (`tests/Api.Tests/Cv/`). Dashboard code goes in `src/Dashboard/src/tracker/cv/`.
- The master CV is `config/cv.json`, which is gitignored and must never be read by tests, printed or committed. The committed format example is `config/cv.example.json` and describes a made-up person, "Alex Example".
- The API reads the master CV from `Tracker:CvPath`, defaulting to `<repo>/config/cv.json`. `ApiFactory` defaults it to the example file.

**Exact messages and values**
- **Missing master CV:** "config/cv.json is missing. Copy config/cv.example.json to config/cv.json and fill in your CV."
- **Posting-text save:** a stale version gets the existing 409, "This entry changed in another tab. Reload to see the latest."
- **Generated document:**
  - label "Tailored CV", kind `Cv`, `SentOn` = today;
  - content type `application/vnd.openxmlformats-officedocument.wordprocessingml.document`;
  - file name `<person.name> - CV - <company>.docx`, or `<person.name> - CV.docx` for the full CV, at most 120 characters.
- **Weights:** a posting skill also found in the entry title weighs 2; every other posting skill weighs 1.
- **Thin text:** fewer than 3 posting skills, or the text is the market description of a posting that isn't full text.
- **Defaults and limits:**
  - `maxBullets` defaults to 5;
  - `headline` up to 200 characters and required;
  - `summary` up to 2,000 characters;
  - `bulletIds` up to 300, unique, each one present in `cv.json`;
  - a NUL character in `headline`, `summary` or `postingText` is a 400 for that field.
- **Length estimate:** 450 words a page, shown to one decimal place, with a warning above 2.0 pages.
- **Text source:** exactly `entry`, `market` or `none`.
- **Telemetry:** the generate activity is named `tracker.cv.generate`, with tags for the entry id, the bullet count and the byte size. No CV or posting text goes into logs or traces. Master CV problems are logged as a count only.

**Carried from 2b**
- Every non-GET tracker request needs `X-Tracker-Client`.
- Problem details: 400 carries `field`.
- Files go under `<DataRoot>/tracker/files/<entryId:N>/<documentId:N>-<file name>`, written file first and row second, with the file deleted if the save fails.
- Every write goes through `Tracking.CommitAsync`, which refreshes `tracker.json`.
- Dashboard writes use `send()`, which adds the header, and a successful write invalidates `['tracker']`.

**Code style**
- Code that contains backslashes is written with the Write or Edit tools, never with shell heredocs.
- Every commit message ends with the session's attribution trailer lines.

**After merge (not a task):** Task 1 changes Tracker.Core, which the Processor uses. After merging, re-run `scripts/register-task.ps1` and the published `Processor --report-only`.

## Review Focus

1. **The owner's real `config/cv.json` must stay private.**
   - Tests must never read it: `ApiFactory` defaults `Tracker:CvPath` to the example.
   - It must be gitignored.
   - Logs carry only the problem count.
   - Owner: Task 2 pins the `.gitignore` line and Task 8 pins the factory default. The reviewer must not open `config/cv.json`.
2. **A hand-edited `cv.json` with mistakes** (duplicate ids, a misspelt skill, a bad date, a link without `https://`, broken JSON) gives a readable list of problems, each with its path, and never a 500. Owner: Task 2 (validation) and Task 8 (400 `field: "cv"`).
3. **Saved choices that point at bullets since renamed or deleted in `cv.json`.**
   - The page still opens and reports the dropped ids.
   - Generating with a stale id gets a 400 on `bulletIds` asking for a reload, not a 500.
   - Owner: Task 4 and Task 8.
4. **CV text with characters that are special or invalid in XML** (`&`, `<`, quotes, emoji, control characters) still produces a document Word opens without "unreadable content". Owner: Task 5 (`Special_characters_survive_and_stay_valid`).
5. **Company names with characters Windows forbids, or with path pieces** (`AT&T / Bell: Canada?`, `..\..\Windows`) produce a safe file name, and the file stays inside the entry's folder. Owner: Task 5 (file-name theory) and Task 8 (generated file name).

---

## File Structure

```
src/Tracker.Core/Interpreting/
  SkillText.cs            Task 1  the one text-to-match rule (title + description minus hiring notices)
  Posting.cs              Task 1  uses SkillText
src/Api/Cv/
  MasterCv.cs             Task 2  master CV model records + CvLoad
  MasterCvFile.cs         Task 2  reads and validates cv.json, every problem with its path
  CvLayout.cs             Task 3  CvChoices, CvParagraph/CvRun/CvStyle, paragraphs in page order, word counts
  CvProposal.cs           Task 4  proposal view records (what the Tailor page gets)
  CvTailor.cs             Task 4  posting skills, scores, pre-selection, summary, saved choices
  CvDocx.cs               Task 5  Word renderer + safe file names
  CvSettings.cs           Task 8  where cv.json lives
  CvRequests.cs           Task 8  request/response records, CvRules (input checks), CvChoicesJson
  CvEndpoints.cs          Task 8  /cv, /cv/full, /applications/{id}/cv (GET, POST)
src/Api/Applications/
  Entities.cs             Task 6  ApplicationDocument.CvChoices
  Migrations/*_AddCvChoices.cs  Task 6
  TrackerViews.cs         Task 6  DocumentView.Generated
  EntryText.cs            Task 7  entry text first, then market text
  TrackerEditEndpoints.cs Task 7  PUT /applications/{id}/posting-text
  TrackerListEndpoints.cs Tasks 6-7  detail uses EntryTexts and the Generated flag
  Rules.cs                Task 7  NoNul
  TrackerEndpoints.cs     Task 8  MapCvEndpoints
src/Api/Program.cs        Task 8  CvSettings
src/Api/Api.csproj        Task 5  DocumentFormat.OpenXml
config/cv.example.json    Task 2
.gitignore                Task 2  config/cv.json
src/Dashboard/src/tracker/
  api.ts                  Task 9  CV types and calls
  cv/tailor.ts            Task 9  pure coverage/selection/length logic
  cv/sample.ts            Task 9  test proposal
  cv/download.ts          Task 10 startDownload
  cv/PostingTextForm.tsx  Task 10
  cv/CoveragePanel.tsx    Task 10
  cv/SectionPicker.tsx    Task 10
  cv/TailorPage.tsx       Task 10
  entry/Documents.tsx     Task 11 Tailor CV link, generated badge
  TrackerPage.tsx         Task 11 Download full CV link / hint
src/Dashboard/src/App.tsx Task 10 route /tracker/:id/cv
src/Dashboard/src/theme.css Task 10
docs/decisions/0012-*.md, 0013-*.md, README.md  Task 12
tests/Tracker.Core.Tests/Interpreting/SkillTextTests.cs   Task 1
tests/Api.Tests/Cv/{MasterCvTests,CvSamples,CvLayoutTests,CvTailorTests,CvDocxTests,CvRulesTests,CvEndpointTests}.cs  Tasks 2-5, 8
tests/Api.Tests/Applications/{TrackerUpgradeTests,PostingTextTests}.cs  Tasks 6-7
tests/Api.Tests/Applications/{TrackerDocumentTests,TrackerRestoreTests}.cs  Task 6 (additions)
tests/Api.Tests/TestSupport/ApiFactory.cs  Task 8
```

---

### Task 1: One skill-text rule in Tracker.Core

**Files:**
- Create: `src/Tracker.Core/Interpreting/SkillText.cs`
- Modify: `src/Tracker.Core/Interpreting/Posting.cs:47-48` and `:57-63`
- Test: `tests/Tracker.Core.Tests/Interpreting/SkillTextTests.cs`

**Interfaces:**
- Produces: `public static string Tracker.Interpreting.SkillText.ForMatching(string title, string description)`. It returns `"{title}\n{description without hiring AI disclosures and accommodation notices}"`.

- [ ] **Step 1: Write the failing test**

`tests/Tracker.Core.Tests/Interpreting/SkillTextTests.cs`:

```csharp
using Tracker.Interpreting;

namespace Tracker.Core.Tests.Interpreting;

public class SkillTextTests
{
    [Fact]
    public void Keeps_the_title_and_drops_hiring_notices()
    {
        var text = SkillText.ForMatching("Senior .NET Developer",
            "We use AI tools to screen applications. Build services in C#. We will accommodate accessibility needs.");

        Assert.StartsWith("Senior .NET Developer\n", text);
        Assert.Contains("Build services in C#.", text);
        Assert.DoesNotContain("screen applications", text);
        Assert.DoesNotContain("accommodate", text);
    }

    [Fact]
    public void Real_ai_work_is_kept() =>
        Assert.Contains("Build AI features", SkillText.ForMatching("Developer", "Build AI features for our dashboard."));
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Tracker.Core.Tests --filter "FullyQualifiedName~SkillTextTests"`
Expected: build error CS0103, "The name 'SkillText' does not exist in the current context".

- [ ] **Step 3: Move the rule into SkillText**

Create `src/Tracker.Core/Interpreting/SkillText.cs`. Then cut lines 57–63 of `Posting.cs`, which hold the two `[GeneratedRegex]` members `HiringAiDisclosure` and `AccommodationNotice` with their comments, and paste them into the class body below **unchanged**:

```csharp
using System.Text.RegularExpressions;

namespace Tracker.Interpreting;

// The text a posting's skills are matched in: its title and description, minus hiring notices (Ontario's AI-in-hiring
// disclosure, accommodation statements) that say nothing about the job's skills. The market data and CV tailoring both
// use it, so they always agree on a posting's skills.
public static partial class SkillText
{
    public static string ForMatching(string title, string description) =>
        $"{title}\n{AccommodationNotice().Replace(HiringAiDisclosure().Replace(description, " "), " ")}";

    // (paste the two [GeneratedRegex] members from Posting.cs here, unchanged)
}
```

In `Posting.cs`, replace lines 47–48:

```csharp
        // Hiring notices (Ontario's AI-in-hiring disclosure, accommodation statements) say nothing about the job's skills.
        var skillText = $"{raw.Title}\n{AccommodationNotice().Replace(HiringAiDisclosure().Replace(raw.Description, " "), " ")}";
```

with:

```csharp
        var skillText = SkillText.ForMatching(raw.Title, raw.Description);
```

The `var text = $"{raw.Title}\n{raw.Description}";` line on 46 stays, because the citizenship check uses it. `Interpreter` stays `partial`, since `CitizenshipOrClearance` is still a generated regex.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Tracker.Core.Tests`
Expected: PASS, 242 tests (the existing 240 plus 2). `InterpreterTests` still pass, which shows the Processor's skills are unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/Tracker.Core/Interpreting/SkillText.cs src/Tracker.Core/Interpreting/Posting.cs tests/Tracker.Core.Tests/Interpreting/SkillTextTests.cs
git commit -m "Share the posting skill-text rule in Tracker.Core"
```

---

### Task 2: The master CV file

**Files:**
- Create: `src/Api/Cv/MasterCv.cs`, `src/Api/Cv/MasterCvFile.cs`, `config/cv.example.json`
- Modify: `.gitignore` (after the `config/profile.json` line)
- Test: `tests/Api.Tests/Cv/MasterCvTests.cs`

**Interfaces:**
- Consumes: `SkillCatalog.Match(string)`, `SkillCatalog.Skills`, `TitleRules.ParseFamily(string)`, all from Tracker.Core.
- Produces, in namespace `Api.Cv`:
  - `record CvLink(string Label, string Url)`
  - `record CvPerson(string Name, string Location, string Status, string Phone, string Email, IReadOnlyList<CvLink> Links)`
  - `record CvSummary(string Id, IReadOnlySet<RoleFamily> Families, string Text)`
  - `record CvSkillItem(string Text, IReadOnlySet<string> Skills)`
  - `record CvSkillGroup(string Name, IReadOnlyList<CvSkillItem> Items)`
  - `record CvBullet(string Id, string Text, IReadOnlySet<string> Skills, bool Always)`
  - `enum CvSectionKind { Role, Project }`
  - `record CvSection(string Id, CvSectionKind Kind, string Title, string Company, string Location, string Dates, int MaxBullets, IReadOnlyList<CvBullet> Bullets)`. `Dates` is already formatted, for example `"Oct 2023 – Present"`, or `""`.
  - `record CvEducation(string Degree, string School, string Years)`
  - `record CvCertification(string Name, string Year, IReadOnlySet<string> Skills)`
  - `record MasterCv(CvPerson Person, string Headline, IReadOnlyList<CvSummary> Summaries, IReadOnlyList<CvSkillGroup> SkillGroups, IReadOnlyList<CvSection> Sections, IReadOnlyList<CvEducation> Education, IReadOnlyList<CvCertification> Certifications)`, with `IEnumerable<CvBullet> Bullets`. `Sections` lists the roles, then the projects.
  - `record CvLoad(MasterCv? Cv, IReadOnlyList<string> Problems)`, with `bool Ready`.
  - `static class MasterCvFile`, with `const string Missing`, `const int DefaultMaxBullets = 5` and `static CvLoad Load(string path, SkillCatalog catalog)`.

- [ ] **Step 1: Write the failing tests**

`tests/Api.Tests/Cv/MasterCvTests.cs`:

```csharp
using Api.Cv;
using Tracker.Interpreting;
using Tracker.TestSupport;

namespace Api.Tests.Cv;

public class MasterCvTests
{
    private static readonly SkillCatalog Catalog = SkillCatalog.Load(RepoConfig.PathOf("skills.json"));

    private static CvLoad LoadText(string json)
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "cv.json");
        File.WriteAllText(path, json);
        return MasterCvFile.Load(path, Catalog);
    }

    [Fact]
    public void The_example_file_loads()
    {
        var load = MasterCvFile.Load(RepoConfig.PathOf("cv.example.json"), Catalog);

        Assert.Empty(load.Problems);
        var cv = load.Cv!;
        Assert.Equal("Alex Example", cv.Person.Name);
        Assert.Equal(new[] { "acme", "globex", "tracker" }, cv.Sections.Select(s => s.Id));
        Assert.Equal((CvSectionKind.Role, "Oct 2023 – Present", 4), (cv.Sections[0].Kind, cv.Sections[0].Dates, cv.Sections[0].MaxBullets));
        Assert.Equal("Jun 2016 – Sep 2023", cv.Sections[1].Dates);
        Assert.Equal((CvSectionKind.Project, 5), (cv.Sections[2].Kind, cv.Sections[2].MaxBullets));
    }

    [Fact]
    public void Bullet_skills_are_its_tags_plus_skills_named_in_its_text()
    {
        var cv = MasterCvFile.Load(RepoConfig.PathOf("cv.example.json"), Catalog).Cv!;

        var lead = cv.Bullets.Single(b => b.Id == "acme-lead");
        Assert.True(lead.Always);
        Assert.Superset(new HashSet<string> { "Technical Leadership", ".NET", "Next.js" }, (ISet<string>)lead.Skills.ToHashSet());
        Assert.False(cv.Bullets.Single(b => b.Id == "acme-cicd").Always);
    }

    [Fact]
    public void Every_problem_is_reported_with_its_path()
    {
        var load = LoadText("""
            {
              "person": { "name": "Sam", "links": [ { "label": "Site", "url": "example.com" } ] },
              "headline": "Developer",
              "summaries": [ { "id": "s1", "families": ["dotnet", "chef"], "text": "Hi." } ],
              "roles": [
                { "id": "r1", "title": "Dev", "start": "2023-13", "maxBullets": 0,
                  "bullets": [ { "id": "x-1", "text": "One." }, { "id": "x-1", "text": "Two.", "skills": ["Cobol"], "always": "yes" } ] }
              ]
            }
            """);

        Assert.Null(load.Cv);
        Assert.Equal(new[]
        {
            "person.email is required.",
            "person.links[0].url: use a full web address starting with https://.",
            "summaries[0].families[1]: unknown family 'chef' (use dotnet, fullstack, software_engineer, lead_manager or other).",
            "roles[0].company is required.",
            "roles[0].start: use year and month like 2023-10, not '2023-13'.",
            "roles[0].maxBullets must be a whole number, 1 or more.",
            "roles[0].bullets[1].id: \"x-1\" is used twice.",
            "roles[0].bullets[1].always must be true or false.",
            "roles[0].bullets[1].skills[0]: 'Cobol' is not a skill in config/skills.json (names must match exactly).",
        }, load.Problems);
    }

    [Fact]
    public void A_missing_file_is_one_problem_naming_the_example() =>
        Assert.Equal(new[] { MasterCvFile.Missing },
            MasterCvFile.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "cv.json"), Catalog).Problems);

    [Fact]
    public void Broken_json_names_the_line() =>
        Assert.Equal(new[] { "config/cv.json is not valid JSON near line 3." },
            LoadText("{\n  \"headline\": \"Dev\",\n  \"person\": { oops }\n}").Problems);

    [Fact]
    public void Leaving_out_optional_parts_uses_the_defaults()
    {
        var load = LoadText("""
            { "person": { "name": "Sam", "email": "sam@example.com" }, "headline": "Developer",
              "summaries": [ { "id": "s", "text": "Hi." } ],
              "roles": [ { "id": "r", "title": "Dev", "company": "Co", "start": "2020-01", "bullets": [ { "id": "b", "text": "Did C# work." } ] } ] }
            """);

        var cv = Assert.IsType<MasterCv>(load.Cv);
        var role = Assert.Single(cv.Sections);
        Assert.Equal((5, "Jan 2020 – Present", false), (role.MaxBullets, role.Dates, role.Bullets[0].Always));
        Assert.Contains("C#", role.Bullets[0].Skills);
        Assert.Empty(cv.SkillGroups);
        Assert.Empty(cv.Certifications);
    }

    [Fact]
    public void A_file_without_roles_or_summaries_says_what_to_add() =>
        Assert.Equal(new[] { "summaries: add at least one summary.", "roles: add at least one role." },
            LoadText("""{ "person": { "name": "Sam", "email": "s@example.com" }, "headline": "Dev" }""").Problems);

    [Fact]
    public void The_real_master_cv_is_gitignored() =>
        Assert.Contains("config/cv.json", File.ReadAllLines(Path.Combine(RepoConfig.Root, ".gitignore")));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~Api.Tests.Cv"`
Expected: build error CS0246, "The type or namespace name 'Cv' does not exist in the namespace 'Api'".

- [ ] **Step 3: Write the model**

`src/Api/Cv/MasterCv.cs`:

```csharp
using Tracker.Interpreting;

namespace Api.Cv;

public sealed record CvLink(string Label, string Url);

public sealed record CvPerson(string Name, string Location, string Status, string Phone, string Email, IReadOnlyList<CvLink> Links);

// A summary variant, suggested for postings whose title falls in one of its role families.
public sealed record CvSummary(string Id, IReadOnlySet<RoleFamily> Families, string Text);

// Skills: the tags written in cv.json plus every skill the catalog finds in the text.
public sealed record CvSkillItem(string Text, IReadOnlySet<string> Skills);

public sealed record CvSkillGroup(string Name, IReadOnlyList<CvSkillItem> Items);

public sealed record CvBullet(string Id, string Text, IReadOnlySet<string> Skills, bool Always);

public enum CvSectionKind { Role, Project }

// A role or a project. Dates are already formatted ("Oct 2023 – Present"; empty for an undated project).
public sealed record CvSection(string Id, CvSectionKind Kind, string Title, string Company, string Location, string Dates,
    int MaxBullets, IReadOnlyList<CvBullet> Bullets);

public sealed record CvEducation(string Degree, string School, string Years);

public sealed record CvCertification(string Name, string Year, IReadOnlySet<string> Skills);

// config/cv.json after validation: every true bullet the owner can put on a CV. Roles come first, then projects.
public sealed record MasterCv(CvPerson Person, string Headline, IReadOnlyList<CvSummary> Summaries, IReadOnlyList<CvSkillGroup> SkillGroups,
    IReadOnlyList<CvSection> Sections, IReadOnlyList<CvEducation> Education, IReadOnlyList<CvCertification> Certifications)
{
    public IEnumerable<CvBullet> Bullets => Sections.SelectMany(s => s.Bullets);
}

// Either a usable master CV or the problems that stop it loading.
public sealed record CvLoad(MasterCv? Cv, IReadOnlyList<string> Problems)
{
    public bool Ready => Cv is not null;
}
```

- [ ] **Step 4: Write the loader**

`src/Api/Cv/MasterCvFile.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Tracker.Interpreting;

namespace Api.Cv;

// Reads config/cv.json on every call (it is small, and edits then apply without a restart). Every problem is reported
// with its path, the way config/profile.json reports them, and a file with problems gives no CV at all.
public static class MasterCvFile
{
    public const string Missing = "config/cv.json is missing. Copy config/cv.example.json to config/cv.json and fill in your CV.";
    public const int DefaultMaxBullets = 5;

    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static CvLoad Load(string path, SkillCatalog catalog)
    {
        string content;
        try
        {
            if (!File.Exists(path)) return new(null, [Missing]);
            content = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(null, [$"config/cv.json can't be read: {ex.Message}"]);
        }
        try
        {
            using var doc = JsonDocument.Parse(content, Options);
            return new Reader(catalog).Read(doc.RootElement);
        }
        catch (JsonException ex)
        {
            return new(null, [$"config/cv.json is not valid JSON near line {(ex.LineNumber ?? 0) + 1}."]);
        }
    }

    private static string MonthText(DateOnly month) => month.ToString("MMM yyyy", CultureInfo.InvariantCulture);

    private sealed class Reader(SkillCatalog catalog)
    {
        private readonly List<string> _problems = [];
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
        private readonly HashSet<string> _known = catalog.Skills.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        public CvLoad Read(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object) return new(null, ["config/cv.json must hold one JSON object ({ ... })."]);
            var person = ReadPerson(root);
            var headline = Text(root, "headline", "headline", required: true);
            var summaries = Items(root, "summaries", "summaries", ReadSummary);
            if (summaries.Count == 0) Problem("summaries: add at least one summary.");
            var groups = Items(root, "skillGroups", "skillGroups", ReadGroup);
            var roles = Items(root, "roles", "roles", (e, p) => ReadSection(e, p, CvSectionKind.Role));
            if (roles.Count == 0) Problem("roles: add at least one role.");
            var projects = Items(root, "projects", "projects", (e, p) => ReadSection(e, p, CvSectionKind.Project));
            var education = Items(root, "education", "education", ReadEducation);
            var certifications = Items(root, "certifications", "certifications", ReadCertification);
            return _problems.Count > 0
                ? new(null, _problems)
                : new(new MasterCv(person, headline, summaries, groups, [.. roles, .. projects], education, certifications), []);
        }

        private CvPerson ReadPerson(JsonElement root)
        {
            if (!root.TryGetProperty("person", out var person) || person.ValueKind != JsonValueKind.Object)
            {
                Problem("person is required: your name, location and contact details.");
                return new CvPerson("", "", "", "", "", []);
            }
            return new CvPerson(Text(person, "name", "person.name", required: true), Text(person, "location", "person.location"),
                Text(person, "status", "person.status"), Text(person, "phone", "person.phone"),
                Text(person, "email", "person.email", required: true), Items(person, "links", "person.links", ReadLink));
        }

        private CvLink ReadLink(JsonElement e, string path)
        {
            var url = Text(e, "url", $"{path}.url", required: true);
            if (url.Length > 0 && !(Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
                Problem($"{path}.url: use a full web address starting with https://.");
            return new CvLink(Text(e, "label", $"{path}.label", required: true), url);
        }

        private CvSummary ReadSummary(JsonElement e, string path)
        {
            var id = Id(e, path);
            var families = new HashSet<RoleFamily>();
            foreach (var (value, at) in Strings(e, "families", $"{path}.families"))
            {
                if (TitleRules.ParseFamily(value) is { } family) families.Add(family);
                else Problem($"{at}: unknown family '{value}' (use dotnet, fullstack, software_engineer, lead_manager or other).");
            }
            return new CvSummary(id, families, Text(e, "text", $"{path}.text", required: true));
        }

        private CvSkillGroup ReadGroup(JsonElement e, string path) =>
            new(Text(e, "name", $"{path}.name", required: true), Items(e, "items", $"{path}.items", ReadItem));

        private CvSkillItem ReadItem(JsonElement e, string path)
        {
            var text = Text(e, "text", $"{path}.text", required: true);
            return new CvSkillItem(text, Skills(e, path, text));
        }

        private CvSection ReadSection(JsonElement e, string path, CvSectionKind kind)
        {
            var role = kind == CvSectionKind.Role;
            var id = Id(e, path);
            var title = Text(e, "title", $"{path}.title", required: true);
            var company = Text(e, "company", $"{path}.company", required: role);
            var location = Text(e, "location", $"{path}.location");
            var start = Month(e, "start", $"{path}.start", required: role);
            var end = Month(e, "end", $"{path}.end", required: false);
            var dates = start is null ? "" : $"{MonthText(start.Value)} – {(end is null ? "Present" : MonthText(end.Value))}";
            var max = MaxBullets(e, path);
            var bullets = Items(e, "bullets", $"{path}.bullets", ReadBullet);
            if (role && bullets.Count == 0) Problem($"{path}.bullets: add at least one bullet.");
            return new CvSection(id, kind, title, company, location, dates, max, bullets);
        }

        private CvBullet ReadBullet(JsonElement e, string path)
        {
            var id = Id(e, path);
            var text = Text(e, "text", $"{path}.text", required: true);
            var always = false;
            if (e.TryGetProperty("always", out var flag) && flag.ValueKind != JsonValueKind.Null)
            {
                if (flag.ValueKind is JsonValueKind.True or JsonValueKind.False) always = flag.GetBoolean();
                else Problem($"{path}.always must be true or false.");
            }
            return new CvBullet(id, text, Skills(e, path, text), always);
        }

        private CvEducation ReadEducation(JsonElement e, string path) =>
            new(Text(e, "degree", $"{path}.degree", required: true), Text(e, "school", $"{path}.school", required: true),
                Text(e, "years", $"{path}.years"));

        private CvCertification ReadCertification(JsonElement e, string path)
        {
            var name = Text(e, "name", $"{path}.name", required: true);
            return new CvCertification(name, Text(e, "year", $"{path}.year"), Skills(e, path, name));
        }

        private void Problem(string text) => _problems.Add(text);

        private string Id(JsonElement e, string path)
        {
            var id = Text(e, "id", $"{path}.id", required: true);
            if (id.Length > 0 && !_ids.Add(id)) Problem($"{path}.id: \"{id}\" is used twice.");
            return id;
        }

        private string Text(JsonElement parent, string name, string path, bool required = false)
        {
            if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                if (required) Problem($"{path} is required.");
                return "";
            }
            if (value.ValueKind != JsonValueKind.String)
            {
                Problem($"{path} must be text in quotes.");
                return "";
            }
            var text = value.GetString()!.Trim();
            if (required && text.Length == 0) Problem($"{path} is required.");
            return text;
        }

        private List<T> Items<T>(JsonElement parent, string name, string path, Func<JsonElement, string, T> read)
        {
            if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return [];
            if (value.ValueKind != JsonValueKind.Array)
            {
                Problem($"{path} must be a list ([ ... ]).");
                return [];
            }
            var items = new List<T>();
            var i = 0;
            foreach (var item in value.EnumerateArray())
            {
                var at = $"{path}[{i++}]";
                if (item.ValueKind != JsonValueKind.Object) Problem($"{at} must be an object ({{ ... }}).");
                else items.Add(read(item, at));
            }
            return items;
        }

        private IEnumerable<(string Value, string Path)> Strings(JsonElement parent, string name, string path)
        {
            if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) yield break;
            if (value.ValueKind != JsonValueKind.Array)
            {
                Problem($"{path} must be a list ([ ... ]).");
                yield break;
            }
            var i = 0;
            foreach (var item in value.EnumerateArray())
            {
                var at = $"{path}[{i++}]";
                if (item.ValueKind == JsonValueKind.String) yield return (item.GetString()!.Trim(), at);
                else Problem($"{at} must be text in quotes.");
            }
        }

        private IReadOnlySet<string> Skills(JsonElement e, string path, string text)
        {
            var skills = new HashSet<string>(catalog.Match(text), StringComparer.Ordinal);
            foreach (var (value, at) in Strings(e, "skills", $"{path}.skills"))
            {
                if (_known.Contains(value)) skills.Add(value);
                else Problem($"{at}: '{value}' is not a skill in config/skills.json (names must match exactly).");
            }
            return skills;
        }

        private DateOnly? Month(JsonElement e, string name, string path, bool required)
        {
            var text = Text(e, name, path, required);
            if (text.Length == 0) return null;
            if (DateOnly.TryParseExact(text, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)) return month;
            Problem($"{path}: use year and month like 2023-10, not '{text}'.");
            return null;
        }

        private int MaxBullets(JsonElement e, string path)
        {
            if (!e.TryGetProperty("maxBullets", out var value) || value.ValueKind == JsonValueKind.Null) return DefaultMaxBullets;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var max) && max >= 1) return max;
            Problem($"{path}.maxBullets must be a whole number, 1 or more.");
            return DefaultMaxBullets;
        }
    }
}
```

- [ ] **Step 5: Add the example file and ignore the real one**

`config/cv.example.json`:

```jsonc
// The master CV the Tailor CV page selects from. Copy this file to config/cv.json (gitignored) and replace everything
// with your own, true details. The tool only picks and orders what you write here; it never adds a claim.
//
// - ids: any unique text. Saved CVs point at them, so keep a bullet's id when you reword it.
// - skills: optional tags, spelled exactly as a "name" in config/skills.json. Skills named in the text are found anyway.
// - summaries: the Tailor page suggests the first one whose families include the posting title's family
//   (dotnet, fullstack, software_engineer, lead_manager or other).
// - roles: newest first. start and end are year-month ("2023-10"); "end": null means current. maxBullets defaults to 5.
// - always: true keeps a bullet on every tailored CV.
{
  "person": {
    "name": "Alex Example",
    "location": "Toronto, ON",
    "status": "Canadian Permanent Resident",
    "phone": "+1 416 555 0100",
    "email": "alex@example.com",
    "links": [
      { "label": "GitHub", "url": "https://github.com/alex-example" },
      { "label": "LinkedIn", "url": "https://www.linkedin.com/in/alex-example" }
    ]
  },
  "headline": "Senior Full Stack .NET Developer",
  "summaries": [
    {
      "id": "dotnet",
      "families": ["dotnet", "fullstack", "software_engineer"],
      "text": "Full stack developer with nine years of C# and .NET, building business systems end to end: APIs, React front ends, SQL Server and the pipelines that ship them."
    },
    {
      "id": "lead",
      "families": ["lead_manager"],
      "text": "Hands-on technical lead who plans the work, reviews the code and still ships features, with nine years of .NET and a record of setting up CI/CD from scratch."
    }
  ],
  "skillGroups": [
    { "name": "Backend", "items": [
      { "text": "C# / .NET 8-10", "skills": ["C#", ".NET"] },
      { "text": "ASP.NET Core Web API" },
      { "text": "Entity Framework Core", "skills": ["Entity Framework"] }
    ] },
    { "name": "Frontend", "items": [ { "text": "React, Next.js" }, { "text": "TypeScript" } ] },
    { "name": "Cloud and DevOps", "items": [
      { "text": "Azure App Service, Azure Functions" },
      { "text": "Azure DevOps pipelines", "skills": ["CI/CD"] },
      { "text": "Docker" }
    ] },
    { "name": "Data", "items": [ { "text": "SQL Server" }, { "text": "PostgreSQL" } ] }
  ],
  "roles": [
    {
      "id": "acme", "title": "Senior Developer", "company": "Acme Corp", "location": "Toronto, ON",
      "start": "2023-10", "end": null, "maxBullets": 4,
      "bullets": [
        { "id": "acme-lead", "text": "Led a four-person team building a contract management system in .NET and Next.js.", "skills": ["Technical Leadership"], "always": true },
        { "id": "acme-cicd", "text": "Set up Azure DevOps CI/CD pipelines that cut a release from a day to an hour." },
        { "id": "acme-sql", "text": "Tuned SQL Server queries and indexes, halving report load times.", "skills": ["Performance Optimization"] },
        { "id": "acme-docker", "text": "Ran internal tools on Docker Compose for a 50-person office." },
        { "id": "acme-mentor", "text": "Mentored two junior developers through code reviews and pairing.", "skills": ["Mentoring", "Code Review"] }
      ]
    },
    {
      "id": "globex", "title": "Software Developer", "company": "Globex", "location": "Ottawa, ON",
      "start": "2016-06", "end": "2023-09", "maxBullets": 3,
      "bullets": [
        { "id": "globex-api", "text": "Built REST APIs in ASP.NET for an assessment platform used by 40 universities.", "skills": ["REST APIs"] },
        { "id": "globex-react", "text": "Rebuilt the exam dashboard in React and TypeScript." },
        { "id": "globex-wpf", "text": "Wrote a WPF desktop tool that reads scanned answer sheets." },
        { "id": "globex-import", "text": "Wrote data importers that cut client onboarding from weeks to days." }
      ]
    }
  ],
  "projects": [
    {
      "id": "tracker", "title": "Job Market Tracker", "start": "2026-09",
      "bullets": [
        { "id": "tracker-aspire", "text": "Built a .NET 10 and React dashboard over PostgreSQL, run with .NET Aspire and traced with OpenTelemetry." }
      ]
    }
  ],
  "education": [
    { "degree": "BSc Computer Engineering", "school": "Example University", "years": "2012-2016" }
  ],
  "certifications": [
    { "name": "Microsoft Certified: Azure Developer Associate (AZ-204)", "year": "2026", "skills": ["Azure"] }
  ]
}
```

In `.gitignore`, directly after the line `config/profile.json`, add:

```
# Owner's real master CV (config/cv.example.json shows the format)
config/cv.json
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~Api.Tests.Cv"`
Expected: PASS, 8 tests. If a problem message's order differs from the test, the reading order in `Reader` is the contract: fix the code, not the expected list, so problems keep the file's order.

- [ ] **Step 7: Commit**

```bash
git add src/Api/Cv/MasterCv.cs src/Api/Cv/MasterCvFile.cs config/cv.example.json .gitignore tests/Api.Tests/Cv/MasterCvTests.cs
git commit -m "Read and validate the master CV file"
```

---

### Task 3: CV layout in page order

**Files:**
- Create: `src/Api/Cv/CvLayout.cs`
- Test: `tests/Api.Tests/Cv/CvSamples.cs`, `tests/Api.Tests/Cv/CvLayoutTests.cs`

**Interfaces:**
- Consumes: the Task 2 model (`MasterCv`, `CvSection`, `CvSkillGroup`, `CvSkillItem`, `CvPerson`, `CvSectionKind`).
- Produces, in namespace `Api.Cv`:
  - `record CvChoices(string Headline, string Summary, IReadOnlyList<string> BulletIds)`
  - `enum CvStyle { Name, Contact, Headline, Heading, EntryTitle, EntryMeta, Paragraph, SkillLine, Bullet }`
  - `record CvRun(string Text, bool Bold = false, string? Url = null)`
  - `record CvParagraph(CvStyle Style, IReadOnlyList<CvRun> Runs)`, with `string Text` and `static CvParagraph Plain(CvStyle, string)`.
  - `static class CvLayout`:
    - `IReadOnlyList<CvParagraph> Paragraphs(MasterCv cv, CvChoices choices, IReadOnlySet<string> postingSkills)`
    - `IReadOnlyList<CvSkillItem> OrderItems(CvSkillGroup group, IReadOnlySet<string> postingSkills)`
    - `int Words(string)` and `int Words(IEnumerable<CvParagraph>)`
    - `int FixedWords(MasterCv cv)`
    - `const string Separator = " · "`
  - Test helper `Api.Tests.Cv.CvSamples`, with `Set(...)`, `B(...)`, `Role(...)`, `Project(...)`, `Person` and `Cv(...)`. Later tasks' tests use it.

- [ ] **Step 1: Write the test samples and failing tests**

`tests/Api.Tests/Cv/CvSamples.cs`:

```csharp
using Api.Cv;
using Tracker.Interpreting;

namespace Api.Tests.Cv;

// Small hand-made master CVs, so tailoring and layout tests don't depend on the skill catalog's aliases.
internal static class CvSamples
{
    public static IReadOnlySet<string> Set(params string[] items) => items.ToHashSet(StringComparer.Ordinal);

    public static CvBullet B(string id, string text, string[]? skills = null, bool always = false) => new(id, text, Set(skills ?? []), always);

    public static CvSection Role(string id, string title, string company, int max, params CvBullet[] bullets) =>
        new(id, CvSectionKind.Role, title, company, "Toronto, ON", "Oct 2023 – Present", max, bullets);

    public static CvSection Project(string id, string title, params CvBullet[] bullets) =>
        new(id, CvSectionKind.Project, title, "", "", "", 5, bullets);

    public static readonly CvPerson Person = new("Alex Example", "Toronto, ON", "Canadian Permanent Resident", "+1 416 555 0100",
        "alex@example.com", [new CvLink("GitHub", "https://github.com/alex-example")]);

    // Acme (max 3): a1 always [.NET], a2 [Azure DevOps, CI/CD], a3 [SQL Server], a4 [Docker].
    // Globex (max 2): g1 [REST APIs], g2 [React].
    public static MasterCv Cv(IReadOnlyList<CvSection>? sections = null, IReadOnlyList<CvSkillGroup>? groups = null,
        IReadOnlyList<CvEducation>? education = null, IReadOnlyList<CvCertification>? certifications = null, CvPerson? person = null) => new(
        person ?? Person,
        "Senior .NET Developer",
        [
            new CvSummary("dotnet", new HashSet<RoleFamily> { RoleFamily.Dotnet, RoleFamily.Fullstack }, "Builds .NET systems end to end."),
            new CvSummary("lead", new HashSet<RoleFamily> { RoleFamily.LeadManager }, "Leads small teams and still ships."),
        ],
        groups ??
        [
            new CvSkillGroup("Backend", [new CvSkillItem("C# / .NET", Set("C#", ".NET")), new CvSkillItem("SQL Server", Set("SQL Server"))]),
            new CvSkillGroup("Cloud", [new CvSkillItem("Docker", Set("Docker")), new CvSkillItem("Azure DevOps", Set("Azure DevOps"))]),
        ],
        sections ??
        [
            Role("acme", "Senior Developer", "Acme Corp", 3,
                B("a1", "Led the team.", [".NET"], always: true), B("a2", "Built CI/CD.", ["Azure DevOps", "CI/CD"]),
                B("a3", "Tuned SQL.", ["SQL Server"]), B("a4", "Ran Docker.", ["Docker"])),
            Role("globex", "Developer", "Globex", 2, B("g1", "Wrote APIs.", ["REST APIs"]), B("g2", "Built a React UI.", ["React"])),
        ],
        education ?? [new CvEducation("BSc Computer Engineering", "Example University", "2012-2016")],
        certifications ?? [new CvCertification("AZ-204", "2026", Set("Azure"))]);
}
```

`tests/Api.Tests/Cv/CvLayoutTests.cs`:

```csharp
using Api.Cv;
using static Api.Tests.Cv.CvSamples;

namespace Api.Tests.Cv;

public class CvLayoutTests
{
    private static readonly IReadOnlySet<string> None = Set();

    [Fact]
    public void Paragraphs_follow_the_page_order()
    {
        var choices = new CvChoices("Senior .NET Developer", "Builds .NET systems.", ["a2", "a1", "g2"]);

        var paragraphs = CvLayout.Paragraphs(Cv(), choices, None);

        Assert.Equal(new (CvStyle, string)[]
        {
            (CvStyle.Name, "Alex Example"),
            (CvStyle.Contact, "Toronto, ON · Canadian Permanent Resident · +1 416 555 0100 · alex@example.com · https://github.com/alex-example"),
            (CvStyle.Headline, "Senior .NET Developer"),
            (CvStyle.Heading, "Summary"),
            (CvStyle.Paragraph, "Builds .NET systems."),
            (CvStyle.Heading, "Skills"),
            (CvStyle.SkillLine, "Backend: C# / .NET, SQL Server"),
            (CvStyle.SkillLine, "Cloud: Docker, Azure DevOps"),
            (CvStyle.Heading, "Experience"),
            (CvStyle.EntryTitle, "Senior Developer — Acme Corp"),
            (CvStyle.EntryMeta, "Toronto, ON | Oct 2023 – Present"),
            (CvStyle.Bullet, "Built CI/CD."),
            (CvStyle.Bullet, "Led the team."),
            (CvStyle.EntryTitle, "Developer — Globex"),
            (CvStyle.EntryMeta, "Toronto, ON | Oct 2023 – Present"),
            (CvStyle.Bullet, "Built a React UI."),
            (CvStyle.Heading, "Education"),
            (CvStyle.Paragraph, "BSc Computer Engineering — Example University (2012-2016)"),
            (CvStyle.Heading, "Certifications"),
            (CvStyle.Paragraph, "AZ-204 (2026)"),
        }, paragraphs.Select(p => (p.Style, p.Text)));
    }

    [Fact]
    public void Empty_parts_are_left_out_but_every_role_keeps_its_title()
    {
        var cv = Cv(groups: [], education: [], certifications: [], person: Person with { Status = "", Phone = "", Links = [] });

        var texts = CvLayout.Paragraphs(cv, new CvChoices("Developer", "", []), None).Select(p => p.Text);

        Assert.Equal(new[]
        {
            "Alex Example", "Toronto, ON · alex@example.com", "Developer",
            "Experience", "Senior Developer — Acme Corp", "Toronto, ON | Oct 2023 – Present", "Developer — Globex", "Toronto, ON | Oct 2023 – Present",
        }, texts);
    }

    [Fact]
    public void Projects_show_under_their_own_heading_without_empty_details()
    {
        var cv = Cv(sections: [Role("acme", "Dev", "Acme", 3, B("a1", "Did it.")), Project("tracker", "Job Market Tracker", B("t1", "Built a dashboard."))],
            groups: [], education: [], certifications: []);

        var texts = CvLayout.Paragraphs(cv, new CvChoices("Dev", "", ["t1"]), None).Select(p => p.Text);

        Assert.Equal(new[] { "Projects", "Job Market Tracker", "Built a dashboard." }, texts.TakeLast(3));
    }

    [Fact]
    public void Skill_items_that_match_the_posting_come_first()
    {
        var line = CvLayout.Paragraphs(Cv(), new CvChoices("Dev", "", []), Set("Azure DevOps")).Single(p => p.Text.StartsWith("Cloud"));

        Assert.Equal("Cloud: Azure DevOps, Docker", line.Text);
        Assert.True(line.Runs[0].Bold);
    }

    [Fact]
    public void Links_are_runs_that_show_their_address()
    {
        var contact = CvLayout.Paragraphs(Cv(), new CvChoices("Dev", "", []), None)[1];

        var link = Assert.Single(contact.Runs, r => r.Url is not null);
        Assert.Equal(("https://github.com/alex-example", "https://github.com/alex-example"), (link.Text, link.Url));
    }

    [Fact]
    public void Fixed_words_count_everything_but_the_headline_summary_and_bullets()
    {
        var cv = Cv();
        var all = CvLayout.Words(CvLayout.Paragraphs(cv, new CvChoices("Senior Developer", "Two words", ["a1"]), None));

        // headline 2, "Summary" heading 1, summary 2, "Led the team." 3
        Assert.Equal(all - 2 - 1 - 2 - 3, CvLayout.FixedWords(cv));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~CvLayoutTests"`
Expected: build error CS0246, "The type or namespace name 'CvChoices' could not be found".

- [ ] **Step 3: Write the layout**

`src/Api/Cv/CvLayout.cs`:

```csharp
namespace Api.Cv;

// What the owner chose for one CV. It is saved with a generated file, so the Tailor page can start from it next time.
public sealed record CvChoices(string Headline, string Summary, IReadOnlyList<string> BulletIds);

public enum CvStyle { Name, Contact, Headline, Heading, EntryTitle, EntryMeta, Paragraph, SkillLine, Bullet }

// A piece of a paragraph: plain, bold, or a link that shows its address.
public sealed record CvRun(string Text, bool Bold = false, string? Url = null);

public sealed record CvParagraph(CvStyle Style, IReadOnlyList<CvRun> Runs)
{
    public string Text => string.Concat(Runs.Select(r => r.Text));

    public static CvParagraph Plain(CvStyle style, string text) => new(style, [new CvRun(text)]);
}

// The CV's content in page order, before any Word formatting. The .docx renderer, the page's word count and the tests
// all read this one list, so the text an applicant-tracking system sees is the text tested here.
public static class CvLayout
{
    public const string Separator = " · ";
    private static readonly IReadOnlySet<string> NoSkills = new HashSet<string>();

    public static IReadOnlyList<CvParagraph> Paragraphs(MasterCv cv, CvChoices choices, IReadOnlySet<string> postingSkills)
    {
        var list = new List<CvParagraph> { CvParagraph.Plain(CvStyle.Name, cv.Person.Name), Contact(cv.Person) };
        var headline = choices.Headline.Trim();
        if (headline.Length > 0) list.Add(CvParagraph.Plain(CvStyle.Headline, headline));
        var summary = choices.Summary.Trim();
        if (summary.Length > 0)
        {
            list.Add(CvParagraph.Plain(CvStyle.Heading, "Summary"));
            list.Add(CvParagraph.Plain(CvStyle.Paragraph, summary));
        }
        if (cv.SkillGroups.Count > 0)
        {
            list.Add(CvParagraph.Plain(CvStyle.Heading, "Skills"));
            list.AddRange(cv.SkillGroups.Select(g => new CvParagraph(CvStyle.SkillLine,
                [new CvRun($"{g.Name}: ", Bold: true), new CvRun(string.Join(", ", OrderItems(g, postingSkills).Select(i => i.Text)))])));
        }
        Entries(list, "Experience", [.. cv.Sections.Where(s => s.Kind == CvSectionKind.Role)], choices);
        Entries(list, "Projects", [.. cv.Sections.Where(s => s.Kind == CvSectionKind.Project)], choices);
        if (cv.Education.Count > 0)
        {
            list.Add(CvParagraph.Plain(CvStyle.Heading, "Education"));
            list.AddRange(cv.Education.Select(e => CvParagraph.Plain(CvStyle.Paragraph,
                $"{e.Degree} — {e.School}{(e.Years.Length > 0 ? $" ({e.Years})" : "")}")));
        }
        if (cv.Certifications.Count > 0)
        {
            list.Add(CvParagraph.Plain(CvStyle.Heading, "Certifications"));
            list.AddRange(cv.Certifications.Select(c => CvParagraph.Plain(CvStyle.Paragraph, c.Year.Length > 0 ? $"{c.Name} ({c.Year})" : c.Name)));
        }
        return list;
    }

    // Items that show a posting skill come first; each half keeps the file's order.
    public static IReadOnlyList<CvSkillItem> OrderItems(CvSkillGroup group, IReadOnlySet<string> postingSkills) =>
        [.. group.Items.Where(i => i.Skills.Overlaps(postingSkills)), .. group.Items.Where(i => !i.Skills.Overlaps(postingSkills))];

    public static int Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    public static int Words(IEnumerable<CvParagraph> paragraphs) => paragraphs.Sum(p => Words(p.Text));

    // Everything on the page except the headline, the summary and the bullets: the Tailor page adds those as they change.
    public static int FixedWords(MasterCv cv) => Words(Paragraphs(cv, new CvChoices("", "", []), NoSkills));

    private static CvParagraph Contact(CvPerson person)
    {
        var parts = new[] { person.Location, person.Status, person.Phone, person.Email }.Where(p => p.Length > 0).Select(p => new CvRun(p))
            .Concat(person.Links.Select(l => new CvRun(l.Url, Url: l.Url)));
        var runs = new List<CvRun>();
        foreach (var part in parts)
        {
            if (runs.Count > 0) runs.Add(new CvRun(Separator));
            runs.Add(part);
        }
        return new CvParagraph(CvStyle.Contact, runs);
    }

    // Every role or project keeps its title line, even with no bullets chosen, so the work history stays complete.
    private static void Entries(List<CvParagraph> list, string heading, IReadOnlyList<CvSection> sections, CvChoices choices)
    {
        if (sections.Count == 0) return;
        list.Add(CvParagraph.Plain(CvStyle.Heading, heading));
        foreach (var section in sections)
        {
            list.Add(CvParagraph.Plain(CvStyle.EntryTitle, section.Company.Length > 0 ? $"{section.Title} — {section.Company}" : section.Title));
            var meta = string.Join(" | ", new[] { section.Location, section.Dates }.Where(p => p.Length > 0));
            if (meta.Length > 0) list.Add(CvParagraph.Plain(CvStyle.EntryMeta, meta));
            var byId = section.Bullets.ToDictionary(b => b.Id, StringComparer.Ordinal);
            list.AddRange(choices.BulletIds.Where(byId.ContainsKey).Select(id => CvParagraph.Plain(CvStyle.Bullet, byId[id].Text)));
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~CvLayoutTests"`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Api/Cv/CvLayout.cs tests/Api.Tests/Cv/CvSamples.cs tests/Api.Tests/Cv/CvLayoutTests.cs
git commit -m "Lay out a CV's paragraphs in page order"
```

---

### Task 4: Proposing a CV for a posting

**Files:**
- Create: `src/Api/Cv/CvProposal.cs`, `src/Api/Cv/CvTailor.cs`
- Test: `tests/Api.Tests/Cv/CvTailorTests.cs`

**Interfaces:**
- Consumes:
  - `SkillText.ForMatching` from Task 1;
  - `MasterCv`, `CvSection`, `CvBullet` and `CvSummary` from Task 2;
  - `CvChoices`, `CvLayout.OrderItems` and `CvLayout.FixedWords` from Task 3;
  - `Profile.LevelOf` and `SkillLevel` from Tracker.Analysis;
  - `TitleRules.Family` and `TitleRules.FamilyKey`.
- Produces, in namespace `Api.Cv`:
  - `record PostingSkill(string Skill, int Weight, SkillLevel Level)`
  - `record ProposalBullet(string Id, string Text, IReadOnlyList<string> Skills, int Score, bool Always)`
  - `record ProposalSection(string Id, CvSectionKind Kind, string Title, string Company, string Location, string Dates, int MaxBullets, IReadOnlyList<ProposalBullet> Bullets)`
  - `record ProposalSkillItem(string Text, IReadOnlyList<string> Skills, bool Matches)`
  - `record ProposalSkillGroup(string Name, IReadOnlyList<ProposalSkillItem> Items)`
  - `record ProposalSummary(string Id, IReadOnlyList<string> Families, string Text)`, where families are keys such as `"dotnet"`.
  - `record ProposalCertification(string Name, string Year, IReadOnlyList<string> Skills)`
  - `record SavedChoices(DateOnly GeneratedOn, string Headline, string Summary, IReadOnlyList<string> BulletIds, IReadOnlyList<string> DroppedIds)`
  - `record TailorInput(Guid EntryId, string Title, string Company, string Text, string TextSource, bool Snippet, uint Version)`
  - `record CvProposal(Guid EntryId, string Title, string Company, string TextSource, bool Thin, uint Version, IReadOnlyList<PostingSkill> PostingSkills, string Headline, IReadOnlyList<ProposalSummary> Summaries, string SuggestedSummaryId, IReadOnlyList<ProposalSkillGroup> SkillGroups, IReadOnlyList<ProposalSection> Sections, IReadOnlyList<ProposalCertification> Certifications, CvChoices Suggestion, SavedChoices? Saved, int FixedWords)`
  - `static class CvTailor`:
    - `const int ThinSkillCount = 3`
    - `PostingSkills(SkillCatalog, Profile, string title, string text)`
    - `IsThin(int skillCount, bool snippet)`
    - `Score(CvBullet, IReadOnlyDictionary<string,int>)`
    - `Suggest(CvSection, IReadOnlyDictionary<string,int>)`, which returns the ordered bullet ids
    - `SuggestSummary(MasterCv, string title)`, which returns a `CvSummary`
    - `Suggestion(MasterCv, IReadOnlyDictionary<string,int>, string title)`
    - `Saved(MasterCv, CvChoices, DateOnly)`
    - `Propose(MasterCv, SkillCatalog, Profile, TailorInput, (CvChoices Choices, DateOnly On)? saved)`

- [ ] **Step 1: Write the failing tests**

`tests/Api.Tests/Cv/CvTailorTests.cs`:

```csharp
using Api.Cv;
using Api.Tests.TestSupport;
using Tracker.Analysis;
using static Api.Tests.Cv.CvSamples;

namespace Api.Tests.Cv;

public class CvTailorTests
{
    private static Dictionary<string, int> W(params string[] skills) => skills.ToDictionary(s => s, _ => 1, StringComparer.Ordinal);

    [Fact]
    public void Posting_skills_weigh_double_when_the_title_names_them()
    {
        // profile.example.json: C# and .NET strong, React learning.
        var skills = CvTailor.PostingSkills(Data.Settings.Catalog, Data.Settings.Profile, "Senior .NET Developer",
            "C# and React. We will accommodate accessibility needs.");

        Assert.Equal(new[]
        {
            new PostingSkill(".NET", 2, SkillLevel.Strong), new PostingSkill("C#", 1, SkillLevel.Strong), new PostingSkill("React", 1, SkillLevel.Learning),
        }, skills);
    }

    [Fact]
    public void Always_bullets_stay_and_the_best_scorers_fill_up_to_max()
    {
        var acme = Cv().Sections[0]; // max 3: a1 always, a2 [Azure DevOps, CI/CD], a3 [SQL Server], a4 [Docker]
        Assert.Equal(new[] { "a2", "a4", "a1" }, CvTailor.Suggest(acme, W("Azure DevOps", "CI/CD", "Docker")));
    }

    [Fact]
    public void Leftover_slots_are_filled_in_file_order() =>
        Assert.Equal(new[] { "a4", "a1", "a2" }, CvTailor.Suggest(Cv().Sections[0], W("Docker")));

    [Fact]
    public void Always_bullets_are_kept_even_beyond_the_maximum()
    {
        var section = Role("r", "Dev", "Co", 1, B("x", "One.", always: true), B("y", "Two.", always: true), B("z", "Three.", ["C#"]));
        Assert.Equal(new[] { "x", "y" }, CvTailor.Suggest(section, W("C#")));
    }

    [Fact]
    public void Ties_keep_the_file_order()
    {
        var section = Role("r", "Dev", "Co", 2, B("x", "One.", ["A"]), B("y", "Two.", ["B"]), B("z", "Three.", ["C"]));
        Assert.Equal(new[] { "x", "y" }, CvTailor.Suggest(section, W("A", "B", "C")));
    }

    [Fact]
    public void Title_skills_outweigh_others()
    {
        var section = Role("r", "Dev", "Co", 1, B("x", "One.", ["React"]), B("y", "Two.", ["C#"]));
        Assert.Equal(new[] { "y" }, CvTailor.Suggest(section, new Dictionary<string, int> { ["C#"] = 2, ["React"] = 1 }));
    }

    [Theory]
    [InlineData("Senior .NET Developer", "dotnet")]
    [InlineData("Engineering Manager", "lead")]
    [InlineData("Pastry Chef", "dotnet")] // no summary for "other": the first one
    public void The_summary_follows_the_title_s_role_family(string title, string expected) =>
        Assert.Equal(expected, CvTailor.SuggestSummary(Cv(), title).Id);

    [Fact]
    public void Saved_choices_drop_bullets_the_cv_no_longer_has()
    {
        var saved = CvTailor.Saved(Cv(), new CvChoices("Dev", "Hi.", ["a2", "gone-1", "g1"]), new DateOnly(2027, 3, 1));

        Assert.Equal(new[] { "a2", "g1" }, saved.BulletIds);
        Assert.Equal(new[] { "gone-1" }, saved.DroppedIds);
    }

    [Theory]
    [InlineData(2, false, true)]
    [InlineData(3, false, false)]
    [InlineData(8, true, true)]
    public void Thin_means_fewer_than_three_skills_or_a_snippet(int skills, bool snippet, bool thin) =>
        Assert.Equal(thin, CvTailor.IsThin(skills, snippet));

    [Fact]
    public void The_proposal_puts_it_together()
    {
        var input = new TailorInput(Guid.NewGuid(), "Senior .NET Developer", "Acme", "Azure DevOps and CI/CD.", "entry", Snippet: false, Version: 7);

        var proposal = CvTailor.Propose(Cv(), Data.Settings.Catalog, Data.Settings.Profile, input, saved: null);

        Assert.False(proposal.Thin);
        Assert.Equal(("dotnet", "Senior .NET Developer", "Builds .NET systems end to end."),
            (proposal.SuggestedSummaryId, proposal.Suggestion.Headline, proposal.Suggestion.Summary));
        Assert.Equal(new[] { "a1", "a2", "a3", "g1", "g2" }, proposal.Suggestion.BulletIds);
        Assert.Equal(new[] { "Azure DevOps", "Docker" }, proposal.SkillGroups[1].Items.Select(i => i.Text));
        Assert.Equal((true, false), (proposal.SkillGroups[1].Items[0].Matches, proposal.SkillGroups[1].Items[1].Matches));
        Assert.Equal(2, proposal.Sections[0].Bullets.Single(b => b.Id == "a2").Score);
        Assert.Equal(new[] { "dotnet", "fullstack" }, proposal.Summaries[0].Families);
        Assert.Equal(CvLayout.FixedWords(Cv()), proposal.FixedWords);
        Assert.Null(proposal.Saved);
    }

    [Fact]
    public void The_proposal_carries_saved_choices()
    {
        var input = new TailorInput(Guid.NewGuid(), "Developer", "Acme", "C#", "entry", Snippet: false, Version: 7);

        var proposal = CvTailor.Propose(Cv(), Data.Settings.Catalog, Data.Settings.Profile, input,
            (new CvChoices("Lead dev", "Hi.", ["g2", "nope"]), new DateOnly(2027, 3, 1)));

        Assert.Equal(("Lead dev", new DateOnly(2027, 3, 1)), (proposal.Saved!.Headline, proposal.Saved.GeneratedOn));
        Assert.Equal(new[] { "nope" }, proposal.Saved.DroppedIds);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~CvTailorTests"`
Expected: build error CS0103, "The name 'CvTailor' does not exist in the current context".

- [ ] **Step 3: Write the view records**

`src/Api/Cv/CvProposal.cs`:

```csharp
using Tracker.Analysis;

namespace Api.Cv;

// A skill the posting asks for: weight 2 when the title names it too. The owner's level comes from config/profile.json.
public sealed record PostingSkill(string Skill, int Weight, SkillLevel Level);

public sealed record ProposalBullet(string Id, string Text, IReadOnlyList<string> Skills, int Score, bool Always);

public sealed record ProposalSection(string Id, CvSectionKind Kind, string Title, string Company, string Location, string Dates,
    int MaxBullets, IReadOnlyList<ProposalBullet> Bullets);

public sealed record ProposalSkillItem(string Text, IReadOnlyList<string> Skills, bool Matches);

public sealed record ProposalSkillGroup(string Name, IReadOnlyList<ProposalSkillItem> Items);

public sealed record ProposalSummary(string Id, IReadOnlyList<string> Families, string Text);

public sealed record ProposalCertification(string Name, string Year, IReadOnlyList<string> Skills);

// The newest generated CV's choices, minus bullets cv.json no longer has (listed in DroppedIds).
public sealed record SavedChoices(DateOnly GeneratedOn, string Headline, string Summary, IReadOnlyList<string> BulletIds,
    IReadOnlyList<string> DroppedIds);

// The entry as tailoring needs it. Its text is already resolved: entry text first, then the market description.
public sealed record TailorInput(Guid EntryId, string Title, string Company, string Text, string TextSource, bool Snippet, uint Version);

// Everything the Tailor page shows. TextSource is "entry", "market" or "none"; Version is the entry's, for the job-text form.
public sealed record CvProposal(Guid EntryId, string Title, string Company, string TextSource, bool Thin, uint Version,
    IReadOnlyList<PostingSkill> PostingSkills, string Headline, IReadOnlyList<ProposalSummary> Summaries, string SuggestedSummaryId,
    IReadOnlyList<ProposalSkillGroup> SkillGroups, IReadOnlyList<ProposalSection> Sections, IReadOnlyList<ProposalCertification> Certifications,
    CvChoices Suggestion, SavedChoices? Saved, int FixedWords);
```

- [ ] **Step 4: Write the tailoring rules**

`src/Api/Cv/CvTailor.cs`:

```csharp
using Tracker.Analysis;
using Tracker.Interpreting;

namespace Api.Cv;

// Proposes a CV for one posting by choosing and ordering the owner's own bullets (spec "Proposal and pre-selection").
// It never writes text: everything it returns comes from config/cv.json.
public static class CvTailor
{
    public const int ThinSkillCount = 3;

    // The posting's skills, found the same way as the market data's, heaviest first.
    public static IReadOnlyList<PostingSkill> PostingSkills(SkillCatalog catalog, Profile profile, string title, string text)
    {
        var inTitle = catalog.Match(title);
        return [.. catalog.Match(SkillText.ForMatching(title, text))
            .Select(s => new PostingSkill(s, inTitle.Contains(s) ? 2 : 1, profile.LevelOf(s)))
            .OrderByDescending(s => s.Weight).ThenBy(s => s.Skill, StringComparer.Ordinal)];
    }

    // Too little to tailor from: fewer than three skills, or only a search-result snippet of the posting.
    public static bool IsThin(int skillCount, bool snippet) => snippet || skillCount < ThinSkillCount;

    public static int Score(CvBullet bullet, IReadOnlyDictionary<string, int> weights) =>
        bullet.Skills.Sum(s => weights.GetValueOrDefault(s));

    // `always` bullets first (even past the maximum), then the best scorers, then the rest in file order, up to
    // MaxBullets; the chosen ones print best score first, ties in file order.
    public static IReadOnlyList<string> Suggest(CvSection section, IReadOnlyDictionary<string, int> weights)
    {
        var ranked = section.Bullets.Select((bullet, index) => (Bullet: bullet, Index: index, Score: Score(bullet, weights))).ToList();
        var chosen = ranked.Where(x => x.Bullet.Always).ToList();
        var candidates = ranked.Where(x => !x.Bullet.Always && x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.Index)
            .Concat(ranked.Where(x => !x.Bullet.Always && x.Score == 0));
        foreach (var x in candidates)
        {
            if (chosen.Count >= section.MaxBullets) break;
            chosen.Add(x);
        }
        return [.. chosen.OrderByDescending(x => x.Score).ThenBy(x => x.Index).Select(x => x.Bullet.Id)];
    }

    public static CvSummary SuggestSummary(MasterCv cv, string title)
    {
        var family = TitleRules.Family(title);
        return cv.Summaries.FirstOrDefault(s => s.Families.Contains(family)) ?? cv.Summaries[0];
    }

    public static CvChoices Suggestion(MasterCv cv, IReadOnlyDictionary<string, int> weights, string title) =>
        new(cv.Headline, SuggestSummary(cv, title).Text, [.. cv.Sections.SelectMany(s => Suggest(s, weights))]);

    public static SavedChoices Saved(MasterCv cv, CvChoices choices, DateOnly generatedOn)
    {
        var known = cv.Bullets.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        return new(generatedOn, choices.Headline, choices.Summary,
            [.. choices.BulletIds.Where(known.Contains)], [.. choices.BulletIds.Where(id => !known.Contains(id))]);
    }

    public static CvProposal Propose(MasterCv cv, SkillCatalog catalog, Profile profile, TailorInput entry, (CvChoices Choices, DateOnly On)? saved)
    {
        var skills = PostingSkills(catalog, profile, entry.Title, entry.Text);
        var weights = skills.ToDictionary(s => s.Skill, s => s.Weight, StringComparer.Ordinal);
        var wanted = weights.Keys.ToHashSet(StringComparer.Ordinal);
        return new CvProposal(entry.EntryId, entry.Title, entry.Company, entry.TextSource, IsThin(skills.Count, entry.Snippet), entry.Version,
            skills, cv.Headline,
            [.. cv.Summaries.Select(s => new ProposalSummary(s.Id, [.. s.Families.Select(TitleRules.FamilyKey).Order(StringComparer.Ordinal)], s.Text))],
            SuggestSummary(cv, entry.Title).Id,
            [.. cv.SkillGroups.Select(g => new ProposalSkillGroup(g.Name,
                [.. CvLayout.OrderItems(g, wanted).Select(i => new ProposalSkillItem(i.Text, Sorted(i.Skills), i.Skills.Overlaps(wanted)))]))],
            [.. cv.Sections.Select(s => new ProposalSection(s.Id, s.Kind, s.Title, s.Company, s.Location, s.Dates, s.MaxBullets,
                [.. s.Bullets.Select(b => new ProposalBullet(b.Id, b.Text, Sorted(b.Skills), Score(b, weights), b.Always))]))],
            [.. cv.Certifications.Select(c => new ProposalCertification(c.Name, c.Year, Sorted(c.Skills)))],
            Suggestion(cv, weights, entry.Title),
            saved is { } previous ? Saved(cv, previous.Choices, previous.On) : null,
            CvLayout.FixedWords(cv));
    }

    private static IReadOnlyList<string> Sorted(IReadOnlySet<string> skills) => [.. skills.Order(StringComparer.Ordinal)];
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~CvTailorTests"`
Expected: PASS, 14 tests.

The posting-skills test pins the real catalog. If it finds an extra skill in "Senior .NET Developer" or in the text, add a Ruling to the ledger and adjust the expected list only to what the catalog really contains. Never change `skills.json` for a test.

- [ ] **Step 6: Commit**

```bash
git add src/Api/Cv/CvProposal.cs src/Api/Cv/CvTailor.cs tests/Api.Tests/Cv/CvTailorTests.cs
git commit -m "Propose a tailored CV from the posting's skills"
```

---

### Task 5: The Word renderer

**Files:**
- Modify: `src/Api/Api.csproj` (package)
- Create: `src/Api/Cv/CvDocx.cs`
- Test: `tests/Api.Tests/Cv/CvDocxTests.cs`

**Interfaces:**
- Consumes: `CvParagraph`, `CvRun` and `CvStyle` from Task 3, and `CvSamples` from Task 3's tests.
- Produces:
  - `static class Api.Cv.CvDocx`, with `const string ContentType`, `byte[] Render(IReadOnlyList<CvParagraph> paragraphs)` and `string FileName(string personName, string? company)`.
  - Style ids used in the document: `Title`, `Subtitle`, `Heading1`, `Heading2`, `EntryMeta`, `ListBullet`, `Normal` and the character style `Hyperlink`. The numbering id for bullets is 1.

- [ ] **Step 1: Add the package**

Run: `dotnet add src/Api package DocumentFormat.OpenXml`
Expected: `src/Api/Api.csproj` gains `<PackageReference Include="DocumentFormat.OpenXml" Version="3.x.y" />` with the latest stable version, which pins it. Record the exact version in the ledger. If NuGet offers a 4.x or newer major version, pin the latest 3.x instead (`--version 3.*` resolves it) and ledger the ruling. This plan's API calls are written against 3.x.

- [ ] **Step 2: Write the failing tests**

`tests/Api.Tests/Cv/CvDocxTests.cs`:

```csharp
using Api.Cv;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using static Api.Tests.Cv.CvSamples;

namespace Api.Tests.Cv;

public class CvDocxTests
{
    private static IReadOnlyList<CvParagraph> Layout(MasterCv? cv = null, CvChoices? choices = null) =>
        CvLayout.Paragraphs(cv ?? Cv(), choices ?? new CvChoices("Senior .NET Developer", "Builds .NET systems.", ["a2", "a1", "g1"]), Set("Docker"));

    private static WordprocessingDocument Open(byte[] bytes) => WordprocessingDocument.Open(new MemoryStream(bytes), false);

    private static IEnumerable<string> Errors(WordprocessingDocument doc) =>
        new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc).Select(e => $"{e.Path?.XPath}: {e.Description}");

    [Fact]
    public void The_document_is_valid_for_word()
    {
        using var doc = Open(CvDocx.Render(Layout()));
        Assert.Empty(Errors(doc));
    }

    [Fact]
    public void Body_text_reads_in_page_order()
    {
        var layout = Layout();
        using var doc = Open(CvDocx.Render(layout));

        Assert.Equal(layout.Select(p => p.Text), doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>().Select(p => p.InnerText));
    }

    [Fact]
    public void Headings_and_bullets_use_word_styles_on_letter_paper()
    {
        using var doc = Open(CvDocx.Render(Layout()));
        var body = doc.MainDocumentPart!.Document.Body!;
        var paragraphs = body.Elements<Paragraph>().ToList();
        string? StyleOf(string text) => paragraphs.First(p => p.InnerText == text).ParagraphProperties?.ParagraphStyleId?.Val?.Value;

        Assert.Equal("Title", StyleOf("Alex Example"));
        Assert.Equal("Heading1", StyleOf("Experience"));
        Assert.Equal("Heading2", StyleOf("Senior Developer — Acme Corp"));
        Assert.Equal("ListBullet", StyleOf("Built CI/CD."));
        Assert.NotNull(paragraphs.First(p => p.InnerText == "Built CI/CD.").ParagraphProperties!.NumberingProperties);
        Assert.Equal(12240U, body.Elements<SectionProperties>().Single().GetFirstChild<PageSize>()!.Width!.Value);
    }

    [Fact]
    public void Nothing_hides_in_headers_tables_or_drawings()
    {
        using var doc = Open(CvDocx.Render(Layout()));
        var main = doc.MainDocumentPart!;

        Assert.Empty(main.HeaderParts);
        Assert.Empty(main.FooterParts);
        Assert.Empty(main.Document.Descendants<Table>());
        Assert.Empty(main.Document.Descendants<Drawing>());
    }

    [Fact]
    public void Links_are_real_hyperlinks()
    {
        using var doc = Open(CvDocx.Render(Layout()));
        var main = doc.MainDocumentPart!;

        var link = Assert.Single(main.Document.Descendants<Hyperlink>());
        Assert.Equal("https://github.com/alex-example", link.InnerText);
        Assert.Equal(new Uri("https://github.com/alex-example"), main.HyperlinkRelationships.Single(r => r.Id == link.Id).Uri);
    }

    [Fact]
    public void Special_characters_survive_and_stay_valid()
    {
        var cv = Cv(sections: [Role("r", "R&D <Lead>", "\"Quotes\" & Co", 3, B("x", "Shipped 🚀 features for R&D <fast>\u000B."))]);

        using var doc = Open(CvDocx.Render(Layout(cv, new CvChoices("Dev & <Lead>", "", ["x"]))));

        Assert.Empty(Errors(doc));
        var texts = doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>().Select(p => p.InnerText).ToList();
        Assert.Contains("Shipped 🚀 features for R&D <fast>.", texts);
        Assert.Contains("R&D <Lead> — \"Quotes\" & Co", texts);
    }

    [Theory]
    [InlineData("Alex Example", "Acme Corp", "Alex Example - CV - Acme Corp.docx")]
    [InlineData("Alex Example", null, "Alex Example - CV.docx")]
    [InlineData("Alex Example", "AT&T / Bell: Canada?", "Alex Example - CV - AT&T Bell Canada.docx")]
    [InlineData("Alex  Example", "..\\..\\Windows", "Alex Example - CV - ....Windows.docx")]
    public void File_names_are_safe_for_windows(string name, string? company, string expected) =>
        Assert.Equal(expected, CvDocx.FileName(name, company));

    [Fact]
    public void Long_file_names_are_cut_to_120_characters()
    {
        var name = CvDocx.FileName("Alex Example", new string('x', 300));

        Assert.Equal(120, name.Length);
        Assert.EndsWith(".docx", name);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~CvDocxTests"`
Expected: build error CS0103, "The name 'CvDocx' does not exist in the current context".

- [ ] **Step 4: Write the renderer**

`src/Api/Cv/CvDocx.cs`:

```csharp
using System.Text.RegularExpressions;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Api.Cv;

// Writes CvLayout's paragraphs as a Word document built for applicant-tracking systems (ADR 0013): one column, US Letter,
// Word's own Title and Heading styles, real bullets, and nothing in tables, text boxes, headers or footers.
public static partial class CvDocx
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const int MaxFileName = 120;
    private const int BulletList = 1;

    public static byte[] Render(IReadOnlyList<CvParagraph> paragraphs)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.AddNewPart<StyleDefinitionsPart>().Styles = Styles();
            main.AddNewPart<NumberingDefinitionsPart>().Numbering = Numbering();
            var body = new Body();
            foreach (var paragraph in paragraphs) body.Append(Paragraph(main, paragraph));
            body.Append(new SectionProperties(
                new PageSize { Width = 12240U, Height = 15840U }, // US Letter, in twentieths of a point
                new PageMargin { Top = 1008, Right = 1008U, Bottom = 1008, Left = 1008U, Header = 720U, Footer = 720U, Gutter = 0U })); // 0.7 inch
            main.Document = new Document(body);
        }
        return stream.ToArray();
    }

    // "<name> - CV - <company>.docx" without characters Windows forbids, at most 120 characters.
    public static string FileName(string personName, string? company)
    {
        var stem = string.IsNullOrWhiteSpace(company) ? $"{personName} - CV" : $"{personName} - CV - {company}";
        var cleaned = Spaces().Replace(Invalid().Replace(stem, ""), " ").Trim();
        if (cleaned.Length == 0) cleaned = "CV";
        if (cleaned.Length > MaxFileName - 5)
        {
            cleaned = cleaned[..(MaxFileName - 5)];
            if (char.IsHighSurrogate(cleaned[^1])) cleaned = cleaned[..^1];
            cleaned = cleaned.TrimEnd();
        }
        return cleaned + ".docx";
    }

    private static Paragraph Paragraph(MainDocumentPart main, CvParagraph source)
    {
        var properties = new ParagraphProperties(new ParagraphStyleId { Val = StyleId(source.Style) });
        if (source.Style == CvStyle.Bullet)
            properties.Append(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = BulletList }));
        var paragraph = new Paragraph(properties);
        foreach (var run in source.Runs)
        {
            if (run.Url is { } url)
            {
                var link = main.AddHyperlinkRelationship(new Uri(url, UriKind.Absolute), true);
                paragraph.Append(new Hyperlink(new Run(new RunProperties(new RunStyle { Val = "Hyperlink" }), TextOf(run.Text))) { Id = link.Id });
            }
            else
            {
                paragraph.Append(run.Bold ? new Run(new RunProperties(new Bold()), TextOf(run.Text)) : new Run(TextOf(run.Text)));
            }
        }
        return paragraph;
    }

    // Characters XML can't hold (control characters from a pasted summary, say) are dropped; emoji and other surrogate
    // pairs are kept.
    private static Text TextOf(string value) =>
        new(new string([.. value.Where(c => XmlConvert.IsXmlChar(c) || char.IsSurrogate(c))])) { Space = SpaceProcessingModeValues.Preserve };

    private static string StyleId(CvStyle style) => style switch
    {
        CvStyle.Name => "Title",
        CvStyle.Headline => "Subtitle",
        CvStyle.Heading => "Heading1",
        CvStyle.EntryTitle => "Heading2",
        CvStyle.EntryMeta => "EntryMeta",
        CvStyle.Bullet => "ListBullet",
        _ => "Normal",
    };

    private static Styles Styles() => new(
        new DocDefaults(
            new RunPropertiesDefault(new RunPropertiesBaseStyle(
                new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", EastAsia = "Calibri", ComplexScript = "Calibri" },
                new FontSize { Val = "21" }, new FontSizeComplexScript { Val = "21" }, new Languages { Val = "en-CA" })),
            new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                new SpacingBetweenLines { After = "40", Line = "252", LineRule = LineSpacingRuleValues.Auto }))),
        ParagraphStyle("Normal", "Normal", basedOn: null, size: null, bold: false),
        ParagraphStyle("Title", "Title", "Normal", size: 40, bold: true),
        ParagraphStyle("Subtitle", "Subtitle", "Normal", size: 24, bold: true, before: 60, after: 60),
        ParagraphStyle("Heading1", "heading 1", "Normal", size: 24, bold: true, before: 200, after: 60, outline: 0),
        ParagraphStyle("Heading2", "heading 2", "Normal", size: 22, bold: true, before: 120, after: 0, outline: 1),
        ParagraphStyle("EntryMeta", "Entry Meta", "Normal", size: null, bold: false, italic: true),
        ParagraphStyle("ListBullet", "List Bullet", "Normal", size: null, bold: false, after: 20),
        new Style(new StyleName { Val = "Hyperlink" }, new UIPriority { Val = 99 }, new UnhideWhenUsed(),
            new StyleRunProperties(new Color { Val = "0563C1" }, new Underline { Val = UnderlineValues.Single }))
        { Type = StyleValues.Character, StyleId = "Hyperlink" });

    // Children in schema order (name, basedOn, next, qFormat, pPr, rPr): the validator test fails on any other order.
    private static Style ParagraphStyle(string id, string name, string? basedOn, int? size, bool bold, bool italic = false,
        int before = 0, int after = 40, int? outline = null)
    {
        var style = new Style { Type = StyleValues.Paragraph, StyleId = id };
        if (id == "Normal") style.Default = true;
        style.Append(new StyleName { Val = name });
        if (basedOn is not null) style.Append(new BasedOn { Val = basedOn }, new NextParagraphStyle { Val = "Normal" });
        style.Append(new PrimaryStyle());
        var paragraph = new StyleParagraphProperties();
        if (outline is not null) paragraph.Append(new KeepNext());
        paragraph.Append(new SpacingBetweenLines { Before = before.ToString(), After = after.ToString() });
        if (outline is { } level) paragraph.Append(new OutlineLevel { Val = level });
        style.Append(paragraph);
        var run = new StyleRunProperties();
        if (bold) run.Append(new Bold());
        if (italic) run.Append(new Italic());
        if (size is { } halfPoints) run.Append(new FontSize { Val = halfPoints.ToString() }, new FontSizeComplexScript { Val = halfPoints.ToString() });
        if (run.HasChildren) style.Append(run);
        return style;
    }

    private static Numbering Numbering() => new(
        new AbstractNum(
            new MultiLevelType { Val = MultiLevelValues.SingleLevel },
            new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = "•" },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Indentation { Left = "360", Hanging = "240" }))
            { LevelIndex = 0 })
        { AbstractNumberId = BulletList },
        new NumberingInstance(new AbstractNumId { Val = BulletList }) { NumberID = BulletList });

    [GeneratedRegex(@"[<>:""/\\|?*\x00-\x1F]")]
    private static partial Regex Invalid();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~CvDocxTests"`
Expected: PASS, 11 tests.

If `The_document_is_valid_for_word` lists errors, each one names an XPath and the element the schema expected. Fix the element order or attribute in `CvDocx`, never the test. Ledger every such fix as a Ruling, with the validator's message.

- [ ] **Step 6: Commit**

```bash
git add src/Api/Api.csproj src/Api/Cv/CvDocx.cs tests/Api.Tests/Cv/CvDocxTests.cs
git commit -m "Render the CV as a single-column Word document"
```

---

### Task 6: Generated documents in the tracker tables

**Files:**
- Modify: `src/Api/Applications/Entities.cs` (`ApplicationDocument`)
- Create: `src/Api/Applications/Migrations/<timestamp>_AddCvChoices.cs` (generated)
- Modify: `src/Api/Applications/TrackerViews.cs:23`, `src/Api/Applications/TrackerListEndpoints.cs:81`
- Test: `tests/Api.Tests/Applications/TrackerUpgradeTests.cs` (new), `tests/Api.Tests/Applications/TrackerDocumentTests.cs` and `tests/Api.Tests/Applications/TrackerRestoreTests.cs` (additions)

**Interfaces:**
- Produces:
  - `ApplicationDocument.CvChoices` (`string?`, column `cv_choices`, text, nullable).
  - `DocumentView(Guid Id, DocumentKind Kind, string Label, string FileName, long SizeBytes, DateOnly SentOn, bool Generated)`. `Generated` is `CvChoices != null`.

- [ ] **Step 1: Write the failing tests**

`tests/Api.Tests/Applications/TrackerUpgradeTests.cs`:

```csharp
using Api.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Tracker.TestSupport;

namespace Api.Tests.Applications;

// A database last used by phase 2b is upgraded in place: its rows stay, and older documents count as sent, not generated.
[Collection("postgres")]
[Trait("Category", "Integration")]
public sealed class TrackerUpgradeTests(PostgresFixture pg)
{
    [Fact]
    public async Task Upgrading_a_2b_database_keeps_its_rows()
    {
        var connection = await FreshDatabaseAsync();
        await using (var db = new ApplicationsDb(ApplicationsDb.Options(connection)))
            await db.GetService<IMigrator>().MigrateAsync("20260929031945_InitialApplications");
        await using (var conn = new NpgsqlConnection(connection))
        {
            await conn.OpenAsync();
            await using var insert = new NpgsqlCommand("""
                INSERT INTO companies (id, name, name_key, website, office_location, company_size, annual_leave, benefits, notes, created_at, updated_at)
                VALUES ('11111111-1111-1111-1111-111111111111', 'RBC', 'rbc', '', '', '', '', '', '', now(), now());
                INSERT INTO applications (id, company_id, posting_id, title, url, location, posting_text, status, origin, found_via, work_model,
                    office_days, team_size, working_hours, salary_posted, salary_asked, salary_offered, notes, created_at, updated_at)
                VALUES ('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', NULL, 'Developer', '', '', 'C#',
                    'Applied', 'Pasted', '', 'Unknown', '', '', '', '', '', '', '', now(), now());
                INSERT INTO application_documents (id, application_id, kind, label, file_name, content_type, size_bytes, sent_on, created_at)
                VALUES ('33333333-3333-3333-3333-333333333333', '22222222-2222-2222-2222-222222222222', 'Cv', 'CV v3', 'CV.pdf',
                    'application/pdf', 10, '2027-03-01', now());
                """, conn);
            await insert.ExecuteNonQueryAsync();
        }

        await using var upgraded = new ApplicationsDb(ApplicationsDb.Options(connection));
        await upgraded.Database.MigrateAsync();

        var document = await upgraded.Documents.SingleAsync();
        Assert.Equal(("CV v3", (string?)null), (document.Label, document.CvChoices));
        Assert.Equal("Developer", (await upgraded.Applications.SingleAsync()).Title);
    }

    private async Task<string> FreshDatabaseAsync()
    {
        var name = "upgrade_" + Guid.NewGuid().ToString("N")[..12];
        await using (var conn = new NpgsqlConnection(pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", conn);
            await create.ExecuteNonQueryAsync();
        }
        return new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Database = name }.ConnectionString;
    }
}
```

Add to `TrackerDocumentTests` (class body):

```csharp
    [Fact]
    public async Task Generated_documents_are_marked_in_the_entry()
    {
        var entry = await tracker.PasteAsync("Genco");
        await tracker.Client.PostAsync($"/api/tracker/applications/{entry.Id}/documents", Upload("%PDF"u8.ToArray(), "Final CV.pdf"));
        await using (var db = tracker.OpenDb())
        {
            db.Documents.Add(new ApplicationDocument
            {
                Id = Guid.NewGuid(), ApplicationId = entry.Id, Kind = DocumentKind.Cv, Label = "Tailored CV", FileName = "cv.docx",
                ContentType = Rules.ContentType("cv.docx"), SizeBytes = 1, SentOn = new DateOnly(2026, 10, 2), CreatedAt = DateTimeOffset.UtcNow,
                CvChoices = """{"headline":"Dev","summary":"","bulletIds":[]}""",
            });
            await db.SaveChangesAsync();
        }

        var documents = (await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{entry.Id}")).Documents;

        Assert.Equal(new[] { ("Final CV", false), ("Tailored CV", true) }, documents.Select(d => (d.Label, d.Generated)).OrderBy(d => d.Label));
    }
```

Add to `TrackerRestoreTests` (add `using System.Text.Json.Nodes;` at the top):

```csharp
    [Fact]
    public async Task Backups_from_before_generated_cvs_restore()
    {
        var sample = MirrorStoreTests.Sample();
        var json = JsonNode.Parse(JsonSerializer.Serialize(sample, MirrorStore.Json))!;
        foreach (var document in json["documents"]!.AsArray()) document!.AsObject().Remove("cvChoices");
        Directory.CreateDirectory(Path.GetDirectoryName(MirrorPath)!);
        File.WriteAllText(MirrorPath, json.ToJsonString());
        await using var factory = new ApiFactory(await FreshDatabaseAsync(), _data.Path);

        var detail = await factory.CreateClient().GetFromJsonAsync<JsonElement>($"/api/tracker/applications/{sample.Applications[0].Id}");

        var restored = Assert.Single(detail.GetProperty("documents").EnumerateArray());
        Assert.False(restored.GetProperty("generated").GetBoolean());
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~TrackerUpgradeTests|FullyQualifiedName~Generated_documents_are_marked|FullyQualifiedName~Backups_from_before_generated_cvs"`
Expected: build error CS0117, "'ApplicationDocument' does not contain a definition for 'CvChoices'" (and `Generated` on `DocumentView`).

- [ ] **Step 3: Add the column and the flag**

In `Entities.cs`, add to `ApplicationDocument` after `CreatedAt`:

```csharp
    // The choices a generated CV was made from (JSON); null for uploaded files (spec "Saving: generated is not sent").
    public string? CvChoices { get; set; }
```

In `TrackerViews.cs`, replace the `DocumentView` line with:

```csharp
// Generated: made by the Tailor CV page (it has saved choices); otherwise an uploaded file, the one actually sent.
public sealed record DocumentView(Guid Id, DocumentKind Kind, string Label, string FileName, long SizeBytes, DateOnly SentOn, bool Generated);
```

In `TrackerListEndpoints.cs` at line 81, replace `new DocumentView(d.Id, d.Kind, d.Label, d.FileName, d.SizeBytes, d.SentOn)` with:

```csharp
new DocumentView(d.Id, d.Kind, d.Label, d.FileName, d.SizeBytes, d.SentOn, d.CvChoices != null)
```

- [ ] **Step 4: Run the upgrade test to see it fail on the missing migration**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~TrackerUpgradeTests"`
Expected: FAIL. `MigrateAsync` reports pending model changes, or the query fails with "column d.cv_choices does not exist".

- [ ] **Step 5: Generate the migration**

Run: `dotnet ef migrations add AddCvChoices --project src/Api --startup-project src/Api --context ApplicationsDb --output-dir Applications/Migrations`
Expected: a new `*_AddCvChoices.cs` whose `Up` is only `migrationBuilder.AddColumn<string>(name: "cv_choices", table: "application_documents", type: "text", nullable: true);`, plus an updated `ApplicationsDbModelSnapshot.cs`. If `Up` contains anything else, stop and rule, because the model must not have drifted.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet build src/Api -warnaserror && dotnet test tests/Api.Tests --filter "FullyQualifiedName~Api.Tests.Applications"`
Expected: PASS for every Applications test, including the 3 new ones.

- [ ] **Step 7: Commit**

```bash
git add src/Api/Applications tests/Api.Tests/Applications
git commit -m "Keep a generated CV's choices on its document row"
```

---

### Task 7: Job text on the entry

**Files:**
- Create: `src/Api/Applications/EntryText.cs`
- Modify: `src/Api/Applications/TrackerListEndpoints.cs` (detail text, remove `MarketTextAsync`), `src/Api/Applications/TrackerEditEndpoints.cs` (new record and endpoint), `src/Api/Applications/Rules.cs` (`NoNul`)
- Test: `tests/Api.Tests/Applications/PostingTextTests.cs`

**Interfaces:**
- Produces:
  - `record EntryText(string Text, string Source)`.
  - `internal static class EntryTexts`, with constants `FromEntry = "entry"`, `FromMarket = "market"` and `None = "none"`, and `Task<EntryText> ResolveAsync(JobApplication entry, TrackerDb marketDb, CancellationToken ct)`.
  - `Rules.NoNul(string value, string field)`.
  - `record PostingTextEdit(uint Version, string? PostingText)`.
  - `PUT /api/tracker/applications/{id}/posting-text`, which answers 204.

- [ ] **Step 1: Write the failing tests**

`tests/Api.Tests/Applications/PostingTextTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Applications;
using Api.Endpoints;
using Api.Tests.TestSupport;

namespace Api.Tests.Applications;

[Collection("tracker")]
[Trait("Category", "Integration")]
public sealed class PostingTextTests(TrackerFixture tracker)
{
    private string Inbox => Path.Combine(tracker.DataRoot, "inbox");

    private Task<HttpResponseMessage> PutText(Guid id, uint version, string text) =>
        tracker.Client.PutAsJsonAsync($"/api/tracker/applications/{id}/posting-text", new { version, postingText = text }, TrackerFixture.Json);

    private Task<ApplicationDetail> Detail(Guid id) => tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{id}");

    [Fact]
    public async Task Text_pasted_onto_a_market_entry_replaces_the_snippet_without_touching_the_inbox()
    {
        var entry = await tracker.PostAsync<NewEntry>("/api/tracker/applications", new { postingId = "adzuna:A1" });
        var before = await Detail(entry.Id);
        Assert.Equal("Build services in C# and .NET on Azure.", before.PostingText);
        var inboxFiles = Directory.GetFiles(Inbox).Order().ToList();

        var saved = await PutText(entry.Id, before.Version, "Full posting: C#, .NET, Azure DevOps, React and SQL Server.");

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var after = await Detail(entry.Id);
        Assert.StartsWith("Full posting:", after.PostingText);
        Assert.Equal(inboxFiles, Directory.GetFiles(Inbox).Order().ToList());

        await PutText(entry.Id, after.Version, "   ");
        Assert.Equal("Build services in C# and .NET on Azure.", (await Detail(entry.Id)).PostingText);
    }

    [Fact]
    public async Task A_pasted_entry_keeps_its_inbox_file_as_first_pasted()
    {
        var entry = await tracker.PasteAsync("Textco");
        var detail = await Detail(entry.Id);
        var file = Directory.GetFiles(Inbox, "*textco*").Single();
        var original = File.ReadAllText(file);

        await PutText(entry.Id, detail.Version, "A longer version of the job text with Kubernetes.");

        Assert.Equal(original, File.ReadAllText(file));
        Assert.Contains("Kubernetes", (await Detail(entry.Id)).PostingText);
    }

    [Fact]
    public async Task A_stale_version_gets_the_reload_message()
    {
        var entry = await tracker.PasteAsync("Staletext");
        var detail = await Detail(entry.Id);
        await PutText(entry.Id, detail.Version, "First save.");

        var stale = await PutText(entry.Id, detail.Version, "Second save from an old tab.");

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ApiExceptionHandler.StaleEdit, (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(100_001, "longer than 100000")]
    [InlineData(-1, "NUL")]
    public async Task Unsavable_text_is_refused_with_the_field(int length, string message)
    {
        var entry = await tracker.PasteAsync($"Badtext {length}");
        var detail = await Detail(entry.Id);

        var response = await PutText(entry.Id, detail.Version, length < 0 ? "C#\0 and Azure" : new string('x', length));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("postingText", problem.GetProperty("field").GetString());
        Assert.Contains(message, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Saving_text_needs_the_dashboard_header()
    {
        using var outsider = tracker.CreateClient(withHeader: false);

        var response = await outsider.PutAsJsonAsync($"/api/tracker/applications/{Guid.NewGuid()}/posting-text", new { version = 1, postingText = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~PostingTextTests"`
Expected: FAIL. The PUT answers 405 or 404 because no route exists yet, so the first assertion on `NoContent` fails. The header test passes already, since the gate refuses before routing.

- [ ] **Step 3: Write the text rule**

`src/Api/Applications/EntryText.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tracker.Data;

namespace Api.Applications;

// An entry's job text and where it came from: "entry" (saved in the tracker), "market" (the collected posting) or "none".
public sealed record EntryText(string Text, string Source);

internal static class EntryTexts
{
    public const string FromEntry = "entry", FromMarket = "market", None = "none";

    // Text saved on the entry wins, so the owner can replace a market snippet with the full posting; otherwise a market
    // entry reads its posting's description.
    public static async Task<EntryText> ResolveAsync(JobApplication entry, TrackerDb marketDb, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(entry.PostingText)) return new(entry.PostingText, FromEntry);
        if (entry.Origin == EntryOrigin.Market && entry.PostingId is { } postingId)
        {
            var text = await MarketTextAsync(marketDb, postingId, ct);
            if (text.Length > 0) return new(text, FromMarket);
        }
        return new("", None);
    }

    // The market tables only exist after the Processor's first rebuild (e.g. right after a restore): no text until then.
    private static async Task<string> MarketTextAsync(TrackerDb marketDb, string postingId, CancellationToken ct)
    {
        try
        {
            return await marketDb.Postings.AsNoTracking().Where(p => p.Id == postingId).Select(p => p.Description).FirstOrDefaultAsync(ct) ?? "";
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return "";
        }
    }
}
```

In `TrackerListEndpoints.cs`:
- In the detail endpoint, replace these lines:

```csharp
            // Pasted jobs keep their own text; market postings are read from the market tables.
            var text = entry.Origin == EntryOrigin.Pasted || entry.PostingId is null
                ? entry.PostingText
                : await MarketTextAsync(marketDb, entry.PostingId, ct);
```

  with:

```csharp
            var text = (await EntryTexts.ResolveAsync(entry, marketDb, ct)).Text;
```

- Delete the private `MarketTextAsync` method and its comment, which now live in `EntryText.cs`.
- Remove `using Npgsql;` if nothing else in the file uses it.

In `Rules.cs`, add after `Optional`:

```csharp
    // Postgres text can't hold the NUL character: refuse it with the field's name instead of failing the save.
    public static string NoNul(string value, string field) =>
        value.Contains('\0') ? throw new TrackerInputException(field, $"{Label(field)} contains a NUL character, which can't be saved.") : value;
```

- [ ] **Step 4: Write the endpoint**

In `TrackerEditEndpoints.cs`, add the record after `ContactInput`:

```csharp
public sealed record PostingTextEdit(uint Version, string? PostingText);
```

and, inside `MapTrackerEdits` after the company PATCH, add:

```csharp
        tracker.MapPut("/applications/{id:guid}/posting-text", async (Guid id, PostingTextEdit body, ApplicationsDb db, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            db.Entry(entry).Property(e => e.Version).OriginalValue = body.Version;
            // Saved on the entry only, never in data/inbox: the next rebuild must not count the job twice.
            entry.PostingText = Rules.NoNul(Rules.Optional(body.PostingText, "postingText", Rules.MaxPostingText), "postingText");
            entry.UpdatedAt = clock.GetUtcNow();
            await Tracking.CommitAsync(db, mirror, "posting-text", ct);
            return Results.NoContent();
        });
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet build src/Api -warnaserror && dotnet test tests/Api.Tests --filter "FullyQualifiedName~Api.Tests.Applications"`
Expected: PASS, with all existing Applications tests unchanged. That includes `Restored_market_entries_open_before_the_next_rebuild`, which still sees `postingText == ""`.

- [ ] **Step 6: Commit**

```bash
git add src/Api/Applications tests/Api.Tests/Applications/PostingTextTests.cs
git commit -m "Let the full job text replace a market snippet on an entry"
```

---

### Task 8: CV endpoints

**Files:**
- Create: `src/Api/Cv/CvSettings.cs`, `src/Api/Cv/CvRequests.cs`, `src/Api/Cv/CvEndpoints.cs`
- Modify: `src/Api/Program.cs` (register `CvSettings`), `src/Api/Applications/TrackerEndpoints.cs` (map the endpoints), `tests/Api.Tests/TestSupport/ApiFactory.cs` (default CV path)
- Test: `tests/Api.Tests/Cv/CvRulesTests.cs`, `tests/Api.Tests/Cv/CvEndpointTests.cs`

**Interfaces:**
- Consumes:
  - Tasks 2–5: `MasterCvFile`, `CvLayout`, `CvTailor`, `CvDocx`, `CvChoices`, `CvProposal`, `TailorInput`.
  - Task 6: `ApplicationDocument.CvChoices`.
  - Task 7: `EntryTexts.ResolveAsync`, `Rules.NoNul`.
  - 2b: `Tracking.CommitAsync`, `Tracking.Today`, `TrackerMirror.DocumentPath`, `TrackerTelemetry.Source`, `MarketData.GetAsync`.
- Produces:
  - `record CvSettings(string Path)`
  - `record CvStatusView(bool Ready, IReadOnlyList<string> Problems)`
  - `record GenerateRequest(string? Headline, string? Summary, IReadOnlyList<string?>? BulletIds)`
  - `record GeneratedCv(Guid DocumentId, string FileName)`
  - `static class CvRules`, with `MaxSummary = 2000`, `MaxChosen = 300` and `CvChoices Check(GenerateRequest, MasterCv)`
  - `static class CvChoicesJson`, with `string Write(CvChoices)` and `CvChoices? Read(string?)`
  - `CvEndpoints.MapCvEndpoints(this RouteGroupBuilder)`, with `const string GeneratedLabel = "Tailored CV"`
  - Routes under `/api/tracker`: `GET /cv`, `GET /cv/full`, `GET /applications/{id}/cv` and `POST /applications/{id}/cv`.
  - `ApiFactory(string connectionString, string dataRoot, string? cvPath = null)`

- [ ] **Step 1: Point the test factory at the example CV**

Replace `tests/Api.Tests/TestSupport/ApiFactory.cs` with:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Tracker.TestSupport;

namespace Api.Tests.TestSupport;

// The real API wired to a given database and archive folder, with the committed example profile and master CV.
// config/profile.json and config/cv.json are gitignored: tests never read the owner's own files.
public sealed class ApiFactory(string connectionString, string dataRoot, string? cvPath = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:tracker", connectionString);
        builder.UseSetting("Tracker:DataRoot", dataRoot);
        builder.UseSetting("Tracker:ProfilePath", RepoConfig.PathOf("profile.example.json"));
        builder.UseSetting("Tracker:CvPath", cvPath ?? RepoConfig.PathOf("cv.example.json"));
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Api.Tests/Cv/CvRulesTests.cs`:

```csharp
using Api.Applications;
using Api.Cv;
using static Api.Tests.Cv.CvSamples;

namespace Api.Tests.Cv;

public class CvRulesTests
{
    [Fact]
    public void A_good_request_becomes_choices()
    {
        var choices = CvRules.Check(new GenerateRequest("  Senior\nDeveloper ", " Hi. ", ["a2", "a1"]), Cv());

        Assert.Equal(("Senior Developer", "Hi."), (choices.Headline, choices.Summary));
        Assert.Equal(new[] { "a2", "a1" }, choices.BulletIds);
    }

    [Theory]
    [InlineData("chosen twice", "a1", "a1")]
    [InlineData("isn't in config/cv.json", "a1", "zzz")]
    public void Bad_bullet_lists_are_refused(string message, params string[] ids)
    {
        var error = Assert.Throws<TrackerInputException>(() => CvRules.Check(new GenerateRequest("Dev", "", ids), Cv()));

        Assert.Equal("bulletIds", error.Field);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void More_than_300_bullets_are_refused() =>
        Assert.Equal("bulletIds", Assert.Throws<TrackerInputException>(() =>
            CvRules.Check(new GenerateRequest("Dev", "", [.. Enumerable.Range(0, 301).Select(i => $"b{i}")]), Cv())).Field);

    [Fact]
    public void Choices_round_trip_and_unreadable_json_is_no_choices()
    {
        var choices = new CvChoices("Dev", "Hi.", ["a1"]);

        Assert.Equal(choices.BulletIds, CvChoicesJson.Read(CvChoicesJson.Write(choices))!.BulletIds);
        Assert.Null(CvChoicesJson.Read("{ not json"));
        Assert.Null(CvChoicesJson.Read("{}"));
    }
}
```

`tests/Api.Tests/Cv/CvEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Applications;
using Api.Cv;
using Api.Tests.TestSupport;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Api.Tests.Cv;

// Runs against the committed example master CV (Alex Example); see ApiFactory.
[Collection("tracker")]
[Trait("Category", "Integration")]
public sealed class CvEndpointTests(TrackerFixture tracker)
{
    private static readonly string[] Chosen = ["acme-lead", "acme-cicd", "globex-react"];

    private Task<HttpResponseMessage> Generate(Guid id, object body) =>
        tracker.Client.PostAsJsonAsync($"/api/tracker/applications/{id}/cv", body, TrackerFixture.Json);

    private Task<NewEntry> PasteAsync(string company, string text) =>
        tracker.PostAsync<NewEntry>("/api/tracker/applications/paste", new
        {
            title = "Senior .NET Developer", company, url = "", location = "Toronto, ON", postingText = text, status = "Shortlisted",
        });

    private string FolderOf(Guid entryId) => Path.Combine(tracker.DataRoot, "tracker", "files", entryId.ToString("N"));

    private static List<string> TextOf(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx), false);
        return [.. doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>().Select(p => p.InnerText)];
    }

    [Fact]
    public async Task The_example_master_cv_is_ready()
    {
        var status = await tracker.GetAsync<CvStatusView>("/api/tracker/cv");

        Assert.Equal((true, 0), (status.Ready, status.Problems.Count));
    }

    [Fact]
    public async Task The_proposal_for_a_pasted_job_suggests_matching_bullets()
    {
        var entry = await PasteAsync("Proposeco", "We need C#, Azure DevOps CI/CD pipelines and React.");

        var proposal = await tracker.GetAsync<CvProposal>($"/api/tracker/applications/{entry.Id}/cv");

        Assert.Equal(("entry", false, "Proposeco"), (proposal.TextSource, proposal.Thin, proposal.Company));
        Assert.Contains(proposal.PostingSkills, s => s.Skill == ".NET" && s.Weight == 2);
        Assert.Equal("dotnet", proposal.SuggestedSummaryId);
        Assert.Equal(new[] { "acme-cicd", "acme-lead" }, proposal.Suggestion.BulletIds.Take(2));
        Assert.Contains("globex-react", proposal.Suggestion.BulletIds);
        Assert.Null(proposal.Saved);
    }

    [Fact]
    public async Task A_snippet_from_the_market_is_thin()
    {
        var entry = await tracker.PostAsync<NewEntry>("/api/tracker/applications", new { postingId = "adzuna:A2" });

        var proposal = await tracker.GetAsync<CvProposal>($"/api/tracker/applications/{entry.Id}/cv");

        Assert.Equal(("market", true), (proposal.TextSource, proposal.Thin));
    }

    [Fact]
    public async Task Generating_saves_the_file_with_its_choices_and_backs_it_up()
    {
        var entry = await PasteAsync("Genco Two", "C#, Azure DevOps and React.");

        var response = await Generate(entry.Id, new { headline = "Senior .NET Developer", summary = "Builds .NET systems.", bulletIds = Chosen });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<GeneratedCv>(TrackerFixture.Json))!;
        Assert.Equal("Alex Example - CV - Genco Two.docx", created.FileName);
        var document = Assert.Single((await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{entry.Id}")).Documents);
        Assert.Equal((created.DocumentId, "Tailored CV", DocumentKind.Cv, true, new DateOnly(2026, 10, 2)),
            (document.Id, document.Label, document.Kind, document.Generated, document.SentOn));

        var download = await tracker.Client.GetAsync($"/api/tracker/documents/{created.DocumentId}");
        Assert.Equal(CvDocx.ContentType, download.Content.Headers.ContentType!.MediaType);
        var text = TextOf(await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("Alex Example", text[0]);
        Assert.Contains("Set up Azure DevOps CI/CD pipelines that cut a release from a day to an hour.", text);
        Assert.DoesNotContain("Tuned SQL Server queries and indexes, halving report load times.", text);

        var backup = MirrorStore.Read(Path.Combine(tracker.DataRoot, "tracker", "tracker.json"))!;
        Assert.Contains("acme-cicd", backup.Documents.Single(d => d.Id == created.DocumentId).CvChoices);

        var again = await tracker.GetAsync<CvProposal>($"/api/tracker/applications/{entry.Id}/cv");
        Assert.Equal((new DateOnly(2026, 10, 2), "Builds .NET systems."), (again.Saved!.GeneratedOn, again.Saved.Summary));
        Assert.Equal(Chosen, again.Saved.BulletIds);
        Assert.Empty(again.Saved.DroppedIds);
    }

    [Fact]
    public async Task Saved_choices_report_bullets_that_left_the_cv()
    {
        var entry = await PasteAsync("Dropco", "C# and React.");
        await using (var db = tracker.OpenDb())
        {
            db.Documents.Add(new ApplicationDocument
            {
                Id = Guid.NewGuid(), ApplicationId = entry.Id, Kind = DocumentKind.Cv, Label = "Tailored CV", FileName = "cv.docx",
                ContentType = CvDocx.ContentType, SizeBytes = 1, SentOn = new DateOnly(2026, 9, 30), CreatedAt = DateTimeOffset.UtcNow,
                CvChoices = CvChoicesJson.Write(new CvChoices("Dev", "", ["acme-lead", "gone-1"])),
            });
            await db.SaveChangesAsync();
        }

        var proposal = await tracker.GetAsync<CvProposal>($"/api/tracker/applications/{entry.Id}/cv");

        Assert.Equal(new[] { "acme-lead" }, proposal.Saved!.BulletIds);
        Assert.Equal(new[] { "gone-1" }, proposal.Saved.DroppedIds);
    }

    [Theory]
    [InlineData("", "", "headline")]
    [InlineData("Dev\0", "", "headline")]
    [InlineData("Dev", "long", "summary")]
    [InlineData("Dev", "", "bulletIds")]
    public async Task Bad_requests_are_refused_and_nothing_is_stored(string headline, string summary, string field)
    {
        var entry = await PasteAsync($"Refuseco {field} {headline.Length}", "C#.");
        object body = field == "bulletIds"
            ? new { headline, summary, bulletIds = new[] { "acme-lead", "nope" } }
            : new { headline, summary = summary == "long" ? new string('x', 2001) : summary, bulletIds = Array.Empty<string>() };

        var response = await Generate(entry.Id, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(field, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("field").GetString());
        Assert.False(Directory.Exists(FolderOf(entry.Id)));
    }

    [Fact]
    public async Task A_missing_master_cv_is_reported_and_blocks_tailoring()
    {
        var entry = await PasteAsync("Nocvco", "C#.");
        await using var factory = new ApiFactory(tracker.ConnectionString, tracker.DataRoot, Path.Combine(tracker.DataRoot, "no-such-cv.json"));
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<CvStatusView>("/api/tracker/cv", TrackerFixture.Json);
        var proposal = await client.GetAsync($"/api/tracker/applications/{entry.Id}/cv");

        Assert.Equal((false, MasterCvFile.Missing), (status!.Ready, Assert.Single(status.Problems)));
        Assert.Equal(HttpStatusCode.BadRequest, proposal.StatusCode);
        Assert.Equal("cv", (await proposal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("field").GetString());
    }

    [Fact]
    public async Task Generating_needs_the_dashboard_header()
    {
        using var outsider = tracker.CreateClient(withHeader: false);

        var response = await outsider.PostAsJsonAsync($"/api/tracker/applications/{Guid.NewGuid()}/cv", new { headline = "Dev" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_the_entry_removes_its_generated_cv()
    {
        var entry = await PasteAsync("Gonecvco", "C#.");
        Assert.Equal(HttpStatusCode.Created, (await Generate(entry.Id, new { headline = "Dev", summary = "", bulletIds = Chosen })).StatusCode);
        Assert.True(Directory.Exists(FolderOf(entry.Id)));

        await tracker.Client.DeleteAsync($"/api/tracker/applications/{entry.Id}");

        Assert.False(Directory.Exists(FolderOf(entry.Id)));
    }

    [Fact]
    public async Task The_full_cv_holds_every_bullet()
    {
        var response = await tracker.Client.GetAsync("/api/tracker/cv/full");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Alex Example - CV.docx", response.Content.Headers.ContentDisposition!.ToString());
        var text = TextOf(await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("Mentored two junior developers through code reviews and pairing.", text);
        Assert.Contains("Built a .NET 10 and React dashboard over PostgreSQL, run with .NET Aspire and traced with OpenTelemetry.", text);
    }

    [Fact]
    public async Task An_unknown_entry_is_a_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await tracker.Client.GetAsync($"/api/tracker/applications/{Guid.NewGuid()}/cv")).StatusCode);
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~CvRulesTests|FullyQualifiedName~CvEndpointTests"`
Expected: build error CS0246, "The type or namespace name 'GenerateRequest' could not be found".

- [ ] **Step 4: Write the settings and request types**

`src/Api/Cv/CvSettings.cs`:

```csharp
namespace Api.Cv;

// Where the master CV lives: config/cv.json unless Tracker:CvPath says otherwise (the tests point it at the example).
public sealed record CvSettings(string Path);
```

`src/Api/Cv/CvRequests.cs`:

```csharp
using System.Text.Json;
using Api.Applications;

namespace Api.Cv;

public sealed record CvStatusView(bool Ready, IReadOnlyList<string> Problems);

public sealed record GenerateRequest(string? Headline, string? Summary, IReadOnlyList<string?>? BulletIds);

public sealed record GeneratedCv(Guid DocumentId, string FileName);

// Input rules for generating: the same trimming, limits and 400-with-field as every tracker write.
public static class CvRules
{
    public const int MaxSummary = 2000, MaxChosen = 300;

    public static CvChoices Check(GenerateRequest body, MasterCv cv)
    {
        var headline = Rules.NoNul(Rules.SingleLine(Rules.Required(body.Headline, "headline")), "headline");
        var summary = Rules.NoNul(Rules.Optional(body.Summary, "summary", MaxSummary), "summary");
        var ids = (body.BulletIds ?? []).Select(i => i ?? "").ToList();
        if (ids.Count > MaxChosen) throw new TrackerInputException("bulletIds", $"Choose up to {MaxChosen} bullets.");
        var known = cv.Bullets.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.FirstOrDefault(i => !known.Contains(i)) is { } unknown)
            throw new TrackerInputException("bulletIds", $"Bullet '{unknown}' isn't in config/cv.json any more. Reload the page to see your current CV.");
        if (ids.GroupBy(i => i, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new TrackerInputException("bulletIds", $"Bullet '{twice.Key}' is chosen twice.");
        return new CvChoices(headline, summary, ids);
    }
}

// The choices stored with a generated document. Unreadable JSON counts as no saved choices.
public static class CvChoicesJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Write(CvChoices choices) => JsonSerializer.Serialize(choices, Json);

    public static CvChoices? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var choices = JsonSerializer.Deserialize<CvChoices>(json, Json);
            return choices is { Headline: not null, Summary: not null, BulletIds: not null } ? choices : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 5: Write the endpoints**

`src/Api/Cv/CvEndpoints.cs`:

```csharp
using Api.Applications;
using Api.Market;
using Microsoft.EntityFrameworkCore;
using Tracker.Data;
using Tracker.Interpreting;

namespace Api.Cv;

// Tailor CV: status of the master CV, the untailored full CV, a proposal per entry, and generating the file. They sit in
// the tracker group, so the gate, the dashboard-header check and the JSON mirror apply as for every tracker write.
public static class CvEndpoints
{
    public const string GeneratedLabel = "Tailored CV";

    public static void MapCvEndpoints(this RouteGroupBuilder tracker)
    {
        tracker.MapGet("/cv", (CvSettings cv, MarketSettings settings, ILoggerFactory loggers) =>
        {
            var load = Load(cv, settings, loggers);
            return new CvStatusView(load.Ready, load.Problems);
        });

        tracker.MapGet("/cv/full", (CvSettings cvSettings, MarketSettings settings, ILoggerFactory loggers) =>
        {
            var cv = Require(cvSettings, settings, loggers);
            var everything = new CvChoices(cv.Headline, cv.Summaries[0].Text, [.. cv.Bullets.Select(b => b.Id)]);
            return Results.File(CvDocx.Render(CvLayout.Paragraphs(cv, everything, new HashSet<string>())), CvDocx.ContentType,
                CvDocx.FileName(cv.Person.Name, null));
        });

        tracker.MapGet("/applications/{id:guid}/cv", async (Guid id, ApplicationsDb db, TrackerDb marketDb, MarketData market,
            CvSettings cvSettings, MarketSettings settings, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var entry = await db.Applications.AsNoTracking().Include(a => a.Company).Include(a => a.Documents).AsSplitQuery()
                .FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new TrackerNotFoundException(Tracking.MissingEntry);
            var cv = Require(cvSettings, settings, loggers);
            var text = await EntryTexts.ResolveAsync(entry, marketDb, ct);
            var snippet = text.Source == EntryTexts.FromMarket
                && (await market.GetAsync(ct)).Data.Postings.FirstOrDefault(p => p.Id == entry.PostingId) is { IsFullText: false };
            var last = entry.Documents.Where(d => d.CvChoices is not null).OrderByDescending(d => d.CreatedAt)
                .Select(d => (Choices: CvChoicesJson.Read(d.CvChoices), d.SentOn)).FirstOrDefault(d => d.Choices is not null);
            return CvTailor.Propose(cv, settings.Catalog, settings.Profile,
                new TailorInput(entry.Id, entry.Title, entry.Company!.Name, text.Text, text.Source, snippet, entry.Version),
                last.Choices is { } choices ? (choices, last.SentOn) : null);
        });

        tracker.MapPost("/applications/{id:guid}/cv", async (Guid id, GenerateRequest body, ApplicationsDb db, TrackerDb marketDb,
            TrackerMirror mirror, CvSettings cvSettings, MarketSettings settings, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var entry = await db.Applications.Include(a => a.Company).FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new TrackerNotFoundException(Tracking.MissingEntry);
            var cv = Require(cvSettings, settings, loggers);
            var choices = CvRules.Check(body, cv);
            var text = await EntryTexts.ResolveAsync(entry, marketDb, ct);
            var wanted = settings.Catalog.Match(SkillText.ForMatching(entry.Title, text.Text));

            using var activity = TrackerTelemetry.Source.StartActivity("tracker.cv.generate");
            var bytes = CvDocx.Render(CvLayout.Paragraphs(cv, choices, wanted));
            activity?.SetTag("tracker.entry_id", id);
            activity?.SetTag("tracker.cv.bullets", choices.BulletIds.Count);
            activity?.SetTag("tracker.cv.bytes", bytes.Length);

            var now = clock.GetUtcNow();
            var document = new ApplicationDocument
            {
                Id = Guid.NewGuid(), ApplicationId = id, Kind = DocumentKind.Cv, Label = GeneratedLabel,
                FileName = CvDocx.FileName(cv.Person.Name, entry.Company!.Name), ContentType = CvDocx.ContentType, SizeBytes = bytes.Length,
                SentOn = Tracking.Today(clock), CreatedAt = now, CvChoices = CvChoicesJson.Write(choices),
            };
            // File first, row second, as for uploads: a failed save removes the file.
            var path = mirror.DocumentPath(document);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, ct);
            try
            {
                db.Documents.Add(document);
                entry.UpdatedAt = now;
                await Tracking.CommitAsync(db, mirror, "generate-cv", ct);
            }
            catch
            {
                File.Delete(path);
                throw;
            }
            return Results.Created($"/api/tracker/documents/{document.Id}", new GeneratedCv(document.Id, document.FileName));
        });
    }

    private static CvLoad Load(CvSettings cv, MarketSettings settings, ILoggerFactory loggers)
    {
        var load = MasterCvFile.Load(cv.Path, settings.Catalog);
        // The count only: problems quote the owner's CV, which stays out of the logs.
        if (!load.Ready) loggers.CreateLogger("Cv").LogWarning("The master CV has {Count} problems", load.Problems.Count);
        return load;
    }

    private static MasterCv Require(CvSettings cv, MarketSettings settings, ILoggerFactory loggers)
    {
        var load = Load(cv, settings, loggers);
        return load.Cv ?? throw new TrackerInputException("cv", string.Join(" ", load.Problems));
    }
}
```

In `src/Api/Program.cs`, add `using Api.Cv;` and, after the `MarketSettings` registration, add:

```csharp
builder.Services.AddSingleton(new CvSettings(builder.Configuration["Tracker:CvPath"] ?? Path.Combine(root, "config", "cv.json")));
```

In `src/Api/Applications/TrackerEndpoints.cs`, add `using Api.Cv;` and, after `tracker.MapTrackerDocuments();`, add:

```csharp
        tracker.MapCvEndpoints();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet build src/Api -warnaserror && dotnet test tests/Api.Tests`
Expected: PASS, the whole Api suite, which is the 2b count plus every Cv test.

If `A_snippet_from_the_market_is_thin` gets a 404 on the shortlist because `adzuna:A2` isn't in the market snapshot, use `adzuna:A1` in `PostingTextTests` and pick another Adzuna posting the fixture archive has here. Ledger it as a Ruling.

- [ ] **Step 7: Commit**

```bash
git add src/Api/Cv src/Api/Program.cs src/Api/Applications/TrackerEndpoints.cs tests/Api.Tests/TestSupport/ApiFactory.cs tests/Api.Tests/Cv
git commit -m "Serve CV proposals and generate tailored CVs on entries"
```

---

### Task 9: Dashboard API client and tailoring logic

**Files:**
- Modify: `src/Dashboard/src/tracker/api.ts`
- Create: `src/Dashboard/src/tracker/cv/tailor.ts`, `src/Dashboard/src/tracker/cv/sample.ts`
- Test: `src/Dashboard/src/tracker/cv/tailor.test.ts`

**Interfaces:**
- Consumes: the JSON shapes from Task 8. They use camelCase; `kind` is `'Role' | 'Project'`, and `level` is `'None' | 'Learning' | 'Working' | 'Strong'`.
- Produces:
  - Types in `tracker/api.ts`: `ProfileLevel`, `PostingSkill`, `ProposalBullet`, `ProposalSection`, `ProposalSkillItem`, `ProposalSkillGroup`, `ProposalSummary`, `ProposalCertification`, `CvChoices`, `SavedChoices`, `CvProposal`, `CvStatus` and `GeneratedCv`. `DocumentView.generated: boolean` is new.
  - `tracker` methods: `cvStatus()`, `fullCvUrl`, `proposal(id)`, `savePostingText(id, { postingText, version })` and `generateCv(id, choices)`.
  - `tailor.ts`:
    - `type Coverage = 'bullet' | 'listed' | 'available' | 'missing'`
    - `WORDS_PER_PAGE = 450` and `PAGE_LIMIT = 2`
    - `coverage(proposal, chosen): Map<string, Coverage>`
    - `bulletsShowing(proposal, skill): Set<string>`
    - `initialChoices(proposal): CvChoices`
    - `chosenIn(section, choices): ProposalBullet[]`
    - `toggle(choices, bulletId): CvChoices`
    - `move(choices, section, bulletId, delta): CvChoices`
    - `words(text): number`
    - `length(proposal, choices): { words, pages }`
  - `sample.ts` exports `PROPOSAL: CvProposal`, the test fixture used by Task 10.

- [ ] **Step 1: Add the API types and calls**

In `src/Dashboard/src/tracker/api.ts`:
- Replace the `DocumentView` interface with:

```ts
export interface DocumentView { id: string; kind: DocumentKind; label: string; fileName: string; sizeBytes: number; sentOn: string; generated: boolean }
```

- After `export interface NewEntry { id: string }`, add:

```ts
export type ProfileLevel = 'None' | 'Learning' | 'Working' | 'Strong';
export interface PostingSkill { skill: string; weight: number; level: ProfileLevel }
export interface ProposalBullet { id: string; text: string; skills: string[]; score: number; always: boolean }
export interface ProposalSection {
  id: string;
  kind: 'Role' | 'Project';
  title: string;
  company: string;
  location: string;
  dates: string;
  maxBullets: number;
  bullets: ProposalBullet[];
}
export interface ProposalSkillItem { text: string; skills: string[]; matches: boolean }
export interface ProposalSkillGroup { name: string; items: ProposalSkillItem[] }
export interface ProposalSummary { id: string; families: string[]; text: string }
export interface ProposalCertification { name: string; year: string; skills: string[] }
export interface CvChoices { headline: string; summary: string; bulletIds: string[] }
export interface SavedChoices extends CvChoices { generatedOn: string; droppedIds: string[] }
export interface CvProposal {
  entryId: string;
  title: string;
  company: string;
  textSource: 'entry' | 'market' | 'none';
  thin: boolean;
  version: number;
  postingSkills: PostingSkill[];
  headline: string;
  summaries: ProposalSummary[];
  suggestedSummaryId: string;
  skillGroups: ProposalSkillGroup[];
  sections: ProposalSection[];
  certifications: ProposalCertification[];
  suggestion: CvChoices;
  saved: SavedChoices | null;
  fixedWords: number;
}
export interface CvStatus { ready: boolean; problems: string[] }
export interface GeneratedCv { documentId: string; fileName: string }
```

- Inside the `tracker` object, after `remove`, add:

```ts
  cvStatus: () => request<CvStatus>(`${BASE}/cv`),
  fullCvUrl: `${BASE}/cv/full`,
  proposal: (id: string) => request<CvProposal>(`${entry(id)}/cv`),
  savePostingText: (id: string, input: { postingText: string; version: number }) => send<void>('PUT', `${entry(id)}/posting-text`, input),
  generateCv: (id: string, choices: CvChoices) => send<GeneratedCv>('POST', `${entry(id)}/cv`, choices),
```

- [ ] **Step 2: Write the test fixture and failing tests**

`src/Dashboard/src/tracker/cv/sample.ts`:

```ts
import type { CvProposal } from '../api';

// A small proposal for tests: two roles, one skill group, one certification.
// Suggested: a1, a2 (Acme, max 2) and g1. Posting skills: .NET and Docker in a ticked bullet, Azure only on the
// certification, Kubernetes only in the unticked a3, SQL Server nowhere (profile says Working).
export const PROPOSAL: CvProposal = {
  entryId: 'a1', title: 'Senior .NET Developer', company: 'RBC', textSource: 'entry', thin: false, version: 42,
  postingSkills: [
    { skill: '.NET', weight: 2, level: 'Strong' },
    { skill: 'Azure', weight: 1, level: 'Working' },
    { skill: 'Docker', weight: 1, level: 'None' },
    { skill: 'Kubernetes', weight: 1, level: 'Learning' },
    { skill: 'SQL Server', weight: 1, level: 'Working' },
  ],
  headline: 'Senior .NET Developer',
  summaries: [
    { id: 'dotnet', families: ['dotnet', 'fullstack'], text: 'Builds .NET systems.' },
    { id: 'lead', families: ['lead_manager'], text: 'Leads small teams.' },
  ],
  suggestedSummaryId: 'dotnet',
  skillGroups: [{ name: 'Backend', items: [{ text: 'C# / .NET', skills: ['.NET', 'C#'], matches: true }, { text: 'SQL', skills: ['SQL'], matches: false }] }],
  sections: [
    {
      id: 'acme', kind: 'Role', title: 'Senior Developer', company: 'Acme', location: 'Toronto, ON', dates: 'Oct 2023 – Present', maxBullets: 2,
      bullets: [
        { id: 'a1', text: 'Led the team on .NET.', skills: ['.NET'], score: 2, always: true },
        { id: 'a2', text: 'Ran Docker in production.', skills: ['Docker'], score: 1, always: false },
        { id: 'a3', text: 'Moved services to Kubernetes.', skills: ['Kubernetes'], score: 1, always: false },
      ],
    },
    {
      id: 'globex', kind: 'Role', title: 'Developer', company: 'Globex', location: '', dates: 'Jun 2016 – Sep 2023', maxBullets: 3,
      bullets: [{ id: 'g1', text: 'Wrote APIs.', skills: ['REST APIs'], score: 0, always: false }],
    },
  ],
  certifications: [{ name: 'AZ-204', year: '2026', skills: ['Azure'] }],
  suggestion: { headline: 'Senior .NET Developer', summary: 'Builds .NET systems.', bulletIds: ['a1', 'a2', 'g1'] },
  saved: null,
  fixedWords: 100,
};
```

`src/Dashboard/src/tracker/cv/tailor.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { PROPOSAL } from './sample';
import { bulletsShowing, chosenIn, coverage, initialChoices, length, move, toggle, words } from './tailor';

describe('CV tailoring logic', () => {
  it('places each posting skill by what the ticked bullets show', () => {
    expect(Object.fromEntries(coverage(PROPOSAL, ['a1', 'a2', 'g1']))).toEqual({
      '.NET': 'bullet', Azure: 'listed', Docker: 'bullet', Kubernetes: 'available', 'SQL Server': 'missing',
    });
    expect(coverage(PROPOSAL, ['a1']).get('Docker')).toBe('available');
  });

  it('finds the bullets that show a skill', () => {
    expect([...bulletsShowing(PROPOSAL, 'Kubernetes')]).toEqual(['a3']);
  });

  it('starts from the saved choices when there are any', () => {
    expect(initialChoices(PROPOSAL)).toEqual(PROPOSAL.suggestion);
    const saved = { generatedOn: '2027-03-01', headline: 'Lead', summary: 'Hi.', bulletIds: ['a3'], droppedIds: [] };
    expect(initialChoices({ ...PROPOSAL, saved })).toEqual({ headline: 'Lead', summary: 'Hi.', bulletIds: ['a3'] });
  });

  it('ticks onto the end, unticks, and moves only within a section', () => {
    const ticked = toggle(PROPOSAL.suggestion, 'a3');
    expect(ticked.bulletIds).toEqual(['a1', 'a2', 'g1', 'a3']);
    expect(chosenIn(PROPOSAL.sections[0], ticked).map((b) => b.id)).toEqual(['a1', 'a2', 'a3']);

    const moved = move(ticked, PROPOSAL.sections[0], 'a3', -1);
    expect(chosenIn(PROPOSAL.sections[0], moved).map((b) => b.id)).toEqual(['a1', 'a3', 'a2']);
    expect(move(moved, PROPOSAL.sections[0], 'a1', -1)).toBe(moved);
    expect(toggle(moved, 'a2').bulletIds).toEqual(['a1', 'a3', 'g1']);
  });

  it('estimates the length from the words on the page', () => {
    expect(words('  Built   CI/CD \n pipelines ')).toBe(3);
    // 100 fixed + 3 headline + 3 summary + (5 + 4 + 2) bullet words = 117; 117 / 450 = 0.26
    expect(length(PROPOSAL, PROPOSAL.suggestion)).toEqual({ words: 117, pages: 0.3 });
  });
});
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd src/Dashboard && npx vitest run src/tracker/cv/tailor.test.ts`
Expected: FAIL with "Failed to resolve import "./tailor"".

- [ ] **Step 4: Write the logic**

`src/Dashboard/src/tracker/cv/tailor.ts`:

```ts
import type { CvChoices, CvProposal, ProposalBullet, ProposalSection } from '../api';

// Where the CV shows a posting skill: a ticked bullet, only the skills list or a certification, only unticked
// bullets, or nowhere in cv.json.
export type Coverage = 'bullet' | 'listed' | 'available' | 'missing';

export const WORDS_PER_PAGE = 450;
export const PAGE_LIMIT = 2;

export function coverage(proposal: CvProposal, chosen: string[]): Map<string, Coverage> {
  const picked = new Set(chosen);
  const bullets = proposal.sections.flatMap((s) => s.bullets);
  const inChosen = new Set(bullets.filter((b) => picked.has(b.id)).flatMap((b) => b.skills));
  const listed = new Set([
    ...proposal.skillGroups.flatMap((g) => g.items.flatMap((i) => i.skills)),
    ...proposal.certifications.flatMap((c) => c.skills),
  ]);
  const anywhere = new Set(bullets.flatMap((b) => b.skills));
  return new Map(proposal.postingSkills.map(({ skill }): [string, Coverage] => [
    skill,
    inChosen.has(skill) ? 'bullet' : listed.has(skill) ? 'listed' : anywhere.has(skill) ? 'available' : 'missing',
  ]));
}

// The bullets that show a skill: clicking an "available" chip highlights them.
export function bulletsShowing(proposal: CvProposal, skill: string): Set<string> {
  return new Set(proposal.sections.flatMap((s) => s.bullets).filter((b) => b.skills.includes(skill)).map((b) => b.id));
}

// Where the page starts: the last generated CV's choices, or the fresh suggestion.
export function initialChoices(proposal: CvProposal): CvChoices {
  const saved = proposal.saved;
  return saved ? { headline: saved.headline, summary: saved.summary, bulletIds: saved.bulletIds } : proposal.suggestion;
}

// The ticked bullets of one section, in the order they will print.
export function chosenIn(section: ProposalSection, choices: CvChoices): ProposalBullet[] {
  const byId = new Map(section.bullets.map((b) => [b.id, b]));
  return choices.bulletIds.flatMap((id) => {
    const bullet = byId.get(id);
    return bullet ? [bullet] : [];
  });
}

// Ticking adds the bullet after its section's ticked ones (the list's order within a section is the print order);
// unticking removes it.
export function toggle(choices: CvChoices, bulletId: string): CvChoices {
  const on = choices.bulletIds.includes(bulletId);
  return { ...choices, bulletIds: on ? choices.bulletIds.filter((id) => id !== bulletId) : [...choices.bulletIds, bulletId] };
}

// Swaps a ticked bullet with the previous (-1) or next (1) ticked bullet of the same section.
export function move(choices: CvChoices, section: ProposalSection, bulletId: string, delta: -1 | 1): CvChoices {
  const mine = new Set(section.bullets.map((b) => b.id));
  const positions = choices.bulletIds.flatMap((id, i) => (mine.has(id) ? [i] : []));
  const at = positions.findIndex((i) => choices.bulletIds[i] === bulletId);
  const other = positions[at + delta];
  if (at < 0 || other === undefined) return choices;
  const ids = [...choices.bulletIds];
  [ids[positions[at]], ids[other]] = [ids[other], ids[positions[at]]];
  return { ...choices, bulletIds: ids };
}

export function words(text: string): number {
  return text.split(/\s+/).filter(Boolean).length;
}

// An estimate: only Word knows the real page count.
export function length(proposal: CvProposal, choices: CvChoices): { words: number; pages: number } {
  const picked = new Set(choices.bulletIds);
  const bulletWords = proposal.sections.flatMap((s) => s.bullets).filter((b) => picked.has(b.id)).reduce((n, b) => n + words(b.text), 0);
  const total = proposal.fixedWords + words(choices.headline) + words(choices.summary) + bulletWords;
  return { words: total, pages: Math.round((total / WORDS_PER_PAGE) * 10) / 10 };
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd src/Dashboard && npx vitest run src/tracker/cv/tailor.test.ts && npm run typecheck`
Expected: PASS, 5 tests, and no type errors. Existing fixtures that build `DocumentView` objects may now need `generated: false`; the type check names each one. Add the field to each.

- [ ] **Step 6: Commit**

```bash
git add src/Dashboard/src/tracker/api.ts src/Dashboard/src/tracker/cv
git commit -m "Add the dashboard's CV types and tailoring logic"
```

---

### Task 10: The Tailor CV page

**Files:**
- Create: `src/Dashboard/src/tracker/cv/download.ts`, `PostingTextForm.tsx`, `CoveragePanel.tsx`, `SectionPicker.tsx`, `TailorPage.tsx` (all in `src/Dashboard/src/tracker/cv/`)
- Modify: `src/Dashboard/src/App.tsx` (route), `src/Dashboard/src/theme.css`
- Test: `src/Dashboard/src/tracker/cv/TailorPage.test.tsx`

**Interfaces:**
- Consumes:
  - Task 9: `tracker.cvStatus`, `tracker.proposal`, `tracker.savePostingText`, `tracker.generateCv`, `tracker.documentUrl`, and every function in `tailor.ts`. The test uses `PROPOSAL`.
  - 2b: `useTrackerMutation`, `SaveError`, `Field`, `QueryState`, `dayText`.
- Produces:
  - `TailorPage`, routed at `/tracker/:id/cv`.
  - `startDownload(url: string): void`.
  - Query keys `['tracker', 'cv', 'status']` and `['tracker', 'cv', 'proposal', id]`.

- [ ] **Step 1: Write the failing tests**

`src/Dashboard/src/tracker/cv/TailorPage.test.tsx`:

```tsx
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { mockApi, renderRoute, reply, sentBody } from '../../test/render';
import { startDownload } from './download';
import { PROPOSAL } from './sample';
import { TailorPage } from './TailorPage';

vi.mock('./download', () => ({ startDownload: vi.fn() }));

const READY = { ready: true, problems: [] };
const open = () => renderRoute('/tracker/a1/cv', <TailorPage />, '/tracker/:id/cv');
const group = (title: string) => screen.getByRole('heading', { level: 4, name: new RegExp(`^${title}`) }).parentElement as HTMLElement;

describe('Tailor CV page', () => {
  beforeEach(() => vi.mocked(startDownload).mockClear());

  it('shows the suggestion and moves skills between groups as bullets are ticked', async () => {
    mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': PROPOSAL });
    open();

    expect(await screen.findByRole('checkbox', { name: 'Ran Docker in production.' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Moved services to Kubernetes.' })).not.toBeChecked();
    expect(within(group('Available')).getByRole('button', { name: 'Kubernetes' })).toBeInTheDocument();
    expect(within(group('Not in your CV')).getByText(/add a true bullet to cv.json/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('checkbox', { name: 'Moved services to Kubernetes.' }));

    expect(within(group('In a bullet')).getByText('Kubernetes')).toBeInTheDocument();
    expect(screen.getByText(/3 of 2 bullets: more than cv.json suggests/)).toBeInTheDocument();
  });

  it('highlights the bullets that show an available skill', async () => {
    mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': PROPOSAL });
    open();

    fireEvent.click(await screen.findByRole('button', { name: 'Kubernetes' }));

    expect(screen.getByRole('checkbox', { name: 'Moved services to Kubernetes.' }).closest('li')).toHaveClass('highlight');
    expect(screen.getByRole('checkbox', { name: 'Ran Docker in production.' }).closest('li')).not.toHaveClass('highlight');
  });

  it('generates with the edited headline and reordered bullets, then downloads', async () => {
    mockApi({
      '/api/tracker/cv': READY,
      '/api/tracker/applications/a1/cv': PROPOSAL,
      'POST /api/tracker/applications/a1/cv': reply(201, { documentId: 'd9', fileName: 'Alex Example - CV - RBC.docx' }),
    });
    open();

    fireEvent.change(await screen.findByLabelText('Headline'), { target: { value: 'Senior .NET Engineer' } });
    fireEvent.click(screen.getByRole('button', { name: 'Move up: Ran Docker in production.' }));
    fireEvent.click(screen.getByRole('button', { name: 'Generate .docx' }));

    await waitFor(() => expect(startDownload).toHaveBeenCalledWith('/api/tracker/documents/d9'));
    expect(sentBody('POST /api/tracker/applications/a1/cv')).toEqual({
      headline: 'Senior .NET Engineer', summary: 'Builds .NET systems.', bulletIds: ['a2', 'a1', 'g1'],
    });
    expect(await screen.findByRole('link', { name: 'Alex Example - CV - RBC.docx' })).toHaveAttribute('href', '/api/tracker/documents/d9');
  });

  it('resets to the suggestion after changes', async () => {
    mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': PROPOSAL });
    open();

    fireEvent.click(await screen.findByRole('checkbox', { name: 'Ran Docker in production.' }));
    expect(screen.getByRole('checkbox', { name: 'Ran Docker in production.' })).not.toBeChecked();
    fireEvent.click(screen.getByRole('button', { name: 'Reset to suggestion' }));

    expect(screen.getByRole('checkbox', { name: 'Ran Docker in production.' })).toBeChecked();
  });

  it('starts from the last generated CV and says when bullets left cv.json', async () => {
    const saved = { generatedOn: '2027-03-01', headline: 'Lead dev', summary: 'Leads small teams.', bulletIds: ['a3'], droppedIds: ['gone-1'] };
    mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': { ...PROPOSAL, saved } });
    open();

    expect(await screen.findByLabelText('Headline')).toHaveValue('Lead dev');
    expect(screen.getByRole('checkbox', { name: 'Moved services to Kubernetes.' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Ran Docker in production.' })).not.toBeChecked();
    expect(screen.getByLabelText('Summary variant')).toHaveValue('lead');
    expect(screen.getByText(/1 bullet from your last CV is no longer in cv.json/)).toBeInTheDocument();
  });

  it('asks for the full job text when the posting is only a snippet', async () => {
    mockApi({
      '/api/tracker/cv': READY,
      '/api/tracker/applications/a1/cv': { ...PROPOSAL, thin: true, textSource: 'market' },
      'PUT /api/tracker/applications/a1/posting-text': reply(204),
    });
    open();

    fireEvent.change(await screen.findByLabelText('Full job text'), { target: { value: 'The whole posting: C#, Kubernetes.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save job text' }));

    await waitFor(() => expect(sentBody('PUT /api/tracker/applications/a1/posting-text'))
      .toEqual({ postingText: 'The whole posting: C#, Kubernetes.', version: 42 }));
  });

  it('lists master CV problems instead of the page', async () => {
    const calls = mockApi({ '/api/tracker/cv': { ready: false, problems: ['roles[0].id: "x" is used twice.'] } });
    open();

    expect(await screen.findByText('roles[0].id: "x" is used twice.')).toBeInTheDocument();
    expect(calls).not.toContain('/api/tracker/applications/a1/cv');
  });

  it('shows the estimated length and warns past two pages', async () => {
    mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': { ...PROPOSAL, fixedWords: 1000 } });
    open();

    // 1000 + 3 + 3 + 11 = 1017 words; 1017 / 450 = 2.26
    expect(await screen.findByText(/About 2.3 pages \(1017 words\): over 2 pages/)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/Dashboard && npx vitest run src/tracker/cv/TailorPage.test.tsx`
Expected: FAIL with "Failed to resolve import "./download"" or "./TailorPage".

- [ ] **Step 3: Write the small pieces**

`src/Dashboard/src/tracker/cv/download.ts`:

```ts
// Starts a browser download of a file the API serves as an attachment (the generated CV).
export function startDownload(url: string): void {
  const link = document.createElement('a');
  link.href = url;
  link.rel = 'noopener';
  document.body.appendChild(link);
  link.click();
  link.remove();
}
```

`src/Dashboard/src/tracker/cv/PostingTextForm.tsx`:

```tsx
import { useState } from 'react';
import { tracker } from '../api';
import { Field } from '../Field';
import { SaveError } from '../SaveError';
import { useTrackerMutation } from '../useTrackerMutation';

// Replaces a thin job text (a search snippet, or none) with the full posting. It's saved on this entry only, never in
// the market data.
export function PostingTextForm({ entryId, version }: { entryId: string; version: number }) {
  const [text, setText] = useState('');
  const save = useTrackerMutation((postingText: string) => tracker.savePostingText(entryId, { postingText, version }), () => setText(''));
  return (
    <form className="panel" aria-labelledby="h-posting-text" onSubmit={(e) => {
      e.preventDefault();
      if (text.trim()) save.mutate(text);
    }}>
      <h3 id="h-posting-text">Paste the full job text</h3>
      <p className="hint">
        This entry only has a short snippet of the posting (or none), so the match below is rough. Paste the whole posting for a
        better one. It's saved on this entry only.
      </p>
      <Field id="full-text" label="Full job text" wide>
        <textarea id="full-text" rows={8} value={text} onChange={(e) => setText(e.target.value)} />
      </Field>
      <div className="actions">
        <button type="submit" className="primary" disabled={!text.trim() || save.isPending}>Save job text</button>
      </div>
      <SaveError error={save.error} />
    </form>
  );
}
```

`src/Dashboard/src/tracker/cv/CoveragePanel.tsx`:

```tsx
import type { CvProposal, ProfileLevel } from '../api';
import type { Coverage } from './tailor';

const GROUPS: { state: Coverage; title: string; hint: string }[] = [
  { state: 'bullet', title: 'In a bullet', hint: 'A ticked bullet shows it.' },
  { state: 'listed', title: 'Listed', hint: 'Only your skills list or a certification shows it.' },
  { state: 'available', title: 'Available', hint: 'Only unticked bullets show it. Click one to find them.' },
  { state: 'missing', title: 'Not in your CV', hint: 'Nothing in cv.json shows it. The page never adds a claim.' },
];

function missingText(level: ProfileLevel): string {
  return level === 'Strong' || level === 'Working' ? 'your profile says you know this: add a true bullet to cv.json' : 'gap';
}

// What the posting asks for, grouped by where the CV (as currently ticked) shows it.
export function CoveragePanel({ proposal, states, highlight, onHighlight }: {
  proposal: CvProposal;
  states: Map<string, Coverage>;
  highlight: string | null;
  onHighlight: (skill: string | null) => void;
}) {
  return (
    <section className="panel" aria-labelledby="h-coverage">
      <h3 id="h-coverage">What the posting asks for</h3>
      {proposal.postingSkills.length === 0 && <p className="empty">No skills found in the job text yet.</p>}
      {GROUPS.map(({ state, title, hint }) => {
        const skills = proposal.postingSkills.filter((s) => states.get(s.skill) === state);
        if (skills.length === 0) return null;
        return (
          <div key={state} className="coverage-group">
            <h4>{title} <span className="count">{skills.length}</span></h4>
            <p className="hint">{hint}</p>
            <ul className="skill-chips">
              {skills.map((s) => (
                <li key={s.skill}>
                  {state === 'available' ? (
                    <button type="button" className={`chip cover-${state}`} aria-pressed={highlight === s.skill}
                      onClick={() => onHighlight(highlight === s.skill ? null : s.skill)}>{s.skill}</button>
                  ) : (
                    <span className={`chip cover-${state}`}>{s.skill}</span>
                  )}
                  {state === 'missing' && <span className="hint"> {missingText(s.level)}</span>}
                </li>
              ))}
            </ul>
          </div>
        );
      })}
    </section>
  );
}
```

`src/Dashboard/src/tracker/cv/SectionPicker.tsx`:

```tsx
import type { CvChoices, ProposalSection } from '../api';
import { chosenIn } from './tailor';

function firstWords(text: string): string {
  const parts = text.split(/\s+/).filter(Boolean);
  return parts.slice(0, 4).join(' ') + (parts.length > 4 ? '…' : '');
}

// One role or project: ticked bullets first, in print order with ↑/↓, then the unticked ones in file order.
export function SectionPicker({ section, choices, wanted, highlighted, onToggle, onMove }: {
  section: ProposalSection;
  choices: CvChoices;
  wanted: Set<string>;
  highlighted: Set<string>;
  onToggle: (bulletId: string) => void;
  onMove: (bulletId: string, delta: -1 | 1) => void;
}) {
  const chosen = chosenIn(section, choices);
  const order = new Map(chosen.map((b, i) => [b.id, i]));
  const rest = section.bullets.filter((b) => !order.has(b.id));
  const over = chosen.length > section.maxBullets;
  return (
    <fieldset className="panel picker">
      <legend>
        <strong>{section.company ? `${section.title} — ${section.company}` : section.title}</strong>{' '}
        <span className="muted small">{[section.location, section.dates].filter(Boolean).join(' | ')}</span>
      </legend>
      <p className={over ? 'field-error' : 'hint'}>
        {chosen.length} of {section.maxBullets} bullets{over ? ': more than cv.json suggests for this role. Fine, but keep the CV tight.' : ''}
      </p>
      <ul className="bullets">
        {[...chosen, ...rest].map((b) => {
          const at = order.get(b.id);
          return (
            <li key={b.id} className={highlighted.has(b.id) ? 'highlight' : undefined}>
              <label className="bullet-pick">
                <input type="checkbox" checked={at !== undefined} onChange={() => onToggle(b.id)} />
                <span>{b.text}</span>
              </label>
              <span className="skill-chips">
                {b.skills.map((s) => <span key={s} className={`chip ${wanted.has(s) ? 'chip-match' : 'chip-plain'}`}>{s}</span>)}
              </span>
              {at !== undefined && (
                <span className="actions">
                  <button type="button" className="small-button" disabled={at === 0} onClick={() => onMove(b.id, -1)}
                    aria-label={`Move up: ${firstWords(b.text)}`}>↑</button>
                  <button type="button" className="small-button" disabled={at === chosen.length - 1} onClick={() => onMove(b.id, 1)}
                    aria-label={`Move down: ${firstWords(b.text)}`}>↓</button>
                </span>
              )}
            </li>
          );
        })}
      </ul>
    </fieldset>
  );
}
```

- [ ] **Step 4: Write the page**

`src/Dashboard/src/tracker/cv/TailorPage.tsx`:

```tsx
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { QueryState } from '../../components/States';
import { tracker, type CvChoices, type CvProposal, type GeneratedCv } from '../api';
import { dayText } from '../dates';
import { Field } from '../Field';
import { SaveError } from '../SaveError';
import { useTrackerMutation } from '../useTrackerMutation';
import { CoveragePanel } from './CoveragePanel';
import { startDownload } from './download';
import { PostingTextForm } from './PostingTextForm';
import { SectionPicker } from './SectionPicker';
import { bulletsShowing, coverage, initialChoices, length, move, PAGE_LIMIT, toggle } from './tailor';

type Made = GeneratedCv & { entryId: string };

export function TailorPage() {
  const { id = '' } = useParams();
  const [made, setMade] = useState<Made | null>(null);
  const status = useQuery({ queryKey: ['tracker', 'cv', 'status'], queryFn: tracker.cvStatus });
  const ready = status.data?.ready === true;
  const proposal = useQuery({ queryKey: ['tracker', 'cv', 'proposal', id], queryFn: () => tracker.proposal(id), enabled: ready });

  if (!status.data) return <QueryState query={status} what="your master CV" />;
  if (!ready) {
    return (
      <section className="section" aria-labelledby="h-tailor">
        <p className="eyebrow"><Link to={`/tracker/${id}`}>Back to the entry</Link></p>
        <h2 id="h-tailor">Tailor CV</h2>
        <div className="banner" role="alert">
          <p>Your master CV needs fixing before you can tailor it:</p>
          <ul>{status.data.problems.map((p, i) => <li key={i}>{p}</li>)}</ul>
        </div>
      </section>
    );
  }
  if (!proposal.data) return <QueryState query={proposal} what="the CV proposal" />;
  // A new entry version (job text saved, a CV generated) starts the page again from the new proposal.
  return (
    <Tailor key={`${proposal.data.entryId}-${proposal.data.version}`} proposal={proposal.data}
      made={made?.entryId === proposal.data.entryId ? made : null} onMade={setMade} />
  );
}

function Tailor({ proposal, made, onMade }: { proposal: CvProposal; made: Made | null; onMade: (made: Made) => void }) {
  const [choices, setChoices] = useState(() => initialChoices(proposal));
  const [highlight, setHighlight] = useState<string | null>(null);
  const generate = useTrackerMutation((picked: CvChoices) => tracker.generateCv(proposal.entryId, picked), (result) => {
    onMade({ ...result, entryId: proposal.entryId });
    startDownload(tracker.documentUrl(result.documentId));
  });

  const states = coverage(proposal, choices.bulletIds);
  const wanted = new Set(proposal.postingSkills.map((s) => s.skill));
  const highlighted = highlight ? bulletsShowing(proposal, highlight) : new Set<string>();
  const size = length(proposal, choices);
  const summaryId = proposal.summaries.find((s) => s.text === choices.summary)?.id ?? '';
  const dropped = proposal.saved?.droppedIds.length ?? 0;

  return (
    <section className="section" aria-labelledby="h-tailor">
      <p className="eyebrow"><Link to={`/tracker/${proposal.entryId}`}>{proposal.company}</Link> / Tailor CV</p>
      <h2 id="h-tailor">Tailor CV: {proposal.title}</h2>
      {proposal.saved && <p className="hint">Starting from the CV you generated on {dayText(proposal.saved.generatedOn)}.</p>}
      {dropped > 0 && (
        <div className="banner warn" role="status">
          {dropped} {dropped === 1 ? 'bullet' : 'bullets'} from your last CV {dropped === 1 ? 'is' : 'are'} no longer in cv.json.
        </div>
      )}
      <div className="tailor">
        <div className="tailor-side">
          {proposal.thin && <PostingTextForm entryId={proposal.entryId} version={proposal.version} />}
          <CoveragePanel proposal={proposal} states={states} highlight={highlight} onHighlight={setHighlight} />
          <p className={size.pages > PAGE_LIMIT ? 'field-error' : 'hint'} role="status">
            About {size.pages} pages ({size.words} words){size.pages > PAGE_LIMIT ? ': over 2 pages, so consider unticking a few bullets.' : ''}
          </p>
        </div>
        <div className="tailor-main">
          <div className="panel form-grid">
            <Field id="cv-headline" label="Headline" wide>
              <input id="cv-headline" type="text" value={choices.headline}
                onChange={(e) => setChoices({ ...choices, headline: e.target.value })} />
            </Field>
            <Field id="cv-variant" label="Summary variant">
              <select id="cv-variant" value={summaryId} onChange={(e) => {
                const picked = proposal.summaries.find((s) => s.id === e.target.value);
                if (picked) setChoices({ ...choices, summary: picked.text });
              }}>
                {summaryId === '' && <option value="">Edited</option>}
                {proposal.summaries.map((s) => (
                  <option key={s.id} value={s.id}>{s.id}{s.id === proposal.suggestedSummaryId ? ' (suggested)' : ''}</option>
                ))}
              </select>
            </Field>
            <Field id="cv-summary" label="Summary" wide>
              <textarea id="cv-summary" rows={4} value={choices.summary}
                onChange={(e) => setChoices({ ...choices, summary: e.target.value })} />
            </Field>
          </div>
          <section className="panel" aria-labelledby="h-cv-skills">
            <h3 id="h-cv-skills">Skills section</h3>
            <ul className="list">
              {proposal.skillGroups.map((g) => <li key={g.name}><strong>{g.name}:</strong> {g.items.map((i) => i.text).join(', ')}</li>)}
            </ul>
          </section>
          {proposal.sections.map((section) => (
            <SectionPicker key={section.id} section={section} choices={choices} wanted={wanted} highlighted={highlighted}
              onToggle={(bulletId) => setChoices(toggle(choices, bulletId))}
              onMove={(bulletId, delta) => setChoices(move(choices, section, bulletId, delta))} />
          ))}
          <div className="actions">
            <button type="button" onClick={() => setChoices(proposal.suggestion)}>Reset to suggestion</button>
            <button type="button" className="primary" disabled={generate.isPending || !choices.headline.trim()}
              onClick={() => generate.mutate(choices)}>Generate .docx</button>
          </div>
          {made && (
            <p role="status">
              Saved on this entry as <a href={tracker.documentUrl(made.documentId)}>{made.fileName}</a>. If you edit it in Word before
              sending, upload the final file on the entry page.
            </p>
          )}
          <SaveError error={generate.error} />
        </div>
      </div>
    </section>
  );
}
```

In `src/Dashboard/src/App.tsx`:
- Add `import { TailorPage } from './tracker/cv/TailorPage';`.
- Inside the tracker `<Routes>`, after the `/tracker/:id` route, add:

```tsx
            <Route path="/tracker/:id/cv" element={<TailorPage />} />
```

Append to `src/Dashboard/src/theme.css`:

```css
.tailor { display: grid; gap: 24px; grid-template-columns: minmax(0, 1fr) minmax(0, 2fr); align-items: start; }
@media (max-width: 900px) { .tailor { grid-template-columns: minmax(0, 1fr); } }
.tailor-side, .tailor-main { display: grid; gap: 16px; min-width: 0; }
.coverage-group { display: grid; gap: 4px; }
.coverage-group h4 { margin: 0; font-size: .95rem; }
.coverage-group .count { font-family: var(--mono); font-size: .78rem; color: var(--muted); margin-left: 4px; }
.skill-chips { display: flex; flex-wrap: wrap; gap: 4px 6px; list-style: none; margin: 0; padding: 0; }
.cover-bullet, .chip-match { background: var(--good-soft); color: var(--good); }
.cover-listed { background: var(--info-soft); color: var(--info); }
.cover-available { background: var(--warn-soft); color: var(--warn); border: 0; cursor: pointer; }
.cover-available[aria-pressed="true"] { outline: 2px solid var(--warn); }
.cover-missing, .chip-plain { background: var(--bar-track); color: var(--muted); }
.picker legend { padding: 0 0 4px; }
.bullets { list-style: none; margin: 0; padding: 0; display: grid; gap: 10px; }
.bullets li { display: grid; gap: 4px; padding: 6px 8px; border-left: 3px solid transparent; }
.bullets li.highlight { border-left-color: var(--warn); background: var(--warn-soft); }
.bullet-pick { display: flex; gap: 8px; align-items: baseline; }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd src/Dashboard && npx vitest run src/tracker/cv && npm run typecheck`
Expected: PASS, 8 page tests plus 5 logic tests, and no type errors.

- [ ] **Step 6: Commit**

```bash
git add src/Dashboard/src/tracker/cv src/Dashboard/src/App.tsx src/Dashboard/src/theme.css
git commit -m "Add the Tailor CV page"
```

---

### Task 11: Links from the entry and tracker pages

**Files:**
- Modify: `src/Dashboard/src/tracker/entry/Documents.tsx`, `src/Dashboard/src/tracker/TrackerPage.tsx`
- Test: `src/Dashboard/src/tracker/entry/EntryPage.test.tsx` and `src/Dashboard/src/tracker/TrackerPage.test.tsx` (additions)

**Interfaces:**
- Consumes: `DocumentView.generated`, `tracker.cvStatus` and `tracker.fullCvUrl` from Task 9, and the route `/tracker/:id/cv` from Task 10.

- [ ] **Step 1: Write the failing tests**

Add to the `describe` in `EntryPage.test.tsx`:

```tsx
  it('marks generated CVs and links to the Tailor page', async () => {
    const documents = [
      { id: 'd1', kind: 'Cv', label: 'Tailored CV', fileName: 'cv.docx', sizeBytes: 2048, sentOn: '2027-03-01', generated: true },
      { id: 'd2', kind: 'Cv', label: 'Final CV', fileName: 'cv.pdf', sizeBytes: 2048, sentOn: '2027-03-02', generated: false },
    ];
    mockApi({ '/api/tracker/applications/a1': { ...ENTRY, documents } });
    open();

    expect(await screen.findByRole('link', { name: 'Tailor CV' })).toHaveAttribute('href', '/tracker/a1/cv');
    expect(screen.getByText(/generated Mar 1/)).toBeInTheDocument();
    expect(screen.getByText(/sent Mar 2/)).toBeInTheDocument();
  });
```

Add to the `describe` in `TrackerPage.test.tsx`:

```tsx
  it('offers the full CV once the master CV is ready', async () => {
    mockApi(routes({ '/api/tracker/cv': { ready: true, problems: [] } }));
    renderRoute('/tracker', <TrackerPage />);

    expect(await screen.findByRole('link', { name: 'Download full CV' })).toHaveAttribute('href', '/api/tracker/cv/full');
  });

  it('points to the example file when there is no master CV yet', async () => {
    mockApi(routes({ '/api/tracker/cv': { ready: false, problems: ['config/cv.json is missing.'] } }));
    renderRoute('/tracker', <TrackerPage />);

    expect(await screen.findByText(/config\/cv.example.json/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Download full CV' })).not.toBeInTheDocument();
  });
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/Dashboard && npx vitest run src/tracker/entry/EntryPage.test.tsx src/tracker/TrackerPage.test.tsx`
Expected: FAIL. The new tests can't find the "Tailor CV" or "Download full CV" link. The existing tests still pass.

- [ ] **Step 3: Change the Documents panel**

In `Documents.tsx`:
- Add `import { Link } from 'react-router';`.
- Replace `<h3 id="h-documents">Documents sent</h3>` with:

```tsx
      <div className="tracker-head">
        <h3 id="h-documents">Documents</h3>
        <Link className="button" to={`/tracker/${entry.id}/cv`}>Tailor CV</Link>
      </div>
```

- Replace the line that shows kind, date and size (`{DOCUMENT_KINDS.find(...)?.label} · sent {dayText(d.sentOn)} · {sizeText(d.sizeBytes)}`) with:

```tsx
                {DOCUMENT_KINDS.find((k) => k.kind === d.kind)?.label} · {d.generated ? 'generated' : 'sent'} {dayText(d.sentOn)} · {sizeText(d.sizeBytes)}
```

- Directly after the `<a href={tracker.documentUrl(d.id)}>{d.label}</a>` line, add:

```tsx
              {d.generated && <span className="status">generated</span>}
```

- [ ] **Step 4: Change the Tracker page header**

In `TrackerPage.tsx`, add a query after the `summary` query:

```tsx
  const cv = useQuery({ queryKey: ['tracker', 'cv', 'status'], queryFn: tracker.cvStatus });
```

and replace `<Link className="button" to="/tracker/new">Paste a job</Link>` with:

```tsx
        <div className="actions">
          {cv.data?.ready && <a className="button" href={tracker.fullCvUrl}>Download full CV</a>}
          {cv.data && !cv.data.ready && (
            <span className="hint">To tailor CVs, copy config/cv.example.json to config/cv.json and fill it in.</span>
          )}
          <Link className="button" to="/tracker/new">Paste a job</Link>
        </div>
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd src/Dashboard && npm test && npm run build`
Expected: PASS. All Vitest files pass (72 before this phase, plus the new ones), and the type check and Vite build succeed.

- [ ] **Step 6: Commit**

```bash
git add src/Dashboard/src/tracker/entry/Documents.tsx src/Dashboard/src/tracker/TrackerPage.tsx src/Dashboard/src/tracker/entry/EntryPage.test.tsx src/Dashboard/src/tracker/TrackerPage.test.tsx
git commit -m "Link the Tailor CV page and the full CV from the tracker"
```

---

### Task 12: Decision notes, README and full verification

**Files:**
- Create: `docs/decisions/0012-tailor-by-selection.md`, `docs/decisions/0013-word-via-open-xml-sdk.md`
- Modify: `README.md` (new section before `## Roadmap`, and the roadmap line)

- [ ] **Step 1: Write ADR 0012**

`docs/decisions/0012-tailor-by-selection.md`:

```markdown
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
```

- [ ] **Step 2: Write ADR 0013**

`docs/decisions/0013-word-via-open-xml-sdk.md`:

```markdown
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
```

- [ ] **Step 3: Update the README**

In `README.md`, insert before `## Roadmap`:

```markdown
## Phase 2c: CV tailoring

**Set up once:** copy `config/cv.example.json` to `config/cv.json` (gitignored) and replace everything with your own, true details. The comments at the top explain the format. The dashboard reads it on every request, so edits apply without a restart, and any problems (a duplicate id, a misspelt skill) are listed on the Tailor page.

**Per application:**
1. Open the entry and choose **Tailor CV**.
2. If the job text is only a snippet, paste the full posting.
3. Tick, untick and reorder bullets, and edit the headline and summary. The left panel shows where the CV shows each skill the posting asks for.
4. **Generate .docx** saves the file on the entry, marked "generated", and downloads it.
5. If you edit it in Word before sending, upload the final file on the entry page. That one is marked "sent".

**Download full CV** on the Tracker page gives the untailored CV with every bullet.

The tool never writes CV text. It only picks and orders what `cv.json` holds ([ADR 0012](docs/decisions/0012-tailor-by-selection.md)).
```

and replace the roadmap line starting `2. ~~API + dashboard~~` with:

```markdown
2. ~~API + dashboard~~ Done in phase 2a (API + React dashboard, .NET Aspire, OpenTelemetry), 2b (shortlist and application tracker) and 2c (CV tailoring per posting)
```

- [ ] **Step 4: Run the full verification**

Run: `dotnet build -warnaserror && dotnet test && cd src/Dashboard && npm test && npm run build`
Expected:
- 0 warnings.
- Archiver 101, Tracker.Core 242, Api at 128 plus the new Cv and Applications tests, all passing.
- Every Vitest test passes, and the build succeeds.

Record the exact counts in the ledger.

- [ ] **Step 5: Commit**

```bash
git add docs/decisions/0012-tailor-by-selection.md docs/decisions/0013-word-via-open-xml-sdk.md README.md
git commit -m "Document the CV tailoring decisions and usage"
```
