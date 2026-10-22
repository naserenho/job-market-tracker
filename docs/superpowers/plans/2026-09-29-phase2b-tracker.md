# Phase 2b Shortlist and Application Tracker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the owner's own records to the dashboard: postings they shortlist and applications they track. Each record carries its timeline, follow-ups, contacts, documents and company details. The records survive the twice-weekly market rebuild and are mirrored to JSON with automatic restore.

**Architecture:**
- **Storage:** a new EF Core context, `ApplicationsDb`, inside the API owns six tables in the same Postgres database. It has its own migrations history table, and its tables are never in `TrackerTables.All`, so the Processor's TRUNCATE-and-reload can't touch them.
- **API:** minimal-API endpoints under `/api/tracker`, behind an endpoint filter that runs the migrations once and refuses writes that lack the dashboard header.
- **Backup:** after each committed write the API rewrites `data/tracker/tracker.json`, and it restores from that file when it finds the tables empty.
- **Dashboard:** new React pages under `/tracker`, plus a Shortlist button on every market posting row.

**Tech Stack:**
- Back end: .NET 10, ASP.NET Core minimal APIs, EF Core 10 with Npgsql 10.0.3 (row-version `xmin`), `EFCore.NamingConventions`, xUnit, Testcontainers, WebApplicationFactory.
- Front end: React 19, TanStack Query 5, React Router 7, Vitest with Testing Library (the versions pinned in phase 2a).

**Spec:** `docs/superpowers/specs/2026-09-29-phase2b-tracker-design.md`

## Global Constraints

**Naming and storage**
- C# namespace `Api.Applications`; tests use `Api.Tests.Applications`. Dashboard code goes in `src/Dashboard/src/tracker/`.
- The six tables are `companies`, `applications`, `application_events`, `follow_ups`, `contacts` and `application_documents`, with migrations history table `__applications_migrations`. None of them may appear in `TrackerTables.All`.
- The API writes only its own tables and files under `<DataRoot>/tracker/` and `<DataRoot>/inbox/`. Market tables stay read-only.
- File locations:
  - mirror: `<DataRoot>/tracker/tracker.json`, written to a temp file in the same folder and then moved over the old one;
  - documents: `<DataRoot>/tracker/files/<applicationId:N>/<documentId:N>-<safe file name>`;
  - pasted jobs: `<DataRoot>/inbox/<yyyy-MM-dd>-<company-slug>-<title-slug>[-n].txt`, in the existing inbox format.

**Security**
- Every non-GET tracker request must carry the header `X-Tracker-Client` (any value). Otherwise the API answers 403 "Tracker changes must come from the dashboard."
- `AllowedHosts` for the API is `localhost;127.0.0.1;[::1]`.

**Input rules**
- Single-line text fields allow up to 200 characters, notes up to 20,000, and job text up to 100,000.
- Links must be absolute `http` or `https`.
- Uploads accept `.pdf`, `.docx`, `.doc`, `.txt` and `.md`, up to 10 MB.
- Dates are `DateOnly` (JSON `yyyy-MM-dd`). "Today" is the local date of the injected `TimeProvider`.
- Enums are stored as strings (their names) in the database, the mirror and JSON.

**Problem details and exact messages**
- 400 carries `field`. 404 for unknown ids. 409 carries `id` for a duplicate shortlist.
- A stale-version 409 says exactly "This entry changed in another tab. Reload to see the latest."
- A stopped database gives 503 with the phase 2a message: "The tracker database isn't running. Start Docker Desktop (and `docker compose up -d postgres`)."
- Statuses: `Shortlisted, Applied, Screening, Interviewing, Offer, Accepted, Rejected, Withdrawn`. Every status change adds an `application_events` row, and new entries start with one.

**Telemetry**
- ActivitySource and Meter are both named `Tracker.Applications`.
- The counter `tracker.applications.changes` is tagged with `operation`.

**Dashboard**
- Every tracker write goes through `send()`, which adds `X-Tracker-Client: dashboard`, and invalidates query key `['tracker']` on success.
- Edits use explicit **Save** buttons, and a 409 shows its message with a **Reload** button.

**Code style:** code containing backslashes is written with the Write or Edit tools, never shell heredocs.

## Review Focus

1. **A malicious website or DNS-rebinding page tries to change the tracker through the owner's browser.** The request is refused: 403 without the header, 400 for a foreign Host. Owner: Task 2 (host) and Task 3 (header).
2. **The Docker volume is lost and `tracker.json` is corrupt or unreadable.** The tracker must refuse to start and must never overwrite the file; the owner gets a message naming the file. Owner: Task 2.
3. **Uploaded file names with paths, odd characters, Windows backslashes, huge sizes or wrong types.** Nothing escapes `data/tracker/files/<id>/`, and a clear 400 comes back. Owner: Task 1 (names) and Task 5 (endpoint).
4. **Two tabs edit the same entry or company.** The second save gets the 409 message plus **Reload**, and nothing is silently lost. Owner: Task 4 (API) and Task 9 (UI).
5. **Pasted text with line breaks in the title or company, or a job text that looks like headers.** The inbox file still parses, and the entry keeps a single-line title. Owner: Task 1 and Task 3.

---

## File Structure

```
src/Api/Applications/
  Entities.cs                 Task 1  (Company, JobApplication, ApplicationEvent, FollowUp, Contact, ApplicationDocument, enums)
  Rules.cs                    Task 1  (input rules + TrackerInputException / NotFound / Conflict / Backup exceptions)
  InboxWriter.cs              Task 1  (pasted job → inbox file the rebuild reads)
  MirrorStore.cs              Task 1  (MirrorFile + atomic JSON read/write)
  ApplicationsDb.cs           Task 2  (context + design-time factory)
  Migrations/*                Task 2  (dotnet ef migrations add InitialApplications)
  TrackerMirror.cs            Task 2  (DB ⇄ tracker.json, backup status, file paths)
  TrackerSchema.cs            Task 2  (migrate once + restore)
  TrackerGate.cs              Task 2  (endpoint filter: header check + schema)
  TrackerTelemetry.cs         Task 2
  Tracking.cs                 Task 2  (Today, CommitAsync)
  TrackerEndpoints.cs         Task 2  (group + /status) → Tasks 3–5 add one line each
  TrackerViews.cs             Task 3  (response + create request records)
  TrackerListEndpoints.cs     Task 3  (lists, detail, shortlist, paste)
  TrackerEditEndpoints.cs     Task 4  (edits, status, follow-ups, contacts, companies)
  TrackerDocumentEndpoints.cs Task 5  (documents, delete entry)
src/Api/DatabaseErrors.cs     Task 2  (moved from DbMarketSource)
src/Api/Market/IMarketSource.cs  Task 2 (DatabaseUnavailableException.Text)
src/Api/Market/DbMarketSource.cs Task 2 (uses DatabaseErrors)
src/Api/Endpoints/ApiExceptionHandler.cs Task 2
src/Api/Program.cs            Task 2
src/Api/appsettings.json      Task 2  (AllowedHosts)
src/Api/Api.csproj            Task 2  (EF Design package)
src/Tracker.Core/Analysis/{ReportModel,Analyzer}.cs  Task 10 (PostingMatch.PostingId)
tests/Api.Tests/TestSupport/TrackerFixture.cs        Task 2 (+ helpers used by Tasks 3–5)
tests/Api.Tests/Applications/*Tests.cs               Tasks 1–5
src/Dashboard/src/api.ts                  Task 6 (request<T>, ApiError.details)
src/Dashboard/src/test/render.tsx         Task 6 (method-aware mockApi, reply())
src/Dashboard/src/tracker/api.ts          Task 6
src/Dashboard/src/tracker/{dates.ts,StatusChip.tsx,Field.tsx,SaveError.tsx,useTrackerMutation.ts}  Tasks 6–7
src/Dashboard/src/tracker/{TrackerPage,UpNext}.tsx   Task 7
src/Dashboard/src/tracker/PasteJob.tsx               Task 8
src/Dashboard/src/tracker/entry/*.tsx                Task 9
src/Dashboard/src/tracker/ShortlistButton.tsx        Task 10
src/Dashboard/src/{App.tsx,theme.css,components/PostingList.tsx,pages/Overview.tsx}  Tasks 7, 8, 9, 10
docs/decisions/0010-*.md, 0011-*.md, README.md       Task 11
```

All commands run from the repo root unless a step says `cd src/Dashboard`.

---

### Task 1: Tracker entities, input rules, inbox writer and mirror file

**Files:**
- Create: `src/Api/Applications/Entities.cs`, `src/Api/Applications/Rules.cs`, `src/Api/Applications/InboxWriter.cs`, `src/Api/Applications/MirrorStore.cs`
- Test: `tests/Api.Tests/Applications/RulesTests.cs`, `tests/Api.Tests/Applications/InboxWriterTests.cs`, `tests/Api.Tests/Applications/MirrorStoreTests.cs`

**Interfaces:**
- Consumes: `Tracker.Reading.InboxReader.Parse(fileName, content, runId)` (phase 1)
- Produces:
  - **Enums:** `ApplicationStatus`, `WorkModel` (`Unknown, Remote, Hybrid, Onsite`), `DocumentKind` (`Cv, CoverLetter, Other`), `EntryOrigin` (`Market, Pasted`)
  - **Entity classes** with settable properties:
    - `Company`: `Id, Name, NameKey, Website, OfficeLocation, CompanySize, AnnualLeave, Benefits, Notes, CreatedAt, UpdatedAt, Version`.
    - `JobApplication`: `Id, CompanyId, Company, PostingId, Title, Url, Location, PostingText, Status, Origin, FoundVia, WorkModel, OfficeDays, TeamSize, WorkingHours, SalaryPosted, SalaryAsked, SalaryOffered, Notes, CreatedAt, UpdatedAt, Version, Events, FollowUps, Contacts, Documents`.
    - `ApplicationEvent`: `Id, ApplicationId, OnDate, FromStatus, ToStatus, Note, CreatedAt`.
    - `FollowUp`: `Id, ApplicationId, Action, DueOn, DoneOn, CreatedAt`.
    - `Contact`: `Id, ApplicationId, Name, Role, Email, Phone, LinkedIn, Notes, CreatedAt`.
    - `ApplicationDocument`: `Id, ApplicationId, Kind, Label, FileName, ContentType, SizeBytes, SentOn, CreatedAt`.
    - `Version` is `uint`; navigations and `Version` are `[JsonIgnore]`.
  - **Exceptions:**
    - `TrackerInputException(string field, string message)` with `Field`;
    - `TrackerNotFoundException(string message)`;
    - `TrackerConflictException(string message, Guid? existingId = null)` with `ExistingId`;
    - `TrackerBackupException(string message, Exception inner)`.
  - **`static class Rules`:**
    - constants `MaxText = 200`, `MaxLongText = 20_000`, `MaxPostingText = 100_000`, `MaxFileBytes = 10 MiB`;
    - text: `Required(value, field, max = MaxText)`, `Optional(value, field, max = MaxText)`, `SingleLine(value)`, `CompanyKey(name)`;
    - format checks: `OptionalUrl(value, field)`, `OptionalEmail(value, field)`, `ParseEnum<T>(value, field)`;
    - files: `SafeFileName(name)`, `ContentType(fileName)`, `CheckUpload(fileName, length)`;
    - `Label(field)`.
  - **`static class InboxWriter`:**
    - `Content(title, company, url, location, text, DateOnly saved) : string`;
    - `FileName(company, title, DateOnly saved, Func<string, bool> exists) : string`;
    - `Slug(string) : string`;
    - `Write(inboxFolder, title, company, url, location, text, DateOnly saved) : (string PostingId, string Path)`.
  - **Mirror file:**
    - `record MirrorFile(int Format, DateTimeOffset SavedAt, List<Company> Companies, List<JobApplication> Applications, List<ApplicationEvent> Events, List<FollowUp> FollowUps, List<Contact> Contacts, List<ApplicationDocument> Documents)` with `const int CurrentFormat = 1`;
    - `static class MirrorStore` with `Json` options, `Write(path, MirrorFile)` and `Read(path) : MirrorFile?`. `Read` throws `TrackerBackupException` when the file can't be read.

- [ ] **Step 1: Write the failing tests**

`tests/Api.Tests/Applications/RulesTests.cs`:
```csharp
using Api.Applications;

namespace Api.Tests.Applications;

public class RulesTests
{
    [Fact]
    public void Required_text_is_trimmed_and_names_the_field_when_missing()
    {
        Assert.Equal("Senior Dev", Rules.Required("  Senior Dev ", "title"));
        var error = Assert.Throws<TrackerInputException>(() => Rules.Required("   ", "postingText"));
        Assert.Equal(("postingText", "Job text is required."), (error.Field, error.Message));
    }

    [Fact]
    public void Text_over_the_limit_is_refused()
    {
        var error = Assert.Throws<TrackerInputException>(() => Rules.Optional(new string('x', 201), "teamSize"));
        Assert.Equal("teamSize", error.Field);
        Assert.Contains("Team size is longer than 200", error.Message);
    }

    [Fact]
    public void Single_line_collapses_breaks_and_company_keys_ignore_case_and_spacing()
    {
        Assert.Equal("Senior .NET Developer (Payments)", Rules.SingleLine("Senior .NET Developer\r\n (Payments) "));
        Assert.Equal(Rules.CompanyKey("RBC"), Rules.CompanyKey("  rbc "));
        Assert.Equal("rbc capital markets", Rules.CompanyKey("RBC \n Capital   Markets"));
    }

    [Theory]
    [InlineData("https://careers.example.com/1", true)]
    [InlineData("http://example.com", true)]
    [InlineData("", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("www.example.com/job", false)]
    [InlineData("file:///C:/secret.txt", false)]
    public void Links_must_be_web_addresses(string value, bool ok)
    {
        if (ok) Assert.Equal(value, Rules.OptionalUrl(value, "url"));
        else Assert.Equal("url", Assert.Throws<TrackerInputException>(() => Rules.OptionalUrl(value, "url")).Field);
    }

    [Fact]
    public void Emails_need_a_plausible_shape()
    {
        Assert.Equal("", Rules.OptionalEmail(" ", "email"));
        Assert.Equal("sam@rbc.com", Rules.OptionalEmail("sam@rbc.com", "email"));
        Assert.Equal("email", Assert.Throws<TrackerInputException>(() => Rules.OptionalEmail("sam at rbc", "email")).Field);
    }

    [Fact]
    public void Enums_parse_by_name_ignoring_case_and_refuse_numbers()
    {
        Assert.Equal(ApplicationStatus.Interviewing, Rules.ParseEnum<ApplicationStatus>("interviewing", "status"));
        Assert.Equal("status", Assert.Throws<TrackerInputException>(() => Rules.ParseEnum<ApplicationStatus>("3", "status")).Field);
        var error = Assert.Throws<TrackerInputException>(() => Rules.ParseEnum<ApplicationStatus>("wizard", "status"));
        Assert.Contains("Shortlisted", error.Message);
    }

    [Theory]
    [InlineData("CV.pdf", "CV.pdf")]
    [InlineData("../../evil name?.pdf", "evil-name-.pdf")]
    [InlineData("..\\CV (RBC).docx", "CV-RBC-.docx")]
    [InlineData("C:\\Users\\me\\Cover letter.txt", "Cover-letter.txt")]
    [InlineData("???", "file")]
    public void File_names_lose_folders_and_odd_characters(string name, string expected) =>
        Assert.Equal(expected, Rules.SafeFileName(name));

    [Fact]
    public void Uploads_are_limited_by_type_and_size()
    {
        Rules.CheckUpload("CV.PDF", 1000);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", Rules.ContentType("x.DOCX"));
        Assert.Contains("PDF, DOCX", Assert.Throws<TrackerInputException>(() => Rules.CheckUpload("tool.exe", 10)).Message);
        Assert.Contains("empty", Assert.Throws<TrackerInputException>(() => Rules.CheckUpload("CV.pdf", 0)).Message);
        Assert.Contains("10 MB", Assert.Throws<TrackerInputException>(() => Rules.CheckUpload("CV.pdf", Rules.MaxFileBytes + 1)).Message);
    }

    [Theory]
    [InlineData("postingText", "Job text")]
    [InlineData("url", "Link")]
    [InlineData("linkedIn", "LinkedIn")]
    [InlineData("salaryAsked", "Salary asked")]
    public void Labels_read_like_the_form(string field, string label) => Assert.Equal(label, Rules.Label(field));
}
```

`tests/Api.Tests/Applications/InboxWriterTests.cs`:
```csharp
using Api.Applications;
using Tracker.Reading;
using Tracker.TestSupport;

namespace Api.Tests.Applications;

public class InboxWriterTests
{
    private static readonly DateOnly Saved = new(2027, 3, 1);

    [Fact]
    public void Content_is_a_valid_inbox_file()
    {
        var content = InboxWriter.Content("Senior .NET Developer", "Bank of Testing", "https://x.test/1", "Toronto, ON",
            "Title: not a header here\n\nWe need C# and Azure.", Saved);

        var (posting, warning) = InboxReader.Parse("2027-03-01-bank-of-testing-senior-net-developer.txt", content, "run");

        Assert.Null(warning);
        Assert.Equal(("Senior .NET Developer", "Bank of Testing", "Toronto, ON", "https://x.test/1"),
            (posting!.Title, posting.Company, posting.Location, posting.Url));
        Assert.Contains("We need C# and Azure.", posting.Description);
        Assert.True(posting.IsFullText);
    }

    [Fact]
    public void File_names_are_dated_slugs_with_a_suffix_when_taken()
    {
        var taken = new HashSet<string> { "2027-03-01-rbc-senior-net-developer.txt", "2027-03-01-rbc-senior-net-developer-2.txt" };
        Assert.Equal("2027-03-01-rbc-senior-net-developer-3.txt", InboxWriter.FileName("RBC", "Senior .NET Developer", Saved, taken.Contains));
        Assert.Equal("2027-03-01-x-x.txt", InboxWriter.FileName("???", "!!!", Saved, _ => false));
    }

    [Fact]
    public void Write_saves_the_file_and_returns_its_future_posting_id()
    {
        using var tmp = new TempDir();
        var inbox = Path.Combine(tmp.Path, "inbox");

        var (postingId, path) = InboxWriter.Write(inbox, "Tech Lead", "Shopify", "", "Remote (Canada)", "Lead a team of five.", Saved);

        Assert.Equal("inbox:2027-03-01-shopify-tech-lead.txt", postingId);
        Assert.Equal(Path.Combine(inbox, "2027-03-01-shopify-tech-lead.txt"), path);
        Assert.StartsWith("URL: \nCompany: Shopify\nTitle: Tech Lead\n", File.ReadAllText(path));
    }

    [Fact]
    public void Write_refuses_text_the_rebuild_could_not_read()
    {
        using var tmp = new TempDir();
        var error = Assert.Throws<TrackerInputException>(() => InboxWriter.Write(Path.Combine(tmp.Path, "inbox"), "Dev", "Acme", "", "", "   ", Saved));
        Assert.Equal("postingText", error.Field);
        Assert.False(Directory.Exists(Path.Combine(tmp.Path, "inbox")) && Directory.EnumerateFiles(Path.Combine(tmp.Path, "inbox")).Any());
    }
}
```

`tests/Api.Tests/Applications/MirrorStoreTests.cs`:
```csharp
using System.Text.Json;
using Api.Applications;
using Tracker.TestSupport;

namespace Api.Tests.Applications;

public class MirrorStoreTests
{
    private static readonly DateTimeOffset At = new(2027, 3, 1, 14, 0, 0, TimeSpan.Zero);

    internal static MirrorFile Sample()
    {
        var company = new Company { Id = Guid.NewGuid(), Name = "RBC", NameKey = "rbc", AnnualLeave = "20 days", CreatedAt = At, UpdatedAt = At };
        var entry = new JobApplication
        {
            Id = Guid.NewGuid(), CompanyId = company.Id, PostingId = "greenhouse:101", Title = "Senior .NET Developer",
            Status = ApplicationStatus.Interviewing, Origin = EntryOrigin.Market, WorkModel = WorkModel.Hybrid, OfficeDays = "3 days",
            CreatedAt = At, UpdatedAt = At,
        };
        return new MirrorFile(MirrorFile.CurrentFormat, At, [company], [entry],
            [new ApplicationEvent { Id = Guid.NewGuid(), ApplicationId = entry.Id, OnDate = new DateOnly(2027, 3, 1), ToStatus = ApplicationStatus.Applied, CreatedAt = At }],
            [new FollowUp { Id = Guid.NewGuid(), ApplicationId = entry.Id, Action = "Email Sam", DueOn = new DateOnly(2027, 3, 8), CreatedAt = At }],
            [new Contact { Id = Guid.NewGuid(), ApplicationId = entry.Id, Name = "Sam", Email = "sam@rbc.com", CreatedAt = At }],
            [new ApplicationDocument { Id = Guid.NewGuid(), ApplicationId = entry.Id, Kind = DocumentKind.Cv, Label = "CV v3", FileName = "CV.pdf",
                ContentType = "application/pdf", SizeBytes = 1234, SentOn = new DateOnly(2027, 3, 1), CreatedAt = At }]);
    }

    [Fact]
    public void Written_files_read_back_identically()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "tracker", "tracker.json");
        var file = Sample();

        MirrorStore.Write(path, file);
        var back = MirrorStore.Read(path)!;

        Assert.Equal(JsonSerializer.Serialize(file, MirrorStore.Json), JsonSerializer.Serialize(back, MirrorStore.Json));
        Assert.Contains("\"status\": \"Interviewing\"", File.ReadAllText(path));
        Assert.DoesNotContain("version", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_file_reads_as_nothing() => Assert.Null(MirrorStore.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "tracker.json")));

    [Fact]
    public void A_failed_write_leaves_the_previous_copy_intact()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "tracker.json");
        MirrorStore.Write(path, Sample());
        var before = File.ReadAllText(path);
        Directory.CreateDirectory(path + ".tmp"); // the temp file can't be created

        Assert.ThrowsAny<Exception>(() => MirrorStore.Write(path, Sample() with { SavedAt = At.AddDays(1) })); // IOException or, on Windows, access denied
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"format\": 99, \"savedAt\": \"2027-03-01T00:00:00Z\", \"companies\": [], \"applications\": [], \"events\": [], \"followUps\": [], \"contacts\": [], \"documents\": []}")]
    public void Unreadable_or_newer_files_are_refused_with_the_file_named(string content)
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "tracker.json");
        File.WriteAllText(path, content);

        var error = Assert.Throws<TrackerBackupException>(() => MirrorStore.Read(path));

        Assert.Contains("tracker.json", error.Message);
    }
}
```
- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~Api.Tests.Applications"`
Expected: build FAILS: `Api.Applications` types don't exist.

- [ ] **Step 3: Implement**

`src/Api/Applications/Entities.cs`:
```csharp
using System.Text.Json.Serialization;

namespace Api.Applications;

public enum ApplicationStatus { Shortlisted, Applied, Screening, Interviewing, Offer, Accepted, Rejected, Withdrawn }

public enum WorkModel { Unknown, Remote, Hybrid, Onsite }

public enum DocumentKind { Cv, CoverLetter, Other }

public enum EntryOrigin { Market, Pasted }

// Shared by every application to the same company; NameKey ("rbc") makes "RBC" and "rbc " one company.
public sealed class Company
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public string Website { get; set; } = "";
    public string OfficeLocation { get; set; } = "";
    public string CompanySize { get; set; } = "";
    public string AnnualLeave { get; set; } = "";
    public string Benefits { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Postgres xmin: changes on every update, so a save that carries an older value is refused (see ADR 0011).
    [JsonIgnore] public uint Version { get; set; }
}

// One job the owner shortlisted or applied to. It keeps its own copy of the posting's title, link and location, so it
// outlives the posting in the market data.
public sealed class JobApplication
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string? PostingId { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Location { get; set; } = "";
    public string PostingText { get; set; } = "";
    public ApplicationStatus Status { get; set; }
    public EntryOrigin Origin { get; set; }
    public string FoundVia { get; set; } = "";
    public WorkModel WorkModel { get; set; }
    public string OfficeDays { get; set; } = "";
    public string TeamSize { get; set; } = "";
    public string WorkingHours { get; set; } = "";
    public string SalaryPosted { get; set; } = "";
    public string SalaryAsked { get; set; } = "";
    public string SalaryOffered { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore] public uint Version { get; set; }
    [JsonIgnore] public Company? Company { get; set; }
    [JsonIgnore] public List<ApplicationEvent> Events { get; set; } = [];
    [JsonIgnore] public List<FollowUp> FollowUps { get; set; } = [];
    [JsonIgnore] public List<Contact> Contacts { get; set; } = [];
    [JsonIgnore] public List<ApplicationDocument> Documents { get; set; } = [];
}

// One line of an application's timeline: a status change (or the entry's creation, with FromStatus null).
public sealed class ApplicationEvent
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DateOnly OnDate { get; set; }
    public ApplicationStatus? FromStatus { get; set; }
    public ApplicationStatus ToStatus { get; set; }
    public string Note { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FollowUp
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string Action { get; set; } = "";
    public DateOnly DueOn { get; set; }
    public DateOnly? DoneOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Contact
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string LinkedIn { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

// A file sent with an application; the bytes live in data/tracker/files, not in the database.
public sealed class ApplicationDocument
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public DocumentKind Kind { get; set; }
    public string Label { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateOnly SentOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

`src/Api/Applications/Rules.cs`:
```csharp
using System.Text.RegularExpressions;

namespace Api.Applications;

public sealed class TrackerInputException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public sealed class TrackerNotFoundException(string message) : Exception(message);

public sealed class TrackerConflictException(string message, Guid? existingId = null) : Exception(message)
{
    public Guid? ExistingId { get; } = existingId;
}

// tracker.json exists but can't be used: the tracker stays closed so the copy is never overwritten.
public sealed class TrackerBackupException(string message, Exception inner) : Exception(message, inner);

// Input rules shared by every tracker endpoint: trimmed text with length limits, one-line names, web links, safe files.
public static partial class Rules
{
    public const int MaxText = 200, MaxLongText = 20_000, MaxPostingText = 100_000;
    public const long MaxFileBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".doc"] = "application/msword",
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
    };

    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["postingText"] = "Job text",
        ["postingId"] = "Posting",
        ["url"] = "Link",
        ["linkedIn"] = "LinkedIn",
    };

    public static string Required(string? value, string field, int max = MaxText)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) throw new TrackerInputException(field, $"{Label(field)} is required.");
        return Limit(text, field, max);
    }

    public static string Optional(string? value, string field, int max = MaxText) => Limit((value ?? "").Trim(), field, max);

    // Titles and names go into one-line places (inbox headers, table cells): collapse line breaks and runs of spaces.
    public static string SingleLine(string value) => Spaces().Replace(value, " ").Trim();

    public static string CompanyKey(string name) => SingleLine(name).ToLowerInvariant();

    public static string OptionalUrl(string? value, string field)
    {
        var text = Optional(value, field, 2000);
        if (text.Length == 0) return text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? text
            : throw new TrackerInputException(field, $"{Label(field)} must be a full web address starting with https://.");
    }

    public static string OptionalEmail(string? value, string field)
    {
        var text = Optional(value, field);
        return text.Length == 0 || EmailShape().IsMatch(text)
            ? text
            : throw new TrackerInputException(field, "Enter an email address like name@company.com.");
    }

    public static T ParseEnum<T>(string? value, string field) where T : struct, Enum
    {
        var text = (value ?? "").Trim();
        return Enum.TryParse<T>(text, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(text, out _)
            ? parsed
            : throw new TrackerInputException(field, $"Unknown {Label(field).ToLowerInvariant()} '{text}'. Use {string.Join(", ", Enum.GetNames<T>())}.");
    }

    // Keeps only the last path segment (Windows or Unix separators) and letters, digits, '-', '_' and '.'.
    public static string SafeFileName(string name)
    {
        var cleaned = Unsafe().Replace(Path.GetFileName(name.Replace('\\', '/')), "-").Trim('-', '.');
        if (cleaned.Length == 0) return "file";
        return cleaned.Length <= 100 ? cleaned : cleaned[^100..].TrimStart('-', '.');
    }

    public static string ContentType(string fileName) =>
        ContentTypes.TryGetValue(Path.GetExtension(fileName), out var type) ? type : "application/octet-stream";

    public static void CheckUpload(string fileName, long length)
    {
        if (!ContentTypes.ContainsKey(Path.GetExtension(fileName)))
            throw new TrackerInputException("file", "Upload a PDF, DOCX, DOC, TXT or MD file.");
        if (length == 0) throw new TrackerInputException("file", "The file is empty.");
        if (length > MaxFileBytes) throw new TrackerInputException("file", "Files can be up to 10 MB.");
    }

    // "salaryAsked" → "Salary asked", with a few names the form spells differently.
    public static string Label(string field) =>
        Labels.TryGetValue(field, out var label)
            ? label
            : char.ToUpperInvariant(field[0]) + Capitals().Replace(field[1..], m => " " + m.Value.ToLowerInvariant());

    private static string Limit(string text, string field, int max) =>
        text.Length <= max ? text : throw new TrackerInputException(field, $"{Label(field)} is longer than {max} characters.");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex("[A-Z]")]
    private static partial Regex Capitals();

    [GeneratedRegex(@"[^A-Za-z0-9._-]+")]
    private static partial Regex Unsafe();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailShape();
}
```

`src/Api/Applications/InboxWriter.cs`:
```csharp
using System.Text.RegularExpressions;
using Tracker.Reading;

namespace Api.Applications;

// A pasted job becomes an inbox file in the same format as a saved LinkedIn post, so the next rebuild adds it to the
// market data as posting "inbox:<file name>".
public static partial class InboxWriter
{
    public static string Content(string title, string company, string url, string location, string text, DateOnly saved) =>
        $"URL: {Rules.SingleLine(url)}\nCompany: {Rules.SingleLine(company)}\nTitle: {Rules.SingleLine(title)}\n" +
        $"Location: {Rules.SingleLine(location)}\nSaved: {saved:yyyy-MM-dd}\n\n{text.Trim()}\n";

    public static string FileName(string company, string title, DateOnly saved, Func<string, bool> exists)
    {
        var stem = $"{saved:yyyy-MM-dd}-{Slug(company)}-{Slug(title)}";
        var name = stem + ".txt";
        for (var n = 2; exists(name); n++) name = $"{stem}-{n}.txt";
        return name;
    }

    public static string Slug(string value)
    {
        var slug = NonAlphanumeric().Replace(value.ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        return slug.Length == 0 ? "x" : slug;
    }

    // Writes the file only when the rebuild's own reader accepts it, and returns the posting id it will get.
    public static (string PostingId, string Path) Write(string inboxFolder, string title, string company, string url,
        string location, string text, DateOnly saved)
    {
        var name = FileName(company, title, saved, candidate => File.Exists(Path.Combine(inboxFolder, candidate)));
        var content = Content(title, company, url, location, text, saved);
        var (posting, warning) = InboxReader.Parse(name, content, "check");
        if (posting is null)
            throw new TrackerInputException("postingText", warning ?? "The pasted job can't be saved for the market data.");
        Directory.CreateDirectory(inboxFolder);
        var path = Path.Combine(inboxFolder, name);
        File.WriteAllText(path, content);
        return ("inbox:" + name, path);
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
```

`src/Api/Applications/MirrorStore.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Api.Applications;

// Everything in the tracker, as one JSON document: the copy that brings the tracker back if the database is lost.
public sealed record MirrorFile(int Format, DateTimeOffset SavedAt, List<Company> Companies, List<JobApplication> Applications,
    List<ApplicationEvent> Events, List<FollowUp> FollowUps, List<Contact> Contacts, List<ApplicationDocument> Documents)
{
    public const int CurrentFormat = 1;
}

public static class MirrorStore
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // Write a temp file beside the real one, then swap it in: a crash mid-write never leaves half a copy.
    public static void Write(string path, MirrorFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, Json));
        File.Move(temp, path, overwrite: true);
    }

    public static MirrorFile? Read(string path)
    {
        if (!File.Exists(path)) return null;
        MirrorFile? file;
        try
        {
            file = JsonSerializer.Deserialize<MirrorFile>(File.ReadAllText(path), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            throw new TrackerBackupException(
                $"{path} can't be read ({ex.Message}). Fix or move the file; the tracker stays closed until then so the copy isn't overwritten.", ex);
        }
        if (file is null || file.Format != MirrorFile.CurrentFormat)
            throw new TrackerBackupException(
                $"{path} has format {file?.Format.ToString() ?? "none"}; this version reads format {MirrorFile.CurrentFormat}.",
                new InvalidDataException("Unexpected mirror format."));
        return file;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~Api.Tests.Applications"`
Expected: all PASS. A failure means the code is wrong; fix the code, not the test.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add tracker entities, input rules, inbox writer and mirror file"
```

---

### Task 2: ApplicationsDb, schema, mirror, gate and error mapping

**Files:**
- Create: `src/Api/Applications/ApplicationsDb.cs`, `src/Api/Applications/TrackerMirror.cs`, `src/Api/Applications/TrackerSchema.cs`, `src/Api/Applications/TrackerGate.cs`, `src/Api/Applications/TrackerTelemetry.cs`, `src/Api/Applications/Tracking.cs`, `src/Api/Applications/TrackerEndpoints.cs`, `src/Api/DatabaseErrors.cs`
- Create (generated): `src/Api/Applications/Migrations/*`
- Modify: `src/Api/Api.csproj`, `src/Api/Program.cs`, `src/Api/appsettings.json`, `src/Api/Market/IMarketSource.cs`, `src/Api/Market/DbMarketSource.cs`, `src/Api/Endpoints/ApiExceptionHandler.cs`
- Test: `tests/Api.Tests/TestSupport/TrackerFixture.cs`, `tests/Api.Tests/Applications/TrackerSchemaTests.cs`, `tests/Api.Tests/Applications/TrackerRestoreTests.cs`, `tests/Api.Tests/Applications/TrackerMirrorTests.cs`

**Interfaces:**
- Consumes: Task 1 types; `MarketSettings`, `MarketData`, `DatabaseUnavailableException`, `ApiFactory`, `ManualClock`, `Data` (phase 2a); `TrackerTables.All`, `RebuildService` (Tracker.Core)
- Produces:
  - **Database context:** `ApplicationsDb` with `Companies, Applications, Events, FollowUps, Contacts, Documents`, `const string HistoryTable`, and `static Options(connectionString)`.
  - **Error helpers:** `static class DatabaseErrors { bool IsUnavailable(Exception) }` and `DatabaseUnavailableException.Text`.
  - **`TrackerMirror` (singleton):**
    - paths: `FilePath`, `FilesFolder`, `ApplicationFolder(Guid)`, `DocumentPath(ApplicationDocument)`;
    - backup state: `LastBackupAt`, `LastBackupError`;
    - operations: `WriteAsync(ApplicationsDb) : Task` (never throws) and `RestoreIfEmptyAsync(ApplicationsDb, ct) : Task<int>`.
  - **`TrackerSchema` (singleton):** `Ready` and `EnsureReadyAsync(ct)`.
  - **`TrackerGate`:** an endpoint filter with `const string ClientHeader = "X-Tracker-Client"`.
  - **`TrackerTelemetry`:** `Name`, `Source`, `Changes`.
  - **`internal static class Tracking`:** `MissingEntry`, `Today(TimeProvider) : DateOnly`, `FindEntryAsync(ApplicationsDb, Guid, CancellationToken) : Task<JobApplication>` (404 when missing) and `CommitAsync(ApplicationsDb, TrackerMirror, string operation, CancellationToken)`.
  - **Endpoints:** `record TrackerStatusView(bool Migrated, DateTimeOffset? LastBackupAt, string? LastBackupError, IReadOnlyDictionary<string,int> Counts, int Overdue)`; `static class TrackerEndpoints { MapTrackerEndpoints(this IEndpointRouteBuilder) }`, with `GET /api/tracker/status`.
  - **Test support:** `TrackerFixture` with:
    - `Client` (sends the header), `CreateClient(bool withHeader)`, `Clock`, `DataRoot`, `ConnectionString`;
    - `OpenDb()`, `RebuildAsync()`;
    - `GetAsync<T>(url)`, `PostAsync<T>(url, body)`, `PasteAsync(company, status = "Shortlisted") : Task<NewEntry>`, and `Json` options;
    - plus `[CollectionDefinition("tracker")]`.
    - `NewEntry` is created in Task 3; until then `PasteAsync` isn't used. The fixture's `PasteAsync` is added in Task 3 (see there).

- [ ] **Step 1: Packages, context and migration**

In `src/Api/Api.csproj`, add to the package `ItemGroup`. The design package lets `dotnet ef` build the migrations.
```xml
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
```

`src/Api/Applications/ApplicationsDb.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Applications;

// The owner's own records. Separate from the market tables (TrackerDb): its own migrations history table, and never
// in TrackerTables.All, so the Processor's TRUNCATE-and-reload can't touch it (ADR 0010).
public sealed class ApplicationsDb(DbContextOptions<ApplicationsDb> options) : DbContext(options)
{
    public const string HistoryTable = "__applications_migrations";

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<JobApplication> Applications => Set<JobApplication>();
    public DbSet<ApplicationEvent> Events => Set<ApplicationEvent>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ApplicationDocument> Documents => Set<ApplicationDocument>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Company>(e =>
        {
            e.ToTable("companies");
            e.HasIndex(c => c.NameKey).IsUnique();
            e.Property(c => c.Version).IsRowVersion().HasColumnName("xmin");
        });

        b.Entity<JobApplication>(e =>
        {
            e.ToTable("applications");
            e.HasOne(a => a.Company).WithMany().HasForeignKey(a => a.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => a.PostingId).IsUnique();
            e.Property(a => a.Status).HasConversion<string>();
            e.Property(a => a.Origin).HasConversion<string>();
            e.Property(a => a.WorkModel).HasConversion<string>();
            e.Property(a => a.Version).IsRowVersion().HasColumnName("xmin");
            e.HasMany(a => a.Events).WithOne().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(a => a.FollowUps).WithOne().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(a => a.Contacts).WithOne().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(a => a.Documents).WithOne().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ApplicationEvent>(e =>
        {
            e.ToTable("application_events");
            e.Property(x => x.FromStatus).HasConversion<string>();
            e.Property(x => x.ToStatus).HasConversion<string>();
        });
        b.Entity<FollowUp>(e => e.ToTable("follow_ups"));
        b.Entity<Contact>(e => e.ToTable("contacts"));
        b.Entity<ApplicationDocument>(e =>
        {
            e.ToTable("application_documents");
            e.Property(x => x.Kind).HasConversion<string>();
        });
    }

    public static DbContextOptions<ApplicationsDb> Options(string connectionString) =>
        new DbContextOptionsBuilder<ApplicationsDb>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(HistoryTable))
            .UseSnakeCaseNamingConvention()
            .Options;
}

// Used by `dotnet ef` only (no host start-up needed to build migrations).
public sealed class ApplicationsDbFactory : IDesignTimeDbContextFactory<ApplicationsDb>
{
    public ApplicationsDb CreateDbContext(string[] args) =>
        new(ApplicationsDb.Options("Host=localhost;Port=5434;Database=tracker;Username=tracker"));
}
```

Run: `dotnet ef migrations add InitialApplications --project src/Api --startup-project src/Api --context ApplicationsDb --output-dir Applications/Migrations`
Expected:
- `src/Api/Applications/Migrations/*_InitialApplications.cs` creates the six tables, the unique indexes on `name_key` and `posting_id`, and the cascade foreign keys.
- `xmin` may appear as a `xid` column in the C# code; Npgsql skips system columns when it generates SQL.
- The Task 2 integration tests prove the migration applies.

- [ ] **Step 2: Write the failing tests**

`tests/Api.Tests/TestSupport/TrackerFixture.cs`:
```csharp
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Applications;
using Api.Market;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tracker.Rebuild;
using Tracker.TestSupport;

namespace Api.Tests.TestSupport;

// One Postgres container and archive folder for the tracker tests, rebuilt from the fixture archive, with the API in
// front on a manual clock (today = 2 October 2026).
public sealed class TrackerFixture : IAsyncLifetime
{
    private readonly PostgresFixture _pg = new();
    private readonly TempDir _folder = new();
    private WebApplicationFactory<Program>? _factory;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public ManualClock Clock { get; } = new() { Now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero) };
    public HttpClient Client { get; private set; } = null!;
    public string ConnectionString => _pg.ConnectionString;
    public string DataRoot => _folder.Path;

    public async Task InitializeAsync()
    {
        await _pg.InitializeAsync();
        FixtureArchive.Write(DataRoot);
        await RebuildAsync();
        _factory = new ApiFactory(_pg.ConnectionString, DataRoot)
            .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(Clock)));
        Client = CreateClient(withHeader: true);
    }

    public HttpClient CreateClient(bool withHeader)
    {
        var client = _factory!.CreateClient();
        if (withHeader) client.DefaultRequestHeaders.Add(TrackerGate.ClientHeader, "tests");
        return client;
    }

    public ApplicationsDb OpenDb() => new(ApplicationsDb.Options(ConnectionString));

    // The scheduled Processor's destructive rebuild (TRUNCATE + COPY of the market tables), then one market check
    // interval so the API notices it.
    public async Task RebuildAsync()
    {
        await using (var db = await _pg.OpenAsync())
            await new RebuildService(db, RepoConfig.Options(), Data.Settings.Catalog, NullLogger<RebuildService>.Instance)
                .RunAsync(DataRoot, CancellationToken.None);
        Clock.Now += MarketData.CheckInterval;
    }

    public async Task<T> GetAsync<T>(string url)
    {
        var response = await Client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, $"GET {url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await Client.PostAsJsonAsync(url, body, Json);
        Assert.True(response.IsSuccessStatusCode, $"POST {url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        await _pg.DisposeAsync();
        _folder.Dispose();
    }
}

[CollectionDefinition("tracker")]
public sealed class TrackerCollection : ICollectionFixture<TrackerFixture>;
```

`tests/Api.Tests/Applications/TrackerSchemaTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Applications;
using Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Tracker.Data;
using Tracker.TestSupport;

namespace Api.Tests.Applications;

[Collection("tracker")]
[Trait("Category", "Integration")]
public sealed class TrackerSchemaTests(TrackerFixture tracker)
{
    [Fact]
    public async Task Status_answers_once_the_tracker_tables_exist()
    {
        var status = await tracker.GetAsync<JsonElement>("/api/tracker/status");

        Assert.True(status.GetProperty("migrated").GetBoolean());
        await using var db = tracker.OpenDb();
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_InitialApplications"));
    }

    [Fact]
    public async Task Tracker_rows_survive_a_market_rebuild()
    {
        await tracker.GetAsync<JsonElement>("/api/tracker/status");
        var id = Guid.NewGuid();
        await using (var db = tracker.OpenDb())
        {
            var now = DateTimeOffset.UtcNow;
            var company = new Company { Id = Guid.NewGuid(), Name = "Survivor Inc", NameKey = "survivor inc", CreatedAt = now, UpdatedAt = now };
            db.Companies.Add(company);
            db.Applications.Add(new JobApplication { Id = id, CompanyId = company.Id, Title = "Survivor role", Status = ApplicationStatus.Applied, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }

        await tracker.RebuildAsync();

        await using var after = tracker.OpenDb();
        Assert.Equal("Survivor role", (await after.Applications.SingleAsync(a => a.Id == id)).Title);
    }

    [Fact]
    public void Tracker_tables_are_not_in_the_rebuild_list()
    {
        using var db = tracker.OpenDb();
        var rebuilt = TrackerTables.All.Split(", ");
        Assert.All(db.Model.GetEntityTypes().Select(e => e.GetTableName()!), table => Assert.DoesNotContain(table, rebuilt));
    }

    [Fact]
    public async Task Requests_for_other_host_names_are_refused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tracker/status");
        request.Headers.Host = "attacker.example";

        Assert.Equal(HttpStatusCode.BadRequest, (await tracker.Client.SendAsync(request)).StatusCode);
    }
}

[Trait("Category", "Integration")]
public sealed class TrackerUnavailableTests : IDisposable
{
    private readonly TempDir _data = new();

    [Fact]
    public async Task A_stopped_database_answers_503_on_tracker_requests()
    {
        await using var factory = new ApiFactory("Host=127.0.0.1;Port=1;Database=tracker;Username=tracker;Password=x;Timeout=3", _data.Path);

        var response = await factory.CreateClient().GetAsync("/api/tracker/status");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("docker compose up -d postgres", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }

    public void Dispose() => _data.Dispose();
}
```

`tests/Api.Tests/Applications/TrackerRestoreTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Applications;
using Api.Tests.TestSupport;
using Npgsql;
using Tracker.TestSupport;

namespace Api.Tests.Applications;

// Each test gets its own empty database on the shared server, as if the Docker volume had just been lost.
[Collection("postgres")]
[Trait("Category", "Integration")]
public sealed class TrackerRestoreTests(PostgresFixture pg) : IDisposable
{
    private readonly TempDir _data = new();

    private string MirrorPath => Path.Combine(_data.Path, "tracker", "tracker.json");

    private async Task<string> FreshDatabaseAsync()
    {
        var name = "restore_" + Guid.NewGuid().ToString("N")[..12];
        await using (var conn = new NpgsqlConnection(pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", conn);
            await create.ExecuteNonQueryAsync();
        }
        return new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Database = name }.ConnectionString;
    }

    [Fact]
    public async Task Empty_tables_are_restored_from_the_backup_copy_on_first_use()
    {
        var sample = MirrorStoreTests.Sample();
        MirrorStore.Write(MirrorPath, sample);
        await using var factory = new ApiFactory(await FreshDatabaseAsync(), _data.Path);

        var status = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/tracker/status");

        Assert.Equal(1, status.GetProperty("counts").GetProperty("Interviewing").GetInt32());
        Assert.Equal(0, status.GetProperty("overdue").GetInt32()); // its follow-up is due in March 2027
    }

    [Fact]
    public async Task A_corrupt_backup_keeps_the_tracker_closed_and_is_never_overwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MirrorPath)!);
        File.WriteAllText(MirrorPath, "{ not json");
        await using var factory = new ApiFactory(await FreshDatabaseAsync(), _data.Path);

        var response = await factory.CreateClient().GetAsync("/api/tracker/status");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("tracker.json", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
        Assert.Equal("{ not json", File.ReadAllText(MirrorPath));
    }

    public void Dispose() => _data.Dispose();
}
```
`tests/Api.Tests/Applications/TrackerMirrorTests.cs`:
```csharp
using System.Text.Json;
using Api.Applications;
using Api.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Tracker.TestSupport;

namespace Api.Tests.Applications;

[Collection("tracker")]
[Trait("Category", "Integration")]
public sealed class TrackerMirrorTests(TrackerFixture tracker) : IDisposable
{
    private readonly TempDir _root = new();

    private TrackerMirror Mirror() => new(Data.Settings with { DataRoot = _root.Path }, tracker.Clock, NullLogger<TrackerMirror>.Instance);

    [Fact]
    public async Task The_copy_holds_what_the_database_holds()
    {
        await tracker.GetAsync<JsonElement>("/api/tracker/status");
        var companyId = Guid.NewGuid();
        await using var db = tracker.OpenDb();
        db.Companies.Add(new Company { Id = companyId, Name = "Mirror Test Co", NameKey = "mirror test co", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var mirror = Mirror();

        await mirror.WriteAsync(db);

        Assert.Contains(MirrorStore.Read(mirror.FilePath)!.Companies, c => c.Id == companyId);
        Assert.Equal((tracker.Clock.Now, (string?)null), (mirror.LastBackupAt!.Value, mirror.LastBackupError));
    }

    [Fact]
    public async Task A_failed_copy_is_reported_without_throwing()
    {
        await tracker.GetAsync<JsonElement>("/api/tracker/status");
        File.WriteAllText(Path.Combine(_root.Path, "tracker"), "a file where the folder should be");
        await using var db = tracker.OpenDb();
        var mirror = Mirror();

        await mirror.WriteAsync(db);

        Assert.Contains("could not be updated", mirror.LastBackupError);
        Assert.Null(mirror.LastBackupAt);
    }

    [Fact]
    public void Document_files_live_under_their_application_folder()
    {
        var mirror = Mirror();
        var document = new ApplicationDocument { Id = Guid.NewGuid(), ApplicationId = Guid.NewGuid(), FileName = "CV.pdf" };

        Assert.Equal(Path.Combine(_root.Path, "tracker", "files", document.ApplicationId.ToString("N"), $"{document.Id:N}-CV.pdf"),
            mirror.DocumentPath(document));
    }

    public void Dispose() => _root.Dispose();
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~Api.Tests.Applications"`
Expected: build FAILS: `TrackerGate`, `TrackerMirror` and the tracker endpoints don't exist yet.

- [ ] **Step 4: Implement**

`src/Api/DatabaseErrors.cs`:
```csharp
using System.Net.Sockets;
using Npgsql;

namespace Api;

public static class DatabaseErrors
{
    // Not running, wrong port, timed out, starting up or refusing the login: the "start Docker" message applies.
    // Other server errors (bad SQL, a missing column, a unique violation) are not outages.
    public static bool IsUnavailable(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            switch (e)
            {
                case PostgresException pg when pg.SqlState.StartsWith("28") || pg.SqlState.StartsWith("3D") || pg.SqlState.StartsWith("57P"):
                    return true;
                case PostgresException:
                    return false;
                case NpgsqlException or SocketException or TimeoutException:
                    return true;
            }
        return false;
    }
}
```

In `src/Api/Market/DbMarketSource.cs`:
- delete the private `IsUnavailable` method and its comment;
- change `catch (Exception ex) when (IsUnavailable(ex))` to `catch (Exception ex) when (DatabaseErrors.IsUnavailable(ex))`;
- delete `using System.Net.Sockets;`.

In `src/Api/Market/IMarketSource.cs`, replace the exception with:
```csharp
// Postgres can't be reached: not running, wrong port, starting up, or refusing the login.
public sealed class DatabaseUnavailableException(Exception inner) : Exception(Text, inner)
{
    public const string Text = "The tracker database isn't running. Start Docker Desktop (and `docker compose up -d postgres`).";
}
```

`src/Api/Applications/TrackerTelemetry.cs`:
```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Api.Applications;

public static class TrackerTelemetry
{
    public const string Name = "Tracker.Applications";
    public static readonly ActivitySource Source = new(Name);
    private static readonly Meter Meter = new(Name);
    public static readonly Counter<long> Changes = Meter.CreateCounter<long>("tracker.applications.changes",
        description: "Changes saved to the application tracker.");
}
```

`src/Api/Applications/TrackerMirror.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Api.Applications;

// Keeps data/tracker/tracker.json equal to the tracker tables, and brings them back from it when they are empty.
public sealed class TrackerMirror(MarketSettings settings, TimeProvider clock, ILogger<TrackerMirror> log)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string FilePath => Path.Combine(settings.DataRoot, "tracker", "tracker.json");
    public string FilesFolder => Path.Combine(settings.DataRoot, "tracker", "files");
    public DateTimeOffset? LastBackupAt { get; private set; }
    public string? LastBackupError { get; private set; }

    public string ApplicationFolder(Guid applicationId) => Path.Combine(FilesFolder, applicationId.ToString("N"));

    public string DocumentPath(ApplicationDocument document) =>
        Path.Combine(ApplicationFolder(document.ApplicationId), $"{document.Id:N}-{document.FileName}");

    // Called after every committed change. Never throws: the change is already saved, so a failed copy is reported
    // (status endpoint, dashboard banner) instead of failing the request.
    public async Task WriteAsync(ApplicationsDb db)
    {
        await _gate.WaitAsync();
        try
        {
            var file = new MirrorFile(MirrorFile.CurrentFormat, clock.GetUtcNow(),
                await db.Companies.AsNoTracking().OrderBy(c => c.CreatedAt).ToListAsync(),
                await db.Applications.AsNoTracking().OrderBy(a => a.CreatedAt).ToListAsync(),
                await db.Events.AsNoTracking().OrderBy(e => e.CreatedAt).ToListAsync(),
                await db.FollowUps.AsNoTracking().OrderBy(f => f.CreatedAt).ToListAsync(),
                await db.Contacts.AsNoTracking().OrderBy(c => c.CreatedAt).ToListAsync(),
                await db.Documents.AsNoTracking().OrderBy(d => d.CreatedAt).ToListAsync());
            MirrorStore.Write(FilePath, file);
            LastBackupAt = file.SavedAt;
            LastBackupError = null;
        }
        catch (Exception ex)
        {
            LastBackupError = $"The backup copy in {FilePath} could not be updated: {ex.Message}";
            log.LogError(ex, "Tracker backup failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    // When every tracker table is empty (a new or lost database) and the copy has entries, load it back in one
    // transaction, keeping ids. Returns the number of applications restored.
    public async Task<int> RestoreIfEmptyAsync(ApplicationsDb db, CancellationToken ct)
    {
        if (await db.Companies.AnyAsync(ct) || await db.Applications.AnyAsync(ct) || await db.Events.AnyAsync(ct)
            || await db.FollowUps.AnyAsync(ct) || await db.Contacts.AnyAsync(ct) || await db.Documents.AnyAsync(ct))
            return 0;
        var file = MirrorStore.Read(FilePath);
        if (file is null || (file.Companies.Count == 0 && file.Applications.Count == 0)) return 0;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Companies.AddRange(file.Companies);
        db.Applications.AddRange(file.Applications);
        db.Events.AddRange(file.Events);
        db.FollowUps.AddRange(file.FollowUps);
        db.Contacts.AddRange(file.Contacts);
        db.Documents.AddRange(file.Documents);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return file.Applications.Count;
    }
}
```

`src/Api/Applications/TrackerSchema.cs`:
```csharp
using Api.Market;
using Microsoft.EntityFrameworkCore;

namespace Api.Applications;

// Creates or updates the tracker tables once, on the first tracker request, then restores from the JSON copy if they
// are empty. Market endpoints never wait on this.
public sealed class TrackerSchema(IServiceScopeFactory scopes, TrackerMirror mirror, ILogger<TrackerSchema> log)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _ready;

    public bool Ready => _ready;

    public async Task EnsureReadyAsync(CancellationToken ct)
    {
        if (_ready) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (_ready) return;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDb>();
            try
            {
                await db.Database.MigrateAsync(ct);
                var restored = await mirror.RestoreIfEmptyAsync(db, ct);
                if (restored > 0) log.LogInformation("Restored {Count} applications from {Path}", restored, mirror.FilePath);
            }
            catch (Exception ex) when (DatabaseErrors.IsUnavailable(ex))
            {
                throw new DatabaseUnavailableException(ex);
            }
            _ready = true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
```

`src/Api/Applications/TrackerGate.cs`:
```csharp
namespace Api.Applications;

// Runs in front of every tracker endpoint.
public sealed class TrackerGate(TrackerSchema schema) : IEndpointFilter
{
    public const string ClientHeader = "X-Tracker-Client";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        // Changes need a custom header. A browser can't add one to another website's form post without a CORS preflight,
        // which this API never answers, so no other site can change the tracker through the owner's browser.
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !request.Headers.ContainsKey(ClientHeader))
            return Results.Problem(statusCode: 403, title: "Forbidden", detail: "Tracker changes must come from the dashboard.");
        await schema.EnsureReadyAsync(context.HttpContext.RequestAborted);
        return await next(context);
    }
}
```

`src/Api/Applications/Tracking.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Api.Applications;

internal static class Tracking
{
    public const string MissingEntry = "That entry doesn't exist (it may have been deleted).";

    // The owner's calendar date on this PC; follow-ups and timeline dates use it.
    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    // The entry, tracked for changes, or a 404.
    public static async Task<JobApplication> FindEntryAsync(ApplicationsDb db, Guid id, CancellationToken ct) =>
        await db.Applications.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new TrackerNotFoundException(MissingEntry);

    // Saves, counts the change, then refreshes the JSON copy. The copy is written even if the caller has gone away:
    // the database already holds the change.
    public static async Task CommitAsync(ApplicationsDb db, TrackerMirror mirror, string operation, CancellationToken ct)
    {
        using var activity = TrackerTelemetry.Source.StartActivity(operation);
        await db.SaveChangesAsync(ct);
        TrackerTelemetry.Changes.Add(1, new KeyValuePair<string, object?>("operation", operation));
        await mirror.WriteAsync(db);
    }
}
```

`src/Api/Applications/TrackerEndpoints.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Api.Applications;

public sealed record TrackerStatusView(bool Migrated, DateTimeOffset? LastBackupAt, string? LastBackupError,
    IReadOnlyDictionary<string, int> Counts, int Overdue);

public static class TrackerEndpoints
{
    public static void MapTrackerEndpoints(this IEndpointRouteBuilder app)
    {
        var tracker = app.MapGroup("/api/tracker").AddEndpointFilter<TrackerGate>();

        tracker.MapGet("/status", async (ApplicationsDb db, TrackerMirror mirror, TrackerSchema schema, TimeProvider clock, CancellationToken ct) =>
        {
            var statuses = await db.Applications.AsNoTracking().Select(a => a.Status).ToListAsync(ct);
            var today = Tracking.Today(clock);
            var overdue = await db.FollowUps.AsNoTracking().CountAsync(f => f.DoneOn == null && f.DueOn < today, ct);
            return new TrackerStatusView(schema.Ready, mirror.LastBackupAt, mirror.LastBackupError,
                statuses.GroupBy(s => s).ToDictionary(g => g.Key.ToString(), g => g.Count()), overdue);
        });
    }
}
```

`src/Api/Endpoints/ApiExceptionHandler.cs` (replace the whole file):
```csharp
using Api.Applications;
using Api.Market;
using Api.Queries;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Endpoints;

// Known failures become problem details:
// - 400: bad filter or input;
// - 404 / 409: unknown or conflicting tracker entries;
// - 503: database down;
// - 500: an unreadable backup, or anything else, without internals.
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> log) : IExceptionHandler
{
    public const string StaleEdit = "This entry changed in another tab. Reload to see the latest.";

    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var result = exception switch
        {
            FilterException f => Results.Problem(statusCode: 400, title: "Invalid filter", detail: f.Message,
                extensions: new Dictionary<string, object?> { ["parameter"] = f.Parameter }),
            TrackerInputException t => Results.Problem(statusCode: 400, title: "Invalid input", detail: t.Message,
                extensions: new Dictionary<string, object?> { ["field"] = t.Field }),
            TrackerNotFoundException n => Results.Problem(statusCode: 404, title: "Not found", detail: n.Message),
            TrackerConflictException c => Results.Problem(statusCode: 409, title: "Conflict", detail: c.Message,
                extensions: new Dictionary<string, object?> { ["id"] = c.ExistingId }),
            DbUpdateConcurrencyException => Results.Problem(statusCode: 409, title: "Conflict", detail: StaleEdit),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                Results.Problem(statusCode: 409, title: "Conflict", detail: "That's already in your tracker. Reload to see it."),
            DatabaseUnavailableException d => Results.Problem(statusCode: 503, title: "Database unavailable", detail: d.Message),
            _ when DatabaseErrors.IsUnavailable(exception) =>
                Results.Problem(statusCode: 503, title: "Database unavailable", detail: DatabaseUnavailableException.Text),
            TrackerBackupException b => Backup(b),
            _ => null,
        };
        if (result is null)
        {
            log.LogError(exception, "Unhandled error for {Path}", http.Request.Path);
            result = Results.Problem(statusCode: 500, title: "Something went wrong",
                detail: "The error was logged. See the API's structured logs in the Aspire dashboard.");
        }
        await result.ExecuteAsync(http);
        return true;
    }

    private IResult Backup(TrackerBackupException b)
    {
        log.LogError(b, "Tracker backup copy unreadable");
        return Results.Problem(statusCode: 500, title: "Tracker backup unreadable", detail: b.Message);
    }
}
```

In `src/Api/Program.cs`:
- add `using Api.Applications;`;
- after the `builder.AddNpgsqlDbContext<TrackerDb>(…);` statement, add:
```csharp
// The owner's tracker tables: same database, own context and migrations history (ADR 0010).
var trackerConnection = builder.Configuration.GetConnectionString("tracker")
    ?? throw new InvalidOperationException("ConnectionStrings:tracker is not set. See the README's dashboard setup.");
builder.Services.AddDbContext<ApplicationsDb>(o => o
    .UseNpgsql(trackerConnection, npgsql => npgsql.MigrationsHistoryTable(ApplicationsDb.HistoryTable))
    .UseSnakeCaseNamingConvention());
builder.Services.AddSingleton<TrackerMirror>();
builder.Services.AddSingleton<TrackerSchema>();
```
- change the OpenTelemetry lines to:
```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(MarketData.TelemetryName, TrackerTelemetry.Name))
    .WithMetrics(metrics => metrics.AddMeter(MarketData.TelemetryName, TrackerTelemetry.Name));
```
- after `app.MapMarketEndpoints();`, add `app.MapTrackerEndpoints();`.

In `src/Api/appsettings.json`, change `"AllowedHosts": "*"` to `"AllowedHosts": "localhost;127.0.0.1;[::1]"`. Only local host names are accepted, which defeats DNS-rebinding pages.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet build src/Api -warnaserror && dotnet test tests/Api.Tests`
Expected: 0 warnings; all Api.Tests PASS, the 60 from phase 2a plus the new ones.
- If a phase 2a endpoint test now fails on the host filter, WebApplicationFactory is using a host other than `localhost`. Add it to `AllowedHosts` and ledger a ruling.
- If EF refuses the `xmin` mapping, use `IsRowVersion()` alone (Npgsql maps `uint` row versions to `xmin`), regenerate the migration, and ledger it.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add the tracker database context, JSON mirror with restore, gate and error mapping"
```

---

### Task 3: Lists, detail, shortlist and paste endpoints

**Files:**
- Create: `src/Api/Applications/TrackerViews.cs`, `src/Api/Applications/TrackerListEndpoints.cs`
- Modify: `src/Api/Applications/TrackerEndpoints.cs` (one line), `tests/Api.Tests/TestSupport/TrackerFixture.cs` (add `PasteAsync`)
- Test: `tests/Api.Tests/Applications/TrackerListTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2 (`Rules`, `InboxWriter`, `Tracking`, `TrackerMirror`, `ApplicationsDb`, the exceptions); `MarketData`, `MarketSnapshot`, `MarketSettings`, `PostingQueries.Match` (phase 2a); `TrackerDb.Postings` (Tracker.Core)
- Produces:
  - **Response records:**
    - `NextAction(Guid Id, string Action, DateOnly DueOn)`
    - `ApplicationRow(Guid Id, string Title, string Company, ApplicationStatus Status, NextAction? Next, DateTimeOffset UpdatedAt, string? PostingId, bool Live)`
    - `UpNextItem(Guid FollowUpId, Guid ApplicationId, string Title, string Company, string Action, DateOnly DueOn, bool Overdue)`
    - `IndexEntry(Guid Id, ApplicationStatus Status)`, `CompanyOption(Guid Id, string Name)`
    - `CompanyView(Guid Id, string Name, string Website, string OfficeLocation, string CompanySize, string AnnualLeave, string Benefits, string Notes, uint Version, int Applications)`
    - `EventView(Guid Id, DateOnly OnDate, ApplicationStatus? FromStatus, ApplicationStatus ToStatus, string Note)`
    - `FollowUpView(Guid Id, string Action, DateOnly DueOn, DateOnly? DoneOn)`
    - `ContactView(Guid Id, string Name, string Role, string Email, string Phone, string LinkedIn, string Notes)`
    - `DocumentView(Guid Id, DocumentKind Kind, string Label, string FileName, long SizeBytes, DateOnly SentOn)`
    - `MarketFacts(double Match, bool Live, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, IReadOnlyList<string> Skills)`
    - `ApplicationDetail(Guid Id, string Title, string Url, string Location, ApplicationStatus Status, EntryOrigin Origin, string FoundVia, WorkModel WorkModel, string OfficeDays, string TeamSize, string WorkingHours, string SalaryPosted, string SalaryAsked, string SalaryOffered, string Notes, string? PostingId, string PostingText, MarketFacts? Market, CompanyView Company, IReadOnlyList<EventView> Events, IReadOnlyList<FollowUpView> FollowUps, IReadOnlyList<ContactView> Contacts, IReadOnlyList<DocumentView> Documents, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, uint Version)`
    - `NewEntry(Guid Id)`
  - **Request records:** `ShortlistRequest(string? PostingId, string? Status)` and `PasteRequest(string? Title, string? Company, string? Url, string? Location, string? PostingText, string? Status)`
  - **`static class TrackerListEndpoints`:** `MapTrackerLists(this RouteGroupBuilder)`, plus internal helpers `LiveIds(MarketSnapshot)`, `FindOrCreateCompanyAsync(db, name, clock, ct)` and `CreateEntry(company, title, url, location, postingText, status, origin, postingId, clock, note)`.
  - **Routes:** `GET applications`, `GET up-next`, `GET index`, `GET companies`, `GET applications/{id}`, `POST applications`, `POST applications/paste`.
  - **Test support:** `TrackerFixture.PasteAsync(company, status = "Shortlisted") : Task<NewEntry>`.

- [ ] **Step 1: Write the failing tests**

Add to `TrackerFixture` (below `PostAsync`):
```csharp
    // A pasted job in the GTA, one per company name the test chooses.
    public Task<NewEntry> PasteAsync(string company, string status = "Shortlisted") =>
        PostAsync<NewEntry>("/api/tracker/applications/paste", new
        {
            title = $"Developer at {company}", company, url = "https://careers.example.com/" + Uri.EscapeDataString(company),
            location = "Toronto, ON", postingText = "Build services in C# and Azure.", status,
        });
```

`tests/Api.Tests/Applications/TrackerListTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Applications;
using Api.Tests.TestSupport;

namespace Api.Tests.Applications;

[Collection("tracker")]
[Trait("Category", "Integration")]
public sealed class TrackerListTests(TrackerFixture tracker)
{
    [Fact]
    public async Task Shortlisting_a_market_posting_copies_it_and_starts_the_timeline()
    {
        var created = await tracker.PostAsync<NewEntry>("/api/tracker/applications", new { postingId = "greenhouse:101" });

        var detail = await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{created.Id}");
        Assert.Equal(("Senior Full Stack Developer", "Acme", ApplicationStatus.Shortlisted, EntryOrigin.Market),
            (detail.Title, detail.Company.Name, detail.Status, detail.Origin));
        var start = Assert.Single(detail.Events);
        Assert.Equal(((ApplicationStatus?)null, ApplicationStatus.Shortlisted, new DateOnly(2026, 10, 2)), (start.FromStatus, start.ToStatus, start.OnDate));
        Assert.NotNull(detail.Market);
        Assert.True(detail.Market!.Live);
        Assert.NotEmpty(detail.PostingText);
        var index = await tracker.GetAsync<Dictionary<string, IndexEntry>>("/api/tracker/index");
        Assert.Equal(created.Id, index["greenhouse:101"].Id);
    }

    [Fact]
    public async Task Shortlisting_twice_returns_the_existing_entry()
    {
        var first = await tracker.PostAsync<NewEntry>("/api/tracker/applications", new { postingId = "ashby:S1" });

        var again = await tracker.Client.PostAsJsonAsync("/api/tracker/applications", new { postingId = "ashby:S1" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(first.Id.ToString(), (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task Unknown_postings_are_404_and_writes_without_the_header_are_403()
    {
        Assert.Equal(HttpStatusCode.NotFound,
            (await tracker.Client.PostAsJsonAsync("/api/tracker/applications", new { postingId = "nope:1" })).StatusCode);
        using var outsider = tracker.CreateClient(withHeader: false);

        var refused = await outsider.PostAsJsonAsync("/api/tracker/applications", new { postingId = "lever:L1" });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("must come from the dashboard", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_pasted_job_goes_to_the_inbox_and_links_to_the_market_after_the_next_rebuild()
    {
        var created = await tracker.PostAsync<NewEntry>("/api/tracker/applications/paste", new
        {
            title = "Senior .NET Developer\n(Payments)", company = "Bank of Testing", url = "https://careers.example.com/42",
            location = "Toronto, ON", postingText = "We build payments in C#, Azure and SQL Server.", status = "Applied",
        });

        var before = await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{created.Id}");
        Assert.Equal(("Senior .NET Developer (Payments)", ApplicationStatus.Applied, EntryOrigin.Pasted), (before.Title, before.Status, before.Origin));
        Assert.StartsWith("inbox:", before.PostingId);
        Assert.Null(before.Market);
        var file = Path.Combine(tracker.DataRoot, "inbox", before.PostingId!["inbox:".Length..]);
        Assert.Contains("Company: Bank of Testing\nTitle: Senior .NET Developer (Payments)\n", File.ReadAllText(file));

        await tracker.RebuildAsync();

        var after = await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{created.Id}");
        Assert.NotNull(after.Market);
        Assert.True(after.Market!.Live);
        Assert.Contains("C#", after.Market.Skills);
    }

    [Fact]
    public async Task The_list_sorts_by_next_follow_up_and_filters_by_status_and_text()
    {
        var applied = await tracker.PasteAsync("Sortco Alpha", "Applied");
        var waiting = await tracker.PasteAsync("Sortco Beta");
        await using (var db = tracker.OpenDb())
        {
            db.FollowUps.Add(new FollowUp { Id = Guid.NewGuid(), ApplicationId = waiting.Id, Action = "Call the recruiter",
                DueOn = new DateOnly(2026, 10, 5), CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var byNext = await tracker.GetAsync<List<ApplicationRow>>("/api/tracker/applications?q=sortco");
        var onlyApplied = await tracker.GetAsync<List<ApplicationRow>>("/api/tracker/applications?q=SORTCO&status=applied");
        var byCompany = await tracker.GetAsync<List<ApplicationRow>>("/api/tracker/applications?q=sortco&sort=company");

        Assert.Equal(new[] { waiting.Id, applied.Id }, byNext.Select(r => r.Id));
        Assert.Equal("Call the recruiter", byNext[0].Next!.Action);
        Assert.Equal(applied.Id, Assert.Single(onlyApplied).Id);
        Assert.Equal(new[] { applied.Id, waiting.Id }, byCompany.Select(r => r.Id));
    }

    [Fact]
    public async Task Up_next_lists_overdue_and_coming_follow_ups_only()
    {
        var entry = await tracker.PasteAsync("Upnext Co");
        await using (var db = tracker.OpenDb())
        {
            var now = DateTimeOffset.UtcNow;
            db.FollowUps.AddRange(
                new FollowUp { Id = Guid.NewGuid(), ApplicationId = entry.Id, Action = "Overdue call", DueOn = new DateOnly(2026, 9, 30), CreatedAt = now },
                new FollowUp { Id = Guid.NewGuid(), ApplicationId = entry.Id, Action = "Soon call", DueOn = new DateOnly(2026, 10, 5), CreatedAt = now },
                new FollowUp { Id = Guid.NewGuid(), ApplicationId = entry.Id, Action = "Later call", DueOn = new DateOnly(2026, 10, 30), CreatedAt = now },
                new FollowUp { Id = Guid.NewGuid(), ApplicationId = entry.Id, Action = "Done call", DueOn = new DateOnly(2026, 10, 1),
                    DoneOn = new DateOnly(2026, 10, 1), CreatedAt = now });
            await db.SaveChangesAsync();
        }

        var items = (await tracker.GetAsync<List<UpNextItem>>("/api/tracker/up-next")).Where(i => i.ApplicationId == entry.Id).ToList();

        Assert.Equal(new[] { ("Overdue call", true), ("Soon call", false) }, items.Select(i => (i.Action, i.Overdue)));
        Assert.Equal("Upnext Co", items[0].Company);
        Assert.True((await tracker.GetAsync<TrackerStatusView>("/api/tracker/status")).Overdue >= 1);
    }

    [Theory]
    [InlineData("status=wizard", "status")]
    [InlineData("sort=salary", "sort")]
    public async Task Bad_list_parameters_name_the_field(string query, string field)
    {
        var response = await tracker.Client.GetAsync($"/api/tracker/applications?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(field, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("field").GetString());
    }

    [Fact]
    public async Task Paste_validation_names_the_field_and_leaves_no_inbox_file()
    {
        var inbox = Path.Combine(tracker.DataRoot, "inbox");
        var before = Directory.GetFiles(inbox).Length;

        var response = await tracker.Client.PostAsJsonAsync("/api/tracker/applications/paste",
            new { title = "Dev", company = "Nofile Co", postingText = "   ", url = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("postingText", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("field").GetString());
        Assert.Equal(before, Directory.GetFiles(inbox).Length);
    }

    [Fact]
    public async Task The_backup_copy_follows_each_change()
    {
        var created = await tracker.PasteAsync("Mirror Follow Co");

        var copy = MirrorStore.Read(Path.Combine(tracker.DataRoot, "tracker", "tracker.json"))!;

        Assert.Contains(copy.Applications, a => a.Id == created.Id);
        Assert.Contains(copy.Companies, c => c.Name == "Mirror Follow Co");
    }

    [Fact]
    public async Task Companies_are_listed_for_suggestions()
    {
        await tracker.PasteAsync("Suggest Co");

        Assert.Contains(await tracker.GetAsync<List<CompanyOption>>("/api/tracker/companies"), c => c.Name == "Suggest Co");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~TrackerListTests"`
Expected: build FAILS (`NewEntry`, `ApplicationDetail` and the other records don't exist).

- [ ] **Step 3: Implement**

`src/Api/Applications/TrackerViews.cs`:
```csharp
namespace Api.Applications;

public sealed record NextAction(Guid Id, string Action, DateOnly DueOn);

public sealed record ApplicationRow(Guid Id, string Title, string Company, ApplicationStatus Status, NextAction? Next,
    DateTimeOffset UpdatedAt, string? PostingId, bool Live);

public sealed record UpNextItem(Guid FollowUpId, Guid ApplicationId, string Title, string Company, string Action, DateOnly DueOn, bool Overdue);

public sealed record IndexEntry(Guid Id, ApplicationStatus Status);

public sealed record CompanyOption(Guid Id, string Name);

public sealed record CompanyView(Guid Id, string Name, string Website, string OfficeLocation, string CompanySize, string AnnualLeave,
    string Benefits, string Notes, uint Version, int Applications);

public sealed record EventView(Guid Id, DateOnly OnDate, ApplicationStatus? FromStatus, ApplicationStatus ToStatus, string Note);

public sealed record FollowUpView(Guid Id, string Action, DateOnly DueOn, DateOnly? DoneOn);

public sealed record ContactView(Guid Id, string Name, string Role, string Email, string Phone, string LinkedIn, string Notes);

public sealed record DocumentView(Guid Id, DocumentKind Kind, string Label, string FileName, long SizeBytes, DateOnly SentOn);

// What the market data says about the linked posting right now.
public sealed record MarketFacts(double Match, bool Live, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, IReadOnlyList<string> Skills);

public sealed record ApplicationDetail(Guid Id, string Title, string Url, string Location, ApplicationStatus Status, EntryOrigin Origin,
    string FoundVia, WorkModel WorkModel, string OfficeDays, string TeamSize, string WorkingHours, string SalaryPosted, string SalaryAsked,
    string SalaryOffered, string Notes, string? PostingId, string PostingText, MarketFacts? Market, CompanyView Company,
    IReadOnlyList<EventView> Events, IReadOnlyList<FollowUpView> FollowUps, IReadOnlyList<ContactView> Contacts,
    IReadOnlyList<DocumentView> Documents, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, uint Version);

public sealed record NewEntry(Guid Id);

public sealed record ShortlistRequest(string? PostingId, string? Status);

public sealed record PasteRequest(string? Title, string? Company, string? Url, string? Location, string? PostingText, string? Status);
```

`src/Api/Applications/TrackerListEndpoints.cs`:
```csharp
using Api.Market;
using Api.Queries;
using Microsoft.EntityFrameworkCore;
using Tracker.Data;

namespace Api.Applications;

public static class TrackerListEndpoints
{
    public static void MapTrackerLists(this RouteGroupBuilder tracker)
    {
        tracker.MapGet("/applications", async (ApplicationsDb db, MarketData market, string? status, string? q, string? sort, CancellationToken ct) =>
        {
            var statuses = (status ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Rules.ParseEnum<ApplicationStatus>(s, "status")).ToHashSet();
            var order = string.IsNullOrEmpty(sort) ? "next"
                : sort is "next" or "updated" or "company" ? sort
                : throw new TrackerInputException("sort", $"Unknown sort '{sort}'. Use next, updated or company.");
            var term = (q ?? "").Trim();
            var live = LiveIds((await market.GetAsync(ct)).Data);
            var rows = (await db.Applications.AsNoTracking().Include(a => a.Company).Include(a => a.FollowUps).AsSplitQuery().ToListAsync(ct))
                .Where(a => statuses.Count == 0 || statuses.Contains(a.Status))
                .Where(a => term.Length == 0 || a.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || a.Company!.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(a => ToRow(a, live))
                .ToList();
            return order switch
            {
                "updated" => rows.OrderByDescending(r => r.UpdatedAt).ToList(),
                "company" => rows.OrderBy(r => r.Company, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList(),
                _ => rows.OrderBy(r => r.Next is null).ThenBy(r => r.Next?.DueOn).ThenByDescending(r => r.UpdatedAt).ToList(),
            };
        });

        tracker.MapGet("/up-next", async (ApplicationsDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var today = Tracking.Today(clock);
            var until = today.AddDays(7);
            var due = await db.FollowUps.AsNoTracking().Where(f => f.DoneOn == null && f.DueOn <= until).OrderBy(f => f.DueOn).ToListAsync(ct);
            var ids = due.Select(f => f.ApplicationId).Distinct().ToList();
            var entries = await db.Applications.AsNoTracking().Include(a => a.Company).Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
            return due.Select(f => new UpNextItem(f.Id, f.ApplicationId, entries[f.ApplicationId].Title, entries[f.ApplicationId].Company!.Name,
                f.Action, f.DueOn, f.DueOn < today)).ToList();
        });

        tracker.MapGet("/index", async (ApplicationsDb db, CancellationToken ct) =>
            (await db.Applications.AsNoTracking().Where(a => a.PostingId != null).Select(a => new { a.PostingId, a.Id, a.Status }).ToListAsync(ct))
                .ToDictionary(a => a.PostingId!, a => new IndexEntry(a.Id, a.Status), StringComparer.Ordinal));

        tracker.MapGet("/companies", async (ApplicationsDb db, CancellationToken ct) =>
            (await db.Companies.AsNoTracking().Select(c => new CompanyOption(c.Id, c.Name)).ToListAsync(ct))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList());

        tracker.MapGet("/applications/{id:guid}", async (Guid id, ApplicationsDb db, TrackerDb marketDb, MarketData market,
            MarketSettings settings, CancellationToken ct) =>
        {
            var entry = await db.Applications.AsNoTracking().Include(a => a.Company).Include(a => a.Events).Include(a => a.FollowUps)
                .Include(a => a.Contacts).Include(a => a.Documents).AsSplitQuery().FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new TrackerNotFoundException(Tracking.MissingEntry);
            var company = entry.Company!;
            var companyEntries = await db.Applications.CountAsync(a => a.CompanyId == company.Id, ct);
            var snapshot = (await market.GetAsync(ct)).Data;
            var posting = entry.PostingId is null ? null : snapshot.Postings.FirstOrDefault(p => p.Id == entry.PostingId);
            var facts = posting is null ? null : new MarketFacts(PostingQueries.Match(posting, settings.Profile),
                LiveIds(snapshot).Contains(posting.Id), posting.Sightings.Min(), posting.Sightings.Max(),
                [.. posting.Skills.Order(StringComparer.Ordinal)]);
            // Pasted jobs keep their own text; market postings are read from the market tables.
            var text = entry.Origin == EntryOrigin.Pasted || entry.PostingId is null
                ? entry.PostingText
                : await marketDb.Postings.AsNoTracking().Where(p => p.Id == entry.PostingId).Select(p => p.Description).FirstOrDefaultAsync(ct) ?? "";
            return new ApplicationDetail(entry.Id, entry.Title, entry.Url, entry.Location, entry.Status, entry.Origin, entry.FoundVia,
                entry.WorkModel, entry.OfficeDays, entry.TeamSize, entry.WorkingHours, entry.SalaryPosted, entry.SalaryAsked,
                entry.SalaryOffered, entry.Notes, entry.PostingId, text, facts,
                new CompanyView(company.Id, company.Name, company.Website, company.OfficeLocation, company.CompanySize,
                    company.AnnualLeave, company.Benefits, company.Notes, company.Version, companyEntries),
                [.. entry.Events.OrderBy(e => e.OnDate).ThenBy(e => e.CreatedAt).Select(e => new EventView(e.Id, e.OnDate, e.FromStatus, e.ToStatus, e.Note))],
                [.. entry.FollowUps.OrderBy(f => f.DoneOn is not null).ThenBy(f => f.DueOn).Select(f => new FollowUpView(f.Id, f.Action, f.DueOn, f.DoneOn))],
                [.. entry.Contacts.OrderBy(c => c.CreatedAt).Select(c => new ContactView(c.Id, c.Name, c.Role, c.Email, c.Phone, c.LinkedIn, c.Notes))],
                [.. entry.Documents.OrderByDescending(d => d.SentOn).ThenByDescending(d => d.CreatedAt)
                    .Select(d => new DocumentView(d.Id, d.Kind, d.Label, d.FileName, d.SizeBytes, d.SentOn))],
                entry.CreatedAt, entry.UpdatedAt, entry.Version);
        });

        tracker.MapPost("/applications", async (ShortlistRequest body, ApplicationsDb db, MarketData market, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var postingId = Rules.Required(body.PostingId, "postingId", 300);
            var status = string.IsNullOrWhiteSpace(body.Status) ? ApplicationStatus.Shortlisted : Rules.ParseEnum<ApplicationStatus>(body.Status, "status");
            var existing = await db.Applications.AsNoTracking().Where(a => a.PostingId == postingId).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
            if (existing is { } existingId) throw new TrackerConflictException("This posting is already in your tracker.", existingId);
            var posting = (await market.GetAsync(ct)).Data.Postings.FirstOrDefault(p => p.Id == postingId)
                ?? throw new TrackerNotFoundException($"Posting '{postingId}' isn't in the current market data.");
            var company = await FindOrCreateCompanyAsync(db, posting.Company, clock, ct);
            var entry = CreateEntry(company, posting.Title, posting.Url, posting.Location, "", status, EntryOrigin.Market, postingId, clock,
                "Added from the market data");
            db.Applications.Add(entry);
            await Tracking.CommitAsync(db, mirror, "shortlist", ct);
            return Results.Created($"/api/tracker/applications/{entry.Id}", new NewEntry(entry.Id));
        });

        tracker.MapPost("/applications/paste", async (PasteRequest body, ApplicationsDb db, MarketSettings settings, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var title = Rules.SingleLine(Rules.Required(body.Title, "title"));
            var companyName = Rules.SingleLine(Rules.Required(body.Company, "company"));
            var url = Rules.OptionalUrl(body.Url, "url");
            var location = Rules.SingleLine(Rules.Optional(body.Location, "location"));
            var text = Rules.Required(body.PostingText, "postingText", Rules.MaxPostingText);
            var status = string.IsNullOrWhiteSpace(body.Status) ? ApplicationStatus.Shortlisted : Rules.ParseEnum<ApplicationStatus>(body.Status, "status");
            var (postingId, file) = InboxWriter.Write(Path.Combine(settings.DataRoot, "inbox"), title, companyName, url, location, text, Tracking.Today(clock));
            try
            {
                var company = await FindOrCreateCompanyAsync(db, companyName, clock, ct);
                var entry = CreateEntry(company, title, url, location, text, status, EntryOrigin.Pasted, postingId, clock, "Pasted into the tracker");
                db.Applications.Add(entry);
                await Tracking.CommitAsync(db, mirror, "paste", ct);
                return Results.Created($"/api/tracker/applications/{entry.Id}", new NewEntry(entry.Id));
            }
            catch
            {
                File.Delete(file); // no entry, so no stray posting in the next rebuild
                throw;
            }
        });
    }

    // Postings seen in the latest collection: still open, as far as the market data knows.
    internal static IReadOnlySet<string> LiveIds(MarketSnapshot data) =>
        data.LatestRunAt is { } latest
            ? data.Postings.Where(p => p.Sightings.Contains(latest)).Select(p => p.Id).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    internal static async Task<Company> FindOrCreateCompanyAsync(ApplicationsDb db, string name, TimeProvider clock, CancellationToken ct)
    {
        var display = Rules.SingleLine(name);
        var key = Rules.CompanyKey(display);
        var existing = db.Companies.Local.FirstOrDefault(c => c.NameKey == key) ?? await db.Companies.FirstOrDefaultAsync(c => c.NameKey == key, ct);
        if (existing is not null) return existing;
        var now = clock.GetUtcNow();
        var company = new Company { Id = Guid.NewGuid(), Name = display, NameKey = key, CreatedAt = now, UpdatedAt = now };
        db.Companies.Add(company);
        return company;
    }

    internal static JobApplication CreateEntry(Company company, string title, string url, string location, string postingText,
        ApplicationStatus status, EntryOrigin origin, string? postingId, TimeProvider clock, string note)
    {
        var now = clock.GetUtcNow();
        var entry = new JobApplication
        {
            Id = Guid.NewGuid(), CompanyId = company.Id, Company = company, PostingId = postingId, Title = title, Url = url,
            Location = location, PostingText = postingText, Status = status, Origin = origin, CreatedAt = now, UpdatedAt = now,
        };
        entry.Events.Add(new ApplicationEvent { Id = Guid.NewGuid(), ApplicationId = entry.Id, OnDate = Tracking.Today(clock),
            ToStatus = status, Note = note, CreatedAt = now });
        return entry;
    }

    private static ApplicationRow ToRow(JobApplication a, IReadOnlySet<string> live)
    {
        var next = a.FollowUps.Where(f => f.DoneOn is null).OrderBy(f => f.DueOn).FirstOrDefault();
        return new ApplicationRow(a.Id, a.Title, a.Company!.Name, a.Status, next is null ? null : new NextAction(next.Id, next.Action, next.DueOn),
            a.UpdatedAt, a.PostingId, a.PostingId is not null && live.Contains(a.PostingId));
    }
}
```

In `src/Api/Applications/TrackerEndpoints.cs`, add after the `/status` endpoint (inside `MapTrackerEndpoints`):
```csharp
        tracker.MapTrackerLists();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build src/Api -warnaserror && dotnet test tests/Api.Tests --filter "FullyQualifiedName~Api.Tests.Applications"`
Expected: 0 warnings; all tracker tests PASS.
- If the pasted job isn't linked after the rebuild, read `Tracker.Core`'s scope and dedupe rules for inbox postings before changing anything. The test's location is the same form ("Toronto, ON") the fixture's own inbox file uses.
- Ledger any rule you had to follow.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add tracker lists, entry detail, shortlist and paste endpoints"
```

---

### Task 4: Edits, status changes, follow-ups, contacts and companies

**Files:**
- Create: `src/Api/Applications/TrackerEditEndpoints.cs`
- Modify: `src/Api/Applications/TrackerEndpoints.cs` (one line)
- Test: `tests/Api.Tests/Applications/TrackerEditTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3 (`Tracking.FindEntryAsync`, `Tracking.CommitAsync`, `Rules`, the views and `NewEntry`); `ApiExceptionHandler.StaleEdit` (Task 2)
- Produces:
  - **Request records:**
    - `ApplicationEdit(uint Version, string? Title, string? Url, string? Location, string? FoundVia, string? WorkModel, string? OfficeDays, string? TeamSize, string? WorkingHours, string? SalaryPosted, string? SalaryAsked, string? SalaryOffered, string? Notes)`
    - `CompanyEdit(uint Version, string? Name, string? Website, string? OfficeLocation, string? CompanySize, string? AnnualLeave, string? Benefits, string? Notes)`
    - `StatusChange(string? Status, DateOnly? On, string? Note)`
    - `FollowUpInput(string? Action, DateOnly? DueOn, DateOnly? DoneOn)`
    - `ContactInput(string? Name, string? Role, string? Email, string? Phone, string? LinkedIn, string? Notes)`
  - **`static class TrackerEditEndpoints`:** `MapTrackerEdits(this RouteGroupBuilder)`
  - **Routes:**
    - `PATCH applications/{id}` and `PATCH companies/{id}`, both 204, or 409 on a stale version;
    - `POST applications/{id}/status` (204);
    - `POST applications/{id}/follow-ups` (201 `NewEntry`), `PATCH` and `DELETE applications/{id}/follow-ups/{followUpId}` (204);
    - `POST applications/{id}/contacts` (201), `PATCH` and `DELETE applications/{id}/contacts/{contactId}` (204).
  - **Semantics:** a save sends every editable field, and a field left out is cleared.

- [ ] **Step 1: Write the failing tests**

`tests/Api.Tests/Applications/TrackerEditTests.cs`:
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
public sealed class TrackerEditTests(TrackerFixture tracker)
{
    private Task<ApplicationDetail> Detail(Guid id) => tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{id}");

    private static ApplicationEdit EditOf(ApplicationDetail d) => new(d.Version, d.Title, d.Url, d.Location, d.FoundVia, d.WorkModel.ToString(),
        d.OfficeDays, d.TeamSize, d.WorkingHours, d.SalaryPosted, d.SalaryAsked, d.SalaryOffered, d.Notes);

    private static async Task<string?> FieldOf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("field").GetString();
    }

    [Fact]
    public async Task Edits_need_the_current_version()
    {
        var entry = await tracker.PasteAsync("Versionco");
        var detail = await Detail(entry.Id);

        var saved = await tracker.Client.PatchAsJsonAsync($"/api/tracker/applications/{entry.Id}",
            EditOf(detail) with { TeamSize = "8 people", WorkModel = "hybrid", OfficeDays = "3 days" }, TrackerFixture.Json);
        var stale = await tracker.Client.PatchAsJsonAsync($"/api/tracker/applications/{entry.Id}",
            EditOf(detail) with { TeamSize = "9 people" }, TrackerFixture.Json);

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ApiExceptionHandler.StaleEdit, (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
        var after = await Detail(entry.Id);
        Assert.Equal(("8 people", WorkModel.Hybrid, "3 days"), (after.TeamSize, after.WorkModel, after.OfficeDays));
        Assert.NotEqual(detail.Version, after.Version);
    }

    [Fact]
    public async Task Edits_validate_every_field()
    {
        var entry = await tracker.PasteAsync("Validco");
        var detail = await Detail(entry.Id);
        var url = $"/api/tracker/applications/{entry.Id}";

        Assert.Equal("title", await FieldOf(await tracker.Client.PatchAsJsonAsync(url, EditOf(detail) with { Title = " " }, TrackerFixture.Json)));
        Assert.Equal("url", await FieldOf(await tracker.Client.PatchAsJsonAsync(url, EditOf(detail) with { Url = "javascript:alert(1)" }, TrackerFixture.Json)));
        Assert.Equal("workModel", await FieldOf(await tracker.Client.PatchAsJsonAsync(url, EditOf(detail) with { WorkModel = "sometimes" }, TrackerFixture.Json)));
    }

    [Fact]
    public async Task Status_changes_add_to_the_timeline()
    {
        var entry = await tracker.PasteAsync("Statusco");

        var moved = await tracker.Client.PostAsJsonAsync($"/api/tracker/applications/{entry.Id}/status",
            new { status = "Applied", on = "2026-09-30", note = "Sent through Workday" });
        var future = await tracker.Client.PostAsJsonAsync($"/api/tracker/applications/{entry.Id}/status", new { status = "Screening", on = "2026-12-01" });

        Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        Assert.Equal("on", await FieldOf(future));
        var detail = await Detail(entry.Id);
        Assert.Equal(ApplicationStatus.Applied, detail.Status);
        var applied = Assert.Single(detail.Events, e => e.ToStatus == ApplicationStatus.Applied);
        Assert.Equal((ApplicationStatus.Shortlisted, new DateOnly(2026, 9, 30), "Sent through Workday"), (applied.FromStatus!.Value, applied.OnDate, applied.Note));
    }

    [Fact]
    public async Task Follow_ups_can_be_added_completed_and_deleted()
    {
        var entry = await tracker.PasteAsync("Followco");
        var added = await tracker.PostAsync<NewEntry>($"/api/tracker/applications/{entry.Id}/follow-ups", new { action = "Email Sam", dueOn = "2026-10-06" });

        Assert.Equal("dueOn", await FieldOf(await tracker.Client.PostAsJsonAsync($"/api/tracker/applications/{entry.Id}/follow-ups", new { action = "No date" })));

        var done = await tracker.Client.PatchAsJsonAsync($"/api/tracker/applications/{entry.Id}/follow-ups/{added.Id}",
            new { action = "Email Sam", dueOn = "2026-10-06", doneOn = "2026-10-02" });
        Assert.Equal(HttpStatusCode.NoContent, done.StatusCode);
        Assert.Equal(new DateOnly(2026, 10, 2), Assert.Single((await Detail(entry.Id)).FollowUps).DoneOn);
        Assert.DoesNotContain(await tracker.GetAsync<List<UpNextItem>>("/api/tracker/up-next"), i => i.FollowUpId == added.Id);

        var removed = await tracker.Client.DeleteAsync($"/api/tracker/applications/{entry.Id}/follow-ups/{added.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty((await Detail(entry.Id)).FollowUps);
    }

    [Fact]
    public async Task Contacts_validate_email_and_links()
    {
        var entry = await tracker.PasteAsync("Contactco");
        var url = $"/api/tracker/applications/{entry.Id}/contacts";

        Assert.Equal("name", await FieldOf(await tracker.Client.PostAsJsonAsync(url, new { name = " " })));
        Assert.Equal("email", await FieldOf(await tracker.Client.PostAsJsonAsync(url, new { name = "Sam", email = "sam at contactco" })));
        Assert.Equal("linkedIn", await FieldOf(await tracker.Client.PostAsJsonAsync(url, new { name = "Sam", linkedIn = "linkedin.com/in/sam" })));

        var added = await tracker.PostAsync<NewEntry>(url,
            new { name = "Sam Lee", role = "Recruiter", email = "sam@contactco.com", linkedIn = "https://www.linkedin.com/in/samlee" });
        var edited = await tracker.Client.PatchAsJsonAsync($"{url}/{added.Id}", new { name = "Sam Lee", role = "Hiring manager", email = "sam@contactco.com" });

        Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);
        var contact = Assert.Single((await Detail(entry.Id)).Contacts);
        Assert.Equal(("Sam Lee", "Hiring manager", ""), (contact.Name, contact.Role, contact.LinkedIn));
        Assert.Equal(HttpStatusCode.NoContent, (await tracker.Client.DeleteAsync($"{url}/{added.Id}")).StatusCode);
        Assert.Empty((await Detail(entry.Id)).Contacts);
    }

    [Fact]
    public async Task Company_details_are_shared_and_names_stay_unique()
    {
        var first = await tracker.PasteAsync("Northwind Test");
        var second = await tracker.PasteAsync("  northwind   TEST ");
        var other = await tracker.PasteAsync("Otherwind Test");
        var company = (await Detail(first.Id)).Company;

        Assert.Equal(company.Id, (await Detail(second.Id)).Company.Id);
        Assert.Equal(2, company.Applications);

        var saved = await tracker.Client.PatchAsJsonAsync($"/api/tracker/companies/{company.Id}",
            new CompanyEdit(company.Version, "Northwind Test", "https://northwind.example", "Toronto (King St W)", "5,000+", "20 days + 5 personal", "", ""),
            TrackerFixture.Json);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal("20 days + 5 personal", (await Detail(second.Id)).Company.AnnualLeave);

        var otherCompany = (await Detail(other.Id)).Company;
        var clash = await tracker.Client.PatchAsJsonAsync($"/api/tracker/companies/{otherCompany.Id}",
            new CompanyEdit(otherCompany.Version, "NORTHWIND test", "", "", "", "", "", ""), TrackerFixture.Json);
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal(company.Id.ToString(), (await clash.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task Unknown_ids_are_404()
    {
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await tracker.Client.GetAsync($"/api/tracker/applications/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tracker.Client.PostAsJsonAsync($"/api/tracker/applications/{id}/status", new { status = "Applied" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tracker.Client.DeleteAsync($"/api/tracker/applications/{id}/follow-ups/{Guid.NewGuid()}")).StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~TrackerEditTests"`
Expected: build FAILS (`ApplicationEdit`, `CompanyEdit` don't exist).

- [ ] **Step 3: Implement**

`src/Api/Applications/TrackerEditEndpoints.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Api.Applications;

// Every editable field is sent on each save; a field left out is cleared.
public sealed record ApplicationEdit(uint Version, string? Title, string? Url, string? Location, string? FoundVia, string? WorkModel,
    string? OfficeDays, string? TeamSize, string? WorkingHours, string? SalaryPosted, string? SalaryAsked, string? SalaryOffered, string? Notes);

public sealed record CompanyEdit(uint Version, string? Name, string? Website, string? OfficeLocation, string? CompanySize,
    string? AnnualLeave, string? Benefits, string? Notes);

public sealed record StatusChange(string? Status, DateOnly? On, string? Note);

public sealed record FollowUpInput(string? Action, DateOnly? DueOn, DateOnly? DoneOn);

public sealed record ContactInput(string? Name, string? Role, string? Email, string? Phone, string? LinkedIn, string? Notes);

public static class TrackerEditEndpoints
{
    public static void MapTrackerEdits(this RouteGroupBuilder tracker)
    {
        tracker.MapPatch("/applications/{id:guid}", async (Guid id, ApplicationEdit body, ApplicationsDb db, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            // The version the editor saw: if the row changed since, the UPDATE matches nothing and EF throws (409).
            db.Entry(entry).Property(e => e.Version).OriginalValue = body.Version;
            entry.Title = Rules.SingleLine(Rules.Required(body.Title, "title"));
            entry.Url = Rules.OptionalUrl(body.Url, "url");
            entry.Location = Line(body.Location, "location");
            entry.FoundVia = Line(body.FoundVia, "foundVia");
            entry.WorkModel = string.IsNullOrWhiteSpace(body.WorkModel) ? WorkModel.Unknown : Rules.ParseEnum<WorkModel>(body.WorkModel, "workModel");
            entry.OfficeDays = Line(body.OfficeDays, "officeDays");
            entry.TeamSize = Line(body.TeamSize, "teamSize");
            entry.WorkingHours = Line(body.WorkingHours, "workingHours");
            entry.SalaryPosted = Line(body.SalaryPosted, "salaryPosted");
            entry.SalaryAsked = Line(body.SalaryAsked, "salaryAsked");
            entry.SalaryOffered = Line(body.SalaryOffered, "salaryOffered");
            entry.Notes = Rules.Optional(body.Notes, "notes", Rules.MaxLongText);
            entry.UpdatedAt = clock.GetUtcNow();
            await Tracking.CommitAsync(db, mirror, "edit-application", ct);
            return Results.NoContent();
        });

        tracker.MapPatch("/companies/{id:guid}", async (Guid id, CompanyEdit body, ApplicationsDb db, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new TrackerNotFoundException("That company doesn't exist.");
            db.Entry(company).Property(c => c.Version).OriginalValue = body.Version;
            var name = Rules.SingleLine(Rules.Required(body.Name, "name"));
            var key = Rules.CompanyKey(name);
            var clash = await db.Companies.AsNoTracking().Where(c => c.NameKey == key && c.Id != id).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            if (clash is { } otherId) throw new TrackerConflictException($"Another company in your tracker is already called '{name}'.", otherId);
            company.Name = name;
            company.NameKey = key;
            company.Website = Rules.OptionalUrl(body.Website, "website");
            company.OfficeLocation = Line(body.OfficeLocation, "officeLocation");
            company.CompanySize = Line(body.CompanySize, "companySize");
            company.AnnualLeave = Line(body.AnnualLeave, "annualLeave");
            company.Benefits = Rules.Optional(body.Benefits, "benefits", Rules.MaxLongText);
            company.Notes = Rules.Optional(body.Notes, "notes", Rules.MaxLongText);
            company.UpdatedAt = clock.GetUtcNow();
            await Tracking.CommitAsync(db, mirror, "edit-company", ct);
            return Results.NoContent();
        });

        tracker.MapPost("/applications/{id:guid}/status", async (Guid id, StatusChange body, ApplicationsDb db, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            var to = Rules.ParseEnum<ApplicationStatus>(Rules.Required(body.Status, "status"), "status");
            var today = Tracking.Today(clock);
            var on = body.On ?? today;
            if (on > today) throw new TrackerInputException("on", "The date can't be in the future.");
            var now = clock.GetUtcNow();
            db.Events.Add(new ApplicationEvent { Id = Guid.NewGuid(), ApplicationId = id, OnDate = on, FromStatus = entry.Status, ToStatus = to,
                Note = Rules.Optional(body.Note, "note", Rules.MaxLongText), CreatedAt = now });
            entry.Status = to;
            entry.UpdatedAt = now;
            await Tracking.CommitAsync(db, mirror, "status", ct);
            return Results.NoContent();
        });

        tracker.MapPost("/applications/{id:guid}/follow-ups", async (Guid id, FollowUpInput body, ApplicationsDb db, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            var followUp = new FollowUp { Id = Guid.NewGuid(), ApplicationId = id, CreatedAt = clock.GetUtcNow() };
            Apply(followUp, body);
            db.FollowUps.Add(followUp);
            entry.UpdatedAt = followUp.CreatedAt;
            await Tracking.CommitAsync(db, mirror, "add-follow-up", ct);
            return Results.Created($"/api/tracker/applications/{id}/follow-ups/{followUp.Id}", new NewEntry(followUp.Id));
        });

        tracker.MapPatch("/applications/{id:guid}/follow-ups/{followUpId:guid}", async (Guid id, Guid followUpId, FollowUpInput body,
            ApplicationsDb db, TrackerMirror mirror, TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            var followUp = await db.FollowUps.FirstOrDefaultAsync(f => f.Id == followUpId && f.ApplicationId == id, ct)
                ?? throw new TrackerNotFoundException("That follow-up doesn't exist.");
            Apply(followUp, body);
            entry.UpdatedAt = clock.GetUtcNow();
            await Tracking.CommitAsync(db, mirror, "edit-follow-up", ct);
            return Results.NoContent();
        });

        tracker.MapDelete("/applications/{id:guid}/follow-ups/{followUpId:guid}", async (Guid id, Guid followUpId, ApplicationsDb db,
            TrackerMirror mirror, TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            var followUp = await db.FollowUps.FirstOrDefaultAsync(f => f.Id == followUpId && f.ApplicationId == id, ct)
                ?? throw new TrackerNotFoundException("That follow-up doesn't exist.");
            db.FollowUps.Remove(followUp);
            entry.UpdatedAt = clock.GetUtcNow();
            await Tracking.CommitAsync(db, mirror, "delete-follow-up", ct);
            return Results.NoContent();
        });

        tracker.MapPost("/applications/{id:guid}/contacts", async (Guid id, ContactInput body, ApplicationsDb db, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            var contact = new Contact { Id = Guid.NewGuid(), ApplicationId = id, CreatedAt = clock.GetUtcNow() };
            Apply(contact, body);
            db.Contacts.Add(contact);
            entry.UpdatedAt = contact.CreatedAt;
            await Tracking.CommitAsync(db, mirror, "add-contact", ct);
            return Results.Created($"/api/tracker/applications/{id}/contacts/{contact.Id}", new NewEntry(contact.Id));
        });

        tracker.MapPatch("/applications/{id:guid}/contacts/{contactId:guid}", async (Guid id, Guid contactId, ContactInput body,
            ApplicationsDb db, TrackerMirror mirror, TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId && c.ApplicationId == id, ct)
                ?? throw new TrackerNotFoundException("That contact doesn't exist.");
            Apply(contact, body);
            entry.UpdatedAt = clock.GetUtcNow();
            await Tracking.CommitAsync(db, mirror, "edit-contact", ct);
            return Results.NoContent();
        });

        tracker.MapDelete("/applications/{id:guid}/contacts/{contactId:guid}", async (Guid id, Guid contactId, ApplicationsDb db,
            TrackerMirror mirror, TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId && c.ApplicationId == id, ct)
                ?? throw new TrackerNotFoundException("That contact doesn't exist.");
            db.Contacts.Remove(contact);
            entry.UpdatedAt = clock.GetUtcNow();
            await Tracking.CommitAsync(db, mirror, "delete-contact", ct);
            return Results.NoContent();
        });
    }

    private static string Line(string? value, string field) => Rules.SingleLine(Rules.Optional(value, field));

    private static void Apply(FollowUp followUp, FollowUpInput body)
    {
        followUp.Action = Rules.SingleLine(Rules.Required(body.Action, "action"));
        followUp.DueOn = body.DueOn ?? throw new TrackerInputException("dueOn", "Due date is required.");
        followUp.DoneOn = body.DoneOn;
    }

    private static void Apply(Contact contact, ContactInput body)
    {
        contact.Name = Rules.SingleLine(Rules.Required(body.Name, "name"));
        contact.Role = Line(body.Role, "role");
        contact.Email = Rules.OptionalEmail(body.Email, "email");
        contact.Phone = Line(body.Phone, "phone");
        contact.LinkedIn = Rules.OptionalUrl(body.LinkedIn, "linkedIn");
        contact.Notes = Rules.Optional(body.Notes, "notes", Rules.MaxLongText);
    }
}
```

In `src/Api/Applications/TrackerEndpoints.cs`, add after `tracker.MapTrackerLists();`:
```csharp
        tracker.MapTrackerEdits();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build src/Api -warnaserror && dotnet test tests/Api.Tests --filter "FullyQualifiedName~Api.Tests.Applications"`
Expected: 0 warnings; all tracker tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add tracker edits with optimistic concurrency, status changes, follow-ups, contacts and companies"
```

---

### Task 5: Documents and deleting entries

**Files:**
- Create: `src/Api/Applications/TrackerDocumentEndpoints.cs`
- Modify: `src/Api/Applications/TrackerEndpoints.cs` (one line)
- Test: `tests/Api.Tests/Applications/TrackerDocumentTests.cs`

**Interfaces:**
- Consumes: `Tracking`, `TrackerMirror.DocumentPath / ApplicationFolder / FilesFolder`, `Rules.CheckUpload / SafeFileName / ContentType`, `NewEntry`
- Produces:
  - `static class TrackerDocumentEndpoints { MapTrackerDocuments(this RouteGroupBuilder) }`
  - **Routes:**
    - `POST applications/{id}/documents`: multipart fields `file`, `kind` (default `Other`), `label` (default: the original file name without its extension) and `sentOn` (`yyyy-MM-dd`, default today). Answers 201 `NewEntry`.
    - `GET documents/{documentId}`: the file, with its content type and download name.
    - `DELETE documents/{documentId}`: 204.
    - `DELETE applications/{id}`: 204; removes the entry's children in the database and its files folder.

- [ ] **Step 1: Write the failing tests**

`tests/Api.Tests/Applications/TrackerDocumentTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Applications;
using Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Applications;

[Collection("tracker")]
[Trait("Category", "Integration")]
public sealed class TrackerDocumentTests(TrackerFixture tracker)
{
    private static MultipartFormDataContent Upload(byte[] bytes, string fileName, string? kind = "Cv", string? sentOn = "2027-03-01")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new("application/octet-stream");
        form.Add(file, "file", fileName);
        if (kind is not null) form.Add(new StringContent(kind), "kind");
        if (sentOn is not null) form.Add(new StringContent(sentOn), "sentOn");
        return form;
    }

    private string FolderOf(Guid entryId) => Path.Combine(tracker.DataRoot, "tracker", "files", entryId.ToString("N"));

    [Fact]
    public async Task Documents_upload_download_and_delete()
    {
        var entry = await tracker.PasteAsync("Docco");
        var bytes = "%PDF-1.4 tailored CV"u8.ToArray();

        var created = await tracker.Client.PostAsync($"/api/tracker/applications/{entry.Id}/documents", Upload(bytes, "..\\CV (RBC).pdf"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<NewEntry>(TrackerFixture.Json))!.Id;
        var doc = Assert.Single((await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{entry.Id}")).Documents);
        Assert.Equal((id, "CV (RBC)", "CV-RBC-.pdf", DocumentKind.Cv, new DateOnly(2027, 3, 1), (long)bytes.Length),
            (doc.Id, doc.Label, doc.FileName, doc.Kind, doc.SentOn, doc.SizeBytes));
        var onDisk = Path.Combine(FolderOf(entry.Id), $"{id:N}-CV-RBC-.pdf");
        Assert.True(File.Exists(onDisk));

        var download = await tracker.Client.GetAsync($"/api/tracker/documents/{id}");
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Contains("CV-RBC-.pdf", download.Content.Headers.ContentDisposition!.ToString());

        Assert.Equal(HttpStatusCode.NoContent, (await tracker.Client.DeleteAsync($"/api/tracker/documents/{id}")).StatusCode);
        Assert.False(File.Exists(onDisk));
        Assert.Equal(HttpStatusCode.NotFound, (await tracker.Client.GetAsync($"/api/tracker/documents/{id}")).StatusCode);
    }

    [Theory]
    [InlineData("tool.exe", 10, "PDF, DOCX")]
    [InlineData("CV.pdf", 0, "empty")]
    [InlineData("CV.pdf", Rules.MaxFileBytes + 1, "10 MB")]
    public async Task Unsuitable_files_are_refused_and_nothing_is_stored(string name, long size, string message)
    {
        var entry = await tracker.PasteAsync($"Refuse {name} {size}");

        var response = await tracker.Client.PostAsync($"/api/tracker/applications/{entry.Id}/documents", Upload(new byte[size], name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("file", problem.GetProperty("field").GetString());
        Assert.Contains(message, problem.GetProperty("detail").GetString());
        Assert.False(Directory.Exists(FolderOf(entry.Id)));
    }

    [Fact]
    public async Task Deleting_an_entry_removes_its_files_follow_ups_and_backup_record()
    {
        var entry = await tracker.PasteAsync("Deleteco");
        await tracker.Client.PostAsync($"/api/tracker/applications/{entry.Id}/documents", Upload("hello"u8.ToArray(), "notes.txt", kind: "Other"));
        await tracker.PostAsync<NewEntry>($"/api/tracker/applications/{entry.Id}/follow-ups", new { action = "Chase", dueOn = "2026-10-09" });
        Assert.True(Directory.Exists(FolderOf(entry.Id)));

        var deleted = await tracker.Client.DeleteAsync($"/api/tracker/applications/{entry.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.False(Directory.Exists(FolderOf(entry.Id)));
        Assert.Equal(HttpStatusCode.NotFound, (await tracker.Client.GetAsync($"/api/tracker/applications/{entry.Id}")).StatusCode);
        await using var db = tracker.OpenDb();
        Assert.False(await db.FollowUps.AnyAsync(f => f.ApplicationId == entry.Id));
        Assert.DoesNotContain(MirrorStore.Read(Path.Combine(tracker.DataRoot, "tracker", "tracker.json"))!.Applications, a => a.Id == entry.Id);
    }

    [Fact]
    public async Task Uploads_need_the_dashboard_header_and_an_existing_entry()
    {
        using var outsider = tracker.CreateClient(withHeader: false);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.PostAsync($"/api/tracker/applications/{Guid.NewGuid()}/documents", Upload([1], "CV.pdf"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await tracker.Client.PostAsync($"/api/tracker/applications/{Guid.NewGuid()}/documents", Upload([1], "CV.pdf"))).StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~TrackerDocumentTests"`
Expected: the tests FAIL, because the document routes don't exist. Uploads return 404 or 405 instead of 201 or 400; the 403 case already passes, since the gate exists.

- [ ] **Step 3: Implement**

`src/Api/Applications/TrackerDocumentEndpoints.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Api.Applications;

public static class TrackerDocumentEndpoints
{
    public static void MapTrackerDocuments(this RouteGroupBuilder tracker)
    {
        tracker.MapPost("/applications/{id:guid}/documents", async (Guid id, HttpRequest request, ApplicationsDb db, TrackerMirror mirror,
            TimeProvider clock, CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            if (!request.HasFormContentType) throw new TrackerInputException("file", "Send the document as a file upload.");
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? throw new TrackerInputException("file", "Choose a file to upload.");
            Rules.CheckUpload(file.FileName, file.Length);
            string? kindText = form["kind"];
            string? labelText = form["label"];
            string? sentOnText = form["sentOn"];
            var kind = string.IsNullOrWhiteSpace(kindText) ? DocumentKind.Other : Rules.ParseEnum<DocumentKind>(kindText, "kind");
            var sentOn = string.IsNullOrWhiteSpace(sentOnText) ? Tracking.Today(clock)
                : DateOnly.TryParseExact(sentOnText, "yyyy-MM-dd", out var parsed) ? parsed
                : throw new TrackerInputException("sentOn", "Use a date like 2027-03-01.");
            var given = Rules.SingleLine(Rules.Optional(labelText, "label"));
            var fallback = Rules.SingleLine(Path.GetFileNameWithoutExtension(file.FileName.Replace('\\', '/')));
            var document = new ApplicationDocument
            {
                Id = Guid.NewGuid(), ApplicationId = id, Kind = kind, FileName = Rules.SafeFileName(file.FileName),
                ContentType = Rules.ContentType(file.FileName), SizeBytes = file.Length, SentOn = sentOn, CreatedAt = clock.GetUtcNow(),
                Label = given.Length > 0 ? given : fallback.Length > 0 ? fallback[..Math.Min(fallback.Length, Rules.MaxText)] : "Document",
            };

            // File first, row second: a failed save removes the file, so neither is ever left without the other.
            var path = mirror.DocumentPath(document);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var target = File.Create(path)) await file.CopyToAsync(target, ct);
            try
            {
                db.Documents.Add(document);
                entry.UpdatedAt = document.CreatedAt;
                await Tracking.CommitAsync(db, mirror, "add-document", ct);
            }
            catch
            {
                File.Delete(path);
                throw;
            }
            return Results.Created($"/api/tracker/documents/{document.Id}", new NewEntry(document.Id));
        });

        tracker.MapGet("/documents/{documentId:guid}", async (Guid documentId, ApplicationsDb db, TrackerMirror mirror, CancellationToken ct) =>
        {
            var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct)
                ?? throw new TrackerNotFoundException("That document doesn't exist.");
            var path = mirror.DocumentPath(document);
            if (!File.Exists(path)) throw new TrackerNotFoundException($"The file for '{document.Label}' is missing from {mirror.FilesFolder}.");
            return Results.File(path, document.ContentType, document.FileName);
        });

        tracker.MapDelete("/documents/{documentId:guid}", async (Guid documentId, ApplicationsDb db, TrackerMirror mirror, TimeProvider clock,
            ILoggerFactory loggers, CancellationToken ct) =>
        {
            var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct)
                ?? throw new TrackerNotFoundException("That document doesn't exist.");
            var entry = await Tracking.FindEntryAsync(db, document.ApplicationId, ct);
            db.Documents.Remove(document);
            entry.UpdatedAt = clock.GetUtcNow();
            await Tracking.CommitAsync(db, mirror, "delete-document", ct);
            Remove(mirror.DocumentPath(document), loggers);
            return Results.NoContent();
        });

        tracker.MapDelete("/applications/{id:guid}", async (Guid id, ApplicationsDb db, TrackerMirror mirror, ILoggerFactory loggers,
            CancellationToken ct) =>
        {
            var entry = await Tracking.FindEntryAsync(db, id, ct);
            db.Applications.Remove(entry); // its timeline, follow-ups, contacts and documents cascade in the database
            await Tracking.CommitAsync(db, mirror, "delete-application", ct);
            Remove(mirror.ApplicationFolder(id), loggers);
            return Results.NoContent();
        });
    }

    // The database change is already committed; a file that can't be removed is logged, not reported as a failure.
    private static void Remove(string path, ILoggerFactory loggers)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            loggers.CreateLogger("Tracker").LogWarning(ex, "Could not remove {Path}", path);
        }
    }
}
```

In `src/Api/Applications/TrackerEndpoints.cs`, add after `tracker.MapTrackerEdits();`:
```csharp
        tracker.MapTrackerDocuments();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build src/Api -warnaserror && dotnet test tests/Api.Tests`
Expected: 0 warnings; all Api.Tests PASS.
- If the 0-byte case comes back as "Choose a file to upload." (ASP.NET dropped the empty part), that is the server refusing an empty file too. Change that InlineData's expected text to "Choose a file" and ledger it.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add tracker document uploads, downloads and entry deletion"
```

---

### Task 6: Dashboard tracker client and test helpers

**Files:**
- Modify: `src/Dashboard/src/api.ts` (`ApiError.details`, exported `request<T>`), `src/Dashboard/src/test/render.tsx` (method-aware `mockApi`, `reply`, `sentBody`, probe outside the route)
- Create: `src/Dashboard/src/tracker/api.ts`
- Test: `src/Dashboard/src/tracker/api.test.ts`

**Interfaces:**
- Consumes: the Task 3–5 JSON shapes (camelCase, enum names)
- Produces:
  - **`api.ts`:**
    - `class ApiError(message, status, details = {})` with `details: Record<string, unknown>`;
    - `interface RequestOptions { method?; headers?: Record<string,string>; body?: BodyInit }`;
    - `request<T>(path, options?) : Promise<T>` (204 resolves `undefined`).
  - **`tracker/api.ts`:**
    - Types: `AppStatus`, `STATUSES`, `WorkModel`, `WORK_MODELS`, `DocumentKind`, `DOCUMENT_KINDS`, `TrackerSort`, `TrackerStatus`, `NextAction`, `ApplicationRow`, `UpNextItem`, `TrackerIndex`, `CompanyOption`, `CompanyView`, `EventView`, `FollowUpView`, `ContactView`, `DocumentView`, `MarketFacts`, `ApplicationDetail`, `NewEntry`.
    - Inputs: `PasteInput`, `DetailsInput`, `CompanyInput`, `StatusInput`, `FollowUpInput`, `ContactInput`, `ListParams`.
    - Constant: `CLIENT_HEADER`.
    - Client object `tracker`:
      - reads: `status`, `list`, `upNext`, `index`, `companies`, `detail`, `documentUrl`;
      - entries: `shortlist`, `paste`, `setStatus`, `editDetails`, `editCompany`, `remove`;
      - follow-ups: `addFollowUp`, `editFollowUp`, `deleteFollowUp`;
      - contacts: `addContact`, `deleteContact`;
      - documents: `upload`, `deleteDocument`.
  - **`test/render.tsx`:**
    - `reply(status, body?)` and `failure(status, detail, extra = {})`;
    - `mockApi(routes)`: keys are either a GET prefix or `"METHOD /prefix"`, and a function value receives the request init; returns the calls, with GET as the URL and other methods as `"METHOD URL"`;
    - `sentBody(prefix) : unknown`;
    - `renderRoute` now renders `LocationProbe` outside the route, so navigating away keeps it.

- [ ] **Step 1: Write the failing test**

`src/Dashboard/src/tracker/api.test.ts`:
```ts
import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api';
import { failure, mockApi, reply, sentBody } from '../test/render';
import { tracker } from './api';

describe('tracker client', () => {
  it('sends writes as JSON with the dashboard header', async () => {
    mockApi({ 'POST /api/tracker/applications': reply(201, { id: 'a1' }) });

    await expect(tracker.shortlist('greenhouse:101')).resolves.toEqual({ id: 'a1' });

    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect(init?.headers).toMatchObject({ 'X-Tracker-Client': 'dashboard', 'Content-Type': 'application/json' });
    expect(sentBody('POST /api/tracker/applications')).toEqual({ postingId: 'greenhouse:101' });
  });

  it('builds list queries from the non-empty parameters', async () => {
    const calls = mockApi({ '/api/tracker/applications': [] });

    await tracker.list({ status: 'Applied', q: '', sort: 'company' });

    expect(calls).toEqual(['/api/tracker/applications?status=Applied&sort=company']);
  });

  it('resolves 204 answers with nothing', async () => {
    mockApi({ 'PATCH /api/tracker/applications/a1': reply(204) });

    await expect(tracker.editFollowUp('a1', 'f1', { action: 'Call', dueOn: '2027-03-01', doneOn: null })).resolves.toBeUndefined();
  });

  it('keeps the problem details of a refusal', async () => {
    mockApi({ 'POST /api/tracker/applications': failure(409, 'This posting is already in your tracker.', { id: 'a7' }) });

    const error = await tracker.shortlist('ashby:S1').catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(409);
    expect((error as ApiError).details.id).toBe('a7');
  });

  it('uploads documents as form data without a JSON content type', async () => {
    mockApi({ 'POST /api/tracker/applications/a1/documents': reply(201, { id: 'd1' }) });
    const form = new FormData();
    form.append('file', new Blob(['%PDF']), 'CV.pdf');

    await tracker.upload('a1', form);

    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect(init?.body).toBeInstanceOf(FormData);
    expect(init?.headers).toEqual({ Accept: 'application/json', 'X-Tracker-Client': 'dashboard' });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/Dashboard && npx vitest run src/tracker`
Expected: FAIL. `./api` doesn't exist in `src/tracker`, and `reply` and `sentBody` aren't exported yet.

- [ ] **Step 3: Implement**

In `src/Dashboard/src/api.ts`, replace everything from `export class ApiError` down to the line before `export const api = {` with:
```ts
export class ApiError extends Error {
  readonly status: number;
  // The problem-details body (field, id, …) when the API sent one.
  readonly details: Record<string, unknown>;

  constructor(message: string, status: number, details: Record<string, unknown> = {}) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.details = details;
  }
}

export interface RequestOptions {
  method?: string;
  headers?: Record<string, string>;
  body?: BodyInit;
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  let response: Response;
  try {
    response = await fetch(path, { method: options.method, headers: { Accept: 'application/json', ...options.headers }, body: options.body });
  } catch {
    throw new ApiError('The API is not reachable. Is the dashboard (scripts/dashboard.cmd) running?', 0);
  }
  if (!response.ok) {
    let message = `The API answered ${response.status}.`;
    let details: Record<string, unknown> = {};
    try {
      details = (await response.json()) as Record<string, unknown>;
      const text = typeof details.detail === 'string' ? details.detail : typeof details.title === 'string' ? details.title : '';
      if (text) message = text;
    } catch {
      // Not a problem-details body; keep the status message.
    }
    throw new ApiError(message, response.status, details);
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

function get<T>(path: string): Promise<T> {
  return request<T>(path);
}

```

`src/Dashboard/src/tracker/api.ts`:
```ts
import { request } from '../api';

export type AppStatus = 'Shortlisted' | 'Applied' | 'Screening' | 'Interviewing' | 'Offer' | 'Accepted' | 'Rejected' | 'Withdrawn';
export const STATUSES: AppStatus[] = ['Shortlisted', 'Applied', 'Screening', 'Interviewing', 'Offer', 'Accepted', 'Rejected', 'Withdrawn'];
export type WorkModel = 'Unknown' | 'Remote' | 'Hybrid' | 'Onsite';
export const WORK_MODELS: WorkModel[] = ['Unknown', 'Remote', 'Hybrid', 'Onsite'];
export type DocumentKind = 'Cv' | 'CoverLetter' | 'Other';
export const DOCUMENT_KINDS: { kind: DocumentKind; label: string }[] = [
  { kind: 'Cv', label: 'CV' },
  { kind: 'CoverLetter', label: 'Cover letter' },
  { kind: 'Other', label: 'Other' },
];
export type TrackerSort = 'next' | 'updated' | 'company';

export interface TrackerStatus {
  migrated: boolean;
  lastBackupAt: string | null;
  lastBackupError: string | null;
  counts: Partial<Record<AppStatus, number>>;
  overdue: number;
}
export interface NextAction { id: string; action: string; dueOn: string }
export interface ApplicationRow {
  id: string;
  title: string;
  company: string;
  status: AppStatus;
  next: NextAction | null;
  updatedAt: string;
  postingId: string | null;
  live: boolean;
}
export interface UpNextItem {
  followUpId: string;
  applicationId: string;
  title: string;
  company: string;
  action: string;
  dueOn: string;
  overdue: boolean;
}
export type TrackerIndex = Record<string, { id: string; status: AppStatus }>;
export interface CompanyOption { id: string; name: string }
export interface CompanyView {
  id: string;
  name: string;
  website: string;
  officeLocation: string;
  companySize: string;
  annualLeave: string;
  benefits: string;
  notes: string;
  version: number;
  applications: number;
}
export interface EventView { id: string; onDate: string; fromStatus: AppStatus | null; toStatus: AppStatus; note: string }
export interface FollowUpView { id: string; action: string; dueOn: string; doneOn: string | null }
export interface ContactView { id: string; name: string; role: string; email: string; phone: string; linkedIn: string; notes: string }
export interface DocumentView { id: string; kind: DocumentKind; label: string; fileName: string; sizeBytes: number; sentOn: string }
export interface MarketFacts { match: number; live: boolean; firstSeen: string; lastSeen: string; skills: string[] }
export interface ApplicationDetail {
  id: string;
  title: string;
  url: string;
  location: string;
  status: AppStatus;
  origin: 'Market' | 'Pasted';
  foundVia: string;
  workModel: WorkModel;
  officeDays: string;
  teamSize: string;
  workingHours: string;
  salaryPosted: string;
  salaryAsked: string;
  salaryOffered: string;
  notes: string;
  postingId: string | null;
  postingText: string;
  market: MarketFacts | null;
  company: CompanyView;
  events: EventView[];
  followUps: FollowUpView[];
  contacts: ContactView[];
  documents: DocumentView[];
  createdAt: string;
  updatedAt: string;
  version: number;
}
export interface NewEntry { id: string }

export interface PasteInput { title: string; company: string; url: string; location: string; postingText: string; status: AppStatus }
export interface DetailsInput {
  version: number;
  title: string;
  url: string;
  location: string;
  foundVia: string;
  workModel: WorkModel;
  officeDays: string;
  teamSize: string;
  workingHours: string;
  salaryPosted: string;
  salaryAsked: string;
  salaryOffered: string;
  notes: string;
}
export interface CompanyInput {
  version: number;
  name: string;
  website: string;
  officeLocation: string;
  companySize: string;
  annualLeave: string;
  benefits: string;
  notes: string;
}
export interface StatusInput { status: AppStatus; on: string | null; note: string }
export interface FollowUpInput { action: string; dueOn: string; doneOn: string | null }
export interface ContactInput { name: string; role: string; email: string; phone: string; linkedIn: string; notes: string }
export interface ListParams { status?: string; q?: string; sort?: TrackerSort }

// Marks a write as coming from the dashboard. The API refuses tracker writes without it, and another website can't add
// it to a request from your browser.
export const CLIENT_HEADER = { 'X-Tracker-Client': 'dashboard' };

function send<T>(method: string, path: string, body?: unknown): Promise<T> {
  if (body instanceof FormData) return request<T>(path, { method, headers: CLIENT_HEADER, body });
  return request<T>(path, {
    method,
    headers: body === undefined ? CLIENT_HEADER : { ...CLIENT_HEADER, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
}

function query(params: ListParams): string {
  const search = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value) search.set(key, String(value));
  });
  const text = search.toString();
  return text ? `?${text}` : '';
}

const BASE = '/api/tracker';
const entry = (id: string) => `${BASE}/applications/${encodeURIComponent(id)}`;

export const tracker = {
  status: () => request<TrackerStatus>(`${BASE}/status`),
  list: (params: ListParams) => request<ApplicationRow[]>(`${BASE}/applications${query(params)}`),
  upNext: () => request<UpNextItem[]>(`${BASE}/up-next`),
  index: () => request<TrackerIndex>(`${BASE}/index`),
  companies: () => request<CompanyOption[]>(`${BASE}/companies`),
  detail: (id: string) => request<ApplicationDetail>(entry(id)),
  shortlist: (postingId: string) => send<NewEntry>('POST', `${BASE}/applications`, { postingId }),
  paste: (input: PasteInput) => send<NewEntry>('POST', `${BASE}/applications/paste`, input),
  setStatus: (id: string, input: StatusInput) => send<void>('POST', `${entry(id)}/status`, input),
  editDetails: (id: string, input: DetailsInput) => send<void>('PATCH', entry(id), input),
  editCompany: (companyId: string, input: CompanyInput) => send<void>('PATCH', `${BASE}/companies/${encodeURIComponent(companyId)}`, input),
  addFollowUp: (id: string, input: FollowUpInput) => send<NewEntry>('POST', `${entry(id)}/follow-ups`, input),
  editFollowUp: (id: string, followUpId: string, input: FollowUpInput) => send<void>('PATCH', `${entry(id)}/follow-ups/${followUpId}`, input),
  deleteFollowUp: (id: string, followUpId: string) => send<void>('DELETE', `${entry(id)}/follow-ups/${followUpId}`),
  addContact: (id: string, input: ContactInput) => send<NewEntry>('POST', `${entry(id)}/contacts`, input),
  deleteContact: (id: string, contactId: string) => send<void>('DELETE', `${entry(id)}/contacts/${contactId}`),
  upload: (id: string, form: FormData) => send<NewEntry>('POST', `${entry(id)}/documents`, form),
  documentUrl: (documentId: string) => `${BASE}/documents/${documentId}`,
  deleteDocument: (documentId: string) => send<void>('DELETE', `${BASE}/documents/${documentId}`),
  remove: (id: string) => send<void>('DELETE', entry(id)),
};
```

In `src/Dashboard/src/test/render.tsx`:

1. Change `renderRoute` so the probe sits outside the route and the query client is returned:
```tsx
// Renders `element` at `url` inside a router and a fresh query client (no retries), plus a probe showing the current URL
// (outside the route, so it stays visible after navigating away). Returns the query client too.
export function renderRoute(url: string, element: ReactElement, path = '*') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const view = render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[url]}>
        <Routes>
          <Route path={path} element={element} />
        </Routes>
        <LocationProbe />
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return { ...view, client };
}
```

2. Replace everything from `const FAILURE = Symbol('failure');` to the end of the file with:
```tsx
const REPLY = Symbol('reply');

// A canned answer with a status code (and body, if any).
export function reply(status: number, body?: unknown) {
  return { [REPLY]: true, status, body };
}

// A problem-details error, with extra members such as field or id.
export function failure(status: number, detail: string, extra: Record<string, unknown> = {}) {
  return reply(status, { title: 'Error', status, detail, ...extra });
}

// Replaces fetch.
// - A route key is a path prefix for GET ("/api/meta") or "METHOD /prefix" for other methods
//   ("POST /api/tracker/applications"). The longest matching key wins.
// - A function value is called with the request init.
// Returns the requests made: GET as the URL, others as "METHOD URL".
export function mockApi(routes: Record<string, unknown>): string[] {
  const calls: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    const method = (init?.method ?? 'GET').toUpperCase();
    const id = method === 'GET' ? url : `${method} ${url}`;
    calls.push(id);
    const key = Object.keys(routes).filter((k) => id.startsWith(k)).sort((a, b) => b.length - a.length)[0];
    const route = key === undefined ? failure(500, `No mock for ${id}`) : routes[key];
    const value = typeof route === 'function' ? (route as (init?: RequestInit) => unknown)(init) : route;
    const canned = typeof value === 'object' && value !== null && REPLY in value;
    const { status, body } = canned ? (value as unknown as { status: number; body: unknown }) : { status: 200, body: value };
    if (status === 204) return new Response(null, { status });
    return new Response(JSON.stringify(body ?? null), { status, headers: { 'Content-Type': 'application/json' } });
  }));
  return calls;
}

// The body sent with the latest request whose "METHOD URL" (or GET URL) starts with `prefix`; JSON is parsed.
export function sentBody(prefix: string): unknown {
  const calls = vi.mocked(fetch).mock.calls;
  for (let i = calls.length - 1; i >= 0; i--) {
    const [input, init] = calls[i];
    const method = (init?.method ?? 'GET').toUpperCase();
    const id = method === 'GET' ? String(input) : `${method} ${String(input)}`;
    if (id.startsWith(prefix)) return typeof init?.body === 'string' ? JSON.parse(init.body) : init?.body;
  }
  return undefined;
}
```

- [ ] **Step 4: Run tests and type check**

Run: `cd src/Dashboard && npm test && npm run typecheck`
Expected: all files PASS (the 43 existing tests plus 5 new); `tsc` is clean.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add the dashboard tracker client and method-aware test helpers"
```

---

### Task 7: Tracker page, Up next and the nav badge

**Files:**
- Create: `src/Dashboard/src/tracker/dates.ts`, `src/Dashboard/src/tracker/StatusChip.tsx`, `src/Dashboard/src/tracker/UpNext.tsx`, `src/Dashboard/src/tracker/TrackerPage.tsx`, `src/Dashboard/src/tracker/useTrackerMutation.ts`
- Modify: `src/Dashboard/src/App.tsx` (replace the whole file), `src/Dashboard/src/theme.css` (append)
- Test: `src/Dashboard/src/tracker/dates.test.ts`, `src/Dashboard/src/tracker/TrackerPage.test.tsx`, `src/Dashboard/src/tracker/UpNext.test.tsx`, `src/Dashboard/src/App.test.tsx` (one new test)

**Interfaces:**
- Consumes: `tracker`, the tracker types (Task 6); `Empty`, `ErrorBanner`, `Loading` (phase 2a); `dateText` (phase 2a `format.ts`)
- Produces:
  - `localToday(now?) : string` (`yyyy-MM-dd`) and `dayText(iso, today?) : string` ('Today', or a short date);
  - `StatusChip({ status })`, rendering `<span class="status status-<Status>">`;
  - `useTrackerMutation(write, onDone?)`: runs `onDone` first, then invalidates `['tracker']`;
  - `UpNext()` and `TrackerPage()` (route `/tracker`, with URL params `status`, `q`, `sort`);
  - `App`, which now has the Tracker link with an overdue badge (accessible name "Tracker, N overdue"), hides `FilterBar` on `/tracker*`, and gives tracker routes their own `<Routes>`.

- [ ] **Step 1: Write the failing tests**

`src/Dashboard/src/tracker/dates.test.ts`:
```ts
import { describe, expect, it } from 'vitest';
import { dayText, localToday } from './dates';

describe('tracker dates', () => {
  it('writes the local calendar date', () => {
    expect(localToday(new Date(2027, 2, 1, 23, 30))).toBe('2027-03-01');
  });

  it('says Today for today and adds the year only when it differs', () => {
    expect(dayText('2027-03-01', '2027-03-01')).toBe('Today');
    expect(dayText('2026-12-24', '2027-01-02')).toContain('2026');
    expect(dayText('2027-03-08', '2027-03-01')).not.toContain('2027');
  });
});
```

`src/Dashboard/src/tracker/TrackerPage.test.tsx`:
```tsx
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { mockApi, renderRoute } from '../test/render';
import type { ApplicationRow, TrackerStatus } from './api';
import { TrackerPage } from './TrackerPage';

const STATUS: TrackerStatus = { migrated: true, lastBackupAt: '2027-03-01T10:00:00Z', lastBackupError: null, counts: { Applied: 1, Interviewing: 1 }, overdue: 1 };
const ROWS: ApplicationRow[] = [
  { id: 'a1', title: 'Senior .NET Developer', company: 'RBC', status: 'Interviewing', next: { id: 'f1', action: 'Prep panel', dueOn: '2027-03-05' },
    updatedAt: '2027-03-01T10:00:00Z', postingId: 'greenhouse:101', live: true },
  { id: 'a2', title: 'Tech Lead', company: 'Shopify', status: 'Applied', next: null, updatedAt: '2027-02-27T10:00:00Z', postingId: null, live: false },
];
const routes = (extra: Record<string, unknown> = {}) => ({
  '/api/tracker/status': STATUS, '/api/tracker/up-next': [], '/api/tracker/applications': ROWS, ...extra,
});

describe('Tracker page', () => {
  it('lists entries with their status, next action and a link to each', async () => {
    mockApi(routes());
    renderRoute('/tracker', <TrackerPage />);

    const link = await screen.findByRole('link', { name: 'Senior .NET Developer' });

    expect(link).toHaveAttribute('href', '/tracker/a1');
    const row = link.closest('tr') as HTMLElement;
    expect(within(row).getByText('Interviewing')).toBeInTheDocument();
    expect(within(row).getByText(/Prep panel/)).toBeInTheDocument();
    expect(within(row).getByText(/Live/)).toBeInTheDocument();
    expect(screen.getByText('No posting')).toBeInTheDocument();
  });

  it('keeps status, search and sort in the URL and asks the API for them', async () => {
    const calls = mockApi(routes());
    renderRoute('/tracker', <TrackerPage />);
    await screen.findByRole('link', { name: 'Tech Lead' });

    fireEvent.change(screen.getByLabelText('Status'), { target: { value: 'Applied' } });
    fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'company' } });
    fireEvent.change(screen.getByLabelText('Role or company'), { target: { value: 'shop' } });
    fireEvent.click(screen.getByRole('button', { name: 'Search' }));

    await waitFor(() => expect(calls).toContain('/api/tracker/applications?status=Applied&q=shop&sort=company'));
    expect(screen.getByTestId('location').textContent).toBe('/tracker?status=Applied&sort=company&q=shop');
  });

  it('shows the empty state and a failed backup', async () => {
    mockApi(routes({
      '/api/tracker/applications': [],
      '/api/tracker/status': { ...STATUS, counts: {}, lastBackupError: 'The backup copy in data/tracker/tracker.json could not be updated: disk full' },
    }));
    renderRoute('/tracker', <TrackerPage />);

    expect(await screen.findByText(/Nothing tracked yet/)).toBeInTheDocument();
    expect(await screen.findByText(/could not be updated: disk full/)).toBeInTheDocument();
  });
});
```

`src/Dashboard/src/tracker/UpNext.test.tsx`:
```tsx
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { mockApi, renderRoute, reply, sentBody } from '../test/render';
import { localToday } from './dates';
import { UpNext } from './UpNext';

describe('Up next', () => {
  it("marks a follow-up done with today's date", async () => {
    const calls = mockApi({
      '/api/tracker/up-next': [{ followUpId: 'f1', applicationId: 'a1', title: 'Senior .NET Developer', company: 'RBC', action: 'Email Sam', dueOn: '2027-03-01', overdue: true }],
      'PATCH /api/tracker/applications/a1/follow-ups/f1': reply(204),
    });
    renderRoute('/tracker', <UpNext />);

    fireEvent.click(await screen.findByRole('button', { name: 'Mark "Email Sam" done' }));

    await waitFor(() => expect(calls).toContain('PATCH /api/tracker/applications/a1/follow-ups/f1'));
    expect(sentBody('PATCH /api/tracker/applications/a1/follow-ups/f1')).toEqual({ action: 'Email Sam', dueOn: '2027-03-01', doneOn: localToday() });
    expect(screen.getByText(/Overdue/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Senior .NET Developer · RBC' })).toHaveAttribute('href', '/tracker/a1');
  });

  it('says when nothing is due', async () => {
    mockApi({ '/api/tracker/up-next': [] });
    renderRoute('/tracker', <UpNext />);

    expect(await screen.findByText('Nothing due this week.')).toBeInTheDocument();
  });
});
```

Add to `src/Dashboard/src/App.test.tsx`, inside the `describe('App', …)` block:
```tsx
  it('shows the overdue count on the Tracker link and hides the market filters there', async () => {
    mockApi({
      '/api/meta': META,
      '/api/tracker/status': { migrated: true, lastBackupAt: null, lastBackupError: null, counts: {}, overdue: 2 },
      '/api/tracker/up-next': [],
      '/api/tracker/applications': [],
    });
    renderRoute('/tracker', <App />);

    expect(await screen.findByRole('link', { name: 'Tracker, 2 overdue' })).toHaveAttribute('href', '/tracker');
    expect(await screen.findByText(/Nothing tracked yet/)).toBeInTheDocument();
    expect(screen.queryByText('Roles')).not.toBeInTheDocument();
  });
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd src/Dashboard && npm test`
Expected: the new files FAIL to import `./dates`, `./TrackerPage` and `./UpNext`, and the App test finds no Tracker link.

- [ ] **Step 3: Implement**

`src/Dashboard/src/tracker/dates.ts`:
```ts
// The API stores dates as yyyy-MM-dd in this PC's calendar.
export function localToday(now: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

export function dayText(iso: string, today: string = localToday()): string {
  if (iso === today) return 'Today';
  const [year, month, day] = iso.split('-').map(Number);
  const sameYear = iso.slice(0, 4) === today.slice(0, 4);
  return new Date(year, month - 1, day).toLocaleDateString('en-CA',
    sameYear ? { month: 'short', day: 'numeric' } : { month: 'short', day: 'numeric', year: 'numeric' });
}
```

`src/Dashboard/src/tracker/StatusChip.tsx`:
```tsx
import type { AppStatus } from './api';

export function StatusChip({ status }: { status: AppStatus }) {
  return <span className={`status status-${status}`}>{status}</span>;
}
```

`src/Dashboard/src/tracker/useTrackerMutation.ts`:
```ts
import { useMutation, useQueryClient } from '@tanstack/react-query';

// A tracker write. On success it runs onDone (reset a form, navigate away) and then refreshes every tracker query.
export function useTrackerMutation<TInput, TResult = unknown>(write: (input: TInput) => Promise<TResult>, onDone?: (result: TResult) => void) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: write,
    onSuccess: async (result) => {
      onDone?.(result);
      await queryClient.invalidateQueries({ queryKey: ['tracker'] });
    },
  });
}
```

`src/Dashboard/src/tracker/UpNext.tsx`:
```tsx
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';
import { ErrorBanner } from '../components/States';
import { tracker, type UpNextItem } from './api';
import { dayText, localToday } from './dates';
import { useTrackerMutation } from './useTrackerMutation';

export function UpNext() {
  const items = useQuery({ queryKey: ['tracker', 'up-next'], queryFn: tracker.upNext });
  const done = useTrackerMutation((item: UpNextItem) =>
    tracker.editFollowUp(item.applicationId, item.followUpId, { action: item.action, dueOn: item.dueOn, doneOn: localToday() }));

  if (items.error) return <ErrorBanner error={items.error} />;
  if (!items.data) return null;
  return (
    <section className="upnext" aria-labelledby="h-upnext">
      <h3 id="h-upnext">Up next</h3>
      {items.data.length === 0 ? (
        <p className="empty">Nothing due this week.</p>
      ) : (
        <ul>
          {items.data.map((item) => (
            <li key={item.followUpId} className={item.overdue ? 'is-overdue' : undefined}>
              <span className="when">{item.overdue ? `Overdue · ${dayText(item.dueOn)}` : dayText(item.dueOn)}</span>
              <span className="what">{item.action}</span>
              <Link to={`/tracker/${item.applicationId}`}>{item.title} · {item.company}</Link>
              <button type="button" onClick={() => done.mutate(item)} disabled={done.isPending} aria-label={`Mark "${item.action}" done`}>Done</button>
            </li>
          ))}
        </ul>
      )}
      {done.error && <ErrorBanner error={done.error} />}
    </section>
  );
}
```

`src/Dashboard/src/tracker/TrackerPage.tsx`:
```tsx
import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { Empty, ErrorBanner, Loading } from '../components/States';
import { dateText } from '../format';
import { STATUSES, tracker, type TrackerSort } from './api';
import { dayText, localToday } from './dates';
import { StatusChip } from './StatusChip';
import { UpNext } from './UpNext';

const SORTS: Record<TrackerSort, string> = { next: 'Next action first', updated: 'Recently updated', company: 'Company' };

export function TrackerPage() {
  const [params, setParams] = useSearchParams();
  const status = STATUSES.find((s) => s === params.get('status')) ?? '';
  const q = params.get('q') ?? '';
  const sort = (Object.keys(SORTS) as TrackerSort[]).find((s) => s === params.get('sort')) ?? 'next';
  const [draft, setDraft] = useState(q);
  useEffect(() => setDraft(q), [q]);
  const summary = useQuery({ queryKey: ['tracker', 'status'], queryFn: tracker.status });
  const rows = useQuery({ queryKey: ['tracker', 'list', status, q, sort], queryFn: () => tracker.list({ status, q, sort }) });

  const set = (patch: Record<string, string>) =>
    setParams((current) => {
      const out = new URLSearchParams(current);
      Object.entries(patch).forEach(([key, value]) => (value ? out.set(key, value) : out.delete(key)));
      return out;
    });

  const counts = summary.data?.counts ?? {};
  const today = localToday();

  return (
    <section className="section" aria-labelledby="h-tracker">
      <div className="tracker-head">
        <h2 id="h-tracker">Tracker</h2>
        <Link className="button" to="/tracker/new">Paste a job</Link>
      </div>
      {summary.data?.lastBackupError && (
        <div className="banner warn" role="status">
          {summary.data.lastBackupError} Your changes are saved in the database; the copy updates again with your next change.
        </div>
      )}
      <ul className="pills" aria-label="Entries by status">
        {STATUSES.filter((s) => counts[s]).map((s) => (
          <li key={s}>
            <button type="button" aria-pressed={status === s} onClick={() => set({ status: status === s ? '' : s })}>
              {s}<span className="count">{counts[s]}</span>
            </button>
          </li>
        ))}
      </ul>
      <UpNext />
      <form className="toolbar" role="search" onSubmit={(e) => { e.preventDefault(); set({ q: draft.trim() }); }}>
        <div className="field">
          <label className="field-label" htmlFor="tracker-q">Role or company</label>
          <input id="tracker-q" type="search" value={draft} onChange={(e) => setDraft(e.target.value)} />
        </div>
        <button type="submit">Search</button>
        <div className="field">
          <label className="field-label" htmlFor="tracker-status">Status</label>
          <select id="tracker-status" value={status} onChange={(e) => set({ status: e.target.value })}>
            <option value="">All statuses</option>
            {STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>
        <div className="field">
          <label className="field-label" htmlFor="tracker-sort">Sort</label>
          <select id="tracker-sort" value={sort} onChange={(e) => set({ sort: e.target.value === 'next' ? '' : e.target.value })}>
            {(Object.keys(SORTS) as TrackerSort[]).map((s) => <option key={s} value={s}>{SORTS[s]}</option>)}
          </select>
        </div>
      </form>
      {rows.error && <ErrorBanner error={rows.error} />}
      {!rows.data ? (
        !rows.error && <Loading what="your tracker" />
      ) : rows.data.length === 0 ? (
        <Empty>{status || q ? 'Nothing matches these filters.' : 'Nothing tracked yet. Shortlist postings from the Postings page, or paste a job.'}</Empty>
      ) : (
        <div className="table-wrap">
          <table>
            <caption className="sr-only">Tracked postings and applications</caption>
            <thead>
              <tr><th>Role</th><th>Company</th><th>Status</th><th>Next action</th><th className="num">Updated</th><th>Posting</th></tr>
            </thead>
            <tbody>
              {rows.data.map((r) => (
                <tr key={r.id}>
                  <td><Link to={`/tracker/${r.id}`}>{r.title}</Link></td>
                  <td>{r.company}</td>
                  <td><StatusChip status={r.status} /></td>
                  <td>
                    {r.next
                      ? <span className={r.next.dueOn < today ? 'overdue' : undefined}>{r.next.action} · {dayText(r.next.dueOn)}</span>
                      : <span className="muted">—</span>}
                  </td>
                  <td className="num">{dateText(r.updatedAt)}</td>
                  <td>
                    {r.postingId === null ? <span className="muted">No posting</span>
                      : r.live ? <span className="live">● Live</span>
                      : <span className="muted">○ Not in latest run</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
```

`src/Dashboard/src/App.tsx` (replace the whole file):
```tsx
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { NavLink, Route, Routes, useLocation } from 'react-router';
import { api } from './api';
import { FilterBar } from './components/FilterBar';
import { Empty, ErrorBanner, StaleBanner } from './components/States';
import { linkSearch, useFilters } from './filters';
import { dateText } from './format';
import { Overview } from './pages/Overview';
import { Postings } from './pages/Postings';
import { Salaries } from './pages/Salaries';
import { SkillDetail } from './pages/SkillDetail';
import { Skills } from './pages/Skills';
import { tracker } from './tracker/api';
import { TrackerPage } from './tracker/TrackerPage';

const PAGES = [
  { to: '/', label: 'Overview' },
  { to: '/skills', label: 'Skills' },
  { to: '/salaries', label: 'Salaries' },
  { to: '/postings', label: 'Postings' },
];

const NO_PAGE = <Empty>There's no page here. Pick one from the menu above.</Empty>;

export function App() {
  const [filters] = useFilters();
  const location = useLocation();
  const onTracker = location.pathname.startsWith('/tracker');
  const meta = useQuery({ queryKey: ['meta'], queryFn: api.meta, refetchInterval: 60_000 });
  const trackerStatus = useQuery({ queryKey: ['tracker', 'status'], queryFn: tracker.status, refetchInterval: 60_000 });
  const overdue = trackerStatus.data?.overdue ?? 0;
  const queryClient = useQueryClient();
  const runId = meta.data?.latestRunId ?? null;
  const shownRun = useRef<string | null>(null);

  // A new collection landed: refetch every page's data so the numbers match the run in the header.
  useEffect(() => {
    if (shownRun.current !== null && runId !== null && runId !== shownRun.current)
      void queryClient.invalidateQueries({ predicate: (q) => q.queryKey[0] !== 'meta' });
    if (runId !== null) shownRun.current = runId;
  }, [runId, queryClient]);
  const search = linkSearch(filters);

  return (
    <div className="shell">
      <a className="skip" href="#content">Skip to content</a>
      <header className="masthead">
        <h1>Toronto job market</h1>
        {meta.data?.latestRunId && (
          <span className="run-line">
            Run {meta.data.latestRunId} · {dateText(meta.data.latestRunAt)} · {meta.data.runs} runs over {meta.data.historyDays} days
          </span>
        )}
      </header>
      <nav className="nav" aria-label="Pages">
        {PAGES.map((p) => (
          <NavLink key={p.to} to={{ pathname: p.to, search }} end={p.to === '/'}>{p.label}</NavLink>
        ))}
        <NavLink to="/tracker">
          Tracker
          {overdue > 0 && (
            <span className="badge"><span className="sr-only">, </span>{overdue}<span className="sr-only"> overdue</span></span>
          )}
        </NavLink>
      </nav>
      {meta.data && !onTracker && <FilterBar meta={meta.data} />}
      <main id="content">
        {onTracker ? (
          <Routes>
            <Route path="/tracker" element={<TrackerPage />} />
            <Route path="*" element={NO_PAGE} />
          </Routes>
        ) : (
          <>
            {meta.error && <ErrorBanner error={meta.error} />}
            {meta.data?.stale && <StaleBanner />}
            {meta.data && meta.data.runs === 0 && <Empty>No data yet: the first collection runs Tuesday or Friday at 07:00.</Empty>}
            {meta.data && meta.data.runs > 0 && (
              <Routes>
                <Route path="/" element={<Overview />} />
                <Route path="/skills" element={<Skills />} />
                <Route path="/skills/:name" element={<SkillDetail />} />
                <Route path="/salaries" element={<Salaries />} />
                <Route path="/postings" element={<Postings />} />
                <Route path="*" element={NO_PAGE} />
              </Routes>
            )}
          </>
        )}
      </main>
      <footer className="muted">
        Jobs data partly by <a href="https://www.adzuna.ca" target="_blank" rel="noreferrer">Adzuna</a>. Other postings come from company job boards, the Job Bank and saved pastes.
      </footer>
    </div>
  );
}
```

Append to `src/Dashboard/src/theme.css`:
```css
.badge { display: inline-block; min-width: 1.5em; margin-left: 6px; padding: 0 5px; border-radius: 999px; background: var(--accent); color: var(--surface); font-size: .72rem; text-align: center; letter-spacing: 0; }
.tracker-head { display: flex; flex-wrap: wrap; gap: 8px 16px; align-items: baseline; justify-content: space-between; }
.button, button.primary { display: inline-block; font-size: .95rem; padding: 6px 12px; border: 1px solid var(--ink); border-radius: 4px; background: var(--ink); color: var(--bg); text-decoration: none; font-weight: 600; }
button.danger { background: var(--accent); border-color: var(--accent); color: var(--surface); font-weight: 600; }
.small-button { font-size: .8rem; padding: 2px 8px; white-space: nowrap; }
.status { font-family: var(--mono); font-size: .72rem; letter-spacing: .04em; text-transform: uppercase; padding: 2px 6px; border-radius: 3px; white-space: nowrap; background: var(--info-soft); color: var(--info); text-decoration: none; }
.status-Offer, .status-Accepted { background: var(--good-soft); color: var(--good); }
.status-Screening, .status-Interviewing { background: var(--warn-soft); color: var(--warn); }
.status-Rejected, .status-Withdrawn { background: var(--bar-track); color: var(--muted); }
.upnext { display: grid; gap: 8px; padding: 12px 14px; background: var(--surface); border: 1px solid var(--rule); }
.upnext h3 { font-size: 1.05rem; }
.upnext ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; }
.upnext li { display: flex; flex-wrap: wrap; gap: 4px 14px; align-items: baseline; }
.upnext .when { font-family: var(--mono); font-size: .8rem; color: var(--muted); min-width: 7.5em; }
.upnext .is-overdue .when, .overdue { color: var(--accent); }
.live { color: var(--good); font-size: .88rem; }
.pills button[aria-pressed="true"] { border-color: var(--ink); font-weight: 600; }
.panel { display: grid; gap: 12px; padding: 14px 16px; background: var(--surface); border: 1px solid var(--rule); min-width: 0; }
.panel h3 { font-size: 1.1rem; }
.form-grid { display: grid; gap: 12px 18px; grid-template-columns: repeat(auto-fit, minmax(min(100%, 220px), 1fr)); }
.form-grid .wide { grid-column: 1 / -1; }
input[type="text"], input[type="url"], input[type="email"], input[type="date"], input[type="file"], textarea { font: inherit; font-size: .95rem; color: var(--ink); background: var(--surface); border: 1px solid var(--rule); border-radius: 4px; padding: 5px 8px; width: 100%; }
textarea { min-height: 7em; resize: vertical; }
.field-error { color: var(--accent); font-size: .85rem; }
.hint { color: var(--muted); font-size: .85rem; }
.actions { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; }
.facts { display: flex; flex-wrap: wrap; gap: 4px 18px; color: var(--muted); font-size: .92rem; }
.timeline { margin: 0; padding-left: 1.2rem; display: grid; gap: 6px; }
.list { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
.list li { display: flex; flex-wrap: wrap; gap: 4px 12px; align-items: baseline; }
.done { color: var(--muted); text-decoration: line-through; }
.posting-text { white-space: pre-wrap; font-family: var(--body); font-size: .95rem; max-height: 28em; overflow: auto; margin: 0; padding: 12px; background: var(--bg); border: 1px solid var(--rule); }
```

- [ ] **Step 4: Run tests, type check and build**

Run: `cd src/Dashboard && npm test && npm run build`
Expected: all Vitest files PASS; `tsc` clean; the build succeeds.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add the tracker page with Up next, status counts and the overdue nav badge"
```

---

### Task 8: Paste a job

**Files:**
- Create: `src/Dashboard/src/tracker/Field.tsx`, `src/Dashboard/src/tracker/PasteJob.tsx`
- Modify: `src/Dashboard/src/App.tsx` (import + route)
- Test: `src/Dashboard/src/tracker/PasteJob.test.tsx`

**Interfaces:**
- Consumes: `tracker.paste`, `tracker.companies`, `PasteInput`, `ApiError.details.field` (Task 6); `useTrackerMutation` (Task 7)
- Produces:
  - `Field({ id, label, error?, hint?, wide?, children })`, which renders the label, the hint, and the error with id `<id>-error`;
  - `validatePaste(input) : Partial<Record<keyof PasteInput, string>>`;
  - `PasteJob()` at route `/tracker/new`.

- [ ] **Step 1: Write the failing test**

`src/Dashboard/src/tracker/PasteJob.test.tsx`:
```tsx
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { failure, mockApi, renderRoute, reply, sentBody } from '../test/render';
import { PasteJob } from './PasteJob';

const fill = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });

describe('Paste a job', () => {
  it('checks the fields before sending anything', async () => {
    const calls = mockApi({ '/api/tracker/companies': [] });
    renderRoute('/tracker/new', <PasteJob />);

    fill('Link', 'www.rbc.com/job');
    fireEvent.click(screen.getByRole('button', { name: 'Save to tracker' }));

    expect(await screen.findByText('Title is required.')).toBeInTheDocument();
    expect(screen.getByText('Company is required.')).toBeInTheDocument();
    expect(screen.getByText(/Paste the job text/)).toBeInTheDocument();
    expect(screen.getByText(/starting with https/)).toBeInTheDocument();
    expect(screen.getByLabelText('Title')).toHaveAttribute('aria-invalid', 'true');
    expect(calls.filter((c) => c.startsWith('POST'))).toEqual([]);
  });

  it('saves the job and opens the new entry', async () => {
    mockApi({ '/api/tracker/companies': [{ id: 'c1', name: 'RBC' }], 'POST /api/tracker/applications/paste': reply(201, { id: 'a9' }) });
    renderRoute('/tracker/new', <PasteJob />);

    fill('Title', 'Senior .NET Developer');
    fill('Company', 'RBC');
    fill('Job text', 'C# and Azure.');
    fill('Starting status', 'Applied');
    fireEvent.click(screen.getByRole('button', { name: 'Save to tracker' }));

    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/tracker/a9'));
    expect(sentBody('POST /api/tracker/applications/paste')).toEqual({
      title: 'Senior .NET Developer', company: 'RBC', url: '', location: '', postingText: 'C# and Azure.', status: 'Applied',
    });
  });

  it("shows the API's message next to the field it names", async () => {
    mockApi({
      '/api/tracker/companies': [],
      'POST /api/tracker/applications/paste': failure(400, 'Job text is longer than 100000 characters.', { field: 'postingText' }),
    });
    renderRoute('/tracker/new', <PasteJob />);

    fill('Title', 'Dev');
    fill('Company', 'Acme');
    fill('Job text', 'x');
    fireEvent.click(screen.getByRole('button', { name: 'Save to tracker' }));

    expect(await screen.findByText('Job text is longer than 100000 characters.')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/Dashboard && npx vitest run src/tracker/PasteJob.test.tsx`
Expected: FAIL: `./PasteJob` doesn't exist.

- [ ] **Step 3: Implement**

`src/Dashboard/src/tracker/Field.tsx`:
```tsx
import type { ReactNode } from 'react';

// A labelled control with its hint and error. Pass the control as children, with the same id.
export function Field({ id, label, error, hint, wide, children }: {
  id: string;
  label: string;
  error?: string;
  hint?: string;
  wide?: boolean;
  children: ReactNode;
}) {
  return (
    <div className={wide ? 'field wide' : 'field'}>
      <label className="field-label" htmlFor={id}>{label}</label>
      {children}
      {hint && <span className="hint">{hint}</span>}
      {error && <span className="field-error" id={`${id}-error`}>{error}</span>}
    </div>
  );
}
```

`src/Dashboard/src/tracker/PasteJob.tsx`:
```tsx
import { useQuery } from '@tanstack/react-query';
import { useState, type ChangeEvent, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import { ApiError } from '../api';
import { ErrorBanner } from '../components/States';
import { tracker, type NewEntry, type PasteInput } from './api';
import { Field } from './Field';
import { useTrackerMutation } from './useTrackerMutation';

type Errors = Partial<Record<keyof PasteInput, string>>;

const EMPTY: PasteInput = { title: '', company: '', url: '', location: '', postingText: '', status: 'Shortlisted' };

export function validatePaste(input: PasteInput): Errors {
  const errors: Errors = {};
  if (!input.title.trim()) errors.title = 'Title is required.';
  if (!input.company.trim()) errors.company = 'Company is required.';
  if (!input.postingText.trim()) errors.postingText = 'Paste the job text: the skill match and market stats need it.';
  if (input.url.trim() && !/^https?:\/\//i.test(input.url.trim())) errors.url = 'Use a full web address starting with https://.';
  return errors;
}

export function PasteJob() {
  const navigate = useNavigate();
  const companies = useQuery({ queryKey: ['tracker', 'companies'], queryFn: tracker.companies });
  const [form, setForm] = useState<PasteInput>(EMPTY);
  const [errors, setErrors] = useState<Errors>({});
  const save = useTrackerMutation((input: PasteInput) => tracker.paste(input), (created: NewEntry) => navigate(`/tracker/${created.id}`));
  const fieldError = save.error instanceof ApiError && typeof save.error.details.field === 'string' ? save.error.details.field : null;
  const shown: Errors = fieldError && fieldError in EMPTY && save.error ? { ...errors, [fieldError]: save.error.message } : errors;

  const change = (key: keyof PasteInput) => (e: ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) =>
    setForm({ ...form, [key]: e.target.value });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const found = validatePaste(form);
    setErrors(found);
    if (Object.keys(found).length === 0) save.mutate(form);
  };

  const invalid = (key: keyof PasteInput) => (shown[key] ? { 'aria-invalid': true, 'aria-describedby': `paste-${key}-error` } : {});

  return (
    <section className="section" aria-labelledby="h-paste">
      <p className="eyebrow"><Link to="/tracker">Tracker</Link> / Paste a job</p>
      <h2 id="h-paste">Paste a job</h2>
      <p className="muted small">
        For jobs the collector doesn't see (LinkedIn, bank career sites, referrals). The job is tracked now, and the next
        Tuesday or Friday rebuild adds it to the market data too.
      </p>
      <form className="form-grid" onSubmit={submit} noValidate>
        <Field id="paste-title" label="Title" error={shown.title}>
          <input id="paste-title" type="text" value={form.title} onChange={change('title')} {...invalid('title')} />
        </Field>
        <Field id="paste-company" label="Company" error={shown.company}>
          <input id="paste-company" type="text" list="paste-companies" value={form.company} onChange={change('company')} {...invalid('company')} />
          <datalist id="paste-companies">{companies.data?.map((c) => <option key={c.id} value={c.name} />)}</datalist>
        </Field>
        <Field id="paste-url" label="Link" error={shown.url}>
          <input id="paste-url" type="url" value={form.url} onChange={change('url')} placeholder="https://" {...invalid('url')} />
        </Field>
        <Field id="paste-location" label="Location" hint="e.g. Toronto, ON or Remote (Canada)">
          <input id="paste-location" type="text" value={form.location} onChange={change('location')} />
        </Field>
        <Field id="paste-status" label="Starting status">
          <select id="paste-status" value={form.status} onChange={change('status')}>
            <option value="Shortlisted">Shortlisted</option>
            <option value="Applied">Applied</option>
          </select>
        </Field>
        <Field id="paste-postingText" label="Job text" error={shown.postingText} wide>
          <textarea id="paste-postingText" rows={12} value={form.postingText} onChange={change('postingText')} {...invalid('postingText')} />
        </Field>
        <div className="actions wide">
          <button type="submit" className="primary" disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save to tracker'}</button>
          <Link to="/tracker">Cancel</Link>
        </div>
      </form>
      {save.error && !fieldError && <ErrorBanner error={save.error} />}
    </section>
  );
}
```

In `src/Dashboard/src/App.tsx`:
- add `import { PasteJob } from './tracker/PasteJob';`;
- in the tracker `<Routes>`, add after the `/tracker` route:
```tsx
            <Route path="/tracker/new" element={<PasteJob />} />
```

- [ ] **Step 4: Run tests, type check and build**

Run: `cd src/Dashboard && npm test && npm run build`
Expected: all PASS; the build succeeds.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add the paste-a-job form"
```

---

### Task 9: The entry page

**Files:**
- Create: `src/Dashboard/src/tracker/SaveError.tsx`
- Create: `src/Dashboard/src/tracker/entry/` with `EntryPage.tsx`, `StatusForm.tsx`, `Timeline.tsx`, `FollowUps.tsx`, `DetailsForm.tsx`, `CompanyForm.tsx`, `Contacts.tsx`, `Documents.tsx` and `DeleteEntry.tsx`
- Modify: `src/Dashboard/src/App.tsx` (import + route)
- Test: `src/Dashboard/src/tracker/entry/EntryPage.test.tsx`

**Interfaces:**
- Consumes: `tracker.*`, the types and input shapes (Task 6); `useTrackerMutation`, `StatusChip`, `dates` (Task 7); `Field` (Task 8); `QueryState` (phase 2a); `dateText`, `pct` (phase 2a `format.ts`)
- Produces:
  - `SaveError({ error })`: an inline banner, with **Reload** on a 409, which invalidates `['tracker']`;
  - `EntryPage()` at route `/tracker/:id`. The forms are keyed by `version`, so they remount with fresh values after a save or a reload.

- [ ] **Step 1: Write the failing test**

`src/Dashboard/src/tracker/entry/EntryPage.test.tsx`:
```tsx
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { failure, mockApi, renderRoute, reply, sentBody } from '../../test/render';
import type { ApplicationDetail } from '../api';
import { localToday } from '../dates';
import { EntryPage } from './EntryPage';

const ENTRY: ApplicationDetail = {
  id: 'a1', title: 'Senior .NET Developer', url: 'https://jobs.rbc.com/1', location: 'Toronto, ON', status: 'Applied', origin: 'Market',
  foundVia: 'LinkedIn', workModel: 'Hybrid', officeDays: '3 days', teamSize: '8', workingHours: '9 to 5', salaryPosted: 'CAD 130–150k',
  salaryAsked: '', salaryOffered: '', notes: '', postingId: 'greenhouse:101', postingText: 'We need C# and Azure.',
  market: { match: 0.75, live: true, firstSeen: '2027-02-20T12:00:00Z', lastSeen: '2027-03-01T12:00:00Z', skills: ['.NET', 'C#'] },
  company: { id: 'c1', name: 'RBC', website: '', officeLocation: '', companySize: '', annualLeave: '', benefits: '', notes: '', version: 7, applications: 1 },
  events: [
    { id: 'e1', onDate: '2027-02-25', fromStatus: null, toStatus: 'Shortlisted', note: 'Added from the market data' },
    { id: 'e2', onDate: '2027-03-01', fromStatus: 'Shortlisted', toStatus: 'Applied', note: '' },
  ],
  followUps: [{ id: 'f1', action: 'Email Sam', dueOn: '2027-03-08', doneOn: null }],
  contacts: [], documents: [], createdAt: '2027-02-25T12:00:00Z', updatedAt: '2027-03-01T12:00:00Z', version: 42,
};

const open = () => renderRoute('/tracker/a1', <EntryPage />, '/tracker/:id');

describe('Entry page', () => {
  it('shows the entry with its market facts, timeline and follow-ups', async () => {
    mockApi({ '/api/tracker/applications/a1': ENTRY });
    open();

    expect(await screen.findByRole('heading', { level: 2, name: /Senior .NET Developer/ })).toBeInTheDocument();
    expect(screen.getByText(/75% skill match · still live/)).toBeInTheDocument();
    expect(screen.getByText(/Added as/)).toBeInTheDocument();
    expect(screen.getByText(/Email Sam/)).toBeInTheDocument();
    expect(screen.getByDisplayValue('CAD 130–150k')).toBeInTheDocument();
  });

  it('records a status change with its date and note', async () => {
    mockApi({ '/api/tracker/applications/a1': ENTRY, 'POST /api/tracker/applications/a1/status': reply(204) });
    open();

    fireEvent.change(await screen.findByLabelText('New status'), { target: { value: 'Screening' } });
    fireEvent.change(screen.getByLabelText('Note'), { target: { value: 'Recruiter call booked' } });
    fireEvent.click(screen.getByRole('button', { name: 'Update status' }));

    await waitFor(() => expect(sentBody('POST /api/tracker/applications/a1/status'))
      .toEqual({ status: 'Screening', on: localToday(), note: 'Recruiter call booked' }));
  });

  it('offers Reload when the details changed in another tab', async () => {
    const calls = mockApi({
      '/api/tracker/applications/a1': ENTRY,
      'PATCH /api/tracker/applications/a1': failure(409, 'This entry changed in another tab. Reload to see the latest.'),
    });
    open();

    fireEvent.change(await screen.findByLabelText('Team size'), { target: { value: '9' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save details' }));

    expect(await screen.findByText(/changed in another tab/)).toBeInTheDocument();
    expect(sentBody('PATCH /api/tracker/applications/a1')).toMatchObject({ version: 42, teamSize: '9', workModel: 'Hybrid' });
    const loads = calls.filter((c) => c === '/api/tracker/applications/a1').length;
    fireEvent.click(screen.getByRole('button', { name: 'Reload' }));
    await waitFor(() => expect(calls.filter((c) => c === '/api/tracker/applications/a1').length).toBeGreaterThan(loads));
  });

  it('adds a follow-up', async () => {
    mockApi({ '/api/tracker/applications/a1': ENTRY, 'POST /api/tracker/applications/a1/follow-ups': reply(201, { id: 'f2' }) });
    open();

    fireEvent.change(await screen.findByLabelText('Next action'), { target: { value: 'Send thank-you' } });
    fireEvent.change(screen.getByLabelText('Due'), { target: { value: '2027-03-10' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add follow-up' }));

    await waitFor(() => expect(sentBody('POST /api/tracker/applications/a1/follow-ups'))
      .toEqual({ action: 'Send thank-you', dueOn: '2027-03-10', doneOn: null }));
  });

  it('deletes only after a confirmation inside the page', async () => {
    const calls = mockApi({ '/api/tracker/applications/a1': ENTRY, 'DELETE /api/tracker/applications/a1': reply(204) });
    open();

    fireEvent.click(await screen.findByRole('button', { name: 'Delete this entry' }));
    expect(calls).not.toContain('DELETE /api/tracker/applications/a1');
    fireEvent.click(screen.getByRole('button', { name: 'Delete for good' }));

    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/tracker'));
    expect(calls).toContain('DELETE /api/tracker/applications/a1');
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/Dashboard && npx vitest run src/tracker/entry`
Expected: FAIL: `./EntryPage` doesn't exist.

- [ ] **Step 3: Implement**

`src/Dashboard/src/tracker/SaveError.tsx`:
```tsx
import { useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../api';

// A failed save, shown by the form that made it. On a conflict (someone saved first, e.g. in another tab) it offers
// Reload, which fetches the latest values; the forms remount with them.
export function SaveError({ error }: { error: unknown }) {
  const queryClient = useQueryClient();
  if (!error) return null;
  const message = error instanceof Error ? error.message : 'The change was not saved.';
  const conflict = error instanceof ApiError && error.status === 409;
  return (
    <div className="banner" role="alert">
      {message}{' '}
      {conflict && <button type="button" onClick={() => void queryClient.invalidateQueries({ queryKey: ['tracker'] })}>Reload</button>}
    </div>
  );
}
```

`src/Dashboard/src/tracker/entry/EntryPage.tsx`:
```tsx
import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from 'react-router';
import { QueryState } from '../../components/States';
import { dateText, pct } from '../../format';
import { tracker } from '../api';
import { StatusChip } from '../StatusChip';
import { CompanyForm } from './CompanyForm';
import { Contacts } from './Contacts';
import { DeleteEntry } from './DeleteEntry';
import { DetailsForm } from './DetailsForm';
import { Documents } from './Documents';
import { FollowUps } from './FollowUps';
import { StatusForm } from './StatusForm';
import { Timeline } from './Timeline';

export function EntryPage() {
  const { id = '' } = useParams();
  const query = useQuery({ queryKey: ['tracker', 'entry', id], queryFn: () => tracker.detail(id) });
  if (!query.data) return <QueryState query={query} what="this entry" />;
  const entry = query.data;

  return (
    <>
      <section className="section" aria-labelledby="h-entry">
        <p className="eyebrow"><Link to="/tracker">Tracker</Link> / {entry.company.name}</p>
        <h2 id="h-entry">{entry.title} <StatusChip status={entry.status} /></h2>
        <div className="facts">
          <span>{entry.company.name}</span>
          {entry.location && <span>{entry.location}</span>}
          {entry.url && <a href={entry.url} target="_blank" rel="noreferrer">Open the posting ↗</a>}
          {entry.market ? (
            <span>
              {pct(entry.market.match)} skill match · {entry.market.live ? 'still live' : 'not in the latest run'} · first seen {dateText(entry.market.firstSeen)}
            </span>
          ) : entry.postingId?.startsWith('inbox:') ? (
            <span>Not in the market data yet: it joins after the next rebuild if it's in the GTA or remote in Canada.</span>
          ) : null}
        </div>
      </section>
      <StatusForm key={`status-${entry.status}`} entry={entry} />
      <div className="grid-2">
        <Timeline events={entry.events} />
        <FollowUps entry={entry} />
      </div>
      <DetailsForm key={`details-${entry.version}`} entry={entry} />
      <CompanyForm key={`company-${entry.company.version}`} company={entry.company} />
      <div className="grid-2">
        <Contacts entry={entry} />
        <Documents entry={entry} />
      </div>
      <details className="panel">
        <summary>Job text</summary>
        <pre className="posting-text">{entry.postingText || 'No job text saved.'}</pre>
      </details>
      <DeleteEntry entry={entry} />
    </>
  );
}
```

`src/Dashboard/src/tracker/entry/StatusForm.tsx`:
```tsx
import { useState } from 'react';
import { STATUSES, tracker, type AppStatus, type ApplicationDetail } from '../api';
import { localToday } from '../dates';
import { Field } from '../Field';
import { SaveError } from '../SaveError';
import { useTrackerMutation } from '../useTrackerMutation';

export function StatusForm({ entry }: { entry: ApplicationDetail }) {
  const [status, setStatus] = useState<AppStatus>(entry.status);
  const [on, setOn] = useState(localToday());
  const [note, setNote] = useState('');
  const save = useTrackerMutation(() => tracker.setStatus(entry.id, { status, on: on || null, note }));
  const unchanged = status === entry.status;

  return (
    <form className="panel" aria-labelledby="h-status" onSubmit={(e) => { e.preventDefault(); save.mutate(undefined); }}>
      <h3 id="h-status">Update status</h3>
      <div className="form-grid">
        <Field id="status-to" label="New status">
          <select id="status-to" value={status} onChange={(e) => setStatus(e.target.value as AppStatus)}>
            {STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </Field>
        <Field id="status-on" label="On">
          <input id="status-on" type="date" value={on} max={localToday()} onChange={(e) => setOn(e.target.value)} />
        </Field>
        <Field id="status-note" label="Note" wide>
          <input id="status-note" type="text" value={note} onChange={(e) => setNote(e.target.value)} placeholder="e.g. Applied through Workday" />
        </Field>
      </div>
      <div className="actions">
        <button type="submit" className="primary" disabled={save.isPending || unchanged}>Update status</button>
        {unchanged && <span className="hint">Pick a different status to record a change.</span>}
      </div>
      <SaveError error={save.error} />
    </form>
  );
}
```

`src/Dashboard/src/tracker/entry/Timeline.tsx`:
```tsx
import type { EventView } from '../api';
import { dayText } from '../dates';

export function Timeline({ events }: { events: EventView[] }) {
  return (
    <section className="panel" aria-labelledby="h-timeline">
      <h3 id="h-timeline">Timeline</h3>
      <ol className="timeline">
        {events.map((e) => (
          <li key={e.id}>
            <span className="muted small">{dayText(e.onDate)}</span>{' '}
            {e.fromStatus ? <>{e.fromStatus} → <strong>{e.toStatus}</strong></> : <>Added as <strong>{e.toStatus}</strong></>}
            {e.note && <span className="muted"> · {e.note}</span>}
          </li>
        ))}
      </ol>
    </section>
  );
}
```

`src/Dashboard/src/tracker/entry/FollowUps.tsx`:
```tsx
import { useState } from 'react';
import { tracker, type ApplicationDetail, type FollowUpView } from '../api';
import { dayText, localToday } from '../dates';
import { Field } from '../Field';
import { SaveError } from '../SaveError';
import { useTrackerMutation } from '../useTrackerMutation';

export function FollowUps({ entry }: { entry: ApplicationDetail }) {
  const [action, setAction] = useState('');
  const [dueOn, setDueOn] = useState('');
  const add = useTrackerMutation(() => tracker.addFollowUp(entry.id, { action: action.trim(), dueOn, doneOn: null }), () => {
    setAction('');
    setDueOn('');
  });
  const complete = useTrackerMutation((f: FollowUpView) =>
    tracker.editFollowUp(entry.id, f.id, { action: f.action, dueOn: f.dueOn, doneOn: localToday() }));
  const remove = useTrackerMutation((f: FollowUpView) => tracker.deleteFollowUp(entry.id, f.id));
  const today = localToday();

  return (
    <section className="panel" aria-labelledby="h-followups">
      <h3 id="h-followups">Follow-ups</h3>
      {entry.followUps.length === 0 ? (
        <p className="empty">No follow-ups yet.</p>
      ) : (
        <ul className="list">
          {entry.followUps.map((f) => (
            <li key={f.id}>
              <span className={f.doneOn ? 'done' : f.dueOn < today ? 'overdue' : undefined}>{dayText(f.dueOn)} · {f.action}</span>
              {!f.doneOn && (
                <button type="button" onClick={() => complete.mutate(f)} aria-label={`Mark "${f.action}" done`}>Done</button>
              )}
              <button type="button" onClick={() => remove.mutate(f)} aria-label={`Delete "${f.action}"`}>Delete</button>
            </li>
          ))}
        </ul>
      )}
      <form className="form-grid" onSubmit={(e) => { e.preventDefault(); if (action.trim() && dueOn) add.mutate(undefined); }}>
        <Field id="followup-action" label="Next action">
          <input id="followup-action" type="text" value={action} onChange={(e) => setAction(e.target.value)} placeholder="e.g. Email the recruiter" />
        </Field>
        <Field id="followup-due" label="Due">
          <input id="followup-due" type="date" value={dueOn} onChange={(e) => setDueOn(e.target.value)} />
        </Field>
        <div className="actions">
          <button type="submit" className="primary" disabled={!action.trim() || !dueOn || add.isPending}>Add follow-up</button>
        </div>
      </form>
      <SaveError error={add.error ?? complete.error ?? remove.error} />
    </section>
  );
}
```

`src/Dashboard/src/tracker/entry/DetailsForm.tsx`:
```tsx
import { useState, type ChangeEvent } from 'react';
import { WORK_MODELS, tracker, type ApplicationDetail, type DetailsInput, type WorkModel } from '../api';
import { Field } from '../Field';
import { SaveError } from '../SaveError';
import { useTrackerMutation } from '../useTrackerMutation';

type TextKey = Exclude<keyof DetailsInput, 'version' | 'workModel' | 'notes'>;

const TEXT_FIELDS: { key: TextKey; label: string; hint?: string }[] = [
  { key: 'title', label: 'Title' },
  { key: 'url', label: 'Link' },
  { key: 'location', label: 'Location' },
  { key: 'foundVia', label: 'How you found it', hint: 'Referral, LinkedIn, company site…' },
  { key: 'officeDays', label: 'Office days', hint: 'e.g. 3 days a week' },
  { key: 'teamSize', label: 'Team size' },
  { key: 'workingHours', label: 'Working hours' },
  { key: 'salaryPosted', label: 'Salary posted' },
  { key: 'salaryAsked', label: 'Salary you asked' },
  { key: 'salaryOffered', label: 'Salary offered' },
];

const WORK_MODEL_LABELS: Record<WorkModel, string> = { Unknown: 'Not known', Remote: 'Remote', Hybrid: 'Hybrid', Onsite: 'On-site' };

export function DetailsForm({ entry }: { entry: ApplicationDetail }) {
  const [form, setForm] = useState<DetailsInput>({
    version: entry.version, title: entry.title, url: entry.url, location: entry.location, foundVia: entry.foundVia,
    workModel: entry.workModel, officeDays: entry.officeDays, teamSize: entry.teamSize, workingHours: entry.workingHours,
    salaryPosted: entry.salaryPosted, salaryAsked: entry.salaryAsked, salaryOffered: entry.salaryOffered, notes: entry.notes,
  });
  const save = useTrackerMutation(() => tracker.editDetails(entry.id, form));
  const change = (key: keyof DetailsInput) => (e: ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) =>
    setForm({ ...form, [key]: e.target.value });

  return (
    <form className="panel" aria-labelledby="h-details" onSubmit={(e) => { e.preventDefault(); save.mutate(undefined); }}>
      <h3 id="h-details">Job details</h3>
      <div className="form-grid">
        {TEXT_FIELDS.map(({ key, label, hint }) => (
          <Field key={key} id={`details-${key}`} label={label} hint={hint}>
            <input id={`details-${key}`} type={key === 'url' ? 'url' : 'text'} value={form[key]} onChange={change(key)} />
          </Field>
        ))}
        <Field id="details-workModel" label="Work model">
          <select id="details-workModel" value={form.workModel} onChange={change('workModel')}>
            {WORK_MODELS.map((m) => <option key={m} value={m}>{WORK_MODEL_LABELS[m]}</option>)}
          </select>
        </Field>
        <Field id="details-notes" label="Notes" wide>
          <textarea id="details-notes" value={form.notes} onChange={change('notes')} />
        </Field>
      </div>
      <div className="actions">
        <button type="submit" className="primary" disabled={save.isPending}>Save details</button>
      </div>
      <SaveError error={save.error} />
    </form>
  );
}
```

`src/Dashboard/src/tracker/entry/CompanyForm.tsx`:
```tsx
import { useState, type ChangeEvent } from 'react';
import { tracker, type CompanyInput, type CompanyView } from '../api';
import { Field } from '../Field';
import { SaveError } from '../SaveError';
import { useTrackerMutation } from '../useTrackerMutation';

const TEXT_FIELDS: { key: 'name' | 'website' | 'officeLocation' | 'companySize' | 'annualLeave'; label: string; hint?: string }[] = [
  { key: 'name', label: 'Company name' },
  { key: 'website', label: 'Website' },
  { key: 'officeLocation', label: 'Office location' },
  { key: 'companySize', label: 'Company size' },
  { key: 'annualLeave', label: 'Annual leave', hint: 'e.g. 20 days + 5 personal' },
];

export function CompanyForm({ company }: { company: CompanyView }) {
  const [form, setForm] = useState<CompanyInput>({
    version: company.version, name: company.name, website: company.website, officeLocation: company.officeLocation,
    companySize: company.companySize, annualLeave: company.annualLeave, benefits: company.benefits, notes: company.notes,
  });
  const save = useTrackerMutation(() => tracker.editCompany(company.id, form));
  const change = (key: keyof CompanyInput) => (e: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
    setForm({ ...form, [key]: e.target.value });

  return (
    <form className="panel" aria-labelledby="h-company" onSubmit={(e) => { e.preventDefault(); save.mutate(undefined); }}>
      <h3 id="h-company">Company</h3>
      {company.applications > 1 && <p className="hint">Shared by all {company.applications} of your {company.name} entries.</p>}
      <div className="form-grid">
        {TEXT_FIELDS.map(({ key, label, hint }) => (
          <Field key={key} id={`company-${key}`} label={label} hint={hint}>
            <input id={`company-${key}`} type={key === 'website' ? 'url' : 'text'} value={form[key]} onChange={change(key)} />
          </Field>
        ))}
        <Field id="company-benefits" label="Benefits" wide>
          <textarea id="company-benefits" value={form.benefits} onChange={change('benefits')} />
        </Field>
        <Field id="company-notes" label="Company notes" wide>
          <textarea id="company-notes" value={form.notes} onChange={change('notes')} />
        </Field>
      </div>
      <div className="actions">
        <button type="submit" className="primary" disabled={save.isPending}>Save company</button>
      </div>
      <SaveError error={save.error} />
    </form>
  );
}
```

`src/Dashboard/src/tracker/entry/Contacts.tsx`:
```tsx
import { useState, type ChangeEvent } from 'react';
import { tracker, type ApplicationDetail, type ContactInput } from '../api';
import { Field } from '../Field';
import { SaveError } from '../SaveError';
import { useTrackerMutation } from '../useTrackerMutation';

const BLANK: ContactInput = { name: '', role: '', email: '', phone: '', linkedIn: '', notes: '' };

export function Contacts({ entry }: { entry: ApplicationDetail }) {
  const [form, setForm] = useState<ContactInput>(BLANK);
  const add = useTrackerMutation(() => tracker.addContact(entry.id, form), () => setForm(BLANK));
  const remove = useTrackerMutation((contactId: string) => tracker.deleteContact(entry.id, contactId));
  const change = (key: keyof ContactInput) => (e: ChangeEvent<HTMLInputElement>) => setForm({ ...form, [key]: e.target.value });

  return (
    <section className="panel" aria-labelledby="h-contacts">
      <h3 id="h-contacts">Contacts</h3>
      {entry.contacts.length === 0 ? (
        <p className="empty">No contacts yet.</p>
      ) : (
        <ul className="list">
          {entry.contacts.map((c) => (
            <li key={c.id}>
              <strong>{c.name}</strong>
              {c.role && <span className="muted">{c.role}</span>}
              {c.email && <span>{c.email}</span>}
              {c.phone && <span>{c.phone}</span>}
              {c.linkedIn && <a href={c.linkedIn} target="_blank" rel="noreferrer">LinkedIn ↗</a>}
              <button type="button" onClick={() => remove.mutate(c.id)} aria-label={`Delete contact ${c.name}`}>Delete</button>
            </li>
          ))}
        </ul>
      )}
      <form className="form-grid" onSubmit={(e) => { e.preventDefault(); if (form.name.trim()) add.mutate(undefined); }}>
        <Field id="contact-name" label="Name"><input id="contact-name" type="text" value={form.name} onChange={change('name')} /></Field>
        <Field id="contact-role" label="Role"><input id="contact-role" type="text" value={form.role} onChange={change('role')} placeholder="Recruiter, hiring manager…" /></Field>
        <Field id="contact-email" label="Email"><input id="contact-email" type="email" value={form.email} onChange={change('email')} /></Field>
        <Field id="contact-phone" label="Phone"><input id="contact-phone" type="text" value={form.phone} onChange={change('phone')} /></Field>
        <Field id="contact-linkedIn" label="LinkedIn"><input id="contact-linkedIn" type="url" value={form.linkedIn} onChange={change('linkedIn')} placeholder="https://www.linkedin.com/in/…" /></Field>
        <div className="actions"><button type="submit" className="primary" disabled={!form.name.trim() || add.isPending}>Add contact</button></div>
      </form>
      <SaveError error={add.error ?? remove.error} />
    </section>
  );
}
```

`src/Dashboard/src/tracker/entry/Documents.tsx`:
```tsx
import { useState } from 'react';
import { DOCUMENT_KINDS, tracker, type ApplicationDetail, type DocumentKind } from '../api';
import { dayText, localToday } from '../dates';
import { Field } from '../Field';
import { SaveError } from '../SaveError';
import { useTrackerMutation } from '../useTrackerMutation';

function sizeText(bytes: number): string {
  return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

export function Documents({ entry }: { entry: ApplicationDetail }) {
  const [file, setFile] = useState<File | null>(null);
  const [kind, setKind] = useState<DocumentKind>('Cv');
  const [label, setLabel] = useState('');
  const [sentOn, setSentOn] = useState(localToday());
  const [inputKey, setInputKey] = useState(0);
  const upload = useTrackerMutation((chosen: File) => {
    const form = new FormData();
    form.append('file', chosen);
    form.append('kind', kind);
    form.append('label', label);
    form.append('sentOn', sentOn);
    return tracker.upload(entry.id, form);
  }, () => {
    setFile(null);
    setLabel('');
    setInputKey((k) => k + 1); // clears the file input
  });
  const remove = useTrackerMutation((documentId: string) => tracker.deleteDocument(documentId));

  return (
    <section className="panel" aria-labelledby="h-documents">
      <h3 id="h-documents">Documents sent</h3>
      {entry.documents.length === 0 ? (
        <p className="empty">No documents yet.</p>
      ) : (
        <ul className="list">
          {entry.documents.map((d) => (
            <li key={d.id}>
              <a href={tracker.documentUrl(d.id)}>{d.label}</a>
              <span className="muted small">
                {DOCUMENT_KINDS.find((k) => k.kind === d.kind)?.label} · sent {dayText(d.sentOn)} · {sizeText(d.sizeBytes)}
              </span>
              <button type="button" onClick={() => remove.mutate(d.id)} aria-label={`Delete document ${d.label}`}>Delete</button>
            </li>
          ))}
        </ul>
      )}
      <form className="form-grid" onSubmit={(e) => { e.preventDefault(); if (file) upload.mutate(file); }}>
        <Field id="doc-file" label="File" hint="PDF, DOCX, DOC, TXT or MD, up to 10 MB">
          <input key={inputKey} id="doc-file" type="file" accept=".pdf,.docx,.doc,.txt,.md" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
        </Field>
        <Field id="doc-kind" label="Kind">
          <select id="doc-kind" value={kind} onChange={(e) => setKind(e.target.value as DocumentKind)}>
            {DOCUMENT_KINDS.map((k) => <option key={k.kind} value={k.kind}>{k.label}</option>)}
          </select>
        </Field>
        <Field id="doc-label" label="Label" hint="Defaults to the file name">
          <input id="doc-label" type="text" value={label} onChange={(e) => setLabel(e.target.value)} />
        </Field>
        <Field id="doc-sent" label="Sent on">
          <input id="doc-sent" type="date" value={sentOn} onChange={(e) => setSentOn(e.target.value)} />
        </Field>
        <div className="actions"><button type="submit" className="primary" disabled={!file || upload.isPending}>Upload</button></div>
      </form>
      <SaveError error={upload.error ?? remove.error} />
    </section>
  );
}
```

`src/Dashboard/src/tracker/entry/DeleteEntry.tsx`:
```tsx
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { tracker, type ApplicationDetail } from '../api';
import { SaveError } from '../SaveError';
import { useTrackerMutation } from '../useTrackerMutation';

// The confirmation step lives in the page: browser confirm dialogs are easy to click through.
export function DeleteEntry({ entry }: { entry: ApplicationDetail }) {
  const navigate = useNavigate();
  const [asking, setAsking] = useState(false);
  const remove = useTrackerMutation(() => tracker.remove(entry.id), () => navigate('/tracker'));

  return (
    <section className="panel" aria-labelledby="h-delete">
      <h3 id="h-delete">Delete</h3>
      {!asking ? (
        <div className="actions"><button type="button" onClick={() => setAsking(true)}>Delete this entry</button></div>
      ) : (
        <div className="actions" role="group" aria-label="Confirm delete">
          <span>This removes the entry, its timeline, follow-ups, contacts and uploaded files. The company stays.</span>
          <button type="button" className="danger" onClick={() => remove.mutate(undefined)} disabled={remove.isPending}>Delete for good</button>
          <button type="button" onClick={() => setAsking(false)}>Keep it</button>
        </div>
      )}
      <SaveError error={remove.error} />
    </section>
  );
}
```

In `src/Dashboard/src/App.tsx`:
- add `import { EntryPage } from './tracker/entry/EntryPage';`;
- in the tracker `<Routes>`, add after the `/tracker/new` route:
```tsx
            <Route path="/tracker/:id" element={<EntryPage />} />
```

- [ ] **Step 4: Run tests, type check and build**

Run: `cd src/Dashboard && npm test && npm run build`
Expected: all PASS; the build succeeds.
- If a label query finds two elements (for example "Notes" and "Company notes" are distinct, but check), rename the label in the component, not the test, and ledger it.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add the tracker entry page: status, timeline, follow-ups, details, company, contacts, documents and delete"
```

---

### Task 10: Shortlist buttons on the market pages

**Files:**
- Modify: `src/Tracker.Core/Analysis/ReportModel.cs` (`PostingMatch.PostingId`), `src/Tracker.Core/Analysis/Analyzer.cs`
- Modify: `src/Dashboard/src/api.ts` (`ReportModel.matches[].postingId`), `src/Dashboard/src/components/PostingList.tsx`, `src/Dashboard/src/pages/Overview.tsx`
- Create: `src/Dashboard/src/tracker/ShortlistButton.tsx`
- Test: `tests/Tracker.Core.Tests/Analysis/AnalyzerTests.cs` (one assertion), `src/Dashboard/src/tracker/ShortlistButton.test.tsx`, `src/Dashboard/src/components/PostingList.test.tsx` (rewritten to render inside the router and query client)

**Interfaces:**
- Consumes: `tracker.index`, `tracker.shortlist`, `ApiError.details.id` (Task 6); `useTrackerMutation` (Task 7)
- Produces:
  - `PostingMatch(…, bool CitizenshipOrClearance, string PostingId = "")`, which the Analyzer fills with the posting id;
  - `ShortlistButton({ postingId, title })`:
    - a button labelled `Shortlist "<title>"`;
    - once tracked, a status link to `/tracker/<id>` labelled `<Status>: open "<title>" in the tracker`;
    - nothing when the tracker is unavailable.
  - A **Track** column in `PostingList` and in the Overview's best matches.

- [ ] **Step 1: Write the failing tests**

In `tests/Tracker.Core.Tests/Analysis/AnalyzerTests.cs`, inside `Matches_rank_open_postings_by_the_share_of_skills_you_have`, add after the first `Assert.Equal` on `model.Matches`:
```csharp
        Assert.Equal(new[] { "good", "stretch" }, model.Matches.Select(m => m.PostingId));
```

`src/Dashboard/src/tracker/ShortlistButton.test.tsx`:
```tsx
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { failure, mockApi, renderRoute, reply } from '../test/render';
import type { TrackerIndex } from './api';
import { ShortlistButton } from './ShortlistButton';

describe('Shortlist button', () => {
  it('shortlists a posting, then links to its entry', async () => {
    let index: TrackerIndex = {};
    const calls = mockApi({
      '/api/tracker/index': () => index,
      'POST /api/tracker/applications': () => {
        index = { 'greenhouse:101': { id: 'a1', status: 'Shortlisted' } };
        return reply(201, { id: 'a1' });
      },
    });
    renderRoute('/postings', <ShortlistButton postingId="greenhouse:101" title="Senior Dev" />);

    fireEvent.click(await screen.findByRole('button', { name: 'Shortlist "Senior Dev"' }));

    expect(await screen.findByRole('link', { name: 'Shortlisted: open "Senior Dev" in the tracker' })).toHaveAttribute('href', '/tracker/a1');
    expect(calls).toContain('POST /api/tracker/applications');
  });

  it('shows the status of an entry that already exists', async () => {
    mockApi({ '/api/tracker/index': { 'ashby:S1': { id: 'a2', status: 'Interviewing' } } });
    renderRoute('/postings', <ShortlistButton postingId="ashby:S1" title="Platform Engineer" />);

    expect(await screen.findByRole('link', { name: /^Interviewing: open "Platform Engineer"/ })).toHaveAttribute('href', '/tracker/a2');
  });

  it('links to the existing entry when the API says it is already tracked', async () => {
    mockApi({ '/api/tracker/index': {}, 'POST /api/tracker/applications': failure(409, 'This posting is already in your tracker.', { id: 'a3' }) });
    renderRoute('/postings', <ShortlistButton postingId="lever:L1" title="Backend Developer" />);

    fireEvent.click(await screen.findByRole('button', { name: 'Shortlist "Backend Developer"' }));

    expect(await screen.findByRole('link', { name: /open "Backend Developer"/ })).toHaveAttribute('href', '/tracker/a3');
  });

  it('stays out of the way when the tracker is unavailable', async () => {
    mockApi({ '/api/tracker/index': failure(503, 'The tracker database isn’t running.') });
    const { container } = renderRoute('/postings', <ShortlistButton postingId="x:1" title="X" />);

    await waitFor(() => expect(container.querySelector('button')).toBeNull());
  });
});
```

`src/Dashboard/src/components/PostingList.test.tsx` (replace the whole file):
```tsx
import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { PostingRow } from '../api';
import { mockApi, renderRoute } from '../test/render';
import { PostingList } from './PostingList';

const row: PostingRow = {
  id: 'ashby:S1', title: 'Senior Software Engineer', company: 'Cohere', location: 'Remote (Canada)',
  url: 'https://jobs.ashbyhq.com/cohere/S1', salaryMin: 150000, salaryMax: 190000, salaryPeriod: 'Year', match: 0.5,
  firstSeen: '2026-10-01T11:00:00Z', skills: ['Go', 'Kubernetes'], citizenshipOrClearance: true, source: 'ashby', isFullText: true,
};

describe('PostingList', () => {
  it('shows salary, match and the citizenship flag with a link to the posting', async () => {
    mockApi({ '/api/tracker/index': {} });
    renderRoute('/postings', <PostingList rows={[row]} caption="Examples" />);

    expect(screen.getByRole('link', { name: 'Senior Software Engineer' })).toHaveAttribute('href', row.url);
    expect(screen.getByText('$150k–$190k')).toBeInTheDocument();
    expect(screen.getByText('50%')).toBeInTheDocument();
    expect(screen.getByText(/citizenship/)).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Shortlist "Senior Software Engineer"' })).toBeInTheDocument();
  });

  it('says so when nothing matches', () => {
    mockApi({ '/api/tracker/index': {} });
    renderRoute('/postings', <PostingList rows={[]} caption="Examples" />);

    expect(screen.getByText(/No postings match these filters/)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Tracker.Core.Tests --filter AnalyzerTests` and `cd src/Dashboard && npm test`
Expected:
- The C# build FAILS (`PostingId` doesn't exist).
- Vitest FAILS: `./ShortlistButton` is missing, and PostingList has no Shortlist button.

- [ ] **Step 3: Implement**

In `src/Tracker.Core/Analysis/ReportModel.cs`, change the `PostingMatch` record to:
```csharp
public sealed record PostingMatch(string Title, string Company, string Url, string Location, double Score, int SkillCount,
    IReadOnlyList<string> Missing, SalaryRange? Salary, bool CitizenshipOrClearance, string PostingId = "");
```

In `src/Tracker.Core/Analysis/Analyzer.cs`, in `BestMatches`, change the `return new PostingMatch(…)` call's last line from `missing, p.Salary, p.CitizenshipOrClearance);` to:
```csharp
                    missing, p.Salary, p.CitizenshipOrClearance, p.Id);
```

In `src/Dashboard/src/api.ts`, in `ReportModel.matches`, add a field after `citizenshipOrClearance: boolean;`:
```ts
    postingId: string;
```

`src/Dashboard/src/tracker/ShortlistButton.tsx`:
```tsx
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';
import { ApiError } from '../api';
import { tracker } from './api';
import { useTrackerMutation } from './useTrackerMutation';

// "Shortlist" on a market posting. Once tracked, it becomes the entry's status and links to it. It hides itself when
// the tracker can't be reached, so market pages keep working without it.
export function ShortlistButton({ postingId, title }: { postingId: string; title: string }) {
  const index = useQuery({ queryKey: ['tracker', 'index'], queryFn: tracker.index, staleTime: 30_000 });
  const add = useTrackerMutation(() => tracker.shortlist(postingId));
  if (index.isError || !postingId) return null;

  const tracked = index.data?.[postingId];
  const existing = add.error instanceof ApiError && add.error.status === 409 && typeof add.error.details.id === 'string'
    ? add.error.details.id
    : null;
  const id = tracked?.id ?? add.data?.id ?? existing;
  if (id) {
    const status = tracked?.status ?? 'Shortlisted';
    return (
      <Link className={`status status-${status}`} to={`/tracker/${id}`} aria-label={`${status}: open "${title}" in the tracker`}>
        {status}
      </Link>
    );
  }
  return (
    <button type="button" className="small-button" onClick={() => add.mutate(undefined)} disabled={index.isPending || add.isPending}
      aria-label={`Shortlist "${title}"`} title={add.error ? add.error.message : undefined}>
      {add.isPending ? 'Saving…' : 'Shortlist'}
    </button>
  );
}
```

In `src/Dashboard/src/components/PostingList.tsx`:
- add `import { ShortlistButton } from '../tracker/ShortlistButton';`;
- add `<th>Track</th>` after `<th className="num">First seen</th>`;
- add this cell after the `First seen` cell in each row:
```tsx
              <td><ShortlistButton postingId={r.id} title={r.title} /></td>
```

In `src/Dashboard/src/pages/Overview.tsx`:
- add `import { ShortlistButton } from '../tracker/ShortlistButton';`;
- in the best-matches table header, add `<th>Track</th>` after `<th className="num">Salary</th>`;
- in each match row, add after the salary cell:
```tsx
                        <td><ShortlistButton postingId={m.postingId} title={m.title} /></td>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Tracker.Core.Tests && cd src/Dashboard && npm test && npm run build`
Expected:
- Tracker.Core.Tests 240 PASS.
- All Vitest files PASS. The other pages' tests don't mock `/api/tracker/index`, so their buttons hide themselves, which is expected.
- The build succeeds.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add Shortlist buttons to market postings and best matches"
```

---

### Task 11: Decision notes, README and the live check

**Files:**
- Create: `docs/decisions/0010-tracker-beside-destructive-rebuild.md`, `docs/decisions/0011-optimistic-concurrency-xmin.md`
- Modify: `README.md`

- [ ] **Step 1: Decision notes**

`docs/decisions/0010-tracker-beside-destructive-rebuild.md`:
```markdown
# 0010 — The owner's records next to a destructive rebuild

**Status:** Accepted (2026-09-29)

## Context
The market tables are wiped and reloaded from the archive twice a week (ADR 0005). Phase 2b adds data that can't be recollected: the postings the owner shortlists, their applications, timelines, contacts and the files they sent.

## Decision
- **Separate context.** An `ApplicationsDb` context owned by the API holds six tables in the same Postgres database. It has its own migrations history table (`__applications_migrations`), and its tables are never in `TrackerTables.All`, the rebuild's TRUNCATE list. A test fails if they ever are.
- **Stable ids plus copies.** Entries link to market postings by their stable ids (`greenhouse:101`, `inbox:<file>`) and keep their own copy of the title, link and location, so an entry outlives its posting.
- **JSON mirror.** After every committed change, the API rewrites `data/tracker/tracker.json` (a temp file, then a swap).
  - When the tables are found empty and the copy isn't, the API restores them in one transaction.
  - A corrupt copy keeps the tracker closed instead of being overwritten.
- **Pasted jobs.** A pasted job is also written to `data/inbox/`, so the next rebuild counts it as a market posting and the entry links to it automatically.

**Alternatives considered:**
- **JSON files only:** simplest, but no queries, and concurrency by hand.
- **Tables in the Processor's context:** one migration history, but every rebuild change would put the owner's data at risk.

## Consequences
- **The rebuild stays simple and destructive;** the owner's data is structurally out of its reach.
- **Two migration histories in one database.** The API migrates its own tables on the first tracker request.
- **The mirror sits on the same disk.** It covers losing the Docker volume, not losing the disk. Off-site backup can come with the Azure move in phase 4.
```

`docs/decisions/0011-optimistic-concurrency-xmin.md`:
```markdown
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
- The forms refetch the entry after each save anyway, so they always hold the current version.
- Follow-ups, contacts and documents are added and removed without a version check. Those actions don't overwrite anyone's fields.
```

- [ ] **Step 2: README**

In `README.md`, insert before `## Roadmap`:
```markdown
## Phase 2b: shortlist and application tracker

**Tracker** in the dashboard keeps the postings you shortlist and every application you make:
- status and a dated timeline;
- follow-ups (overdue ones show a count on the Tracker link);
- contacts;
- company details;
- the documents you sent.

**Adding entries**
- **Shortlist** any posting from the Postings, Skills or Overview pages.
- **Paste a job** adds jobs the collector never sees, such as LinkedIn or bank career sites. They're also saved to `data/inbox/`, so the next rebuild adds them to the market data.

**Where it's stored**
- Your records have their own tables, which the Tue/Fri rebuild never touches.
- After every change the API also writes `data/tracker/tracker.json`, with uploaded files in `data/tracker/files/`.
- If the database is lost, the tracker restores itself from that copy on first use.

**Protection:** changes only work from the dashboard. The API refuses tracker writes without its header and answers only to localhost names.
```
and change the Roadmap's second line to:
```markdown
2. ~~API + dashboard~~ Done in phase 2a (API + React dashboard, .NET Aspire, OpenTelemetry) and 2b (shortlist and application tracker). Next: 2c CV tailoring per posting
```

- [ ] **Step 3: Full verification**

Run: `dotnet build -warnaserror && dotnet test && (cd src/Dashboard && npm test && npm run build)`
Expected:
- 0 warnings;
- Archiver.Tests 101, Tracker.Core.Tests 240 and Api.Tests all PASS;
- all Vitest files PASS;
- the dashboard builds.

- [ ] **Step 4: Live check on the real database**

Never run the Archiver.

1. **Start and check it's empty.**
   - Run `docker compose up -d --wait postgres`, then start `dotnet run --project src/AppHost --launch-profile http` in the background, logging to `<workspace>/apphost.log`.
   - Wait, with a Monitor until-loop of up to 5 minutes, until `curl -sf http://localhost:5190/api/tracker/status` succeeds.
   - Note its `counts`: the owner's tracker should be empty.
2. **Make test entries.** Use `-H "X-Tracker-Client: live-check" -H "Content-Type: application/json"` on every write.
   - Pick a posting id with `curl -s "http://localhost:5190/api/postings?pageSize=1"` and POST `{"postingId": "<id>"}` to `/api/tracker/applications`. This gives entry A.
   - POST `/api/tracker/applications/paste` with `{"title":"Live check .NET Developer","company":"Live Check Co","location":"Toronto, ON","postingText":"C# and Azure services, live check.","status":"Applied"}`. This gives entry B. Confirm its `data/inbox/*-live-check-co-*.txt` file exists and that `data/tracker/tracker.json` lists both entries.
3. **Check the protections.**
   - A POST without the header returns 403.
   - Find the API's own port in `src/Api/Properties/launchSettings.json` (the `http` profile). Then `curl -s -o /dev/null -w "%{http_code}" -H "Host: attacker.example" http://localhost:<port>/api/tracker/status` must print 400.
4. **Survive a rebuild.**
   - Run a real rebuild with `dotnet run --project src/Processor -c Release`; expect exit 0.
   - Wait with a Monitor until-loop, up to 3 minutes, until `GET /api/tracker/applications/<B>` has a non-null `market`.
   - Then confirm A and B both still exist, and that B's `market.live` is true.
5. **Clean up.**
   - DELETE A and B with the header, and delete the `Live Check Co` inbox file.
   - Only if step 1 showed an empty tracker, delete the two companies the test created (no entries use them):
     ```bash
     docker compose exec -T postgres psql -U tracker -d tracker -c "DELETE FROM companies c WHERE NOT EXISTS (SELECT 1 FROM applications a WHERE a.company_id = c.id)"
     ```
   - If step 1 showed the tracker was empty, delete `data/tracker/tracker.json`, so no test company can ever be restored. It's recreated on the next real change.
   - Run `dotnet run --project src/Processor -c Release` once more, so the test posting leaves the market data.
6. **Stop and record.** Stop the AppHost. Ledger `Task 11: live check passed (…)` with what you observed.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Document phase 2b decisions and tracker usage"
```

**After the merge (not part of this branch):** re-run `scripts/register-task.ps1` from `main`, then `.artifacts/processor/Processor.exe --report-only`. The published Processor then includes the `PostingMatch` change. Never run the Archiver.
