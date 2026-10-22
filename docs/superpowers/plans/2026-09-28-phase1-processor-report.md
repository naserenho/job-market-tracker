# Phase 1 Processor, Gap Analysis and Report Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A .NET 10 `Processor` console app that rebuilds a Postgres database from the whole raw archive on every run, interprets postings (scope, role, seniority, CAD salary, skills, flags, duplicates), compares demand with the owner's skill profile, and writes a one-page HTML report that can be published as a claude.ai page.

**Architecture:** Readers turn raw files into `RawPosting`s, run by run in chronological order. A `SightingCollector` keeps the latest content per posting plus the runs it was seen in. An `Interpreter` applies the deterministic rules, and a `Deduplicator` clusters cross-source copies. `RebuildWriter` truncates and bulk-loads Postgres (Npgsql binary COPY) in one transaction. The `Analyzer` reads the database back into a `ReportModel`, which `ReportRenderer` turns into an HTML fragment (for publishing) plus a full document (for local viewing).

**Tech Stack:** .NET 10, Microsoft.Extensions.Hosting, EF Core 10 + Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 + EFCore.NamingConventions 10.0.1, Npgsql binary COPY, PostgreSQL 17 (Docker Compose), xUnit, Testcontainers.PostgreSql 4.15.0.

**Spec:** `docs/superpowers/specs/2026-09-28-phase1-processor-report-design.md` (read it with this plan; phase 0 spec: `docs/superpowers/specs/2026-09-27-phase0-raw-archiver-design.md`)

## Global Constraints

- Target framework `net10.0`; new projects join `JobMarketTracker.slnx`. `Processor` must not reference `Archiver`; the raw folder layout is the contract.
- Nothing under `data/` and neither `.env` nor `config/profile.json` is ever committed.
- Postgres runs from `docker-compose.yml` as `postgres:17`, bound to `127.0.0.1:5434` (5432 is kept free for other tools and 5433 is taken on this machine). Database `tracker`, user `tracker`, password from `.env` (`POSTGRES_PASSWORD`).
- Connection string comes only from user-secrets `ConnectionStrings:Tracker` (Processor project) or env var `ConnectionStrings__Tracker`.
- Tables and columns are snake_case (EFCore.NamingConventions). Bulk tables are written with Npgsql binary COPY inside the rebuild transaction.
- Rebuild = one transaction: `TRUNCATE` all tables, then insert everything. `_dryrun` is never read.
- Salary is CAD only; hourly × 2080 = yearly; a range is kept only if yearly min ≥ 30,000, yearly max ≤ 500,000 and min ≤ max.
- Analysis window = postings sighted within `WindowDays` (30) up to the latest run, in scope, not duplicates, in the profile's target families. Profile weights: strong 1.0, working 0.6, learning 0.3, none 0. Gaps top 15, strengths top 10, matches top 20, companies top 20, salary cells with n < 5 shown as "not enough data". Trends only when history ≥ 14 days.
- Report: `data/reports/<run>.html` and `data/reports/latest.html` (full documents) plus `data/reports/latest.fragment.html` (publishable fragment: no `<!doctype>`, `<html>`, `<head>`, `<body>`). Title "GTA Job Market". Light palette on bare `:root`, dark under `@media (prefers-color-scheme: dark)` guarded by `:root:not([data-theme="light"])` and again under `:root[data-theme="dark"]`, `color-scheme: dark` in both dark blocks, explicit `body` background, ≥16px side gutter, no horizontal page scroll, no scripts. Only Google Fonts may be linked.
- Exit codes: 0 OK, 1 processing failed (previous `latest.html` kept), 2 config/profile invalid, 3 database unavailable.
- Every file the Processor writes is written via temp file + rename.

## Review Focus

1. **A posting that lists several cities or countries** (`Bellevue, Washington; Chicago; Toronto, Ontario, Canada`, `Remote (United States | Canada)`, `Burlington, Massachusetts`): it must be in scope only when a GTA option or a remote-Canada option truly exists, and never because a US town shares a GTA name. Owner: Task 2.
2. **Salary text with both a US and a Canadian range, or with stipends and bonuses near the salary** (`$500–$1,000 learning budget … $100,000 to $120,000`): the Canadian base range must win and small amounts must never become a salary. Owner: Task 4.
3. **Ambiguous skill words** (`react quickly`, `rest of the team`, `go to`, `R&D`, `JavaScript` vs `Java`, `NoSQL` vs `SQL`, `GitHub` vs `Git`): they must not create false demand. Owner: Task 5.
4. **Docker Desktop not running at 07:00 or Postgres still starting**: the Processor must exit 3 with a readable message, and the archiver's collection must be unaffected. Owner: Task 12 (Processor) and Task 13 (pipeline script).
5. **A second rebuild over the same archive**: it must produce identical rows (idempotent), and a crash mid-write must leave the previous database and report intact. Owner: Task 9 (transaction and idempotency test) and Task 12 (report only written after success).

---

## File Structure

```
job-market-tracker/
  docker-compose.yml                         Task 1
  .env.example                               Task 1
  config/processor.json                      Task 2 (GTA places, company names, Job Bank, window)
  config/skills.json                         Task 5
  config/profile.example.json                Task 10
  config/profile.json                        Task 12 (gitignored; owner's real profile)
  scripts/run-pipeline.cmd                   Task 13
  scripts/register-task.ps1                  Task 13 (modified)
  docs/decisions/0004..0006-*.md             Task 13
  src/Processor/
    Processor.csproj                         Task 1
    Program.cs                               Task 12
    Configuration/ProcessorOptions.cs        Task 2
    Configuration/RepoRoot.cs                Task 1
    Reading/RawPosting.cs                    Task 1  RawPosting, SalaryRange, SalaryPeriod
    Reading/HtmlText.cs                      Task 1
    Reading/JsonFiles.cs                     Task 1
    Reading/Json.cs                          Task 6  JsonElement helpers
    Reading/SourceReaders.cs                 Task 6  Adzuna/Greenhouse/Lever/Ashby readers
    Reading/InboxReader.cs                   Task 6
    Reading/Archive.cs                       Task 6  runs, postings, aggregates
    Reading/JobBankReader.cs                 Task 8
    Interpreting/ScopeRules.cs               Task 2
    Interpreting/TitleRules.cs               Task 3  RoleFamily, Seniority
    Interpreting/SalaryParser.cs             Task 4
    Interpreting/SkillCatalog.cs             Task 5
    Interpreting/Posting.cs                  Task 7  Posting, Interpreter, SightingCollector
    Interpreting/Deduplicator.cs             Task 7
    Data/Entities.cs                         Task 9
    Data/TrackerDb.cs                        Task 9  DbContext + design-time factory
    Data/Migrations/*                        Task 9  (generated)
    Data/RebuildWriter.cs                    Task 9
    Rebuild/RebuildService.cs                Task 12
    Analysis/Profile.cs                      Task 10
    Analysis/ReportModel.cs                  Task 10
    Analysis/Analyzer.cs                     Task 10
    Analysis/AnalysisLoader.cs               Task 12 (DB → AnalysisPosting)
    Reporting/ReportRenderer.cs              Task 11
    Reporting/ReportWriter.cs                Task 11
  tests/Processor.Tests/
    TestSupport/TempDir.cs                   Task 1
    TestSupport/PostgresFixture.cs           Task 9
    (one test file per production file above)
```

Commands run from the repo root. **Code containing backslashes must be written with the Write/Edit tools, not shell heredocs** (the shell collapses `\\`).

---

### Task 1: Scaffold, Compose file and shared primitives

**Files:**
- Create: `src/Processor/Processor.csproj`, `tests/Processor.Tests/Processor.Tests.csproj` (via `dotnet new`)
- Create: `src/Processor/Configuration/RepoRoot.cs`, `src/Processor/Reading/RawPosting.cs`, `src/Processor/Reading/HtmlText.cs`, `src/Processor/Reading/JsonFiles.cs`
- Create: `docker-compose.yml`, `.env.example`; Modify: `.gitignore`
- Create: `tests/Processor.Tests/TestSupport/TempDir.cs`
- Test: `tests/Processor.Tests/Reading/HtmlTextTests.cs`, `JsonFilesTests.cs`, `RawPostingTests.cs`

**Interfaces:**
- Produces:
  - `RepoRoot.Find(string startDirectory) : string?` (folder holding `config/sources.json`)
  - `enum SalaryPeriod { Year, Hour }`; `record SalaryRange(decimal Min, decimal Max, SalaryPeriod Period)` with `YearlyMin`, `YearlyMax`, `YearlyMidpoint`, `IsPlausible`, const `HoursPerYear = 2080`
  - `record RawPosting(string Source, string SourceId, string RunId, string Company, string Title, string Url, string Location, bool? Remote, string? Country, string Description, bool IsFullText, SalaryRange? Salary)` with `string Key` = `"{Source}:{SourceId}"`
  - `HtmlText.ToPlain(string? html) : string`
  - `JsonFiles.IsJson(string path)`, `JsonFiles.Load(string path) : JsonDocument` (`.json` or `.json.gz`), `JsonFiles.BaseName(string path)`
  - `TempDir` test helper

- [ ] **Step 1: Scaffold projects and packages**

```bash
dotnet new console -n Processor -o src/Processor -f net10.0
dotnet new xunit -n Processor.Tests -o tests/Processor.Tests -f net10.0
dotnet sln add src/Processor tests/Processor.Tests
dotnet add tests/Processor.Tests reference src/Processor
dotnet add src/Processor package Microsoft.Extensions.Hosting --version 10.0.12
dotnet add src/Processor package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.3
dotnet add src/Processor package EFCore.NamingConventions --version 10.0.1
dotnet add src/Processor package Microsoft.EntityFrameworkCore.Design --version 10.0.12
dotnet add tests/Processor.Tests package Testcontainers.PostgreSql --version 4.15.0
rm tests/Processor.Tests/UnitTest1.cs
dotnet build
```
Expected: build succeeds. If NuGet reports a version conflict for `Microsoft.EntityFrameworkCore*`, let the Design package follow the EF Core version Npgsql pulls in and record a ledger ruling.

- [ ] **Step 2: Compose file, env example, gitignore**

`docker-compose.yml`:
```yaml
services:
  postgres:
    image: postgres:17
    container_name: jobtracker-postgres
    environment:
      POSTGRES_DB: tracker
      POSTGRES_USER: tracker
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?Set POSTGRES_PASSWORD in .env (see .env.example)}
    ports:
      - "127.0.0.1:5434:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U tracker -d tracker"]
      interval: 5s
      timeout: 3s
      retries: 20
    restart: unless-stopped

volumes:
  pgdata:
```

`.env.example`:
```
# Copy to .env and choose a password. The same password goes into the Processor's user-secrets:
#   dotnet user-secrets set "ConnectionStrings:Tracker" "Host=localhost;Port=5434;Database=tracker;Username=tracker;Password=<password>" --project src/Processor
POSTGRES_PASSWORD=change-me
```

Append to `.gitignore`:
```

# Owner's real skill profile (config/profile.example.json shows the format)
config/profile.json
```
(`.env` is already ignored.)

- [ ] **Step 3: Write the failing tests**

`tests/Processor.Tests/TestSupport/TempDir.cs`:
```csharp
namespace Processor.Tests.TestSupport;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "processor-tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string relative, string content)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}
```

`tests/Processor.Tests/Reading/HtmlTextTests.cs`:
```csharp
using Processor.Reading;

namespace Processor.Tests.Reading;

public class HtmlTextTests
{
    [Fact]
    public void ToPlain_decodes_greenhouse_double_encoded_html_and_keeps_line_breaks()
    {
        var html = "&lt;p&gt;Build with &lt;strong&gt;C#&lt;/strong&gt; &amp;amp; .NET&lt;/p&gt;&lt;ul&gt;&lt;li&gt;Azure&lt;/li&gt;&lt;/ul&gt;";
        Assert.Equal("Build with C# & .NET\nAzure", HtmlText.ToPlain(html));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("plain  text", "plain text")]
    public void ToPlain_handles_empty_and_plain_input(string? input, string expected) =>
        Assert.Equal(expected, HtmlText.ToPlain(input));
}
```

`tests/Processor.Tests/Reading/JsonFilesTests.cs`:
```csharp
using System.IO.Compression;
using System.Text;
using Processor.Reading;
using Processor.Tests.TestSupport;

namespace Processor.Tests.Reading;

public class JsonFilesTests
{
    [Fact]
    public void Load_reads_plain_and_gzipped_json()
    {
        using var tmp = new TempDir();
        var plain = tmp.File("a.json", "{\"n\":1}");
        var gz = Path.Combine(tmp.Path, "b.json.gz");
        using (var file = File.Create(gz))
        using (var zip = new GZipStream(file, CompressionLevel.Fastest))
            zip.Write(Encoding.UTF8.GetBytes("{\"n\":2}"));

        using var a = JsonFiles.Load(plain);
        using var b = JsonFiles.Load(gz);

        Assert.Equal(1, a.RootElement.GetProperty("n").GetInt32());
        Assert.Equal(2, b.RootElement.GetProperty("n").GetInt32());
    }

    [Theory]
    [InlineData("x/greenhouse-faire.json", "greenhouse-faire", true)]
    [InlineData("x/ashby-cohere.json.gz", "ashby-cohere", true)]
    [InlineData("x/notes.txt", "notes.txt", false)]
    [InlineData("x/a.json.tmp", "a.json.tmp", false)]
    public void Names_and_extensions(string path, string baseName, bool isJson)
    {
        Assert.Equal(baseName, JsonFiles.BaseName(path));
        Assert.Equal(isJson, JsonFiles.IsJson(path));
    }
}
```

`tests/Processor.Tests/Reading/RawPostingTests.cs`:
```csharp
using Processor.Reading;

namespace Processor.Tests.Reading;

public class RawPostingTests
{
    [Fact]
    public void Hourly_salary_annualizes_at_2080_hours()
    {
        var s = new SalaryRange(50, 60, SalaryPeriod.Hour);
        Assert.Equal(104_000m, s.YearlyMin);
        Assert.Equal(124_800m, s.YearlyMax);
        Assert.Equal(114_400m, s.YearlyMidpoint);
        Assert.True(s.IsPlausible);
    }

    [Theory]
    [InlineData(500, 1000, false)]       // stipend, not a salary
    [InlineData(120_000, 150_000, true)]
    [InlineData(150_000, 120_000, false)] // inverted
    [InlineData(400_000, 900_000, false)] // above the 500k guard
    public void Plausibility_guard(int min, int max, bool plausible) =>
        Assert.Equal(plausible, new SalaryRange(min, max, SalaryPeriod.Year).IsPlausible);

    [Fact]
    public void Key_combines_source_and_id() =>
        Assert.Equal("lever:abc", new RawPosting("lever", "abc", "r1", "Co", "T", "", "", null, null, "", true, null).Key);
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/Processor.Tests`
Expected: build FAILS — `Processor.Reading` types do not exist.

- [ ] **Step 5: Implement**

`src/Processor/Configuration/RepoRoot.cs`:
```csharp
namespace Processor.Configuration;

public static class RepoRoot
{
    // Walks up from startDirectory to the first folder containing config/sources.json (shared with the archiver).
    public static string? Find(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "config", "sources.json")))
                return dir.FullName;
        return null;
    }
}
```

`src/Processor/Reading/RawPosting.cs`:
```csharp
namespace Processor.Reading;

public enum SalaryPeriod { Year, Hour }

public sealed record SalaryRange(decimal Min, decimal Max, SalaryPeriod Period)
{
    public const decimal HoursPerYear = 2080;

    public decimal YearlyMin => Period == SalaryPeriod.Hour ? Min * HoursPerYear : Min;
    public decimal YearlyMax => Period == SalaryPeriod.Hour ? Max * HoursPerYear : Max;
    public decimal YearlyMidpoint => (YearlyMin + YearlyMax) / 2;

    // Guards against parse errors and non-salary amounts (stipends, bonuses).
    public bool IsPlausible => Min <= Max && YearlyMin >= 30_000 && YearlyMax <= 500_000;
}

// One posting exactly as one source described it in one run. Description is plain text.
public sealed record RawPosting(
    string Source,
    string SourceId,
    string RunId,
    string Company,
    string Title,
    string Url,
    string Location,
    bool? Remote,
    string? Country,
    string Description,
    bool IsFullText,
    SalaryRange? Salary)
{
    public string Key => $"{Source}:{SourceId}";
}
```

`src/Processor/Reading/HtmlText.cs`:
```csharp
using System.Net;
using System.Text.RegularExpressions;

namespace Processor.Reading;

public static partial class HtmlText
{
    // Greenhouse double-encodes its HTML (&lt;p&gt;), so decode, strip tags, then decode entities again.
    public static string ToPlain(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var text = WebUtility.HtmlDecode(html);
        text = BlockTags().Replace(text, "\n");
        text = Tags().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        text = Spaces().Replace(text, " ");
        text = LineBreaks().Replace(text, "\n");
        return text.Trim();
    }

    [GeneratedRegex(@"<\s*(?:br|/p|/div|/li|/h[1-6]|li)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTags();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t\u00A0]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s*\n\s*")]
    private static partial Regex LineBreaks();
}
```

`src/Processor/Reading/JsonFiles.cs`:
```csharp
using System.IO.Compression;
using System.Text.Json;

namespace Processor.Reading;

public static class JsonFiles
{
    public static bool IsJson(string path) =>
        path.EndsWith(".json", StringComparison.Ordinal) || path.EndsWith(".json.gz", StringComparison.Ordinal);

    // Raw runs may be compressed later to save disk; readers accept both forms.
    public static JsonDocument Load(string path)
    {
        using var file = File.OpenRead(path);
        using var stream = path.EndsWith(".gz", StringComparison.Ordinal)
            ? (Stream)new GZipStream(file, CompressionMode.Decompress)
            : file;
        return JsonDocument.Parse(stream);
    }

    public static string BaseName(string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(".json.gz", StringComparison.Ordinal)) return name[..^8];
        if (name.EndsWith(".json", StringComparison.Ordinal)) return name[..^5];
        return name;
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Processor.Tests`
Expected: all Task 1 tests PASS. Then `dotnet test` (whole solution) — archiver tests still pass.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Scaffold Processor with Compose file and reading primitives"
```

---

### Task 2: Scope rules (GTA and remote-Canada)

**Files:**
- Create: `src/Processor/Configuration/ProcessorOptions.cs`, `src/Processor/Interpreting/ScopeRules.cs`, `config/processor.json`
- Test: `tests/Processor.Tests/Interpreting/ScopeRulesTests.cs`, `tests/Processor.Tests/Configuration/ProcessorOptionsTests.cs`

**Interfaces:**
- Consumes: `RawPosting` (Task 1), `RepoRoot` (Task 1)
- Produces:
  - `ProcessorOptions { GtaOptions Gta; Dictionary<string,string> CompanyNames; JobBankOptions JobBank; int WindowDays }` with `static ProcessorOptions Load(string path)` and `string CompanyName(string slug)`
  - `GtaOptions { Dictionary<string,string> Places; List<string> NeedsCanadianContext }`
  - `JobBankOptions { string EconomicRegion; Dictionary<string,string> NocCodes }`
  - `record Scope(string? City, bool IsGta, bool IsRemoteCanada)` with `bool InScope`
  - `ScopeRules(GtaOptions)` with `Scope Classify(RawPosting)` and `bool IsCanadian(RawPosting)`

- [ ] **Step 1: Create the committed configuration**

`config/processor.json`:
```json
{
  "WindowDays": 30,
  "Gta": {
    "Places": {
      "Toronto": "Toronto",
      "Etobicoke": "Toronto",
      "Scarborough": "Toronto",
      "North York": "Toronto",
      "East York": "Toronto",
      "Greater Toronto Area": "Toronto",
      "GTA": "Toronto",
      "Mississauga": "Mississauga",
      "Brampton": "Brampton",
      "Caledon": "Caledon",
      "Peel region": "Peel Region",
      "Markham": "Markham",
      "Unionville": "Markham",
      "Thornhill": "Markham",
      "Vaughan": "Vaughan",
      "Woodbridge": "Vaughan",
      "Concord": "Vaughan",
      "Richmond Hill": "Richmond Hill",
      "Newmarket": "Newmarket",
      "Aurora": "Aurora",
      "Stouffville": "Whitchurch-Stouffville",
      "King City": "King",
      "York region": "York Region",
      "Oakville": "Oakville",
      "Burlington": "Burlington",
      "Milton": "Milton",
      "Halton Hills": "Halton Hills",
      "Halton region": "Halton Region",
      "Pickering": "Pickering",
      "Ajax": "Ajax",
      "Whitby": "Whitby",
      "Oshawa": "Oshawa",
      "Clarington": "Clarington",
      "Durham region": "Durham Region"
    },
    "NeedsCanadianContext": [ "Burlington", "Milton", "Aurora", "Newmarket", "Concord", "Woodbridge", "Unionville", "Ajax", "King City", "Caledon", "GTA" ]
  },
  "CompanyNames": {
    "1password": "1Password",
    "ada18": "Ada",
    "d2l": "D2L",
    "neofinancial": "Neo Financial",
    "pointclickcare": "PointClickCare",
    "waveapps": "Wave",
    "achievers": "Achievers",
    "wattpad": "Wattpad",
    "benevity": "Benevity",
    "clearco": "Clearco",
    "cohere": "Cohere",
    "float": "Float",
    "hopper": "Hopper",
    "jobber": "Jobber",
    "klue": "Klue",
    "loopio": "Loopio",
    "miovision": "Miovision",
    "ramp": "Ramp",
    "snowflake": "Snowflake",
    "trulioo": "Trulioo",
    "wealthsimple": "Wealthsimple"
  },
  "JobBank": {
    "EconomicRegion": "Toronto Region",
    "NocCodes": {
      "21230": "Computer systems developers and programmers",
      "21231": "Software engineers and designers",
      "21232": "Software developers and programmers",
      "21234": "Web developers and programmers",
      "21211": "Data scientists",
      "21220": "Cybersecurity specialists",
      "21221": "Business systems specialists",
      "21222": "Information systems specialists",
      "21223": "Database analysts and data administrators",
      "21311": "Computer engineers",
      "20012": "Computer and information systems managers"
    }
  }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Processor.Tests/Interpreting/ScopeRulesTests.cs`:
```csharp
using Processor.Configuration;
using Processor.Interpreting;
using Processor.Reading;

namespace Processor.Tests.Interpreting;

public class ScopeRulesTests
{
    private static readonly ScopeRules Rules = new(RepoConfig.Options().Gta);

    private static RawPosting Posting(string location, string source = "greenhouse", bool? remote = null, string? country = null, string title = "Software Engineer", string description = "") =>
        new(source, "1", "r1", "Co", title, "", location, remote, country, description, true, null);

    [Theory]
    [InlineData("Toronto, Ontario, Canada", "Toronto")]
    [InlineData("Toronto, ON, CA", "Toronto")]
    [InlineData("Oakville, Ontario - Canada; Toronto, Ontario - Canada; Waterloo, Ontario - Canada", "Oakville")]
    [InlineData("Bellevue, Washington; Chicago, Illinois; Toronto, Ontario, Canada", "Toronto")]
    [InlineData("Kitchener-Waterloo, ON; Toronto, ON", "Toronto")]
    [InlineData("Mississauga, Ontario", "Mississauga")]
    [InlineData("Burlington, Ontario", "Burlington")]
    public void Gta_locations_resolve_to_their_municipality(string location, string city)
    {
        var scope = Rules.Classify(Posting(location));
        Assert.True(scope.IsGta);
        Assert.Equal(city, scope.City);
        Assert.True(scope.InScope);
    }

    [Fact]
    public void Adzuna_neighbourhoods_and_regions_map_to_municipalities()
    {
        Assert.Equal("Vaughan", Rules.Classify(Posting("Woodbridge, York region; Canada, Ontario, York region", "adzuna", country: "CA")).City);
        Assert.Equal("Toronto", Rules.Classify(Posting("Regent Park, City of Toronto; Canada, Ontario, Toronto", "adzuna", country: "CA")).City);
        Assert.Equal("Peel Region", Rules.Classify(Posting("Canada, Ontario, Peel region", "adzuna", country: "CA")).City);
    }

    [Theory]
    [InlineData("Burlington, Massachusetts")]
    [InlineData("Aurora, Colorado")]
    [InlineData("Vancouver, British Columbia, Canada")]
    [InlineData("New York, New York")]
    public void Non_gta_places_are_not_gta(string location) =>
        Assert.False(Rules.Classify(Posting(location)).IsGta);

    [Fact]
    public void Remote_open_to_canada_is_in_scope()
    {
        Assert.True(Rules.Classify(Posting("Remote (United States | Canada)", "ashby", remote: true)).IsRemoteCanada);
        Assert.True(Rules.Classify(Posting("Remote - Canada")).IsRemoteCanada);
        Assert.True(Rules.Classify(Posting("Canada", "lever", remote: true, country: "CA")).IsRemoteCanada);
    }

    [Fact]
    public void Remote_outside_canada_is_out_of_scope()
    {
        var scope = Rules.Classify(Posting("Remote, USA", "lever", remote: true, country: "US"));
        Assert.False(scope.IsRemoteCanada);
        Assert.False(scope.InScope);
    }

    [Fact]
    public void Adzuna_remote_is_read_from_the_snippet()
    {
        Assert.True(Rules.Classify(Posting("Canada; Canada", "adzuna", country: "CA", description: "This is a fully remote role for Canadian residents.")).IsRemoteCanada);
        Assert.False(Rules.Classify(Posting("Canada; Canada", "adzuna", country: "CA", description: "Hybrid, 3 days in office.")).IsRemoteCanada);
    }

    [Fact]
    public void Non_gta_city_is_kept_as_the_first_location_segment() =>
        Assert.Equal("Vancouver", Rules.Classify(Posting("Vancouver, British Columbia, Canada")).City);
}
```

`tests/Processor.Tests/Configuration/ProcessorOptionsTests.cs`:
```csharp
using Processor.Configuration;

namespace Processor.Tests.Configuration;

// Loads the committed config/ files so tests pin their real content.
public static class RepoConfig
{
    public static string Root { get; } = RepoRoot.Find(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Repo root not found.");
    public static string PathOf(string file) => Path.Combine(Root, "config", file);
    public static ProcessorOptions Options() => ProcessorOptions.Load(PathOf("processor.json"));
}

public class ProcessorOptionsTests
{
    [Fact]
    public void Committed_processor_json_loads()
    {
        var o = RepoConfig.Options();
        Assert.Equal(30, o.WindowDays);
        Assert.Equal("Toronto", o.Gta.Places["Toronto"]);
        Assert.Equal("Toronto Region", o.JobBank.EconomicRegion);
        Assert.Contains("21232", o.JobBank.NocCodes.Keys);
    }

    [Theory]
    [InlineData("1password", "1Password")]
    [InlineData("stripe", "Stripe")]
    public void CompanyName_uses_the_map_then_capitalizes_the_slug(string slug, string expected) =>
        Assert.Equal(expected, RepoConfig.Options().CompanyName(slug));
}
```

Add `using Processor.Tests.Configuration;` to `ScopeRulesTests.cs` (for `RepoConfig`).

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Processor.Tests`
Expected: build FAILS — `ProcessorOptions`, `ScopeRules` missing.

- [ ] **Step 4: Implement**

`src/Processor/Configuration/ProcessorOptions.cs`:
```csharp
using System.Text.Json;

namespace Processor.Configuration;

// config/processor.json: interpretation settings that change without code changes.
public sealed class ProcessorOptions
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    public int WindowDays { get; set; } = 30;
    public GtaOptions Gta { get; set; } = new();
    public Dictionary<string, string> CompanyNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public JobBankOptions JobBank { get; set; } = new();

    public static ProcessorOptions Load(string path)
    {
        var options = JsonSerializer.Deserialize<ProcessorOptions>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException($"{path} is empty.");
        options.CompanyNames = new Dictionary<string, string>(options.CompanyNames, StringComparer.OrdinalIgnoreCase);
        if (options.Gta.Places.Count == 0) throw new InvalidDataException($"{path}: Gta.Places must list the GTA municipalities.");
        if (options.WindowDays < 1) throw new InvalidDataException($"{path}: WindowDays must be at least 1.");
        return options;
    }

    // Board files only carry a slug; prefer the configured display name, else capitalize the slug.
    public string CompanyName(string slug) =>
        CompanyNames.TryGetValue(slug, out var name) ? name : slug.Length == 0 ? slug : char.ToUpperInvariant(slug[0]) + slug[1..];
}

public sealed class GtaOptions
{
    // Place name as written in postings → municipality or region shown in reports.
    public Dictionary<string, string> Places { get; set; } = [];

    // Names shared with places outside Canada (Burlington MA, Aurora CO) count only with Canadian context.
    public List<string> NeedsCanadianContext { get; set; } = [];
}

public sealed class JobBankOptions
{
    public string EconomicRegion { get; set; } = "Toronto Region";
    public Dictionary<string, string> NocCodes { get; set; } = [];
}
```

`src/Processor/Interpreting/ScopeRules.cs`:
```csharp
using System.Text.RegularExpressions;
using Processor.Configuration;
using Processor.Reading;

namespace Processor.Interpreting;

public sealed record Scope(string? City, bool IsGta, bool IsRemoteCanada)
{
    public bool InScope => IsGta || IsRemoteCanada;
}

public sealed partial class ScopeRules
{
    private readonly (Regex Pattern, string City, bool NeedsContext)[] _places;

    public ScopeRules(GtaOptions gta)
    {
        var ambiguous = new HashSet<string>(gta.NeedsCanadianContext, StringComparer.OrdinalIgnoreCase);
        _places = [.. gta.Places.Select(p => (
            new Regex($@"(?<![\p{{L}}]){Regex.Escape(p.Key)}(?![\p{{L}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            p.Value,
            ambiguous.Contains(p.Key)))];
    }

    public Scope Classify(RawPosting p)
    {
        var city = GtaCity(p);
        var remote = p.Remote == true
            || RemoteWord().IsMatch(p.Location)
            || RemoteWord().IsMatch(p.Title)
            || (p.Source == "adzuna" && RemoteSnippet().IsMatch(p.Description));
        return new Scope(city ?? FirstSegment(p.Location), city is not null, remote && IsCanadian(p));
    }

    public bool IsCanadian(RawPosting p) =>
        p.Country == "CA" || CanadaWord().IsMatch(p.Location) || ProvinceCode().IsMatch(p.Location);

    // The GTA place that appears earliest in the location text; ambiguous names need Canadian context in their segment.
    private string? GtaCity(RawPosting p)
    {
        string? best = null;
        var bestIndex = int.MaxValue;
        foreach (var segment in p.Location.Split(';'))
        {
            var offset = p.Location.IndexOf(segment, StringComparison.Ordinal);
            foreach (var (pattern, city, needsContext) in _places)
            {
                var m = pattern.Match(segment);
                if (!m.Success || offset + m.Index >= bestIndex) continue;
                if (needsContext && p.Country != "CA" && !CanadaWord().IsMatch(segment) && !ProvinceCode().IsMatch(segment)) continue;
                best = city;
                bestIndex = offset + m.Index;
            }
        }
        return best;
    }

    private static string? FirstSegment(string location)
    {
        var first = location.Split([';', ','], 2)[0].Trim();
        return first.Length == 0 ? null : first.Length > 80 ? first[..80] : first;
    }

    [GeneratedRegex(@"\bremote\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteWord();

    [GeneratedRegex(@"\b(?:fully remote|100% remote|remote[- ]first|remote position|remote role|remote opportunity|work from home|work-from-home|WFH)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteSnippet();

    [GeneratedRegex(@"\b(?:canada|canadian|ontario)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CanadaWord();

    [GeneratedRegex(@"(?:,|-)\s*(?:ON|BC|AB|QC|NS|MB|SK|NB|NL|PE)\b")]
    private static partial Regex ProvinceCode();
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Processor.Tests`
Expected: all scope and options tests PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add processor config and GTA / remote-Canada scope rules"
```

---

### Task 3: Title rules (role family and seniority)

**Files:**
- Create: `src/Processor/Interpreting/TitleRules.cs`
- Test: `tests/Processor.Tests/Interpreting/TitleRulesTests.cs`

**Interfaces:**
- Produces:
  - `enum RoleFamily { Dotnet, Fullstack, SoftwareEngineer, LeadManager, Other }`
  - `enum Seniority { Unspecified, Junior, Intermediate, Senior, StaffPrincipal, Lead, Manager }`
  - `TitleRules.Family(string title) : RoleFamily`, `TitleRules.Seniority(string title) : Seniority`
  - `TitleRules.FamilyKey(RoleFamily)` → `"dotnet" | "fullstack" | "software_engineer" | "lead_manager" | "other"` and `TitleRules.ParseFamily(string key) : RoleFamily?`

- [ ] **Step 1: Write the failing tests**

`tests/Processor.Tests/Interpreting/TitleRulesTests.cs`:
```csharp
using Processor.Interpreting;

namespace Processor.Tests.Interpreting;

public class TitleRulesTests
{
    [Theory]
    [InlineData(".NET Developer", RoleFamily.Dotnet)]
    [InlineData("Senior C# Software Engineer", RoleFamily.Dotnet)]
    [InlineData("ASP.NET Full Stack Developer", RoleFamily.Dotnet)]
    [InlineData("Lead .NET Engineer", RoleFamily.Dotnet)]
    [InlineData("Full Stack Developer (React/Node)", RoleFamily.Fullstack)]
    [InlineData("Senior Fullstack Engineer", RoleFamily.Fullstack)]
    [InlineData("Software Engineer, Payments", RoleFamily.SoftwareEngineer)]
    [InlineData("Senior Backend Engineer", RoleFamily.SoftwareEngineer)]
    [InlineData("Staff Software Developer", RoleFamily.SoftwareEngineer)]
    [InlineData("Web Developer", RoleFamily.SoftwareEngineer)]
    [InlineData("Engineering Manager, Platform", RoleFamily.LeadManager)]
    [InlineData("Software Engineering Manager", RoleFamily.LeadManager)]
    [InlineData("Tech Lead", RoleFamily.LeadManager)]
    [InlineData("Director of Engineering", RoleFamily.LeadManager)]
    [InlineData("Business Developer", RoleFamily.Other)]
    [InlineData("Senior Data Engineer", RoleFamily.Other)]
    [InlineData("Solutions Engineer", RoleFamily.Other)]
    [InlineData("Product Manager", RoleFamily.Other)]
    [InlineData("Site Reliability Engineer", RoleFamily.Other)]
    public void Family_first_match_wins(string title, RoleFamily expected) =>
        Assert.Equal(expected, TitleRules.Family(title));

    [Theory]
    [InlineData("Senior .NET Developer", Seniority.Senior)]
    [InlineData("Sr. Software Engineer", Seniority.Senior)]
    [InlineData("Software Engineer III", Seniority.Senior)]
    [InlineData("Software Engineer II", Seniority.Intermediate)]
    [InlineData("Intermediate Full Stack Developer", Seniority.Intermediate)]
    [InlineData("Junior Developer", Seniority.Junior)]
    [InlineData("Software Engineer Intern", Seniority.Junior)]
    [InlineData("Software Engineer I", Seniority.Junior)]
    [InlineData("Staff Engineer", Seniority.StaffPrincipal)]
    [InlineData("Principal Software Architect", Seniority.StaffPrincipal)]
    [InlineData("Lead Software Engineer", Seniority.Lead)]
    [InlineData("Senior Engineering Manager", Seniority.Manager)]
    [InlineData("Software Engineer", Seniority.Unspecified)]
    public void Seniority_from_title(string title, Seniority expected) =>
        Assert.Equal(expected, TitleRules.Seniority(title));

    [Fact]
    public void Family_keys_round_trip()
    {
        foreach (var family in Enum.GetValues<RoleFamily>())
            Assert.Equal(family, TitleRules.ParseFamily(TitleRules.FamilyKey(family)));
        Assert.Null(TitleRules.ParseFamily("astronaut"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Processor.Tests`
Expected: build FAILS — `TitleRules` missing.

- [ ] **Step 3: Implement**

`src/Processor/Interpreting/TitleRules.cs`:
```csharp
using System.Text.RegularExpressions;

namespace Processor.Interpreting;

public enum RoleFamily { Dotnet, Fullstack, SoftwareEngineer, LeadManager, Other }

public enum Seniority { Unspecified, Junior, Intermediate, Senior, StaffPrincipal, Lead, Manager }

public static partial class TitleRules
{
    // First match wins: a ".NET Full Stack Developer" is .NET, a "Lead Software Engineer" is a software engineer (seniority lead).
    public static RoleFamily Family(string title)
    {
        if (Dotnet().IsMatch(title)) return RoleFamily.Dotnet;
        if (Fullstack().IsMatch(title)) return RoleFamily.Fullstack;
        if (SoftwareEngineer().IsMatch(title) && !NotSoftwareEngineer().IsMatch(title)) return RoleFamily.SoftwareEngineer;
        if (LeadManager().IsMatch(title)) return RoleFamily.LeadManager;
        return RoleFamily.Other;
    }

    public static Seniority Seniority(string title)
    {
        if (Manager().IsMatch(title)) return Interpreting.Seniority.Manager;
        if (Lead().IsMatch(title)) return Interpreting.Seniority.Lead;
        if (Staff().IsMatch(title)) return Interpreting.Seniority.StaffPrincipal;
        if (Senior().IsMatch(title)) return Interpreting.Seniority.Senior;
        if (Intermediate().IsMatch(title)) return Interpreting.Seniority.Intermediate;
        if (Junior().IsMatch(title)) return Interpreting.Seniority.Junior;
        return Interpreting.Seniority.Unspecified;
    }

    public static string FamilyKey(RoleFamily family) => family switch
    {
        RoleFamily.Dotnet => "dotnet",
        RoleFamily.Fullstack => "fullstack",
        RoleFamily.SoftwareEngineer => "software_engineer",
        RoleFamily.LeadManager => "lead_manager",
        _ => "other",
    };

    public static RoleFamily? ParseFamily(string key) =>
        Enum.GetValues<RoleFamily>().Cast<RoleFamily?>().FirstOrDefault(f => FamilyKey(f!.Value) == key);

    [GeneratedRegex(@"\.net\b|\bdotnet\b|\bc#", RegexOptions.IgnoreCase)]
    private static partial Regex Dotnet();

    [GeneratedRegex(@"\bfull[\s-]?stack\b", RegexOptions.IgnoreCase)]
    private static partial Regex Fullstack();

    [GeneratedRegex(@"\b(?:software|back[\s-]?end|front[\s-]?end|web|application|platform|mobile|ios|android|java|python|react|node(?:\.js)?|golang|ruby|php|typescript|javascript|angular|product)\b.{0,25}?\b(?:engineer|developer|programmer)\b|\b(?:developer|programmer)\b|^\s*(?:senior|sr\.?|staff|principal|lead)?\s*engineer\b", RegexOptions.IgnoreCase)]
    private static partial Regex SoftwareEngineer();

    [GeneratedRegex(@"\bbusiness develop|\bsales\b|\bsupport\b|\bdata engineer|\bmachine learning|\bml engineer|\bsecurity\b|\bsite reliability|\bsre\b|\bdevops\b|\bqa\b|\btest|\bhardware|\bmechanical|\belectrical|\bsolutions? engineer|\bfield engineer|\bnetwork", RegexOptions.IgnoreCase)]
    private static partial Regex NotSoftwareEngineer();

    [GeneratedRegex(@"\bengineering manager\b|\bmanager,?\s+(?:of\s+)?(?:software|engineering|development)\b|\bsoftware (?:development |engineering )?manager\b|\bdevelopment manager\b|\bdirector,?\s+(?:of\s+)?(?:software\s+)?engineering\b|\bhead of (?:engineering|development|software)\b|\bvp,?\s+(?:of\s+)?engineering\b|\btech(?:nical)? lead\b|\bteam lead\b|\b(?:development|dev) lead\b|\blead developer\b|\bchief technology\b|\bcto\b", RegexOptions.IgnoreCase)]
    private static partial Regex LeadManager();

    [GeneratedRegex(@"\b(?:manager|director|head of|vp|vice president|chief|cto)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Manager();

    [GeneratedRegex(@"\blead\b", RegexOptions.IgnoreCase)]
    private static partial Regex Lead();

    [GeneratedRegex(@"\b(?:staff|principal|distinguished|architect)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Staff();

    [GeneratedRegex(@"\b(?:senior|sr\.?|iii|iv)(?:\s|$|\b)", RegexOptions.IgnoreCase)]
    private static partial Regex Senior();

    [GeneratedRegex(@"\b(?:intermediate|mid[\s-]?level|ii)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Intermediate();

    [GeneratedRegex(@"\b(?:junior|jr\.?|intern|internship|co-?op|new grad|graduate|entry[\s-]level)\b|\s+i\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Junior();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Processor.Tests`
Expected: all title tests PASS. If a case fails, fix the regex (never the expectation) and note the change in the ledger.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add role family and seniority rules from job titles"
```

---

### Task 4: CAD salary parser

**Files:**
- Create: `src/Processor/Interpreting/SalaryParser.cs`
- Test: `tests/Processor.Tests/Interpreting/SalaryParserTests.cs`

**Interfaces:**
- Consumes: `SalaryRange`, `SalaryPeriod` (Task 1)
- Produces: `SalaryParser.FromText(string text, bool postingInCanada) : SalaryRange?` — returns only plausible CAD ranges.

- [ ] **Step 1: Write the failing tests**

`tests/Processor.Tests/Interpreting/SalaryParserTests.cs`:
```csharp
using Processor.Interpreting;
using Processor.Reading;

namespace Processor.Tests.Interpreting;

public class SalaryParserTests
{
    private static void AssertRange(SalaryRange? actual, decimal min, decimal max, SalaryPeriod period = SalaryPeriod.Year)
    {
        Assert.NotNull(actual);
        Assert.Equal(new SalaryRange(min, max, period), actual);
    }

    [Fact]
    public void Plain_dollar_range_in_a_canadian_posting() =>
        AssertRange(SalaryParser.FromText("The base salary range for this role in Toronto is $120,000 - $150,000.", true), 120_000, 150_000);

    [Fact]
    public void Canadian_range_wins_over_the_us_range() =>
        AssertRange(SalaryParser.FromText("US: $150,000–$190,000 USD\nCanada: CA$130,000–CA$165,000", true), 130_000, 165_000);

    [Fact]
    public void K_suffix_on_both_sides() =>
        AssertRange(SalaryParser.FromText("Salary range: $180K - $220K", true), 180_000, 220_000);

    [Fact]
    public void K_suffix_only_on_the_upper_bound() =>
        AssertRange(SalaryParser.FromText("Pay: $120-150K", true), 120_000, 150_000);

    [Fact]
    public void Ashby_style_ca_dollar_k() =>
        AssertRange(SalaryParser.FromText("CA$215K – CA$310K • Offers Equity", true), 215_000, 310_000);

    [Fact]
    public void Hourly_rate() =>
        AssertRange(SalaryParser.FromText("Rate: $55 - $65 per hour", true), 55, 65, SalaryPeriod.Hour);

    [Fact]
    public void Stipends_are_skipped_in_favour_of_the_salary() =>
        AssertRange(SalaryParser.FromText("$500 - $1,000 learning stipend. Salary $100,000 to $120,000 per year.", true), 100_000, 120_000);

    [Fact]
    public void Ontario_pay_transparency_wording() =>
        AssertRange(SalaryParser.FromText("Ontario Pay Transparency Range CA$77,800 – CA$106,920", true), 77_800, 106_920);

    [Theory]
    [InlineData("US base pay range $150,000-$200,000 for New York", true)]
    [InlineData("Compensation: $140,000 - $160,000", false)]
    [InlineData("Salary: $150,000 - $200,000 USD", true)]
    [InlineData("We offer competitive pay and $2,000 - $3,000 in perks.", true)]
    [InlineData("No numbers here.", true)]
    public void No_cad_salary(string text, bool inCanada) =>
        Assert.Null(SalaryParser.FromText(text, inCanada));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Processor.Tests`
Expected: build FAILS — `SalaryParser` missing.

- [ ] **Step 3: Implement**

`src/Processor/Interpreting/SalaryParser.cs`:
```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using Processor.Reading;

namespace Processor.Interpreting;

public static partial class SalaryParser
{
    // Ontario postings must state pay, often next to a US range. Explicit CAD (currency or nearby "Canada/Toronto/Ontario")
    // wins; USD is skipped; a plain "$" range counts only for postings in Canada. Only plausible yearly ranges are returned.
    public static SalaryRange? FromText(string text, bool postingInCanada)
    {
        SalaryRange? plain = null;
        foreach (Match m in RangePattern().Matches(text))
        {
            var currency = Currency(m);
            if (currency == "USD") continue;
            var range = ToRange(m);
            if (range is null || !range.IsPlausible) continue;

            var context = text.Substring(Math.Max(0, m.Index - 80), Math.Min(80, m.Index));
            if (currency == "CAD" || CanadaContext().IsMatch(context)) return range;
            if (UsContext().IsMatch(context)) continue;
            plain ??= range;
        }
        return postingInCanada ? plain : null;
    }

    private static string? Currency(Match m)
    {
        var marks = string.Concat(m.Groups["c1"].Value, m.Groups["c2"].Value, m.Groups["t1"].Value, m.Groups["t2"].Value).ToUpperInvariant();
        if (marks.Contains("US")) return "USD";
        if (marks.Contains("CA") || marks.Contains("C$")) return "CAD";
        return null;
    }

    private static SalaryRange? ToRange(Match m)
    {
        if (!TryAmount(m.Groups["a"].Value, out var a) || !TryAmount(m.Groups["b"].Value, out var b)) return null;
        var aK = m.Groups["ka"].Success;
        var bK = m.Groups["kb"].Success;
        if (aK || (bK && a < 1000)) a *= 1000;
        if (bK || (aK && b < 1000)) b *= 1000;

        var per = m.Groups["per"].Value;
        var hourly = HourWord().IsMatch(per) || (a < 300 && b < 300);
        return new SalaryRange(Math.Min(a, b), Math.Max(a, b), hourly ? SalaryPeriod.Hour : SalaryPeriod.Year);
    }

    private static bool TryAmount(string value, out decimal amount) =>
        decimal.TryParse(value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out amount);

    [GeneratedRegex(@"(?<c1>CA\$|C\$|CAD\s?\$?|US\$|USD\s?\$?|\$)\s?(?<a>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)\s?(?<ka>[kK])?\s?(?<t1>CAD|USD)?\s*(?:-|–|—|to)\s*(?<c2>CA\$|C\$|CAD\s?\$?|US\$|USD\s?\$?|\$)?\s?(?<b>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)\s?(?<kb>[kK])?(?<t2>\s?(?:CAD|USD|CA|US)\b)?(?<per>\s*(?:/|per|an|a)\s*(?:hour|hr|year|yr|annum)\b|\s*(?:hourly|annually|per annum)\b)?", RegexOptions.IgnoreCase)]
    private static partial Regex RangePattern();

    [GeneratedRegex(@"\b(?:canada|canadian|toronto|ontario|vancouver|montreal|cad)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CanadaContext();

    [GeneratedRegex(@"\b(?:united states|u\.s\.|usa|us-based|us base|new york|san francisco|california|seattle|washington|colorado|texas|massachusetts)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UsContext();

    [GeneratedRegex(@"hour|hr", RegexOptions.IgnoreCase)]
    private static partial Regex HourWord();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Processor.Tests`
Expected: all salary tests PASS. Fix the parser (not the expectations) on failure; ledger any rule change.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add CAD salary parser with US-range and stipend guards"
```

### Task 5: Skill catalog and matcher

**Files:**
- Create: `src/Processor/Interpreting/SkillCatalog.cs`, `config/skills.json`
- Test: `tests/Processor.Tests/Interpreting/SkillCatalogTests.cs`

**Interfaces:**
- Consumes: `RepoConfig` test helper (Task 2)
- Produces:
  - `record SkillDefinition(string Name, string Category, IReadOnlyList<string> Aliases)`
  - `SkillCatalog(IReadOnlyList<SkillDefinition>)` with `Skills`, `IReadOnlySet<string> Match(string text)`, `static SkillCatalog Load(string path)` (throws `InvalidDataException` for duplicate names, empty aliases, or an invalid `re:` alias, naming the skill)

- [ ] **Step 1: Create the skill list**

Alias rules: plain aliases match case-insensitively with custom word edges (letters, digits, `+`, `#` may not touch the alias), so `C#`, `.NET`, `CI/CD` work and `Java` does not match inside `JavaScript`. An alias starting with `re:` is a case-sensitive regular expression, used for words that are also ordinary English (React, REST, Go, R, AI, ML, RAG, Spark, dbt). One alias may appear under several skills (`AKS` → Azure and Kubernetes).

`config/skills.json`:
```json
{
  "skills": [
    { "name": "C#", "category": "language", "aliases": ["C#", "csharp", "C Sharp"] },
    { "name": "F#", "category": "language", "aliases": ["F#"] },
    { "name": "VB.NET", "category": "language", "aliases": ["VB.NET", "Visual Basic"] },
    { "name": "Java", "category": "language", "aliases": ["Java"] },
    { "name": "Kotlin", "category": "language", "aliases": ["Kotlin"] },
    { "name": "Scala", "category": "language", "aliases": ["Scala"] },
    { "name": "Python", "category": "language", "aliases": ["Python"] },
    { "name": "JavaScript", "category": "language", "aliases": ["JavaScript", "JS", "ES6", "ECMAScript"] },
    { "name": "TypeScript", "category": "language", "aliases": ["TypeScript"] },
    { "name": "Go", "category": "language", "aliases": ["Golang", "re:(?<![\\p{L}])Go(?=\\s*(?:,|/|\\)|and\\b|or\\b))", "re:(?<=(?:,|/)\\s*)Go\\b"] },
    { "name": "Rust", "category": "language", "aliases": ["Rust"] },
    { "name": "C++", "category": "language", "aliases": ["C++", "cpp"] },
    { "name": "Ruby", "category": "language", "aliases": ["Ruby"] },
    { "name": "PHP", "category": "language", "aliases": ["PHP"] },
    { "name": "Swift", "category": "language", "aliases": ["Swift", "SwiftUI"] },
    { "name": "Dart", "category": "language", "aliases": ["Dart"] },
    { "name": "Elixir", "category": "language", "aliases": ["Elixir"] },
    { "name": "R", "category": "language", "aliases": ["R programming", "re:\\bR(?=\\s*,)", "re:(?<=,\\s*)R\\b(?![&'])"] },
    { "name": "SQL", "category": "language", "aliases": ["SQL", "T-SQL", "TSQL", "PL/SQL"] },
    { "name": "Bash", "category": "language", "aliases": ["Bash", "shell scripting"] },
    { "name": "PowerShell", "category": "language", "aliases": ["PowerShell"] },

    { "name": ".NET", "category": "backend", "aliases": [".NET", "dotnet", "ASP.NET"] },
    { "name": "ASP.NET Core", "category": "backend", "aliases": ["ASP.NET Core", "ASP.NET MVC", "ASP.NET Web API", "Minimal APIs"] },
    { "name": ".NET Framework", "category": "backend", "aliases": [".NET Framework"] },
    { "name": "Entity Framework", "category": "backend", "aliases": ["Entity Framework", "EF Core", "EFCore"] },
    { "name": "Blazor", "category": "backend", "aliases": ["Blazor"] },
    { "name": "WPF", "category": "backend", "aliases": ["WPF"] },
    { "name": "WinForms", "category": "backend", "aliases": ["WinForms", "Windows Forms"] },
    { "name": ".NET MAUI", "category": "backend", "aliases": [".NET MAUI", "MAUI", "Xamarin"] },
    { "name": "SignalR", "category": "backend", "aliases": ["SignalR"] },
    { "name": "Node.js", "category": "backend", "aliases": ["Node.js", "NodeJS", "Node JS"] },
    { "name": "Express.js", "category": "backend", "aliases": ["Express.js", "ExpressJS"] },
    { "name": "NestJS", "category": "backend", "aliases": ["NestJS", "Nest.js"] },
    { "name": "Spring Boot", "category": "backend", "aliases": ["Spring Boot", "Spring Framework", "Spring MVC"] },
    { "name": "Django", "category": "backend", "aliases": ["Django"] },
    { "name": "Flask", "category": "backend", "aliases": ["Flask"] },
    { "name": "FastAPI", "category": "backend", "aliases": ["FastAPI"] },
    { "name": "Ruby on Rails", "category": "backend", "aliases": ["Ruby on Rails", "Rails"] },
    { "name": "Laravel", "category": "backend", "aliases": ["Laravel"] },
    { "name": "GraphQL", "category": "backend", "aliases": ["GraphQL"] },
    { "name": "gRPC", "category": "backend", "aliases": ["gRPC"] },
    { "name": "REST APIs", "category": "backend", "aliases": ["re:\\bREST(?:ful)?\\b", "RESTful", "REST API", "REST APIs"] },

    { "name": "React", "category": "frontend", "aliases": ["re:\\bReact(?:\\.js|JS)?\\b(?!\\s+Native)", "ReactJS", "React.js"] },
    { "name": "Next.js", "category": "frontend", "aliases": ["Next.js", "NextJS"] },
    { "name": "Angular", "category": "frontend", "aliases": ["Angular", "AngularJS"] },
    { "name": "Vue.js", "category": "frontend", "aliases": ["Vue", "Vue.js", "VueJS", "Nuxt"] },
    { "name": "Svelte", "category": "frontend", "aliases": ["Svelte", "SvelteKit"] },
    { "name": "Redux", "category": "frontend", "aliases": ["Redux"] },
    { "name": "HTML", "category": "frontend", "aliases": ["HTML", "HTML5"] },
    { "name": "CSS", "category": "frontend", "aliases": ["CSS", "CSS3"] },
    { "name": "Tailwind CSS", "category": "frontend", "aliases": ["Tailwind"] },
    { "name": "Sass", "category": "frontend", "aliases": ["Sass", "SCSS"] },
    { "name": "Bootstrap", "category": "frontend", "aliases": ["Bootstrap"] },
    { "name": "jQuery", "category": "frontend", "aliases": ["jQuery"] },
    { "name": "Webpack", "category": "frontend", "aliases": ["Webpack"] },
    { "name": "Vite", "category": "frontend", "aliases": ["Vite"] },
    { "name": "React Native", "category": "frontend", "aliases": ["React Native"] },
    { "name": "Flutter", "category": "frontend", "aliases": ["Flutter"] },
    { "name": "Storybook", "category": "frontend", "aliases": ["Storybook"] },
    { "name": "Figma", "category": "frontend", "aliases": ["Figma"] },
    { "name": "Responsive Design", "category": "frontend", "aliases": ["responsive design", "responsive web", "mobile-first", "mobile first", "responsive UI"] },
    { "name": "Accessibility", "category": "frontend", "aliases": ["accessibility", "WCAG", "a11y", "AODA"] },

    { "name": "SQL Server", "category": "data", "aliases": ["SQL Server", "MSSQL", "MS SQL", "SSMS"] },
    { "name": "PostgreSQL", "category": "data", "aliases": ["PostgreSQL", "Postgres"] },
    { "name": "MySQL", "category": "data", "aliases": ["MySQL"] },
    { "name": "Oracle Database", "category": "data", "aliases": ["Oracle"] },
    { "name": "MongoDB", "category": "data", "aliases": ["MongoDB", "Mongo"] },
    { "name": "Redis", "category": "data", "aliases": ["Redis"] },
    { "name": "Cassandra", "category": "data", "aliases": ["Cassandra"] },
    { "name": "DynamoDB", "category": "data", "aliases": ["DynamoDB"] },
    { "name": "Cosmos DB", "category": "data", "aliases": ["Cosmos DB", "CosmosDB"] },
    { "name": "Elasticsearch", "category": "data", "aliases": ["Elasticsearch", "Elastic Search", "OpenSearch", "ELK"] },
    { "name": "Snowflake", "category": "data", "aliases": ["Snowflake"] },
    { "name": "BigQuery", "category": "data", "aliases": ["BigQuery"] },
    { "name": "Databricks", "category": "data", "aliases": ["Databricks"] },
    { "name": "Apache Spark", "category": "data", "aliases": ["re:\\b(?:Apache Spark|PySpark|Spark)\\b"] },
    { "name": "Kafka", "category": "data", "aliases": ["Kafka"] },
    { "name": "RabbitMQ", "category": "data", "aliases": ["RabbitMQ"] },
    { "name": "Azure Service Bus", "category": "data", "aliases": ["Service Bus", "Azure Service Bus"] },
    { "name": "Amazon SQS", "category": "data", "aliases": ["SQS", "SNS"] },
    { "name": "Airflow", "category": "data", "aliases": ["Airflow"] },
    { "name": "dbt", "category": "data", "aliases": ["re:\\bdbt\\b"] },
    { "name": "ETL", "category": "data", "aliases": ["ETL", "ELT", "data pipeline", "data pipelines"] },
    { "name": "Data Warehousing", "category": "data", "aliases": ["data warehouse", "data warehousing", "data lake", "lakehouse"] },
    { "name": "Power BI", "category": "data", "aliases": ["Power BI", "PowerBI"] },
    { "name": "Tableau", "category": "data", "aliases": ["Tableau"] },
    { "name": "SSIS", "category": "data", "aliases": ["SSIS"] },
    { "name": "SSRS", "category": "data", "aliases": ["SSRS"] },
    { "name": "NoSQL", "category": "data", "aliases": ["NoSQL"] },
    { "name": "Pandas", "category": "data", "aliases": ["Pandas", "NumPy"] },

    { "name": "Azure", "category": "cloud", "aliases": ["Azure", "Microsoft Azure", "AKS"] },
    { "name": "AWS", "category": "cloud", "aliases": ["AWS", "Amazon Web Services", "EKS", "EC2"] },
    { "name": "GCP", "category": "cloud", "aliases": ["GCP", "Google Cloud", "GKE"] },
    { "name": "Azure Functions", "category": "cloud", "aliases": ["Azure Functions"] },
    { "name": "Azure App Service", "category": "cloud", "aliases": ["App Service", "App Services"] },
    { "name": "AWS Lambda", "category": "cloud", "aliases": ["AWS Lambda"] },
    { "name": "Serverless", "category": "cloud", "aliases": ["serverless"] },
    { "name": "Azure DevOps", "category": "cloud", "aliases": ["Azure DevOps", "Azure Pipelines", "TFS", "VSTS", "Team Foundation Server"] },
    { "name": "Entra ID", "category": "cloud", "aliases": ["Entra ID", "Azure AD", "Azure Active Directory", "Active Directory"] },
    { "name": "Azure API Management", "category": "cloud", "aliases": ["API Management", "APIM"] },

    { "name": "Docker", "category": "devops", "aliases": ["Docker", "containers", "containerization", "containerized", "Docker Compose"] },
    { "name": "Kubernetes", "category": "devops", "aliases": ["Kubernetes", "K8s", "AKS", "EKS", "GKE", "OpenShift"] },
    { "name": "Helm", "category": "devops", "aliases": ["Helm"] },
    { "name": "Terraform", "category": "devops", "aliases": ["Terraform"] },
    { "name": "Bicep", "category": "devops", "aliases": ["Bicep", "ARM templates"] },
    { "name": "Infrastructure as Code", "category": "devops", "aliases": ["infrastructure as code", "IaC", "Pulumi", "CloudFormation", "Terraform", "Bicep"] },
    { "name": "Ansible", "category": "devops", "aliases": ["Ansible"] },
    { "name": "CI/CD", "category": "devops", "aliases": ["CI/CD", "CI / CD", "CICD", "continuous integration", "continuous delivery", "continuous deployment", "build pipelines", "deployment pipelines"] },
    { "name": "GitHub Actions", "category": "devops", "aliases": ["GitHub Actions"] },
    { "name": "Jenkins", "category": "devops", "aliases": ["Jenkins"] },
    { "name": "GitLab CI", "category": "devops", "aliases": ["GitLab CI", "GitLab"] },
    { "name": "Git", "category": "devops", "aliases": ["Git"] },
    { "name": "Linux", "category": "devops", "aliases": ["Linux", "Ubuntu", "RHEL"] },
    { "name": "Nginx", "category": "devops", "aliases": ["Nginx"] },
    { "name": "IIS", "category": "devops", "aliases": ["IIS"] },
    { "name": "Observability", "category": "devops", "aliases": ["observability", "monitoring and alerting", "APM", "Prometheus", "Grafana", "Datadog", "New Relic", "Splunk", "Application Insights", "OpenTelemetry"] },
    { "name": "OpenTelemetry", "category": "devops", "aliases": ["OpenTelemetry", "OTel"] },

    { "name": "AI (general)", "category": "ai", "aliases": ["re:\\bAI\\b", "artificial intelligence"] },
    { "name": "Machine Learning", "category": "ai", "aliases": ["machine learning", "re:\\bML\\b"] },
    { "name": "LLM", "category": "ai", "aliases": ["LLM", "LLMs", "large language model", "large language models"] },
    { "name": "Generative AI", "category": "ai", "aliases": ["generative AI", "GenAI", "Gen AI"] },
    { "name": "OpenAI API", "category": "ai", "aliases": ["OpenAI", "GPT-4", "GPT-4o", "ChatGPT"] },
    { "name": "Azure OpenAI", "category": "ai", "aliases": ["Azure OpenAI", "Azure AI Foundry", "Azure AI Services", "Azure Cognitive Services"] },
    { "name": "RAG", "category": "ai", "aliases": ["re:\\bRAG\\b", "retrieval-augmented", "retrieval augmented"] },
    { "name": "Vector Databases", "category": "ai", "aliases": ["vector database", "vector databases", "vector store", "vector search", "pgvector", "Pinecone", "embeddings"] },
    { "name": "LangChain", "category": "ai", "aliases": ["LangChain", "LangGraph", "LlamaIndex"] },
    { "name": "Semantic Kernel", "category": "ai", "aliases": ["Semantic Kernel", "Microsoft.Extensions.AI"] },
    { "name": "Prompt Engineering", "category": "ai", "aliases": ["prompt engineering"] },
    { "name": "AI Agents", "category": "ai", "aliases": ["AI agents", "agentic", "Model Context Protocol", "MCP"] },
    { "name": "AI Coding Assistants", "category": "ai", "aliases": ["GitHub Copilot", "Copilot", "Claude Code", "Cursor AI", "AI-assisted development", "AI coding", "AI tools"] },
    { "name": "PyTorch", "category": "ai", "aliases": ["PyTorch"] },
    { "name": "TensorFlow", "category": "ai", "aliases": ["TensorFlow", "Keras"] },
    { "name": "MLOps", "category": "ai", "aliases": ["MLOps"] },
    { "name": "NLP", "category": "ai", "aliases": ["NLP", "natural language processing"] },
    { "name": "Computer Vision", "category": "ai", "aliases": ["computer vision", "image processing", "OpenCV"] },

    { "name": "Unit Testing", "category": "testing", "aliases": ["unit test", "unit tests", "unit testing"] },
    { "name": "xUnit", "category": "testing", "aliases": ["xUnit", "xUnit.net"] },
    { "name": "NUnit", "category": "testing", "aliases": ["NUnit"] },
    { "name": "MSTest", "category": "testing", "aliases": ["MSTest"] },
    { "name": "Jest", "category": "testing", "aliases": ["Jest"] },
    { "name": "Cypress", "category": "testing", "aliases": ["Cypress"] },
    { "name": "Playwright", "category": "testing", "aliases": ["Playwright"] },
    { "name": "Selenium", "category": "testing", "aliases": ["Selenium"] },
    { "name": "TDD", "category": "testing", "aliases": ["TDD", "test-driven", "test driven development"] },
    { "name": "Integration Testing", "category": "testing", "aliases": ["integration test", "integration tests", "integration testing", "end-to-end tests", "E2E"] },
    { "name": "Test Automation", "category": "testing", "aliases": ["test automation", "automated testing", "automated tests"] },

    { "name": "Microservices", "category": "architecture", "aliases": ["microservice", "microservices", "micro-services"] },
    { "name": "Distributed Systems", "category": "architecture", "aliases": ["distributed systems", "distributed system"] },
    { "name": "System Design", "category": "architecture", "aliases": ["system design", "systems design", "software architecture", "solution architecture"] },
    { "name": "Event-Driven Architecture", "category": "architecture", "aliases": ["event-driven", "event driven", "event sourcing", "CQRS", "pub/sub"] },
    { "name": "Domain-Driven Design", "category": "architecture", "aliases": ["domain-driven design", "domain driven design", "DDD"] },
    { "name": "Clean Architecture", "category": "architecture", "aliases": ["clean architecture", "clean code", "hexagonal architecture", "onion architecture"] },
    { "name": "Design Patterns", "category": "architecture", "aliases": ["design patterns", "SOLID"] },
    { "name": "API Design", "category": "architecture", "aliases": ["API design", "OpenAPI", "Swagger"] },
    { "name": "Scalability", "category": "architecture", "aliases": ["scalability", "scalable", "high availability", "high-availability", "fault tolerance", "fault-tolerant"] },
    { "name": "Performance Optimization", "category": "architecture", "aliases": ["performance optimization", "performance optimisation", "performance tuning", "query optimization"] },
    { "name": "Caching", "category": "architecture", "aliases": ["caching", "cache"] },
    { "name": "Application Security", "category": "architecture", "aliases": ["OWASP", "application security", "secure coding", "AppSec", "threat modeling"] },
    { "name": "OAuth / OIDC", "category": "architecture", "aliases": ["OAuth", "OAuth2", "OAuth 2.0", "OpenID Connect", "OIDC", "SSO", "SAML", "JWT"] },

    { "name": "Agile", "category": "practice", "aliases": ["Agile"] },
    { "name": "Scrum", "category": "practice", "aliases": ["Scrum"] },
    { "name": "Kanban", "category": "practice", "aliases": ["Kanban"] },
    { "name": "Code Review", "category": "practice", "aliases": ["code review", "code reviews", "peer review", "pull request"] },
    { "name": "Mentoring", "category": "practice", "aliases": ["mentor", "mentoring", "mentorship", "coaching"] },
    { "name": "Technical Leadership", "category": "practice", "aliases": ["technical leadership", "tech lead", "lead a team", "leading teams", "lead engineers", "technical direction"] },
    { "name": "Stakeholder Management", "category": "practice", "aliases": ["stakeholder", "stakeholders", "stakeholder management"] },
    { "name": "Requirements Gathering", "category": "practice", "aliases": ["requirements gathering", "gather requirements", "gathering requirements", "business requirements", "requirements analysis"] },
    { "name": "Estimation & Planning", "category": "practice", "aliases": ["estimation", "estimates", "sprint planning", "roadmap"] },
    { "name": "Documentation", "category": "practice", "aliases": ["technical documentation", "documentation"] },
    { "name": "Production Support", "category": "practice", "aliases": ["on-call", "on call rotation", "production support", "incident response"] },

    { "name": "Fintech", "category": "domain", "aliases": ["fintech", "financial services", "capital markets", "trading"] },
    { "name": "Payments", "category": "domain", "aliases": ["payments", "payment processing"] },
    { "name": "Banking", "category": "domain", "aliases": ["banking"] },
    { "name": "Insurance", "category": "domain", "aliases": ["insurance", "insurtech"] },
    { "name": "Healthcare", "category": "domain", "aliases": ["healthcare", "health care", "EHR", "EMR", "clinical"] },
    { "name": "E-commerce", "category": "domain", "aliases": ["e-commerce", "ecommerce"] },
    { "name": "SaaS", "category": "domain", "aliases": ["SaaS"] },
    { "name": "ERP", "category": "domain", "aliases": ["ERP"] },
    { "name": "CRM", "category": "domain", "aliases": ["CRM"] },
    { "name": "Salesforce", "category": "domain", "aliases": ["Salesforce"] },
    { "name": "SAP", "category": "domain", "aliases": ["SAP"] },
    { "name": "Dynamics 365", "category": "domain", "aliases": ["Dynamics 365", "D365", "Dynamics CRM", "Power Platform", "Power Apps"] },
    { "name": "Public Sector", "category": "domain", "aliases": ["public sector", "government"] },
    { "name": "EdTech", "category": "domain", "aliases": ["edtech", "education technology", "e-learning", "LMS"] }
  ]
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Processor.Tests/Interpreting/SkillCatalogTests.cs`:
```csharp
using Processor.Interpreting;
using Processor.Tests.Configuration;
using Processor.Tests.TestSupport;

namespace Processor.Tests.Interpreting;

public class SkillCatalogTests
{
    private static readonly SkillCatalog Catalog = SkillCatalog.Load(RepoConfig.PathOf("skills.json"));

    private static IReadOnlySet<string> Match(string text) => Catalog.Match(text);

    [Fact]
    public void Committed_catalog_has_unique_names_and_a_useful_size()
    {
        Assert.InRange(Catalog.Skills.Count, 150, 220);
        Assert.Equal(Catalog.Skills.Count, Catalog.Skills.Select(s => s.Name).Distinct().Count());
    }

    [Fact]
    public void Dotnet_tokens_match()
    {
        var found = Match("Experience with C#, .NET 8 and ASP.NET Core, plus Entity Framework.");
        Assert.Superset(new HashSet<string> { "C#", ".NET", "ASP.NET Core", "Entity Framework" }, (ISet<string>)found.ToHashSet());
    }

    [Theory]
    [InlineData("React, Redux and TypeScript", "React")]
    [InlineData("RESTful APIs", "REST APIs")]
    [InlineData("Java, Go, Python", "Go")]
    [InlineData("Golang services", "Go")]
    [InlineData("Python, R, SQL", "R")]
    [InlineData("CI/CD pipelines", "CI/CD")]
    [InlineData("AKS clusters", "Kubernetes")]
    [InlineData("AKS clusters", "Azure")]
    [InlineData("Terraform modules", "Infrastructure as Code")]
    [InlineData("Strong JavaScript skills", "JavaScript")]
    [InlineData("Accessibility (AODA)", "Accessibility")]
    public void Real_mentions_match(string text, string skill) =>
        Assert.Contains(skill, Match(text));

    [Theory]
    [InlineData("We react quickly to incidents", "React")]
    [InlineData("the rest of the team", "REST APIs")]
    [InlineData("Let's go to the office", "Go")]
    [InlineData("Our R&D group", "R")]
    [InlineData("NoSQL stores", "SQL")]
    [InlineData("Push to GitHub", "Git")]
    [InlineData("Strong JavaScript skills", "Java")]
    [InlineData("OpenAI models", "AI (general)")]
    [InlineData("HTML and XML", "Machine Learning")]
    [InlineData("React Native apps", "React")]
    public void Ambiguous_words_do_not_create_demand(string text, string skill) =>
        Assert.DoesNotContain(skill, Match(text));

    [Fact]
    public void Load_rejects_duplicate_names()
    {
        using var tmp = new TempDir();
        var path = tmp.File("skills.json", """{"skills":[{"name":"A","category":"x","aliases":["a"]},{"name":"A","category":"x","aliases":["b"]}]}""");
        Assert.Contains("A", Assert.Throws<InvalidDataException>(() => SkillCatalog.Load(path)).Message);
    }

    [Fact]
    public void Load_names_the_skill_with_an_invalid_regex()
    {
        using var tmp = new TempDir();
        var path = tmp.File("skills.json", """{"skills":[{"name":"Broken","category":"x","aliases":["re:(unclosed"]}]}""");
        Assert.Contains("Broken", Assert.Throws<InvalidDataException>(() => SkillCatalog.Load(path)).Message);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Processor.Tests`
Expected: build FAILS — `SkillCatalog` missing.

- [ ] **Step 4: Implement**

`src/Processor/Interpreting/SkillCatalog.cs`:
```csharp
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Processor.Interpreting;

public sealed record SkillDefinition(string Name, string Category, IReadOnlyList<string> Aliases);

public sealed class SkillCatalog
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly (string Name, Regex Pattern)[] _matchers;

    public SkillCatalog(IReadOnlyList<SkillDefinition> skills)
    {
        Skills = skills;
        _matchers = [.. skills.Select(s => (s.Name, BuildPattern(s)))];
    }

    public IReadOnlyList<SkillDefinition> Skills { get; }

    public IReadOnlySet<string> Match(string text)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, pattern) in _matchers)
            if (pattern.IsMatch(text)) found.Add(name);
        return found;
    }

    public static SkillCatalog Load(string path)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException($"{path} is empty.");
        var duplicates = file.Skills.GroupBy(s => s.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0) throw new InvalidDataException($"{path}: duplicate skill names: {string.Join(", ", duplicates)}.");
        var empty = file.Skills.Where(s => s.Aliases.Count == 0).Select(s => s.Name).ToList();
        if (empty.Count > 0) throw new InvalidDataException($"{path}: skills without aliases: {string.Join(", ", empty)}.");
        return new SkillCatalog(file.Skills);
    }

    // Plain aliases: case-insensitive, and no letter, digit, '+' or '#' may touch them, so "C#", ".NET" and "CI/CD" work
    // while "Java" does not match inside "JavaScript". "re:" aliases are case-sensitive regexes for ordinary English words.
    private static Regex BuildPattern(SkillDefinition skill)
    {
        var parts = skill.Aliases.Select(a => a.StartsWith("re:", StringComparison.Ordinal)
            ? $"(?-i:{a[3..]})"
            : $@"(?<![\p{{L}}\p{{N}}+#]){Regex.Escape(a)}(?![\p{{L}}\p{{N}}+#])");
        try
        {
            return new Regex(string.Join('|', parts), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"Skill '{skill.Name}' has an invalid alias pattern: {ex.Message}", ex);
        }
    }

    private sealed record CatalogFile(List<SkillDefinition> Skills);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Processor.Tests`
Expected: all skill tests PASS. If an ambiguous-word case fails, fix the alias in `skills.json` (not the test) and ledger it.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add curated skill catalog with alias matching"
```

---

### Task 6: Source readers and archive enumeration

**Files:**
- Create: `src/Processor/Reading/Json.cs`, `src/Processor/Reading/SourceReaders.cs`, `src/Processor/Reading/InboxReader.cs`, `src/Processor/Reading/Archive.cs`
- Create: `tests/Processor.Tests/TestSupport/FixtureArchive.cs`
- Test: `tests/Processor.Tests/Reading/SourceReadersTests.cs`, `InboxReaderTests.cs`, `ArchiveTests.cs`

**Interfaces:**
- Consumes: `RawPosting`, `SalaryRange`, `HtmlText`, `JsonFiles` (Task 1)
- Produces:
  - `SourceReaders.Adzuna(JsonElement root, string runId)`, `.Greenhouse(root, slug, runId, Func<string,string> companyName)`, `.Lever(...)`, `.Ashby(...)` → `IEnumerable<RawPosting>`
  - `record InboxResult(IReadOnlyList<RawPosting> Postings, IReadOnlyList<string> Warnings)`; `InboxReader.ReadAll(string inboxDir, string runId) : InboxResult`; `InboxReader.Parse(string fileName, string content, string runId) : (RawPosting? Posting, string? Warning)`
  - `record ArchiveRun(string RunId, DateTimeOffset StartedAt, string FullPath)`; `record AdzunaAggregate(string RunId, string Term, string Kind, string PayloadJson)`
  - `Archive.Runs(string dataRoot) : IReadOnlyList<ArchiveRun>` (oldest first, skips `_`-prefixed and manifest-less folders), `Archive.ReadPostings(ArchiveRun, Func<string,string> companyName) : IEnumerable<RawPosting>`, `Archive.ReadAggregates(ArchiveRun) : IEnumerable<AdzunaAggregate>`
  - Test helper `FixtureArchive.Write(string dataRoot)` — a 3-run archive used again in Task 12 (contents documented in the helper)

- [ ] **Step 1: Write the fixture archive helper**

`tests/Processor.Tests/TestSupport/FixtureArchive.cs`:
```csharp
using System.Text.Json;

namespace Processor.Tests.TestSupport;

// A small, realistic archive:
//  run 1 (2026-09-01): adzuna A1 (.NET Developer, Toronto, salary) + A2 (Data Engineer, Toronto);
//                      greenhouse-acme G1 (Senior Full Stack Developer, Toronto, CAD salary in text) + G2 (Bellevue, US)
//  run 2 (2026-09-15): A1 again, G1 again, lever-wattpad L1 (Backend Developer, Toronto hybrid, structured CAD salary)
//  run 3 (2026-10-01): G1 again, ashby-cohere S1 (Senior Software Engineer, Remote (Canada), structured CAD salary),
//                      adzuna A3 = the same job as G1 (cross-source duplicate), a histogram aggregate
//  plus: raw/_dryrun (ignored), raw/broken-run without manifest (ignored), inbox/one.txt (LinkedIn paste)
public static class FixtureArchive
{
    public const string Run1 = "2026-09-01_0700";
    public const string Run2 = "2026-09-15_0700";
    public const string Run3 = "2026-10-01_0700";

    public static void Write(string dataRoot)
    {
        Manifest(dataRoot, Run1, "2026-09-01T07:00:00-04:00");
        Manifest(dataRoot, Run2, "2026-09-15T07:00:00-04:00");
        Manifest(dataRoot, Run3, "2026-10-01T07:00:00-04:00");
        Manifest(dataRoot, "_dryrun", "2026-10-02T07:00:00-04:00");
        Put(dataRoot, "raw/_dryrun/adzuna/search/x-gta-p1.json", Adzuna(("DRY", ".NET Developer", "Acme", "Toronto, Ontario", "C#", null)));
        Put(dataRoot, "raw/broken-run/adzuna/search/x-gta-p1.json", Adzuna(("BROKEN", ".NET Developer", "Acme", "Toronto, Ontario", "C#", null)));

        Put(dataRoot, $"raw/{Run1}/adzuna/search/net-developer-gta-p1.json", Adzuna(
            ("A1", ".NET Developer", "Northwind", "Toronto, Ontario", "Build services in C# and .NET on Azure.", 100000),
            ("A2", "Senior Data Engineer", "Contoso", "Toronto, Ontario", "Spark and Python pipelines.", null)));
        Put(dataRoot, $"raw/{Run1}/boards/greenhouse-acme.json", Greenhouse(G1, G2));

        Put(dataRoot, $"raw/{Run2}/adzuna/search/net-developer-gta-p1.json", Adzuna(
            ("A1", ".NET Developer", "Northwind", "Toronto, Ontario", "Build services in C# and .NET on Azure.", 100000)));
        Put(dataRoot, $"raw/{Run2}/boards/greenhouse-acme.json", Greenhouse(G1));
        Put(dataRoot, $"raw/{Run2}/boards/lever-wattpad.json", """
            [{"id":"L1","text":"Backend Developer","categories":{"location":"Toronto","allLocations":["Toronto"]},
              "workplaceType":"hybrid","country":"CA","hostedUrl":"https://jobs.lever.co/wattpad/L1",
              "descriptionPlain":"Java and Kafka services.","lists":[{"text":"Nice to have","content":"<li>Kubernetes</li>"}],
              "additionalPlain":"","salaryRange":{"min":110000,"max":140000,"currency":"CAD","interval":"per-year-salary"}}]
            """);

        Put(dataRoot, $"raw/{Run3}/boards/greenhouse-acme.json", Greenhouse(G1));
        Put(dataRoot, $"raw/{Run3}/boards/ashby-cohere.json", """
            {"jobs":[{"id":"S1","title":"Senior Software Engineer","location":"Remote (Canada)","secondaryLocations":[],
              "isRemote":true,"workplaceType":"Remote","isListed":true,"jobUrl":"https://jobs.ashbyhq.com/cohere/S1",
              "address":{"postalAddress":{"addressCountry":"Canada"}},
              "descriptionPlain":"Python, Kubernetes and AKS. Canadian citizenship required.",
              "compensation":{"compensationTiers":[{"components":[{"compensationType":"Salary","interval":"1 YEAR","currencyCode":"CAD","minValue":150000,"maxValue":200000}]}]}}]}
            """);
        Put(dataRoot, $"raw/{Run3}/adzuna/search/full-stack-developer-gta-p1.json", Adzuna(
            ("A3", "Senior Full Stack Developer", "Acme", "Toronto, Ontario", "React and C#...", null)));
        Put(dataRoot, $"raw/{Run3}/adzuna/histogram/net-developer-gta.json", """{"histogram":{"100000":7}}""");

        Put(dataRoot, "inbox/one.txt", "URL: https://www.linkedin.com/jobs/view/1\nCompany: Bank of Example\nTitle: Senior .NET Developer\nLocation: Toronto, ON\nSaved: 2026-09-30\n\nWe need C#, SQL Server and Azure DevOps.\n");
    }

    private const string G1 = """
        {"id":101,"title":"Senior Full Stack Developer","company_name":"Acme","location":{"name":"Toronto, Ontario, Canada"},
         "absolute_url":"https://boards.greenhouse.io/acme/jobs/101",
         "content":"&lt;p&gt;React, TypeScript and C#. The salary range in Toronto is $130,000 - $150,000.&lt;/p&gt;"}
        """;

    private const string G2 = """
        {"id":102,"title":"Software Engineer","company_name":"Acme","location":{"name":"Bellevue, Washington"},
         "absolute_url":"https://boards.greenhouse.io/acme/jobs/102","content":"&lt;p&gt;Go and Rust.&lt;/p&gt;"}
        """;

    private static string Greenhouse(params string[] jobs) => $$"""{"jobs":[{{string.Join(",", jobs)}}]}""";

    private static string Adzuna(params (string Id, string Title, string Company, string Location, string Description, int? Salary)[] results) =>
        JsonSerializer.Serialize(new
        {
            count = results.Length,
            results = results.Select(r =>
            {
                var job = new Dictionary<string, object?>
                {
                    ["id"] = r.Id,
                    ["title"] = r.Title,
                    ["company"] = new { display_name = r.Company },
                    ["location"] = new { display_name = r.Location, area = new[] { "Canada", "Ontario", "Toronto" } },
                    ["description"] = r.Description,
                    ["redirect_url"] = $"https://www.adzuna.ca/details/{r.Id}",
                };
                if (r.Salary is { } s)
                {
                    job["salary_min"] = s;
                    job["salary_max"] = s + 20000;
                    job["salary_is_predicted"] = "0";
                }
                return job;
            }),
        });

    private static void Manifest(string dataRoot, string runId, string startedAt) =>
        Put(dataRoot, $"raw/{runId}/manifest.json", $$"""{"runId":"{{runId}}","startedAt":"{{startedAt}}"}""");

    private static void Put(string dataRoot, string relative, string content)
    {
        var full = Path.Combine(dataRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Processor.Tests/Reading/SourceReadersTests.cs`:
```csharp
using System.Text.Json;
using Processor.Reading;

namespace Processor.Tests.Reading;

public class SourceReadersTests
{
    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement;
    private static string Company(string slug) => slug == "wattpad" ? "Wattpad" : slug;

    [Fact]
    public void Adzuna_reads_snippet_location_and_real_salary()
    {
        var p = Assert.Single(SourceReaders.Adzuna(Root("""
            {"results":[{"id":"5869671117","title":"<strong>.Net</strong> Developer","company":{"display_name":"Astra"},
              "location":{"display_name":"Toronto, Ontario","area":["Canada","Ontario","Toronto"]},
              "description":"Design and maintain <strong>.NET</strong> apps…","redirect_url":"https://www.adzuna.ca/details/5869671117",
              "salary_min":90000,"salary_max":110000,"salary_is_predicted":"0"}]}
            """), "r1"));

        Assert.Equal("adzuna:5869671117", p.Key);
        Assert.Equal(".Net Developer", p.Title);
        Assert.Equal("Toronto, Ontario; Canada, Ontario, Toronto", p.Location);
        Assert.Equal("CA", p.Country);
        Assert.False(p.IsFullText);
        Assert.Equal(new SalaryRange(90000, 110000, SalaryPeriod.Year), p.Salary);
    }

    [Fact]
    public void Adzuna_ignores_predicted_salaries_and_skips_results_without_id()
    {
        var postings = SourceReaders.Adzuna(Root("""
            {"results":[{"id":"1","title":"Dev","salary_min":50000,"salary_is_predicted":"1"},{"title":"No id"}]}
            """), "r1").ToList();
        Assert.Null(Assert.Single(postings).Salary);
    }

    [Fact]
    public void Greenhouse_decodes_content_and_uses_company_name()
    {
        var p = Assert.Single(SourceReaders.Greenhouse(Root("""
            {"jobs":[{"id":101,"title":"Senior Developer","company_name":"Acme Corp","location":{"name":"Toronto, Canada"},
              "absolute_url":"https://boards.greenhouse.io/acme/jobs/101","content":"&lt;p&gt;C# &amp;amp; Azure&lt;/p&gt;"}]}
            """), "acme", "r1", Company));

        Assert.Equal("greenhouse:101", p.Key);
        Assert.Equal("Acme Corp", p.Company);
        Assert.Equal("C# & Azure", p.Description);
        Assert.True(p.IsFullText);
    }

    [Fact]
    public void Lever_reads_locations_workplace_lists_and_cad_salary()
    {
        var p = Assert.Single(SourceReaders.Lever(Root("""
            [{"id":"L1","text":"Backend Developer","categories":{"location":"Remote or Mississauga","allLocations":["Remote or Mississauga","Toronto"]},
              "workplaceType":"remote","country":"CA","hostedUrl":"https://jobs.lever.co/wattpad/L1","descriptionPlain":"Java.",
              "lists":[{"text":"Requirements","content":"<li>Kafka</li>"}],"additionalPlain":"Benefits.",
              "salaryRange":{"min":158000,"max":198000,"currency":"CAD","interval":"per-year-salary"}}]
            """), "wattpad", "r1", Company));

        Assert.Equal("Wattpad", p.Company);
        Assert.Equal("Remote or Mississauga; Toronto", p.Location);
        Assert.True(p.Remote);
        Assert.Equal("CA", p.Country);
        Assert.Contains("Kafka", p.Description);
        Assert.Contains("Benefits.", p.Description);
        Assert.Equal(new SalaryRange(158000, 198000, SalaryPeriod.Year), p.Salary);
    }

    [Fact]
    public void Lever_ignores_usd_salary()
    {
        var p = Assert.Single(SourceReaders.Lever(Root("""
            [{"id":"L2","text":"Dev","salaryRange":{"min":150000,"max":180000,"currency":"USD","interval":"per-year-salary"}}]
            """), "x", "r1", Company));
        Assert.Null(p.Salary);
    }

    [Fact]
    public void Ashby_reads_remote_country_and_cad_compensation()
    {
        var p = Assert.Single(SourceReaders.Ashby(Root("""
            {"jobs":[{"id":"S1","title":"Engineer","location":"Toronto","secondaryLocations":[{"location":"Remote (Canada)"}],
              "isRemote":true,"workplaceType":"Hybrid","isListed":true,"jobUrl":"https://jobs.ashbyhq.com/cohere/S1",
              "address":{"postalAddress":{"addressCountry":"Canada","addressLocality":"Toronto"}},"descriptionPlain":"Python.",
              "compensation":{"compensationTiers":[{"components":[
                 {"compensationType":"EquityPercentage","currencyCode":null,"minValue":0.1,"maxValue":0.2},
                 {"compensationType":"Salary","interval":"1 YEAR","currencyCode":"CAD","minValue":77800,"maxValue":106920}]}]}},
             {"id":"S2","title":"Hidden","isListed":false}]}
            """), "cohere", "r1", Company));

        Assert.Equal("Toronto; Remote (Canada)", p.Location);
        Assert.False(p.Remote); // hybrid; remote-Canada still comes from the location text in ScopeRules
        Assert.Equal("CA", p.Country);
        Assert.Equal(new SalaryRange(77800, 106920, SalaryPeriod.Year), p.Salary);
    }
}
```

`tests/Processor.Tests/Reading/InboxReaderTests.cs`:
```csharp
using Processor.Reading;
using Processor.Tests.TestSupport;

namespace Processor.Tests.Reading;

public class InboxReaderTests
{
    [Fact]
    public void Parse_reads_headers_then_body()
    {
        var (p, warning) = InboxReader.Parse("a.txt", "URL: https://x.test/1\r\nCompany: Bank\r\nTitle: Senior .NET Developer\r\nLocation: Toronto, ON\r\n\r\nWe need C#.\r\nAnd SQL.", "r9");

        Assert.Null(warning);
        Assert.NotNull(p);
        Assert.Equal("inbox:a.txt", p.Key);
        Assert.Equal("https://x.test/1", p.Url);
        Assert.Equal("Bank", p.Company);
        Assert.Equal("Toronto, ON", p.Location);
        Assert.Equal("We need C#.\nAnd SQL.", p.Description);
        Assert.Equal("r9", p.RunId);
        Assert.True(p.IsFullText);
    }

    [Fact]
    public void Missing_optional_headers_warn_but_keep_the_posting()
    {
        var (p, warning) = InboxReader.Parse("b.txt", "Title: Developer\n\nBody text.", "r1");
        Assert.NotNull(p);
        Assert.Contains("Company", warning);
        Assert.Contains("Location", warning);
    }

    [Fact]
    public void Missing_title_or_body_skips_the_file_with_a_warning()
    {
        Assert.Null(InboxReader.Parse("c.txt", "Just pasted text without headers", "r1").Posting);
        Assert.Contains("c.txt", InboxReader.Parse("c.txt", "Title: Dev\n\n", "r1").Warning);
    }

    [Fact]
    public void ReadAll_reads_only_txt_files_and_tolerates_a_missing_folder()
    {
        using var tmp = new TempDir();
        tmp.File("inbox/one.txt", "Title: Dev\nCompany: A\nLocation: Toronto\n\nBody.");
        tmp.File("inbox/README.md", "not a posting");

        Assert.Single(InboxReader.ReadAll(Path.Combine(tmp.Path, "inbox"), "r1").Postings);
        Assert.Empty(InboxReader.ReadAll(Path.Combine(tmp.Path, "missing"), "r1").Postings);
    }
}
```

`tests/Processor.Tests/Reading/ArchiveTests.cs`:
```csharp
using Processor.Reading;
using Processor.Tests.TestSupport;

namespace Processor.Tests.Reading;

public class ArchiveTests
{
    [Fact]
    public void Runs_are_completed_real_runs_oldest_first()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);

        var runs = Archive.Runs(tmp.Path);

        Assert.Equal(new[] { FixtureArchive.Run1, FixtureArchive.Run2, FixtureArchive.Run3 }, runs.Select(r => r.RunId));
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 7, 0, 0, TimeSpan.FromHours(-4)), runs[0].StartedAt);
    }

    [Fact]
    public void ReadPostings_reads_every_source_in_a_run()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);
        var run2 = Archive.Runs(tmp.Path)[1];

        var keys = Archive.ReadPostings(run2, slug => slug).Select(p => p.Key).Order().ToList();

        Assert.Equal(new[] { "adzuna:A1", "greenhouse:101", "lever:L1" }, keys);
    }

    [Fact]
    public void ReadAggregates_keeps_payload_and_term()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);
        var run3 = Archive.Runs(tmp.Path)[2];

        var agg = Assert.Single(Archive.ReadAggregates(run3));

        Assert.Equal(new AdzunaAggregate(FixtureArchive.Run3, "net-developer", "histogram", """{"histogram":{"100000":7}}"""), agg);
    }

    [Fact]
    public void Runs_of_an_empty_data_folder_is_empty()
    {
        using var tmp = new TempDir();
        Assert.Empty(Archive.Runs(tmp.Path));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Processor.Tests`
Expected: build FAILS — readers missing.

- [ ] **Step 4: Implement**

`src/Processor/Reading/Json.cs`:
```csharp
using System.Globalization;
using System.Text.Json;

namespace Processor.Reading;

// Tolerant accessors: sources change shape over time, so a missing or mistyped field reads as null instead of throwing.
internal static class Json
{
    public static string? Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            }
            : null;

    public static JsonElement? Obj(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    public static IEnumerable<JsonElement> Arr(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    public static decimal? Num(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out d)) return d;
        return null;
    }

    public static bool? Bool(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;
}
```

`src/Processor/Reading/SourceReaders.cs`:
```csharp
using System.Text.Json;

namespace Processor.Reading;

// One method per source: raw JSON (as archived by phase 0) → RawPosting. Postings missing an id or title are skipped.
public static class SourceReaders
{
    public static IEnumerable<RawPosting> Adzuna(JsonElement root, string runId)
    {
        foreach (var r in root.Arr("results"))
        {
            var id = r.Str("id");
            var title = r.Str("title");
            if (id is null || title is null) continue;

            var location = r.Obj("location");
            var area = string.Join(", ", (location?.Arr("area") ?? []).Select(a => a.GetString()).Where(a => !string.IsNullOrEmpty(a)));
            var where = string.Join("; ", new[] { location?.Str("display_name"), area }.Where(s => !string.IsNullOrEmpty(s)));

            SalaryRange? salary = null;
            if (r.Num("salary_min") is { } min && r.Str("salary_is_predicted") != "1")
                salary = new SalaryRange(min, r.Num("salary_max") ?? min, SalaryPeriod.Year);

            yield return new RawPosting("adzuna", id, runId, r.Obj("company")?.Str("display_name") ?? "Unknown",
                HtmlText.ToPlain(title), r.Str("redirect_url") ?? "", where, Remote: null, Country: "CA",
                HtmlText.ToPlain(r.Str("description")), IsFullText: false, salary);
        }
    }

    public static IEnumerable<RawPosting> Greenhouse(JsonElement root, string slug, string runId, Func<string, string> companyName)
    {
        foreach (var j in root.Arr("jobs"))
        {
            var id = j.Str("id");
            var title = j.Str("title");
            if (id is null || title is null) continue;

            yield return new RawPosting("greenhouse", id, runId, j.Str("company_name") ?? companyName(slug), title.Trim(),
                j.Str("absolute_url") ?? "", j.Obj("location")?.Str("name") ?? "", Remote: null, Country: null,
                HtmlText.ToPlain(j.Str("content")), IsFullText: true, Salary: null);
        }
    }

    public static IEnumerable<RawPosting> Lever(JsonElement root, string slug, string runId, Func<string, string> companyName)
    {
        if (root.ValueKind != JsonValueKind.Array) yield break;
        foreach (var j in root.EnumerateArray())
        {
            var id = j.Str("id");
            var title = j.Str("text");
            if (id is null || title is null) continue;

            var categories = j.Obj("categories");
            var all = (categories?.Arr("allLocations") ?? []).Select(l => l.GetString()).Where(l => !string.IsNullOrEmpty(l)).ToList();
            var location = all.Count > 0 ? string.Join("; ", all) : categories?.Str("location") ?? "";
            var workplace = j.Str("workplaceType");

            var parts = new List<string?> { j.Str("descriptionPlain") };
            parts.AddRange(j.Arr("lists").Select(l => $"{l.Str("text")}\n{HtmlText.ToPlain(l.Str("content"))}"));
            parts.Add(j.Str("additionalPlain"));

            SalaryRange? salary = null;
            if (j.Obj("salaryRange") is { } range && range.Str("currency") == "CAD" && range.Num("min") is { } min && range.Num("max") is { } max)
                salary = new SalaryRange(min, max, range.Str("interval")?.Contains("hour", StringComparison.OrdinalIgnoreCase) == true ? SalaryPeriod.Hour : SalaryPeriod.Year);

            yield return new RawPosting("lever", id, runId, companyName(slug), title.Trim(), j.Str("hostedUrl") ?? "", location,
                Remote: workplace is null ? null : workplace.Equals("remote", StringComparison.OrdinalIgnoreCase),
                Country: j.Str("country"), string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p))), IsFullText: true, salary);
        }
    }

    public static IEnumerable<RawPosting> Ashby(JsonElement root, string slug, string runId, Func<string, string> companyName)
    {
        foreach (var j in root.Arr("jobs"))
        {
            if (j.Bool("isListed") == false) continue;
            var id = j.Str("id");
            var title = j.Str("title");
            if (id is null || title is null) continue;

            var locations = new List<string?> { j.Str("location") };
            locations.AddRange(j.Arr("secondaryLocations").Select(s => s.Str("location")));
            var workplace = j.Str("workplaceType");
            var country = j.Obj("address")?.Obj("postalAddress")?.Str("addressCountry");

            yield return new RawPosting("ashby", id, runId, companyName(slug), title.Trim(), j.Str("jobUrl") ?? "",
                string.Join("; ", locations.Where(l => !string.IsNullOrEmpty(l))),
                Remote: workplace is null ? j.Bool("isRemote") : workplace.Equals("Remote", StringComparison.OrdinalIgnoreCase),
                Country: country is "Canada" or "CA" ? "CA" : string.IsNullOrEmpty(country) ? null : country,
                j.Str("descriptionPlain") ?? HtmlText.ToPlain(j.Str("descriptionHtml")), IsFullText: true, AshbySalary(j));
        }
    }

    private static SalaryRange? AshbySalary(JsonElement job)
    {
        var components = (job.Obj("compensation")?.Arr("compensationTiers") ?? []).SelectMany(t => t.Arr("components"));
        foreach (var c in components)
        {
            if (c.Str("compensationType") != "Salary" || c.Str("currencyCode") != "CAD") continue;
            if (c.Num("minValue") is not { } min || c.Num("maxValue") is not { } max) continue;
            var hourly = c.Str("interval")?.Contains("HOUR", StringComparison.OrdinalIgnoreCase) == true;
            return new SalaryRange(min, max, hourly ? SalaryPeriod.Hour : SalaryPeriod.Year);
        }
        return null;
    }
}
```

`src/Processor/Reading/InboxReader.cs`:
```csharp
namespace Processor.Reading;

public sealed record InboxResult(IReadOnlyList<RawPosting> Postings, IReadOnlyList<string> Warnings);

// data/inbox/*.txt: header lines (URL/Company/Title/Location/Saved), a blank line, then the posting text.
// Inbox postings count as seen in the latest run for as long as the file exists.
public static class InboxReader
{
    public static InboxResult ReadAll(string inboxDir, string runId)
    {
        if (!Directory.Exists(inboxDir)) return new InboxResult([], []);
        var postings = new List<RawPosting>();
        var warnings = new List<string>();
        foreach (var file in Directory.EnumerateFiles(inboxDir, "*.txt").Order(StringComparer.Ordinal))
        {
            var (posting, warning) = Parse(Path.GetFileName(file), File.ReadAllText(file), runId);
            if (posting is not null) postings.Add(posting);
            if (warning is not null) warnings.Add(warning);
        }
        return new InboxResult(postings, warnings);
    }

    public static (RawPosting? Posting, string? Warning) Parse(string fileName, string content, string runId)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        for (; i < lines.Length && lines[i].Trim().Length > 0; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon > 0) headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }
        var body = string.Join('\n', lines.Skip(i)).Trim();

        if (!headers.TryGetValue("Title", out var title) || title.Length == 0 || body.Length == 0)
            return (null, $"Inbox file {fileName} was skipped: it needs a 'Title:' line, a blank line, then the posting text.");

        var missing = new[] { "Company", "Location" }.Where(h => !headers.ContainsKey(h)).ToList();
        var warning = missing.Count > 0 ? $"Inbox file {fileName} has no {string.Join(" or ", missing)} line." : null;

        return (new RawPosting("inbox", fileName, runId, headers.GetValueOrDefault("Company", "Unknown"), title,
            headers.GetValueOrDefault("URL", ""), headers.GetValueOrDefault("Location", ""), Remote: null, Country: null,
            body, IsFullText: true, Salary: null), warning);
    }
}
```

`src/Processor/Reading/Archive.cs`:
```csharp
namespace Processor.Reading;

public sealed record ArchiveRun(string RunId, DateTimeOffset StartedAt, string FullPath);

public sealed record AdzunaAggregate(string RunId, string Term, string Kind, string PayloadJson);

// Reads the phase 0 layout: data/raw/<run>/{manifest.json, adzuna/search, adzuna/histogram, adzuna/top_companies, boards}.
public static class Archive
{
    private static readonly string[] AggregateKinds = ["histogram", "top_companies"];

    // Completed real runs only (they have a manifest); "_dryrun" and other "_" folders are never read.
    public static IReadOnlyList<ArchiveRun> Runs(string dataRoot)
    {
        var raw = Path.Combine(dataRoot, "raw");
        if (!Directory.Exists(raw)) return [];
        var runs = new List<ArchiveRun>();
        foreach (var dir in Directory.EnumerateDirectories(raw))
        {
            var id = Path.GetFileName(dir);
            var manifest = Path.Combine(dir, "manifest.json");
            if (id.StartsWith('_') || !File.Exists(manifest)) continue;
            using var doc = JsonFiles.Load(manifest);
            runs.Add(new ArchiveRun(id, doc.RootElement.GetProperty("startedAt").GetDateTimeOffset(), dir));
        }
        return [.. runs.OrderBy(r => r.StartedAt)];
    }

    public static IEnumerable<RawPosting> ReadPostings(ArchiveRun run, Func<string, string> companyName)
    {
        foreach (var file in Files(run, "adzuna", "search"))
        {
            using var doc = JsonFiles.Load(file);
            foreach (var p in SourceReaders.Adzuna(doc.RootElement, run.RunId)) yield return p;
        }

        foreach (var file in Files(run, "boards"))
        {
            var name = JsonFiles.BaseName(file);
            var dash = name.IndexOf('-');
            if (dash <= 0) continue;
            var (ats, slug) = (name[..dash], name[(dash + 1)..]);
            using var doc = JsonFiles.Load(file);
            var postings = ats switch
            {
                "greenhouse" => SourceReaders.Greenhouse(doc.RootElement, slug, run.RunId, companyName),
                "lever" => SourceReaders.Lever(doc.RootElement, slug, run.RunId, companyName),
                "ashby" => SourceReaders.Ashby(doc.RootElement, slug, run.RunId, companyName),
                _ => [],
            };
            foreach (var p in postings) yield return p;
        }
    }

    public static IEnumerable<AdzunaAggregate> ReadAggregates(ArchiveRun run)
    {
        foreach (var kind in AggregateKinds)
            foreach (var file in Files(run, "adzuna", kind))
            {
                var name = JsonFiles.BaseName(file);
                var term = name.EndsWith("-gta", StringComparison.Ordinal) ? name[..^4] : name;
                using var doc = JsonFiles.Load(file);
                yield return new AdzunaAggregate(run.RunId, term, kind, doc.RootElement.GetRawText());
            }
    }

    private static IEnumerable<string> Files(ArchiveRun run, params string[] parts)
    {
        var dir = Path.Combine([run.FullPath, .. parts]);
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir).Where(JsonFiles.IsJson).Order(StringComparer.Ordinal)
            : Enumerable.Empty<string>();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Processor.Tests`
Expected: all reader, inbox and archive tests PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add raw archive readers for Adzuna, boards and inbox"
```

---

### Task 7: Interpretation, sightings and duplicates

**Files:**
- Create: `src/Processor/Interpreting/Posting.cs`, `src/Processor/Interpreting/Deduplicator.cs`
- Test: `tests/Processor.Tests/Interpreting/InterpreterTests.cs`, `SightingCollectorTests.cs`, `DeduplicatorTests.cs`

**Interfaces:**
- Consumes: `RawPosting`, `SalaryRange` (Task 1), `ScopeRules`, `Scope` (Task 2), `TitleRules`, `RoleFamily`, `Seniority` (Task 3), `SalaryParser` (Task 4), `SkillCatalog` (Task 5)
- Produces:
  - `record Posting(string Id, string Source, string SourceId, string Company, string Title, string Url, string LocationText, Scope Scope, RoleFamily Family, Seniority Seniority, SalaryRange? Salary, string SalaryOrigin, string Description, bool IsFullText, bool CitizenshipOrClearance, IReadOnlySet<string> Skills, IReadOnlyList<string> SightingRuns, string? DuplicateOf)` with `FirstSeenRun`, `LastSeenRun`
  - `Interpreter(ScopeRules, SkillCatalog)` with `Posting Interpret(RawPosting latest, IReadOnlyList<string> sightingRuns)`
  - `SightingCollector` with `Add(RawPosting)` and `IEnumerable<(RawPosting Latest, IReadOnlyList<string> Runs)> Entries`
  - `Deduplicator.MarkDuplicates(IReadOnlyList<Posting>, IReadOnlyList<string> runOrder) : IReadOnlyList<Posting>`; `Deduplicator.Normalize(string) : string`

- [ ] **Step 1: Write the failing tests**

`tests/Processor.Tests/Interpreting/InterpreterTests.cs`:
```csharp
using Processor.Interpreting;
using Processor.Reading;
using Processor.Tests.Configuration;

namespace Processor.Tests.Interpreting;

public class InterpreterTests
{
    private static readonly Interpreter Interpreter = new(
        new ScopeRules(RepoConfig.Options().Gta),
        SkillCatalog.Load(RepoConfig.PathOf("skills.json")));

    private static RawPosting Raw(string title = "Senior .NET Developer", string location = "Toronto, Ontario, Canada", string description = "C# and Azure.", SalaryRange? salary = null, string? country = null) =>
        new("greenhouse", "7", "r2", "Acme", title, "https://x.test/7", location, null, country, description, true, salary);

    [Fact]
    public void Interprets_scope_title_and_skills()
    {
        var p = Interpreter.Interpret(Raw(), ["r1", "r2"]);

        Assert.Equal("greenhouse:7", p.Id);
        Assert.True(p.Scope.IsGta);
        Assert.Equal(RoleFamily.Dotnet, p.Family);
        Assert.Equal(Seniority.Senior, p.Seniority);
        Assert.Superset(new HashSet<string> { ".NET", "C#", "Azure" }, (ISet<string>)p.Skills.ToHashSet());
        Assert.Equal("r1", p.FirstSeenRun);
        Assert.Equal("r2", p.LastSeenRun);
    }

    [Fact]
    public void Structured_salary_wins_over_text()
    {
        var p = Interpreter.Interpret(Raw(description: "Range $90,000 - $100,000 in Toronto.", salary: new SalaryRange(120000, 140000, SalaryPeriod.Year)), ["r1"]);
        Assert.Equal("structured", p.SalaryOrigin);
        Assert.Equal(120000, p.Salary!.Min);
    }

    [Fact]
    public void Text_salary_is_parsed_when_no_structured_salary()
    {
        var p = Interpreter.Interpret(Raw(description: "Salary in Toronto: $110,000 - $130,000."), ["r1"]);
        Assert.Equal("parsed", p.SalaryOrigin);
        Assert.Equal(110000, p.Salary!.Min);
    }

    [Fact]
    public void Implausible_structured_salary_is_dropped()
    {
        var p = Interpreter.Interpret(Raw(salary: new SalaryRange(10000, 10000, SalaryPeriod.Year)), ["r1"]);
        Assert.Null(p.Salary);
        Assert.Equal("none", p.SalaryOrigin);
    }

    [Theory]
    [InlineData("Must be a Canadian citizen.", true)]
    [InlineData("Ability to obtain Secret clearance.", true)]
    [InlineData("Enhanced Reliability security screening required.", true)]
    [InlineData("Open to permanent residents and citizens alike.", false)]
    public void Citizenship_or_clearance_flag(string description, bool flagged) =>
        Assert.Equal(flagged, Interpreter.Interpret(Raw(description: description), ["r1"]).CitizenshipOrClearance);
}
```

`tests/Processor.Tests/Interpreting/SightingCollectorTests.cs`:
```csharp
using Processor.Interpreting;
using Processor.Reading;

namespace Processor.Tests.Interpreting;

public class SightingCollectorTests
{
    private static RawPosting Raw(string id, string run, string title) =>
        new("adzuna", id, run, "Co", title, "", "Toronto", null, "CA", "", false, null);

    [Fact]
    public void Latest_content_wins_and_each_run_counts_once()
    {
        var c = new SightingCollector();
        c.Add(Raw("1", "r1", "Old title"));
        c.Add(Raw("1", "r1", "Old title")); // same job found by two search terms in one run
        c.Add(Raw("1", "r2", "New title"));
        c.Add(Raw("2", "r2", "Other"));

        var entries = c.Entries.OrderBy(e => e.Latest.SourceId).ToList();

        Assert.Equal("New title", entries[0].Latest.Title);
        Assert.Equal(new[] { "r1", "r2" }, entries[0].Runs);
        Assert.Equal(new[] { "r2" }, entries[1].Runs);
    }
}
```

`tests/Processor.Tests/Interpreting/DeduplicatorTests.cs`:
```csharp
using Processor.Interpreting;

namespace Processor.Tests.Interpreting;

public class DeduplicatorTests
{
    private static Posting P(string id, string company, string title, string? city, bool fullText, params string[] runs) =>
        new(id, id.Split(':')[0], id.Split(':')[1], company, title, "", "", new Scope(city, city is not null, false),
            RoleFamily.Fullstack, Seniority.Senior, null, "none", "", fullText, false, new HashSet<string>(), runs, null);

    private static readonly string[] RunOrder = ["r1", "r2", "r3"];

    [Fact]
    public void Cross_source_copy_points_at_the_full_text_posting()
    {
        var result = Deduplicator.MarkDuplicates([
            P("adzuna:A3", "Acme", "Senior Full Stack Developer", "Toronto", false, "r3"),
            P("greenhouse:101", "Acme Inc.", "Senior Full-Stack Developer", "Toronto", true, "r1", "r2", "r3"),
        ], RunOrder);

        Assert.Null(result.Single(p => p.Id == "greenhouse:101").DuplicateOf);
        Assert.Equal("greenhouse:101", result.Single(p => p.Id == "adzuna:A3").DuplicateOf);
    }

    [Fact]
    public void Same_title_in_different_cities_is_not_a_duplicate()
    {
        var result = Deduplicator.MarkDuplicates([
            P("greenhouse:1", "Acme", "Developer", "Toronto", true, "r1"),
            P("greenhouse:2", "Acme", "Developer", "Mississauga", true, "r1"),
        ], RunOrder);
        Assert.All(result, p => Assert.Null(p.DuplicateOf));
    }

    [Fact]
    public void Among_full_text_copies_the_most_recently_seen_wins()
    {
        var result = Deduplicator.MarkDuplicates([
            P("greenhouse:1", "Acme", "Developer", "Toronto", true, "r1"),
            P("lever:9", "Acme", "Developer", "Toronto", true, "r1", "r3"),
        ], RunOrder);
        Assert.Equal("lever:9", result.Single(p => p.Id == "greenhouse:1").DuplicateOf);
    }

    [Theory]
    [InlineData("Acme Inc.", "acme")]
    [InlineData("The Example Company Ltd", "example")]
    [InlineData("Senior Full-Stack Developer", "senior full stack developer")]
    [InlineData("C# / .NET Developer", "c# net developer")]
    public void Normalize(string input, string expected) => Assert.Equal(expected, Deduplicator.Normalize(input));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Processor.Tests`
Expected: build FAILS — `Posting`, `Interpreter`, `SightingCollector`, `Deduplicator` missing.

- [ ] **Step 3: Implement**

`src/Processor/Interpreting/Posting.cs`:
```csharp
using System.Text.RegularExpressions;
using Processor.Reading;

namespace Processor.Interpreting;

// One unique job after interpretation. Id = "<source>:<sourceId>"; SightingRuns are in chronological order.
public sealed record Posting(
    string Id,
    string Source,
    string SourceId,
    string Company,
    string Title,
    string Url,
    string LocationText,
    Scope Scope,
    RoleFamily Family,
    Seniority Seniority,
    SalaryRange? Salary,
    string SalaryOrigin,
    string Description,
    bool IsFullText,
    bool CitizenshipOrClearance,
    IReadOnlySet<string> Skills,
    IReadOnlyList<string> SightingRuns,
    string? DuplicateOf)
{
    public string FirstSeenRun => SightingRuns[0];
    public string LastSeenRun => SightingRuns[^1];
}

public sealed partial class Interpreter(ScopeRules scope, SkillCatalog skills)
{
    public Posting Interpret(RawPosting raw, IReadOnlyList<string> sightingRuns)
    {
        var where = scope.Classify(raw);
        var inCanada = where.InScope || scope.IsCanadian(raw);

        var salary = raw.Salary is { IsPlausible: true } structured ? structured : null;
        var origin = "structured";
        if (salary is null)
        {
            salary = SalaryParser.FromText(raw.Description, inCanada);
            origin = salary is null ? "none" : "parsed";
        }

        var text = $"{raw.Title}\n{raw.Description}";
        return new Posting(raw.Key, raw.Source, raw.SourceId, raw.Company, raw.Title, raw.Url, raw.Location, where,
            TitleRules.Family(raw.Title), TitleRules.Seniority(raw.Title), salary, origin, raw.Description, raw.IsFullText,
            CitizenshipOrClearance().IsMatch(text), skills.Match(text), sightingRuns, DuplicateOf: null);
    }

    [GeneratedRegex(@"\b(?:canadian citizen(?:ship)?|citizens? of canada|security clearance|secret clearance|top secret|enhanced reliability|reliability (?:status|clearance)|nato secret)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CitizenshipOrClearance();
}

// Keeps the latest content per posting plus every run that saw it. Runs must be added in chronological order.
public sealed class SightingCollector
{
    private readonly Dictionary<string, (RawPosting Latest, List<string> Runs)> _byKey = new(StringComparer.Ordinal);

    public void Add(RawPosting raw)
    {
        if (_byKey.TryGetValue(raw.Key, out var entry))
        {
            if (entry.Runs[^1] != raw.RunId) entry.Runs.Add(raw.RunId);
            _byKey[raw.Key] = (raw, entry.Runs);
        }
        else
        {
            _byKey[raw.Key] = (raw, [raw.RunId]);
        }
    }

    public IEnumerable<(RawPosting Latest, IReadOnlyList<string> Runs)> Entries =>
        _byKey.Values.Select(e => (e.Latest, (IReadOnlyList<string>)e.Runs));
}
```

`src/Processor/Interpreting/Deduplicator.cs`:
```csharp
using System.Text.RegularExpressions;

namespace Processor.Interpreting;

// The same job often appears on Adzuna and on the company's own board, or is reposted with a new id.
// Postings with the same normalized company + title + city form a cluster; one is kept, the rest point at it.
public static partial class Deduplicator
{
    private static readonly HashSet<string> CompanyNoise = ["inc", "ltd", "llc", "corp", "corporation", "limited", "co", "company", "the"];

    public static IReadOnlyList<Posting> MarkDuplicates(IReadOnlyList<Posting> postings, IReadOnlyList<string> runOrder)
    {
        var order = runOrder.Select((run, i) => (run, i)).ToDictionary(x => x.run, x => x.i, StringComparer.Ordinal);
        var duplicateOf = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var cluster in postings.GroupBy(Key).Where(g => g.Key.Length > 0 && g.Count() > 1))
        {
            var keep = cluster
                .OrderByDescending(p => p.IsFullText)
                .ThenByDescending(p => order.GetValueOrDefault(p.LastSeenRun, -1))
                .ThenBy(p => p.Id, StringComparer.Ordinal)
                .First();
            foreach (var p in cluster.Where(p => p.Id != keep.Id)) duplicateOf[p.Id] = keep.Id;
        }

        return [.. postings.Select(p => duplicateOf.TryGetValue(p.Id, out var keepId) ? p with { DuplicateOf = keepId } : p)];
    }

    public static string Normalize(string text)
    {
        var words = NonWord().Replace(text.ToLowerInvariant(), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !CompanyNoise.Contains(w));
        return string.Join(' ', words);
    }

    private static string Key(Posting p)
    {
        var company = Normalize(p.Company);
        var title = Normalize(p.Title);
        return company.Length == 0 || title.Length == 0 ? "" : $"{company}|{title}|{p.Scope.City?.ToLowerInvariant()}";
    }

    [GeneratedRegex(@"[^\p{L}\p{N}#+]+")]
    private static partial Regex NonWord();
}
```

Note: `Normalize` removes `CompanyNoise` words from titles too; titles rarely contain them, and the same rule on both sides keeps keys consistent.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Processor.Tests`
Expected: all interpreter, collector and dedupe tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Interpret postings, collect sightings and mark cross-source duplicates"
```

---

### Task 8: Job Bank summary and statistics helper

**Files:**
- Create: `src/Processor/Reading/JobBankReader.cs`, `src/Processor/Stats.cs`
- Test: `tests/Processor.Tests/Reading/JobBankReaderTests.cs`, `tests/Processor.Tests/StatsTests.cs`

**Interfaces:**
- Consumes: `JobBankOptions` (Task 2), `SalaryRange.HoursPerYear` (Task 1)
- Produces:
  - `record JobBankMonth(DateOnly Month, string NocCode, string NocName, int Postings, int Vacancies, decimal? WageMinMedian, decimal? WageMaxMedian)`
  - `JobBankReader.Summarize(TextReader reader, DateOnly month, JobBankOptions options) : IReadOnlyList<JobBankMonth>`
  - `JobBankReader.SummarizeDirectory(string jobBankDir, JobBankOptions options) : IReadOnlyList<JobBankMonth>` (files `yyyy-MM_*.csv.gz`, UTF-16 TSV)
  - `Stats.Percentile(IReadOnlyList<decimal> sorted, double p) : decimal` (linear interpolation; throws on empty) and `Stats.Median(IEnumerable<decimal>) : decimal?`

- [ ] **Step 1: Write the failing tests**

`tests/Processor.Tests/StatsTests.cs`:
```csharp
namespace Processor.Tests;

public class StatsTests
{
    [Fact]
    public void Percentile_interpolates()
    {
        decimal[] sorted = [100, 200, 300, 400];
        Assert.Equal(250m, Stats.Percentile(sorted, 0.5));
        Assert.Equal(175m, Stats.Percentile(sorted, 0.25));
        Assert.Equal(100m, Stats.Percentile(sorted, 0));
        Assert.Equal(400m, Stats.Percentile(sorted, 1));
    }

    [Fact]
    public void Median_of_nothing_is_null()
    {
        Assert.Null(Stats.Median([]));
        Assert.Equal(3m, Stats.Median([5, 1, 3]));
    }
}
```

`tests/Processor.Tests/Reading/JobBankReaderTests.cs`:
```csharp
using System.IO.Compression;
using System.Text;
using Processor.Configuration;
using Processor.Reading;
using Processor.Tests.TestSupport;

namespace Processor.Tests.Reading;

public class JobBankReaderTests
{
    private static readonly JobBankOptions Options = new()
    {
        EconomicRegion = "Toronto Region",
        NocCodes = new() { ["21232"] = "Software developers and programmers", ["21231"] = "Software engineers and designers" },
    };

    // Real header names, including the double space in "Economic  Region".
    private const string Tsv =
        "Job Title\tNOC21 Code\tNOC21 Code Name\tEconomic  Region\tVacancy Count\tSalary Per\tSalary Minimum\tSalary Maximum\n" +
        "Dev A\t21232\tSoftware developers and programmers\tToronto Region\t2\tHour\t40\t50\n" +
        "Dev B\t21232\tSoftware developers and programmers\tToronto Region\t1\tYear\t90000\t110000\n" +
        "Dev C\t21232\tSoftware developers and programmers\tOttawa Region\t1\tYear\t80000\t90000\n" +
        "Cook\t63200\tCooks\tToronto Region\t3\tHour\t17\t18\n" +
        "Eng\t21231\tSoftware engineers and designers\tToronto Region\tNA\tNA\tNA\tNA\n";

    [Fact]
    public void Summarize_counts_configured_nocs_in_the_region_with_yearly_wage_medians()
    {
        var months = JobBankReader.Summarize(new StringReader(Tsv), new DateOnly(2026, 8, 1), Options).OrderBy(m => m.NocCode).ToList();

        Assert.Equal(2, months.Count);
        var engineers = months[0];
        Assert.Equal(("21231", 1, 1), (engineers.NocCode, engineers.Postings, engineers.Vacancies)); // NA vacancy counts as 1
        Assert.Null(engineers.WageMinMedian);

        var developers = months[1];
        Assert.Equal(("21232", 2, 3), (developers.NocCode, developers.Postings, developers.Vacancies));
        Assert.Equal((83_200m + 90_000m) / 2, developers.WageMinMedian); // 40/h × 2080 = 83,200
        Assert.Equal((104_000m + 110_000m) / 2, developers.WageMaxMedian);
    }

    [Fact]
    public void SummarizeDirectory_reads_gzipped_utf16_files_named_by_month()
    {
        using var tmp = new TempDir();
        var file = Path.Combine(tmp.Path, "2026-08_abc.csv.gz");
        using (var gz = new GZipStream(File.Create(file), CompressionLevel.Fastest))
        using (var writer = new StreamWriter(gz, Encoding.Unicode))
            writer.Write(Tsv);
        File.WriteAllText(Path.Combine(tmp.Path, "2026-07_x.csv.gz.tmp"), "partial");

        var months = JobBankReader.SummarizeDirectory(tmp.Path, Options);

        Assert.All(months, m => Assert.Equal(new DateOnly(2026, 8, 1), m.Month));
        Assert.Equal(2, months.Count);
        Assert.Empty(JobBankReader.SummarizeDirectory(Path.Combine(tmp.Path, "missing"), Options));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Processor.Tests`
Expected: build FAILS — `Stats`, `JobBankReader` missing.

- [ ] **Step 3: Implement**

`src/Processor/Stats.cs`:
```csharp
namespace Processor;

public static class Stats
{
    // Linear interpolation between closest ranks; `sorted` must be ascending and non-empty.
    public static decimal Percentile(IReadOnlyList<decimal> sorted, double p)
    {
        if (sorted.Count == 0) throw new ArgumentException("No values.", nameof(sorted));
        var rank = (decimal)p * (sorted.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
    }

    public static decimal? Median(IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? null : Percentile(sorted, 0.5);
    }
}
```

`src/Processor/Reading/JobBankReader.cs`:
```csharp
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Processor.Configuration;

namespace Processor.Reading;

public sealed record JobBankMonth(DateOnly Month, string NocCode, string NocName, int Postings, int Vacancies, decimal? WageMinMedian, decimal? WageMaxMedian);

// Job Bank's monthly open-data file: UTF-16 tab-separated, one row per posting, gzipped by the archiver.
public static partial class JobBankReader
{
    public static IReadOnlyList<JobBankMonth> SummarizeDirectory(string jobBankDir, JobBankOptions options)
    {
        if (!Directory.Exists(jobBankDir)) return [];
        var months = new List<JobBankMonth>();
        foreach (var file in Directory.EnumerateFiles(jobBankDir).Where(f => f.EndsWith(".csv.gz", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            if (!DateOnly.TryParseExact(name[..Math.Min(7, name.Length)], "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)) continue;
            using var gz = new GZipStream(File.OpenRead(file), CompressionMode.Decompress);
            using var reader = new StreamReader(gz, Encoding.Unicode, detectEncodingFromByteOrderMarks: true);
            months.AddRange(Summarize(reader, month, options));
        }
        return months;
    }

    public static IReadOnlyList<JobBankMonth> Summarize(TextReader reader, DateOnly month, JobBankOptions options)
    {
        var header = reader.ReadLine()?.Split('\t').Select(h => Spaces().Replace(h.Trim(), " ")).ToList() ?? [];
        int Col(string name) => header.IndexOf(name);
        var (noc, region, vacancies, per, min, max) = (Col("NOC21 Code"), Col("Economic Region"), Col("Vacancy Count"), Col("Salary Per"), Col("Salary Minimum"), Col("Salary Maximum"));
        if (noc < 0 || region < 0) throw new InvalidDataException("Job Bank file has no 'NOC21 Code' or 'Economic Region' column.");

        var groups = new Dictionary<string, (int Postings, int Vacancies, List<decimal> Mins, List<decimal> Maxes)>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var cells = line.Split('\t');
            string Cell(int i) => i >= 0 && i < cells.Length ? cells[i].Trim() : "";
            var code = Cell(noc);
            if (!options.NocCodes.ContainsKey(code) || !Cell(region).Equals(options.EconomicRegion, StringComparison.OrdinalIgnoreCase)) continue;

            var g = groups.TryGetValue(code, out var existing) ? existing : (0, 0, new List<decimal>(), new List<decimal>());
            g.Postings++;
            g.Vacancies += int.TryParse(Cell(vacancies), out var v) && v > 0 ? v : 1;
            var factor = Cell(per).ToLowerInvariant() switch { "hour" => SalaryRange.HoursPerYear, "week" => 52m, "month" => 12m, "year" => 1m, _ => 0m };
            if (factor > 0 && TryMoney(Cell(min), out var lo) && TryMoney(Cell(max), out var hi))
            {
                g.Mins.Add(lo * factor);
                g.Maxes.Add(hi * factor);
            }
            groups[code] = g;
        }

        return [.. groups.Select(kv => new JobBankMonth(month, kv.Key, options.NocCodes[kv.Key], kv.Value.Postings, kv.Value.Vacancies,
            Stats.Median(kv.Value.Mins), Stats.Median(kv.Value.Maxes)))];
    }

    private static bool TryMoney(string value, out decimal amount) =>
        decimal.TryParse(value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount > 0;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
```

`using Processor.Reading;` is already the namespace; `SalaryRange` lives there, so no extra using is needed.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Processor.Tests`
Expected: Job Bank and Stats tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Summarize Job Bank tech postings for the Toronto region"
```

### Task 9: Database schema and transactional rebuild writer

**Files:**
- Create: `src/Processor/Data/Entities.cs`, `src/Processor/Data/TrackerDb.cs`, `src/Processor/Data/RebuildWriter.cs`
- Create (generated): `src/Processor/Data/Migrations/*`
- Create: `tests/Processor.Tests/TestSupport/PostgresFixture.cs`, `tests/Processor.Tests/TestSupport/Make.cs`
- Test: `tests/Processor.Tests/Data/RebuildWriterTests.cs`

**Interfaces:**
- Consumes: `ArchiveRun`, `AdzunaAggregate`, `JobBankMonth` (Tasks 6, 8); `Posting`, `SkillDefinition`, `TitleRules.FamilyKey` (Tasks 3, 5, 7)
- Produces:
  - Entities `RunEntity`, `PostingEntity`, `SightingEntity`, `SkillEntity`, `PostingSkillEntity`, `AdzunaAggregateEntity`, `JobBankMonthEntity` (snake_case tables `runs`, `postings`, `posting_sightings`, `skills`, `posting_skills`, `adzuna_aggregates`, `jobbank_monthly`)
  - `TrackerDb(DbContextOptions<TrackerDb>)` with DbSets `Runs`, `Postings`, `PostingSightings`, `Skills`, `PostingSkills`, `AdzunaAggregates`, `JobBankMonths`
  - `TrackerDbFactory.Options(string connectionString) : DbContextOptions<TrackerDb>` (Npgsql + snake_case) and design-time factory
  - `record RebuildData(IReadOnlyList<ArchiveRun> Runs, IReadOnlyList<SkillDefinition> Skills, IReadOnlyList<Posting> Postings, IReadOnlyList<AdzunaAggregate> Aggregates, IReadOnlyList<JobBankMonth> JobBank)`
  - `RebuildWriter(TrackerDb).WriteAsync(RebuildData, CancellationToken)` — one transaction; throws (and rolls back) if a posting references an unknown run or skill
  - Test helpers `PostgresFixture` (Testcontainers, collection `"postgres"`, `OpenAsync()` returns a migrated `TrackerDb`) and `Make.Posting(...)`

Decision (resolves the spec's open item): bulk tables use Npgsql **binary COPY** from the start. By March there will be about 20k postings, about 300k posting–skill rows and about 200k sightings, and EF `SaveChanges` at that volume takes minutes, not seconds. EF Core stays in charge of the schema (migrations) and of reading.

- [ ] **Step 1: Write the entities and context**

`src/Processor/Data/Entities.cs`:
```csharp
namespace Processor.Data;

public sealed class RunEntity
{
    public string RunId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
}

public sealed class PostingEntity
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string Company { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string LocationText { get; set; } = "";
    public string? City { get; set; }
    public bool IsGta { get; set; }
    public bool IsRemoteCanada { get; set; }
    public bool InScope { get; set; }
    public string RoleFamily { get; set; } = "";
    public string Seniority { get; set; } = "";
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public string? SalaryPeriod { get; set; }
    public string SalaryOrigin { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsFullText { get; set; }
    public bool CitizenshipOrClearance { get; set; }
    public string FirstSeenRun { get; set; } = "";
    public string LastSeenRun { get; set; } = "";
    public string? DuplicateOf { get; set; }
    public List<SightingEntity> Sightings { get; set; } = [];
    public List<PostingSkillEntity> Skills { get; set; } = [];
}

public sealed class SightingEntity
{
    public string PostingId { get; set; } = "";
    public string RunId { get; set; } = "";
    public RunEntity? Run { get; set; }
}

public sealed class SkillEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
}

public sealed class PostingSkillEntity
{
    public string PostingId { get; set; } = "";
    public int SkillId { get; set; }
    public SkillEntity? Skill { get; set; }
}

public sealed class AdzunaAggregateEntity
{
    public string RunId { get; set; } = "";
    public string Term { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Payload { get; set; } = "";
}

public sealed class JobBankMonthEntity
{
    public DateOnly Month { get; set; }
    public string NocCode { get; set; } = "";
    public string NocName { get; set; } = "";
    public int Postings { get; set; }
    public int Vacancies { get; set; }
    public decimal? WageMinMedian { get; set; }
    public decimal? WageMaxMedian { get; set; }
}
```

`src/Processor/Data/TrackerDb.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Processor.Data;

public sealed class TrackerDb(DbContextOptions<TrackerDb> options) : DbContext(options)
{
    public DbSet<RunEntity> Runs => Set<RunEntity>();
    public DbSet<PostingEntity> Postings => Set<PostingEntity>();
    public DbSet<SightingEntity> PostingSightings => Set<SightingEntity>();
    public DbSet<SkillEntity> Skills => Set<SkillEntity>();
    public DbSet<PostingSkillEntity> PostingSkills => Set<PostingSkillEntity>();
    public DbSet<AdzunaAggregateEntity> AdzunaAggregates => Set<AdzunaAggregateEntity>();
    public DbSet<JobBankMonthEntity> JobBankMonths => Set<JobBankMonthEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<RunEntity>().HasKey(r => r.RunId);

        b.Entity<PostingEntity>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => new { p.InScope, p.DuplicateOf });
            e.HasMany(p => p.Sightings).WithOne().HasForeignKey(s => s.PostingId);
            e.HasMany(p => p.Skills).WithOne().HasForeignKey(s => s.PostingId);
        });

        b.Entity<SightingEntity>(e =>
        {
            e.HasKey(s => new { s.PostingId, s.RunId });
            e.HasOne(s => s.Run).WithMany().HasForeignKey(s => s.RunId);
        });

        b.Entity<SkillEntity>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).ValueGeneratedNever();
            e.HasIndex(s => s.Name).IsUnique();
        });

        b.Entity<PostingSkillEntity>(e =>
        {
            e.HasKey(s => new { s.PostingId, s.SkillId });
            e.HasOne(s => s.Skill).WithMany().HasForeignKey(s => s.SkillId);
        });

        b.Entity<AdzunaAggregateEntity>(e =>
        {
            e.HasKey(a => new { a.RunId, a.Term, a.Kind });
            e.Property(a => a.Payload).HasColumnType("jsonb");
        });

        b.Entity<JobBankMonthEntity>(e =>
        {
            e.ToTable("jobbank_monthly");
            e.HasKey(j => new { j.Month, j.NocCode });
        });
    }
}

// Used by `dotnet ef` (no connection needed to add migrations) and by tests.
public sealed class TrackerDbFactory : IDesignTimeDbContextFactory<TrackerDb>
{
    public TrackerDb CreateDbContext(string[] args) => new(Options("Host=localhost;Port=5434;Database=tracker;Username=tracker"));

    public static DbContextOptions<TrackerDb> Options(string connectionString) =>
        new DbContextOptionsBuilder<TrackerDb>().UseNpgsql(connectionString).UseSnakeCaseNamingConvention().Options;
}
```

- [ ] **Step 2: Generate the initial migration**

```bash
dotnet ef migrations add InitialCreate --project src/Processor --output-dir Data/Migrations
```
Expected: `Done.` and files under `src/Processor/Data/Migrations/`. Open the generated `*_InitialCreate.cs` and confirm the tables are named `runs`, `postings`, `posting_sightings`, `skills`, `posting_skills`, `adzuna_aggregates`, `jobbank_monthly`, the columns are snake_case, and `payload` is `jsonb`. If any name differs, fix the model (not the COPY statements) and regenerate.

- [ ] **Step 3: Write the test helpers and failing tests**

`tests/Processor.Tests/TestSupport/PostgresFixture.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Processor.Data;
using Testcontainers.PostgreSql;

namespace Processor.Tests.TestSupport;

// One throwaway Postgres 17 container per test run (Docker must be running).
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:17").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<TrackerDb> OpenAsync()
    {
        var db = new TrackerDb(TrackerDbFactory.Options(ConnectionString));
        await db.Database.MigrateAsync();
        return db;
    }
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
```

`tests/Processor.Tests/TestSupport/Make.cs`:
```csharp
using Processor.Interpreting;
using Processor.Reading;

namespace Processor.Tests.TestSupport;

public static class Make
{
    public static Posting Posting(string id, string[] runs, string[] skills, string? city = "Toronto", bool remote = false,
        RoleFamily family = RoleFamily.Dotnet, Seniority seniority = Seniority.Senior, SalaryRange? salary = null,
        bool fullText = true, string company = "Acme", string title = "Senior .NET Developer", bool flagged = false, string? duplicateOf = null) =>
        new(id, id.Split(':')[0], id.Split(':')[1], company, title, $"https://x.test/{id}", city ?? "Remote (Canada)",
            new Scope(city, city is not null, remote), family, seniority, salary, salary is null ? "none" : "structured",
            $"Description of {id}", fullText, flagged, skills.ToHashSet(), runs, duplicateOf);
}
```

`tests/Processor.Tests/Data/RebuildWriterTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Processor.Data;
using Processor.Interpreting;
using Processor.Reading;
using Processor.Tests.TestSupport;

namespace Processor.Tests.Data;

[Collection("postgres")]
[Trait("Category", "Integration")]
public class RebuildWriterTests(PostgresFixture pg)
{
    private static readonly ArchiveRun R1 = new("r1", new DateTimeOffset(2026, 9, 1, 7, 0, 0, TimeSpan.FromHours(-4)), "");
    private static readonly ArchiveRun R2 = new("r2", new DateTimeOffset(2026, 9, 4, 7, 0, 0, TimeSpan.FromHours(-4)), "");
    private static readonly SkillDefinition[] Skills = [new("C#", "language", ["C#"]), new("Azure", "cloud", ["Azure"])];

    private static RebuildData Data(params string[] skillsOfSecond) => new(
        [R1, R2],
        Skills,
        [
            Make.Posting("greenhouse:1", ["r1", "r2"], ["C#"], salary: new SalaryRange(120000, 150000, SalaryPeriod.Year)),
            Make.Posting("adzuna:2", ["r2"], skillsOfSecond, city: null, remote: true, fullText: false, duplicateOf: "greenhouse:1"),
        ],
        [new AdzunaAggregate("r2", "net-developer", "histogram", """{"histogram":{"100000":7}}""")],
        [new JobBankMonth(new DateOnly(2026, 8, 1), "21232", "Software developers and programmers", 2, 3, 86600, 107000)]);

    private static async Task<string> Snapshot(TrackerDb db)
    {
        var postings = await db.Postings.AsNoTracking().OrderBy(p => p.Id)
            .Select(p => $"{p.Id}|{p.City}|{p.InScope}|{p.RoleFamily}|{p.Seniority}|{p.SalaryMin}|{p.SalaryPeriod}|{p.DuplicateOf}|{p.FirstSeenRun}|{p.LastSeenRun}")
            .ToListAsync();
        var counts = $"{await db.Runs.CountAsync()}|{await db.Skills.CountAsync()}|{await db.PostingSightings.CountAsync()}|{await db.PostingSkills.CountAsync()}|{await db.AdzunaAggregates.CountAsync()}|{await db.JobBankMonths.CountAsync()}";
        return string.Join("\n", postings.Append(counts));
    }

    [Fact]
    public async Task Rebuild_writes_every_table()
    {
        await using var db = await pg.OpenAsync();

        await new RebuildWriter(db).WriteAsync(Data("Azure"), CancellationToken.None);

        var first = await db.Postings.AsNoTracking().SingleAsync(p => p.Id == "greenhouse:1");
        Assert.Equal(("dotnet", "Senior", 120000m, "Year", true), (first.RoleFamily, first.Seniority, first.SalaryMin!.Value, first.SalaryPeriod, first.InScope));
        var second = await db.Postings.AsNoTracking().SingleAsync(p => p.Id == "adzuna:2");
        Assert.Equal("greenhouse:1", second.DuplicateOf);
        Assert.True(second.IsRemoteCanada);
        Assert.Equal(3, await db.PostingSightings.CountAsync());
        Assert.Equal(2, await db.PostingSkills.CountAsync());
        Assert.Equal(2, await db.Runs.CountAsync());
        Assert.Contains("100000", (await db.AdzunaAggregates.SingleAsync()).Payload);
        Assert.Equal(86600m, (await db.JobBankMonths.SingleAsync()).WageMinMedian);
    }

    [Fact]
    public async Task Rebuilding_twice_gives_identical_rows()
    {
        await using var db = await pg.OpenAsync();
        var writer = new RebuildWriter(db);

        await writer.WriteAsync(Data("Azure"), CancellationToken.None);
        var once = await Snapshot(db);
        await writer.WriteAsync(Data("Azure"), CancellationToken.None);

        Assert.Equal(once, await Snapshot(db));
    }

    [Fact]
    public async Task A_failed_rebuild_keeps_the_previous_data()
    {
        await using var db = await pg.OpenAsync();
        var writer = new RebuildWriter(db);
        await writer.WriteAsync(Data("Azure"), CancellationToken.None);
        var before = await Snapshot(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => writer.WriteAsync(Data("Cobol"), CancellationToken.None));

        Assert.Equal(before, await Snapshot(db));
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/Processor.Tests --filter "Category=Integration"`
Expected: build FAILS — `RebuildWriter` and `RebuildData` missing. (Docker Desktop must be running for this task's tests.)

- [ ] **Step 5: Implement the writer**

`src/Processor/Data/RebuildWriter.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Processor.Interpreting;
using Processor.Reading;

namespace Processor.Data;

public sealed record RebuildData(
    IReadOnlyList<ArchiveRun> Runs,
    IReadOnlyList<SkillDefinition> Skills,
    IReadOnlyList<Posting> Postings,
    IReadOnlyList<AdzunaAggregate> Aggregates,
    IReadOnlyList<JobBankMonth> JobBank);

// Replaces all rows in one transaction: nobody sees a half-built database, and a failure leaves the
// previous rebuild in place. Bulk rows use Postgres binary COPY on the transaction's connection.
public sealed class RebuildWriter(TrackerDb db)
{
    private const string Truncate = "TRUNCATE posting_skills, posting_sightings, postings, skills, runs, adzuna_aggregates, jobbank_monthly";

    public async Task WriteAsync(RebuildData data, CancellationToken ct)
    {
        var skillIds = data.Skills.Select((s, i) => (s.Name, Id: i + 1)).ToDictionary(x => x.Name, x => x.Id, StringComparer.Ordinal);
        var runIds = data.Runs.Select(r => r.RunId).ToHashSet(StringComparer.Ordinal);

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await using (var truncate = new NpgsqlCommand(Truncate, conn))
                await truncate.ExecuteNonQueryAsync(ct);

            await CopyAsync(conn, "COPY runs (run_id, started_at) FROM STDIN (FORMAT BINARY)", data.Runs, async (w, r) =>
            {
                await Text(w, r.RunId, ct);
                await w.WriteAsync(r.StartedAt.ToUniversalTime(), NpgsqlDbType.TimestampTz, ct);
            }, ct);

            await CopyAsync(conn, "COPY skills (id, name, category) FROM STDIN (FORMAT BINARY)", data.Skills, async (w, s) =>
            {
                await w.WriteAsync(skillIds[s.Name], NpgsqlDbType.Integer, ct);
                await Text(w, s.Name, ct);
                await Text(w, s.Category, ct);
            }, ct);

            await CopyAsync(conn,
                "COPY postings (id, source, source_id, company, title, url, location_text, city, is_gta, is_remote_canada, in_scope, role_family, seniority, salary_min, salary_max, salary_period, salary_origin, description, is_full_text, citizenship_or_clearance, first_seen_run, last_seen_run, duplicate_of) FROM STDIN (FORMAT BINARY)",
                data.Postings, async (w, p) =>
                {
                    await Text(w, p.Id, ct);
                    await Text(w, p.Source, ct);
                    await Text(w, p.SourceId, ct);
                    await Text(w, p.Company, ct);
                    await Text(w, p.Title, ct);
                    await Text(w, p.Url, ct);
                    await Text(w, p.LocationText, ct);
                    await Text(w, p.Scope.City, ct);
                    await w.WriteAsync(p.Scope.IsGta, NpgsqlDbType.Boolean, ct);
                    await w.WriteAsync(p.Scope.IsRemoteCanada, NpgsqlDbType.Boolean, ct);
                    await w.WriteAsync(p.Scope.InScope, NpgsqlDbType.Boolean, ct);
                    await Text(w, TitleRules.FamilyKey(p.Family), ct);
                    await Text(w, p.Seniority.ToString(), ct);
                    await Numeric(w, p.Salary?.Min, ct);
                    await Numeric(w, p.Salary?.Max, ct);
                    await Text(w, p.Salary?.Period.ToString(), ct);
                    await Text(w, p.SalaryOrigin, ct);
                    await Text(w, p.Description, ct);
                    await w.WriteAsync(p.IsFullText, NpgsqlDbType.Boolean, ct);
                    await w.WriteAsync(p.CitizenshipOrClearance, NpgsqlDbType.Boolean, ct);
                    await Text(w, p.FirstSeenRun, ct);
                    await Text(w, p.LastSeenRun, ct);
                    await Text(w, p.DuplicateOf, ct);
                }, ct);

            var sightings = data.Postings.SelectMany(p => p.SightingRuns.Select(run =>
                runIds.Contains(run) ? (p.Id, run) : throw new KeyNotFoundException($"Posting {p.Id} was seen in unknown run {run}.")));
            await CopyAsync(conn, "COPY posting_sightings (posting_id, run_id) FROM STDIN (FORMAT BINARY)", sightings, async (w, s) =>
            {
                await Text(w, s.Id, ct);
                await Text(w, s.run, ct);
            }, ct);

            var postingSkills = data.Postings.SelectMany(p => p.Skills.Select(name =>
                skillIds.TryGetValue(name, out var id) ? (p.Id, id) : throw new KeyNotFoundException($"Posting {p.Id} has unknown skill '{name}'.")));
            await CopyAsync(conn, "COPY posting_skills (posting_id, skill_id) FROM STDIN (FORMAT BINARY)", postingSkills, async (w, s) =>
            {
                await Text(w, s.Id, ct);
                await w.WriteAsync(s.id, NpgsqlDbType.Integer, ct);
            }, ct);

            await CopyAsync(conn, "COPY adzuna_aggregates (run_id, term, kind, payload) FROM STDIN (FORMAT BINARY)", data.Aggregates, async (w, a) =>
            {
                await Text(w, a.RunId, ct);
                await Text(w, a.Term, ct);
                await Text(w, a.Kind, ct);
                await w.WriteAsync(a.PayloadJson, NpgsqlDbType.Jsonb, ct);
            }, ct);

            await CopyAsync(conn, "COPY jobbank_monthly (month, noc_code, noc_name, postings, vacancies, wage_min_median, wage_max_median) FROM STDIN (FORMAT BINARY)", data.JobBank, async (w, j) =>
            {
                await w.WriteAsync(j.Month, NpgsqlDbType.Date, ct);
                await Text(w, j.NocCode, ct);
                await Text(w, j.NocName, ct);
                await w.WriteAsync(j.Postings, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(j.Vacancies, NpgsqlDbType.Integer, ct);
                await Numeric(w, j.WageMinMedian, ct);
                await Numeric(w, j.WageMaxMedian, ct);
            }, ct);

            await tx.CommitAsync(ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task CopyAsync<T>(NpgsqlConnection conn, string copy, IEnumerable<T> rows, Func<NpgsqlBinaryImporter, T, Task> writeRow, CancellationToken ct)
    {
        await using var importer = await conn.BeginBinaryImportAsync(copy, ct);
        foreach (var row in rows)
        {
            await importer.StartRowAsync(ct);
            await writeRow(importer, row);
        }
        await importer.CompleteAsync(ct);
    }

    // Postgres text cannot hold NUL characters, which occasionally appear in scraped descriptions.
    private static Task Text(NpgsqlBinaryImporter w, string? value, CancellationToken ct) =>
        value is null ? w.WriteNullAsync(ct) : w.WriteAsync(value.Replace("\0", ""), NpgsqlDbType.Text, ct);

    private static Task Numeric(NpgsqlBinaryImporter w, decimal? value, CancellationToken ct) =>
        value is { } d ? w.WriteAsync(d, NpgsqlDbType.Numeric, ct) : w.WriteNullAsync(ct);
}
```

Note: the tuple element in `(p.Id, id)` is named `id` (lower case) and `(p.Id, run)` is named `run`; use those names as written. If the compiler names them differently, name them explicitly: `(Id: p.Id, SkillId: id)`.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Processor.Tests --filter "Category=Integration"`
Expected: the 3 writer tests PASS (the first run pulls `postgres:17`, which can take a minute). Then `dotnet test` — everything passes.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "Add Postgres schema and transactional COPY-based rebuild writer"
```

---

### Task 10: Skill profile and analysis

**Files:**
- Create: `src/Processor/Analysis/Profile.cs`, `src/Processor/Analysis/ReportModel.cs`, `src/Processor/Analysis/Analyzer.cs`, `config/profile.example.json`
- Test: `tests/Processor.Tests/Analysis/ProfileTests.cs`, `AnalyzerTests.cs`

**Interfaces:**
- Consumes: `SkillCatalog`, `SkillDefinition` (Task 5), `RoleFamily`, `Seniority`, `TitleRules.ParseFamily` (Task 3), `SalaryRange` (Task 1), `JobBankMonth`, `Stats` (Task 8)
- Produces:
  - `enum SkillLevel { None, Learning, Working, Strong }`; `Profile(IReadOnlySet<RoleFamily>, IReadOnlyDictionary<string, SkillLevel>)` with `TargetFamilies`, `Skills`, `LevelOf(string)`, `static Weight(SkillLevel)`, `static Load(string path, SkillCatalog)`
  - `record AnalysisPosting(string Id, string Source, string Company, string Title, string Url, string Location, RoleFamily Family, Seniority Seniority, SalaryRange? Salary, bool IsFullText, bool CitizenshipOrClearance, IReadOnlySet<string> Skills, IReadOnlyList<DateTimeOffset> Sightings)`
  - `record RunInfo(string RunId, DateTimeOffset StartedAt)`
  - `ReportModel` and its records `Snapshot`, `SkillDemand`, `SkillScore`, `SalaryCell`, `CompanyCount`, `PostingMatch`, `TrendData`, `WeekCount`, `SkillMove` (exact shapes below)
  - `Analyzer(Profile, SkillCatalog, int windowDays).Build(IReadOnlyList<AnalysisPosting> inScope, IReadOnlyList<RunInfo> runs, IReadOnlyList<JobBankMonth> jobBank, IReadOnlyList<string> warnings, DateTimeOffset generatedAt) : ReportModel`

- [ ] **Step 1: Create the example profile**

`config/profile.example.json`:
```json
// Copy to config/profile.json (gitignored) and set your own levels.
// Levels: strong | working | learning | none. Skills you leave out count as none.
// Skill names must match "name" entries in config/skills.json exactly.
{
  "targetFamilies": ["dotnet", "fullstack", "software_engineer", "lead_manager"],
  "skills": {
    "C#": "strong",
    ".NET": "strong",
    "SQL Server": "working",
    "React": "learning",
    "Docker": "none"
  }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Processor.Tests/Analysis/ProfileTests.cs`:
```csharp
using Processor.Analysis;
using Processor.Interpreting;
using Processor.Tests.Configuration;
using Processor.Tests.TestSupport;

namespace Processor.Tests.Analysis;

public class ProfileTests
{
    private static readonly SkillCatalog Catalog = new([new("C#", "language", ["C#"]), new("Azure", "cloud", ["Azure"])]);

    [Fact]
    public void Load_reads_families_and_levels()
    {
        using var tmp = new TempDir();
        var path = tmp.File("profile.json", """{"targetFamilies":["dotnet","lead_manager"],"skills":{"C#":"strong","Azure":"Learning"}}""");

        var profile = Profile.Load(path, Catalog);

        Assert.Equal(new HashSet<RoleFamily> { RoleFamily.Dotnet, RoleFamily.LeadManager }, profile.TargetFamilies);
        Assert.Equal(SkillLevel.Learning, profile.LevelOf("Azure"));
        Assert.Equal(SkillLevel.None, profile.LevelOf("Go"));
    }

    [Fact]
    public void Load_lists_every_problem()
    {
        using var tmp = new TempDir();
        var path = tmp.File("profile.json", """{"targetFamilies":["astronaut"],"skills":{"Cobol":"strong","C#":"expert"}}""");

        var message = Assert.Throws<InvalidDataException>(() => Profile.Load(path, Catalog)).Message;

        Assert.Contains("astronaut", message);
        Assert.Contains("Cobol", message);
        Assert.Contains("expert", message);
    }

    [Fact]
    public void Committed_example_profile_is_valid() =>
        Profile.Load(RepoConfig.PathOf("profile.example.json"), SkillCatalog.Load(RepoConfig.PathOf("skills.json")));

    [Theory]
    [InlineData(SkillLevel.Strong, 1.0)]
    [InlineData(SkillLevel.Working, 0.6)]
    [InlineData(SkillLevel.Learning, 0.3)]
    [InlineData(SkillLevel.None, 0.0)]
    public void Weights(SkillLevel level, double weight) => Assert.Equal(weight, Profile.Weight(level));
}
```

`tests/Processor.Tests/Analysis/AnalyzerTests.cs`:
```csharp
using Processor.Analysis;
using Processor.Interpreting;
using Processor.Reading;

namespace Processor.Tests.Analysis;

public class AnalyzerTests
{
    private static readonly SkillCatalog Catalog = new([
        new("C#", "language", ["C#"]), new("Azure", "cloud", ["Azure"]), new("Kubernetes", "devops", ["Kubernetes"]),
        new("React", "frontend", ["React"]), new("Java", "language", ["Java"])]);

    private static readonly Profile Profile = new(
        new HashSet<RoleFamily> { RoleFamily.Dotnet, RoleFamily.Fullstack },
        new Dictionary<string, SkillLevel> { ["C#"] = SkillLevel.Strong, ["React"] = SkillLevel.Working, ["Azure"] = SkillLevel.Learning });

    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 7, 0, 0, TimeSpan.FromHours(-4));

    private static DateTimeOffset Day(int d) => T0.AddDays(d);

    private static AnalysisPosting P(string id, string[] skills, int[] seenDays, RoleFamily family = RoleFamily.Dotnet, bool fullText = true,
        decimal? salary = null, Seniority seniority = Seniority.Senior, string company = "Acme", bool flagged = false, string source = "greenhouse") =>
        new(id, source, company, $"Job {id}", $"https://x.test/{id}", "Toronto", family, seniority,
            salary is { } s ? new SalaryRange(s, s, SalaryPeriod.Year) : null, fullText, flagged, skills.ToHashSet(), [.. seenDays.Select(Day)]);

    private static RunInfo[] Runs(params int[] days) => [.. days.Select(d => new RunInfo($"r{d}", Day(d)))];

    private static ReportModel Build(IReadOnlyList<AnalysisPosting> postings, RunInfo[] runs) =>
        new Analyzer(Profile, Catalog, 30).Build(postings, runs, [], ["a warning"], Day(99));

    [Fact]
    public void Demand_counts_full_text_postings_in_target_families_within_the_window()
    {
        var model = Build([
            P("1", ["C#", "Azure"], [0]),
            P("2", ["C#", "Kubernetes"], [0]),
            P("3", ["Azure"], [0], fullText: false),
            P("4", ["Java"], [0], family: RoleFamily.Other),
            P("5", ["Java"], [-40]),
        ], Runs(-40, 0));

        var csharp = model.TopSkills.Single(s => s.Skill == "C#");
        Assert.Equal((1.0, 2, SkillLevel.Strong), (csharp.FullTextShare, csharp.Count, csharp.Level));
        var azure = model.TopSkills.Single(s => s.Skill == "Azure");
        Assert.Equal((0.5, 1.0), (azure.FullTextShare, azure.SnippetShare));
        Assert.DoesNotContain(model.TopSkills, s => s.Skill == "Java");
        Assert.Equal(3, model.Snapshot.InWindow);
        Assert.Equal(new[] { "a warning" }, model.Warnings);
    }

    [Fact]
    public void Gaps_weight_demand_by_the_missing_level_and_strengths_by_the_level()
    {
        var model = Build([P("1", ["C#", "Azure", "Kubernetes"], [0]), P("2", ["C#", "Kubernetes"], [0])], Runs(0));

        Assert.Equal(new[] { "Kubernetes", "Azure" }, model.Gaps.Select(g => g.Skill));
        Assert.Equal(1.0, model.Gaps[0].Score, 3);
        Assert.Equal(0.35, model.Gaps[1].Score, 3);
        Assert.Equal(new[] { "C#", "Azure" }, model.Strengths.Select(s => s.Skill));
        Assert.Equal(0.15, model.Strengths[1].Score, 3);
    }

    [Fact]
    public void Matches_rank_open_postings_by_the_share_of_skills_you_have()
    {
        var model = Build([
            P("good", ["C#", "React", "Azure"], [0]),
            P("stretch", ["C#", "Kubernetes", "Java"], [0]),
            P("tiny", ["C#", "React"], [0]),
            P("closed", ["C#", "React", "Azure"], [-10]),
        ], Runs(-10, 0));

        Assert.Equal(new[] { "Job good", "Job stretch" }, model.Matches.Select(m => m.Title));
        Assert.Equal(2.0 / 3, model.Matches[0].Score, 3);
        Assert.Empty(model.Matches[0].Missing);
        Assert.Equal(new[] { "Java", "Kubernetes" }, model.Matches[1].Missing.Order());
    }

    [Fact]
    public void Salary_cells_need_five_postings()
    {
        var postings = Enumerable.Range(0, 5).Select(i => P($"s{i}", ["C#"], [0], salary: 100_000 + i * 10_000)).ToList();
        postings.Add(P("lead", ["C#"], [0], seniority: Seniority.Lead, salary: 150_000));

        var model = Build(postings, Runs(0));

        var senior = model.Salaries.Single(c => c.Seniority == Seniority.Senior);
        Assert.Equal(5, senior.Count);
        Assert.Equal(120_000m, senior.Median);
        Assert.Equal(110_000m, senior.P25);
        Assert.Equal(130_000m, senior.P75);
        var lead = model.Salaries.Single(c => c.Seniority == Seniority.Lead);
        Assert.Equal(1, lead.Count);
        Assert.Null(lead.Median);
    }

    [Fact]
    public void Companies_and_snapshot()
    {
        var model = Build([
            P("1", ["C#"], [-10, 0], company: "Acme"),
            P("2", ["C#"], [0], company: "ACME"),
            P("3", ["C#"], [0], company: "Globex", flagged: true, source: "adzuna", fullText: false),
        ], Runs(-10, 0));

        Assert.Equal(("Acme", 2), (model.Companies[0].Company, model.Companies[0].Postings));
        var s = model.Snapshot;
        Assert.Equal((3, 2, 1, 2, 10), (s.ActiveNow, s.NewSinceLastRun, s.CitizenshipFlagged, s.Runs, s.HistoryDays));
        Assert.Equal(2, s.BySource["greenhouse"]);
        Assert.Equal(1, s.BySource["adzuna"]);
        Assert.Equal("r0", model.LatestRunId);
    }

    [Fact]
    public void Trends_appear_after_two_weeks_of_history()
    {
        Assert.Null(Build([P("1", ["C#"], [0])], Runs(-7, 0)).Trends);

        var model = Build([P("old", ["Java", "C#"], [-45]), P("new", ["C#"], [0]), P("both", ["C#", "Azure"], [-45, 0])], Runs(-45, 0));

        Assert.NotNull(model.Trends);
        Assert.All(model.Trends.Weekly, w => Assert.Equal(DayOfWeek.Monday, w.WeekStart.DayOfWeek));
        Assert.Equal(2, model.Trends.Weekly[0].Active);
        Assert.Equal(2, model.Trends.Weekly[^1].Active);
        var java = model.Trends.Moves.Single(m => m.Skill == "Java");
        Assert.Equal((0.5, 0.0), (java.Previous, java.Current));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Processor.Tests`
Expected: build FAILS — `Processor.Analysis` types missing.

- [ ] **Step 4: Implement**

`src/Processor/Analysis/Profile.cs`:
```csharp
using System.Text.Json;
using Processor.Interpreting;

namespace Processor.Analysis;

public enum SkillLevel { None, Learning, Working, Strong }

// config/profile.json: the owner's target roles and self-assessed skill levels.
public sealed class Profile(IReadOnlySet<RoleFamily> targetFamilies, IReadOnlyDictionary<string, SkillLevel> skills)
{
    public IReadOnlySet<RoleFamily> TargetFamilies { get; } = targetFamilies;
    public IReadOnlyDictionary<string, SkillLevel> Skills { get; } = skills;

    public SkillLevel LevelOf(string skill) => Skills.TryGetValue(skill, out var level) ? level : SkillLevel.None;

    public static double Weight(SkillLevel level) => level switch
    {
        SkillLevel.Strong => 1.0,
        SkillLevel.Working => 0.6,
        SkillLevel.Learning => 0.3,
        _ => 0.0,
    };

    public static Profile Load(string path, SkillCatalog catalog)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var problems = new List<string>();

        var families = new HashSet<RoleFamily>();
        if (root.TryGetProperty("targetFamilies", out var familyList) && familyList.ValueKind == JsonValueKind.Array)
            foreach (var f in familyList.EnumerateArray())
            {
                if (TitleRules.ParseFamily(f.ValueKind == JsonValueKind.String ? f.GetString()! : "") is { } family) families.Add(family);
                else problems.Add($"unknown target family '{f}' (use dotnet, fullstack, software_engineer, lead_manager or other)");
            }
        if (families.Count == 0 && problems.Count == 0) problems.Add("targetFamilies must list at least one role family");

        var known = catalog.Skills.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var levels = new Dictionary<string, SkillLevel>(StringComparer.Ordinal);
        if (root.TryGetProperty("skills", out var skillMap) && skillMap.ValueKind == JsonValueKind.Object)
            foreach (var entry in skillMap.EnumerateObject())
            {
                if (!known.Contains(entry.Name))
                {
                    problems.Add($"skill '{entry.Name}' is not in config/skills.json");
                    continue;
                }
                var text = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null;
                if (!Enum.TryParse<SkillLevel>(text, ignoreCase: true, out var level) || !Enum.IsDefined(level) || int.TryParse(text, out _))
                {
                    problems.Add($"skill '{entry.Name}' has level '{entry.Value}' (use strong, working, learning or none)");
                    continue;
                }
                levels[entry.Name] = level;
            }

        if (problems.Count > 0) throw new InvalidDataException($"{path}: {string.Join("; ", problems)}.");
        return new Profile(families, levels);
    }
}
```

`src/Processor/Analysis/ReportModel.cs`:
```csharp
using Processor.Interpreting;
using Processor.Reading;

namespace Processor.Analysis;

public sealed record ReportModel(
    DateTimeOffset GeneratedAt,
    string LatestRunId,
    DateTimeOffset LatestRunAt,
    int WindowDays,
    Snapshot Snapshot,
    IReadOnlyList<SkillDemand> TopSkills,
    IReadOnlyList<SkillScore> Gaps,
    IReadOnlyList<SkillScore> Strengths,
    IReadOnlyList<SalaryCell> Salaries,
    IReadOnlyList<CompanyCount> Companies,
    IReadOnlyList<PostingMatch> Matches,
    TrendData? Trends,
    IReadOnlyList<JobBankMonth> JobBank,
    IReadOnlyList<string> Warnings);

public sealed record Snapshot(int ActiveNow, int NewSinceLastRun, int InWindow, int FullText, int Snippets, int WithSalary,
    int CitizenshipFlagged, int Runs, int HistoryDays, IReadOnlyDictionary<string, int> BySource);

public sealed record SkillDemand(string Skill, string Category, double FullTextShare, double SnippetShare, int Count, SkillLevel Level);

public sealed record SkillScore(string Skill, string Category, double Demand, SkillLevel Level, double Score);

public sealed record SalaryCell(RoleFamily Family, Seniority Seniority, int Count, decimal? Median, decimal? P25, decimal? P75);

public sealed record CompanyCount(string Company, int Postings);

public sealed record PostingMatch(string Title, string Company, string Url, string Location, double Score, int SkillCount,
    IReadOnlyList<string> Missing, SalaryRange? Salary, bool CitizenshipOrClearance);

public sealed record TrendData(IReadOnlyList<WeekCount> Weekly, IReadOnlyList<SkillMove> Moves);

public sealed record WeekCount(DateOnly WeekStart, int Active);

public sealed record SkillMove(string Skill, double Previous, double Current);
```

`src/Processor/Analysis/Analyzer.cs`:
```csharp
using Processor.Interpreting;
using Processor.Reading;

namespace Processor.Analysis;

public sealed record AnalysisPosting(string Id, string Source, string Company, string Title, string Url, string Location,
    RoleFamily Family, Seniority Seniority, SalaryRange? Salary, bool IsFullText, bool CitizenshipOrClearance,
    IReadOnlySet<string> Skills, IReadOnlyList<DateTimeOffset> Sightings);

public sealed record RunInfo(string RunId, DateTimeOffset StartedAt);

public sealed class Analyzer(Profile profile, SkillCatalog catalog, int windowDays)
{
    public const int TopSkillCount = 25, TopGapCount = 15, TopStrengthCount = 10, TopMatchCount = 20, TopCompanyCount = 20;
    public const int MinSalarySample = 5, MinTrendDays = 14, MinMatchSkills = 3;

    // inScope: in-scope, non-duplicate postings (all families); runs: at least one.
    public ReportModel Build(IReadOnlyList<AnalysisPosting> inScope, IReadOnlyList<RunInfo> runs, IReadOnlyList<JobBankMonth> jobBank,
        IReadOnlyList<string> warnings, DateTimeOffset generatedAt)
    {
        var ordered = runs.OrderBy(r => r.StartedAt).ToList();
        var latest = ordered[^1];
        var windowStart = latest.StartedAt.AddDays(-windowDays);

        var targetAll = inScope.Where(p => profile.TargetFamilies.Contains(p.Family)).ToList();
        var window = targetAll.Where(p => p.Sightings.Any(s => s >= windowStart)).ToList();
        var full = window.Where(p => p.IsFullText).ToList();
        var snippets = window.Where(p => !p.IsFullText).ToList();
        var active = window.Where(p => p.Sightings.Contains(latest.StartedAt)).ToList();

        var demand = catalog.Skills
            .Select(s => new SkillDemand(s.Name, s.Category, Share(full, s.Name), Share(snippets, s.Name),
                full.Count(p => p.Skills.Contains(s.Name)), profile.LevelOf(s.Name)))
            .ToList();

        return new ReportModel(
            generatedAt, latest.RunId, latest.StartedAt, windowDays,
            BuildSnapshot(window, full, snippets, active, ordered),
            [.. demand.Where(d => d.Count > 0).OrderByDescending(d => d.FullTextShare).ThenBy(d => d.Skill, StringComparer.Ordinal).Take(TopSkillCount)],
            Rank(demand, d => 1 - Profile.Weight(d.Level), TopGapCount),
            Rank(demand, d => Profile.Weight(d.Level), TopStrengthCount),
            SalaryCells(window),
            TopCompanies(window),
            BestMatches(active, demand.ToDictionary(d => d.Skill, d => d.FullTextShare, StringComparer.Ordinal)),
            BuildTrends(targetAll, ordered),
            [.. jobBank.OrderByDescending(j => j.Month).ThenBy(j => j.NocCode, StringComparer.Ordinal)],
            warnings);
    }

    private static double Share(IReadOnlyList<AnalysisPosting> postings, string skill) =>
        postings.Count == 0 ? 0 : (double)postings.Count(p => p.Skills.Contains(skill)) / postings.Count;

    private static IReadOnlyList<SkillScore> Rank(IEnumerable<SkillDemand> demand, Func<SkillDemand, double> factor, int take) =>
        [.. demand.Select(d => new SkillScore(d.Skill, d.Category, d.FullTextShare, d.Level, d.FullTextShare * factor(d)))
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score).ThenBy(s => s.Skill, StringComparer.Ordinal)
            .Take(take)];

    private static Snapshot BuildSnapshot(IReadOnlyList<AnalysisPosting> window, IReadOnlyList<AnalysisPosting> full,
        IReadOnlyList<AnalysisPosting> snippets, IReadOnlyList<AnalysisPosting> active, IReadOnlyList<RunInfo> runs)
    {
        var latest = runs[^1].StartedAt;
        return new Snapshot(
            ActiveNow: active.Count,
            NewSinceLastRun: runs.Count > 1 ? active.Count(p => p.Sightings.Min() == latest) : 0,
            InWindow: window.Count,
            FullText: full.Count,
            Snippets: snippets.Count,
            WithSalary: window.Count(p => p.Salary is not null),
            CitizenshipFlagged: window.Count(p => p.CitizenshipOrClearance),
            Runs: runs.Count,
            HistoryDays: (int)Math.Round((latest - runs[0].StartedAt).TotalDays),
            BySource: window.GroupBy(p => p.Source).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()));
    }

    private static IReadOnlyList<SalaryCell> SalaryCells(IReadOnlyList<AnalysisPosting> window) =>
        [.. window.Where(p => p.Salary is not null)
            .GroupBy(p => (p.Family, p.Seniority))
            .Select(g =>
            {
                var mids = g.Select(p => p.Salary!.YearlyMidpoint).Order().ToList();
                var enough = mids.Count >= MinSalarySample;
                return new SalaryCell(g.Key.Family, g.Key.Seniority, mids.Count,
                    enough ? Stats.Percentile(mids, 0.5) : null,
                    enough ? Stats.Percentile(mids, 0.25) : null,
                    enough ? Stats.Percentile(mids, 0.75) : null);
            })
            .OrderBy(c => c.Family).ThenBy(c => c.Seniority)];

    private static IReadOnlyList<CompanyCount> TopCompanies(IReadOnlyList<AnalysisPosting> window) =>
        [.. window.GroupBy(p => p.Company.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new CompanyCount(g.First().Company.Trim(), g.Count()))
            .OrderByDescending(c => c.Postings).ThenBy(c => c.Company, StringComparer.Ordinal)
            .Take(TopCompanyCount)];

    private IReadOnlyList<PostingMatch> BestMatches(IReadOnlyList<AnalysisPosting> active, IReadOnlyDictionary<string, double> demand) =>
        [.. active.Where(p => p.Skills.Count >= MinMatchSkills)
            .Select(p =>
            {
                var covered = p.Skills.Count(s => profile.LevelOf(s) >= SkillLevel.Working);
                var missing = p.Skills.Where(s => profile.LevelOf(s) == SkillLevel.None)
                    .OrderByDescending(s => demand.GetValueOrDefault(s)).ThenBy(s => s, StringComparer.Ordinal)
                    .Take(3).ToList();
                return new PostingMatch(p.Title, p.Company, p.Url, p.Location, (double)covered / p.Skills.Count, p.Skills.Count,
                    missing, p.Salary, p.CitizenshipOrClearance);
            })
            .OrderByDescending(m => m.Score).ThenByDescending(m => m.SkillCount).ThenBy(m => m.Title, StringComparer.Ordinal)
            .Take(TopMatchCount)];

    private TrendData? BuildTrends(IReadOnlyList<AnalysisPosting> targetAll, IReadOnlyList<RunInfo> runs)
    {
        var first = runs[0].StartedAt;
        var latest = runs[^1].StartedAt;
        if ((latest - first).TotalDays < MinTrendDays) return null;

        var weeks = new List<WeekCount>();
        for (var week = WeekStart(first); week <= WeekStart(latest); week = week.AddDays(7))
        {
            var (start, end) = (week, week.AddDays(7));
            weeks.Add(new WeekCount(start, targetAll.Count(p => p.Sightings.Any(s =>
            {
                var day = DateOnly.FromDateTime(s.Date);
                return day >= start && day < end;
            }))));
        }

        var currentStart = latest.AddDays(-windowDays);
        var previousStart = currentStart.AddDays(-windowDays);
        var current = targetAll.Where(p => p.IsFullText && p.Sightings.Any(s => s >= currentStart)).ToList();
        var previous = targetAll.Where(p => p.IsFullText && p.Sightings.Any(s => s >= previousStart && s < currentStart)).ToList();
        IReadOnlyList<SkillMove> moves = previous.Count == 0
            ? []
            : [.. catalog.Skills.Select(s => new SkillMove(s.Name, Share(previous, s.Name), Share(current, s.Name)))
                .Where(m => m.Previous > 0 || m.Current > 0)
                .OrderByDescending(m => Math.Abs(m.Current - m.Previous)).ThenBy(m => m.Skill, StringComparer.Ordinal)
                .Take(10)];

        return new TrendData(weeks, moves);
    }

    private static DateOnly WeekStart(DateTimeOffset time)
    {
        var day = DateOnly.FromDateTime(time.Date);
        return day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Processor.Tests`
Expected: profile and analyzer tests PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add skill profile and demand, gap, match, salary and trend analysis"
```

---

### Task 11: Report renderer and writer

**Files:**
- Create: `src/Processor/Reporting/ReportRenderer.cs`, `src/Processor/Reporting/ReportWriter.cs`
- Test: `tests/Processor.Tests/Reporting/ReportRendererTests.cs`, `ReportWriterTests.cs`

**Interfaces:**
- Consumes: `ReportModel` and its records, `SkillLevel` (Task 10), `SalaryRange` (Task 1), `RoleFamily`, `Seniority` (Task 3), `JobBankMonth` (Task 8)
- Produces:
  - `ReportRenderer.Title` (`"GTA Job Market"`), `ReportRenderer.RenderFragment(ReportModel) : string` (publishable fragment), `ReportRenderer.RenderDocument(string fragment) : string`
  - `ReportWriter.WriteAsync(string dataRoot, ReportModel, CancellationToken) : Task<string>` → path of `latest.html`; also writes `<run>.html` and `latest.fragment.html` atomically

Design plan (from the artifact-design pass):
- **Colour:**
  - Cool slate neutrals biased toward steel blue (`#F2F4F7` background, `#17212B` ink, `#586676` muted).
  - One deep maple-red accent (`#A8132B`) for the masthead rule and the "Gap" chip.
  - Steel blue (`#2B5B8C`) for data bars.
  - Semantic green, blue and amber for skill levels.
  - The dark theme re-tunes each token rather than inverting it.
- **Type:**
  - Archivo at 87.5% width for headings and big figures, a condensed signage feel suited to a Toronto street-level brief.
  - Source Sans 3 for body text.
  - IBM Plex Mono with tabular figures for labels, percentages and money.
- **Layout:**
  - A single 980px column.
  - A masthead with a thick red rule.
  - A four-figure snapshot row.
  - Skills as labelled horizontal bars on one scale.
  - Gaps and strengths side by side (stacked on phones).
  - Tables inside their own horizontal-scroll containers.
  - Matches as rows with a match-% pill.
  - A methodology footer.
- **Subject detail:** CAD `$…k` figures, the 2,080-hour convention, Ontario's January 2026 pay-transparency rule, NOC 2021 codes and "Toronto economic region".

- [ ] **Step 1: Write the failing tests**

`tests/Processor.Tests/Reporting/ReportRendererTests.cs`:
```csharp
using Processor.Analysis;
using Processor.Interpreting;
using Processor.Reading;
using Processor.Reporting;

namespace Processor.Tests.Reporting;

public class ReportRendererTests
{
    internal static ReportModel Model(TrendData? trends = null, IReadOnlyList<string>? warnings = null) => new(
        GeneratedAt: new DateTimeOffset(2026, 10, 2, 7, 5, 0, TimeSpan.FromHours(-4)),
        LatestRunId: "2026-10-02_0700",
        LatestRunAt: new DateTimeOffset(2026, 10, 2, 7, 0, 0, TimeSpan.FromHours(-4)),
        WindowDays: 30,
        Snapshot: new Snapshot(ActiveNow: 812, NewSinceLastRun: 64, InWindow: 1204, FullText: 410, Snippets: 794, WithSalary: 602,
            CitizenshipFlagged: 9, Runs: 3, HistoryDays: 14, BySource: new Dictionary<string, int> { ["adzuna"] = 794, ["greenhouse"] = 410 }),
        TopSkills: [new SkillDemand("C#", "language", 0.62, 0.4, 254, SkillLevel.Strong), new SkillDemand("Kubernetes", "devops", 0.31, 0.1, 127, SkillLevel.None)],
        Gaps: [new SkillScore("Kubernetes", "devops", 0.31, SkillLevel.None, 0.31)],
        Strengths: [new SkillScore("C#", "language", 0.62, SkillLevel.Strong, 0.62)],
        Salaries: [new SalaryCell(RoleFamily.Dotnet, Seniority.Senior, 12, 128_000, 115_000, 140_000), new SalaryCell(RoleFamily.LeadManager, Seniority.Manager, 2, null, null, null)],
        Companies: [new CompanyCount("<script>alert(1)</script> Corp", 7)],
        Matches: [
            new PostingMatch("Senior .NET Developer", "Acme", "https://jobs.example/1", "Toronto", 0.8, 5, ["Kubernetes"], new SalaryRange(120_000, 150_000, SalaryPeriod.Year), true),
            new PostingMatch("Odd link", "Globex", "javascript:alert(1)", "Remote (Canada)", 0.5, 4, [], null, false)],
        Trends: trends,
        JobBank: [new JobBankMonth(new DateOnly(2026, 8, 1), "21232", "Software developers and programmers", 115, 140, 83_200, 104_000)],
        Warnings: warnings ?? []);

    [Fact]
    public void Fragment_follows_the_page_contract()
    {
        var html = ReportRenderer.RenderFragment(Model());

        Assert.StartsWith("<title>GTA Job Market</title>", html);
        foreach (var banned in new[] { "<!doctype", "<html", "<head", "<body", "<script" })
            Assert.DoesNotContain(banned, html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(":root:not([data-theme=\"light\"])", html);
        Assert.Contains(":root[data-theme=\"dark\"]", html);
        Assert.Contains("color-scheme:dark", html);
        Assert.Contains("background:var(--bg)", html);
    }

    [Fact]
    public void Dynamic_text_is_escaped_and_unsafe_links_are_dropped()
    {
        var html = ReportRenderer.RenderFragment(Model());

        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; Corp", html);
        Assert.DoesNotContain("href=\"javascript:", html);
        Assert.Contains("href=\"https://jobs.example/1\"", html);
        Assert.Contains("rel=\"noopener noreferrer\"", html);
    }

    [Theory]
    [InlineData("812")]
    [InlineData("62%")]
    [InlineData("$128k")]
    [InlineData("$115k–$140k")]
    [InlineData("not enough data")]
    [InlineData("80% match")]
    [InlineData("Missing: Kubernetes")]
    [InlineData("$120k–$150k")]
    [InlineData("Software developers and programmers")]
    [InlineData("Jobs data partly by")]
    [InlineData("Your biggest gap")]
    public void Sections_show_the_numbers(string expected) =>
        Assert.Contains(expected, ReportRenderer.RenderFragment(Model()));

    [Fact]
    public void Trends_section_only_with_trend_data()
    {
        Assert.DoesNotContain("id=\"trends\"", ReportRenderer.RenderFragment(Model()));

        var html = ReportRenderer.RenderFragment(Model(new TrendData(
            [new WeekCount(new DateOnly(2026, 9, 14), 700), new WeekCount(new DateOnly(2026, 9, 21), 760), new WeekCount(new DateOnly(2026, 9, 28), 812)],
            [new SkillMove("Azure", 0.2, 0.3)])));

        Assert.Contains("id=\"trends\"", html);
        Assert.Contains("<svg", html);
        Assert.Contains("Azure", html);
    }

    [Fact]
    public void Warnings_are_listed() =>
        Assert.Contains("Inbox file a.txt has no Company line.", ReportRenderer.RenderFragment(Model(warnings: ["Inbox file a.txt has no Company line."])));

    [Fact]
    public void Document_wraps_the_fragment()
    {
        var doc = ReportRenderer.RenderDocument("<p>x</p>");
        Assert.StartsWith("<!doctype html>", doc);
        Assert.Contains("<meta charset=\"utf-8\">", doc);
        Assert.Contains("<p>x</p>", doc);
    }
}
```

`tests/Processor.Tests/Reporting/ReportWriterTests.cs`:
```csharp
using Processor.Reporting;
using Processor.Tests.TestSupport;

namespace Processor.Tests.Reporting;

public class ReportWriterTests
{
    [Fact]
    public async Task Writes_run_latest_and_fragment_files_without_leftovers()
    {
        using var tmp = new TempDir();

        var latest = await ReportWriter.WriteAsync(tmp.Path, ReportRendererTests.Model(), CancellationToken.None);

        var dir = Path.Combine(tmp.Path, "reports");
        Assert.Equal(Path.Combine(dir, "latest.html"), latest);
        Assert.Equal(File.ReadAllText(latest), File.ReadAllText(Path.Combine(dir, "2026-10-02_0700.html")));
        Assert.StartsWith("<title>", File.ReadAllText(Path.Combine(dir, "latest.fragment.html")));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Processor.Tests`
Expected: build FAILS — `Processor.Reporting` types missing.

- [ ] **Step 3: Implement the renderer**

`src/Processor/Reporting/ReportRenderer.cs`:
```csharp
using System.Globalization;
using System.Net;
using System.Text;
using Processor.Analysis;
using Processor.Interpreting;
using Processor.Reading;

namespace Processor.Reporting;

public static class ReportRenderer
{
    public const string Title = "GTA Job Market";

    private static readonly CultureInfo Canada = CultureInfo.GetCultureInfo("en-CA");
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const string FontLink =
        "<link rel=\"stylesheet\" href=\"https://fonts.googleapis.com/css2?family=Archivo:wdth,wght@87.5,700;87.5,800&amp;family=IBM+Plex+Mono:wght@400;500&amp;family=Source+Sans+3:wght@400;600&amp;display=swap\">";

    // A claude.ai page fragment: the viewer supplies doctype/html/head/body, so the title and styles come first.
    public static string RenderFragment(ReportModel m)
    {
        var h = new StringBuilder();
        h.Append("<title>").Append(Title).Append("</title>\n").Append(FontLink).Append('\n');
        h.Append("<style>").Append(Css).Append("</style>\n");
        h.Append("<main class=\"report\">\n");
        Masthead(h, m);
        SnapshotRow(h, m.Snapshot);
        SkillBars(h, m);
        GapsAndStrengths(h, m);
        SalaryTable(h, m);
        MatchList(h, m);
        CompanyList(h, m);
        if (m.Trends is not null) TrendSection(h, m);
        JobBankTable(h, m);
        Method(h, m);
        h.Append("</main>\n");
        return h.ToString();
    }

    // The same page as a standalone file for opening on this PC.
    public static string RenderDocument(string fragment) =>
        "<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1, viewport-fit=cover\">\n</head>\n<body>\n"
        + fragment + "</body>\n</html>\n";

    private static void Masthead(StringBuilder h, ReportModel m)
    {
        h.Append("<header class=\"masthead\"><p class=\"eyebrow\">Toronto &amp; GTA · remote Canada · updated ")
            .Append(E(m.LatestRunAt.ToString("MMMM d, yyyy", Canada))).Append("</p>")
            .Append("<h1>").Append(Title).Append("</h1>")
            .Append("<p class=\"lede\">").Append(Num(m.Snapshot.InWindow)).Append(" postings in your target roles over the last ")
            .Append(m.WindowDays).Append(" days, ").Append(Num(m.Snapshot.ActiveNow)).Append(" open right now.");
        if (m.Gaps.Count > 0) h.Append(" Your biggest gap: <strong>").Append(E(m.Gaps[0].Skill)).Append("</strong>.");
        h.Append("</p></header>\n");
    }

    private static void SnapshotRow(StringBuilder h, Snapshot s)
    {
        h.Append("<section class=\"stats\" aria-label=\"Snapshot\">");
        Stat(h, "Open now", Num(s.ActiveNow), "seen in the latest run");
        Stat(h, "New since last run", Num(s.NewSinceLastRun), s.Runs > 1 ? "first seen this run" : "needs a second run");
        Stat(h, "State a salary", Pct(s.InWindow == 0 ? 0 : (double)s.WithSalary / s.InWindow), $"{Num(s.WithSalary)} of {Num(s.InWindow)} postings");
        Stat(h, "History", $"{s.HistoryDays} days", $"{s.Runs} runs collected");
        h.Append("</section>\n");
    }

    private static void Stat(StringBuilder h, string label, string value, string note) =>
        h.Append("<div class=\"stat\"><p class=\"stat-label\">").Append(E(label)).Append("</p><p class=\"stat-value\">")
            .Append(E(value)).Append("</p><p class=\"stat-note\">").Append(E(note)).Append("</p></div>");

    private static void SkillBars(StringBuilder h, ReportModel m)
    {
        Open(h, "skills", "Skills in demand",
            $"Share of the {Num(m.Snapshot.FullText)} full-text postings in your target roles over the last {m.WindowDays} days that mention each skill. Adzuna snippet postings ({Num(m.Snapshot.Snippets)}) are too short to count here.");
        if (m.TopSkills.Count == 0)
        {
            Empty(h, "No full-text postings in the window yet.");
        }
        else
        {
            var max = m.TopSkills.Max(s => s.FullTextShare);
            h.Append("<ol class=\"bars\">");
            foreach (var s in m.TopSkills)
            {
                var width = max <= 0 ? 0 : s.FullTextShare / max * 100;
                h.Append("<li class=\"bar-row\"><span class=\"bar-name\">").Append(E(s.Skill)).Append("</span>")
                    .Append("<span class=\"bar-track\" aria-hidden=\"true\"><span class=\"bar-fill\" style=\"width:").Append(width.ToString("0.#", Inv)).Append("%\"></span></span>")
                    .Append("<span class=\"bar-value\">").Append(E(Pct(s.FullTextShare))).Append("</span>")
                    .Append(Chip(s.Level)).Append("</li>");
            }
            h.Append("</ol>");
        }
        Close(h);
    }

    private static void GapsAndStrengths(StringBuilder h, ReportModel m)
    {
        h.Append("<section id=\"gaps\" class=\"pair\" aria-label=\"Gaps and strengths\">");
        Ranked(h, "Your gaps", "Demand × how far you are from the skill. Close these first.", m.Gaps);
        Ranked(h, "Strengths employers want", "Demand × your level. Lead your CV with these.", m.Strengths);
        h.Append("</section>\n");
    }

    private static void Ranked(StringBuilder h, string title, string note, IReadOnlyList<SkillScore> items)
    {
        h.Append("<div class=\"col\"><h2>").Append(E(title)).Append("</h2><p class=\"note\">").Append(E(note)).Append("</p>");
        if (items.Count == 0)
        {
            Empty(h, "Nothing to rank yet.");
        }
        else
        {
            h.Append("<ol class=\"ranked\">");
            foreach (var s in items)
                h.Append("<li><span class=\"rank-line\"><span class=\"rank-name\">").Append(E(s.Skill)).Append("</span>")
                    .Append("<span class=\"rank-meta\">in ").Append(E(Pct(s.Demand))).Append(" of postings</span>")
                    .Append(Chip(s.Level)).Append("</span></li>");
            h.Append("</ol>");
        }
        h.Append("</div>");
    }

    private static void SalaryTable(StringBuilder h, ReportModel m)
    {
        Open(h, "salary", "Salary (CAD, yearly)",
            "Midpoint of each posted range, by role and seniority. Hourly rates count as 2,080 hours a year. Ontario has required pay ranges in public job ads since January 1, 2026.");
        if (m.Salaries.Count == 0)
        {
            Empty(h, "No postings with a salary in the window yet.");
        }
        else
        {
            h.Append("<div class=\"scroll\"><table><thead><tr><th scope=\"col\">Role</th><th scope=\"col\">Seniority</th>")
                .Append("<th scope=\"col\" class=\"num\">Postings</th><th scope=\"col\" class=\"num\">Median</th><th scope=\"col\" class=\"num\">Middle 50%</th></tr></thead><tbody>");
            foreach (var c in m.Salaries)
            {
                h.Append("<tr><td>").Append(E(FamilyLabel(c.Family))).Append("</td><td>").Append(E(SeniorityLabel(c.Seniority)))
                    .Append("</td><td class=\"num\">").Append(Num(c.Count)).Append("</td>");
                if (c.Median is { } median && c.P25 is { } p25 && c.P75 is { } p75)
                    h.Append("<td class=\"num\">").Append(Money(median)).Append("</td><td class=\"num\">").Append(Money(p25)).Append('–').Append(Money(p75)).Append("</td>");
                else
                    h.Append("<td class=\"num muted\" colspan=\"2\">not enough data</td>");
                h.Append("</tr>");
            }
            h.Append("</tbody></table></div>");
        }
        Close(h);
    }

    private static void MatchList(StringBuilder h, ReportModel m)
    {
        Open(h, "matches", "Best-matching open postings",
            "Open in the latest run, ranked by the share of their skills you already have at working level or better.");
        if (m.Matches.Count == 0)
        {
            Empty(h, "No open postings with enough recognised skills yet.");
        }
        else
        {
            h.Append("<ol class=\"matches\">");
            foreach (var p in m.Matches)
            {
                h.Append("<li class=\"match\"><div class=\"match-main\">");
                if (SafeUrl(p.Url) is { } url)
                    h.Append("<a class=\"match-title\" href=\"").Append(E(url)).Append("\" target=\"_blank\" rel=\"noopener noreferrer\">").Append(E(p.Title)).Append("</a>");
                else
                    h.Append("<span class=\"match-title\">").Append(E(p.Title)).Append("</span>");
                h.Append("<p class=\"match-meta\">").Append(E(p.Company)).Append(" · ").Append(E(p.Location));
                if (p.Salary is { } salary) h.Append(" · ").Append(E(SalaryText(salary)));
                h.Append("</p>");
                if (p.Missing.Count > 0) h.Append("<p class=\"match-missing\">Missing: ").Append(E(string.Join(", ", p.Missing))).Append("</p>");
                h.Append("</div><div class=\"match-side\"><span class=\"pill\">").Append(E(Pct(p.Score))).Append(" match</span>");
                if (p.CitizenshipOrClearance) h.Append("<span class=\"flag\">Citizenship or clearance</span>");
                h.Append("</div></li>");
            }
            h.Append("</ol>");
        }
        Close(h);
    }

    private static void CompanyList(StringBuilder h, ReportModel m)
    {
        Open(h, "companies", "Who's hiring", $"Distinct postings in your target roles over the last {m.WindowDays} days.");
        if (m.Companies.Count == 0)
        {
            Empty(h, "No companies yet.");
        }
        else
        {
            h.Append("<ol class=\"companies\">");
            foreach (var c in m.Companies)
                h.Append("<li><span>").Append(E(c.Company)).Append("</span><span class=\"num\">").Append(Num(c.Postings)).Append("</span></li>");
            h.Append("</ol>");
        }
        Close(h);
    }

    private static void TrendSection(StringBuilder h, ReportModel m)
    {
        var t = m.Trends!;
        Open(h, "trends", "Trends",
            $"Postings in your target roles seen each week, and the skills whose share moved most between the previous {m.WindowDays} days and the latest {m.WindowDays}.");
        WeeklyChart(h, t.Weekly);
        if (t.Moves.Count > 0)
        {
            h.Append("<div class=\"scroll\"><table><thead><tr><th scope=\"col\">Skill</th><th scope=\"col\" class=\"num\">Previous</th>")
                .Append("<th scope=\"col\" class=\"num\">Latest</th><th scope=\"col\" class=\"num\">Change</th></tr></thead><tbody>");
            foreach (var move in t.Moves)
            {
                var points = (move.Current - move.Previous) * 100;
                var css = points > 0 ? "up" : points < 0 ? "down" : "muted";
                h.Append("<tr><td>").Append(E(move.Skill)).Append("</td><td class=\"num\">").Append(E(Pct(move.Previous)))
                    .Append("</td><td class=\"num\">").Append(E(Pct(move.Current))).Append("</td><td class=\"num ").Append(css).Append("\">")
                    .Append(points > 0 ? "+" : "").Append(points.ToString("0", Inv)).Append(" pts</td></tr>");
            }
            h.Append("</tbody></table></div>");
        }
        Close(h);
    }

    private static void WeeklyChart(StringBuilder h, IReadOnlyList<WeekCount> weeks)
    {
        const double W = 640, H = 180, L = 48, R = 20, T = 16, B = 34;
        var max = Math.Max(1, weeks.Max(w => w.Active));
        double X(int i) => weeks.Count == 1 ? L + (W - L - R) / 2 : L + i * (W - L - R) / (weeks.Count - 1);
        double Y(int v) => T + (H - T - B) * (1 - (double)v / max);
        var line = string.Join(' ', weeks.Select((w, i) => $"{F(X(i))},{F(Y(w.Active))}"));
        var area = $"{F(X(0))},{F(Y(0))} {line} {F(X(weeks.Count - 1))},{F(Y(0))}";

        h.Append("<figure class=\"chart\"><svg viewBox=\"0 0 640 180\" role=\"img\" aria-label=\"Open postings per week, from ")
            .Append(weeks[0].Active).Append(" to ").Append(weeks[^1].Active).Append("\">")
            .Append($"<line class=\"grid\" x1=\"{F(L)}\" x2=\"{F(W - R)}\" y1=\"{F(Y(0))}\" y2=\"{F(Y(0))}\"/>")
            .Append($"<line class=\"grid\" x1=\"{F(L)}\" x2=\"{F(W - R)}\" y1=\"{F(Y(max))}\" y2=\"{F(Y(max))}\"/>")
            .Append($"<text class=\"axis\" x=\"{F(L - 8)}\" y=\"{F(Y(0) + 4)}\" text-anchor=\"end\">0</text>")
            .Append($"<text class=\"axis\" x=\"{F(L - 8)}\" y=\"{F(Y(max) + 4)}\" text-anchor=\"end\">{max}</text>")
            .Append($"<polygon class=\"area\" points=\"{area}\"/>")
            .Append($"<polyline class=\"line\" fill=\"none\" points=\"{line}\"/>")
            .Append($"<circle class=\"dot\" r=\"4.5\" cx=\"{F(X(weeks.Count - 1))}\" cy=\"{F(Y(weeks[^1].Active))}\"/>")
            .Append($"<text class=\"axis\" x=\"{F(L)}\" y=\"{F(H - 10)}\">{E(WeekLabel(weeks[0].WeekStart))}</text>")
            .Append($"<text class=\"axis\" x=\"{F(W - R)}\" y=\"{F(H - 10)}\" text-anchor=\"end\">{E(WeekLabel(weeks[^1].WeekStart))}</text>")
            .Append("</svg><figcaption>Open postings per week in your target roles</figcaption></figure>");
    }

    private static void JobBankTable(StringBuilder h, ReportModel m)
    {
        Open(h, "jobbank", "Job Bank context",
            "Government of Canada Job Bank postings for tech occupations (NOC 2021) in the Toronto economic region. Job Bank carries few tech roles, so read this as background.");
        if (m.JobBank.Count == 0)
        {
            Empty(h, "No Job Bank data yet.");
        }
        else
        {
            var month = m.JobBank.Max(j => j.Month);
            h.Append("<p class=\"note\">").Append(E(month.ToString("MMMM yyyy", Canada))).Append("</p>")
                .Append("<div class=\"scroll\"><table><thead><tr><th scope=\"col\">Occupation</th><th scope=\"col\" class=\"num\">Postings</th>")
                .Append("<th scope=\"col\" class=\"num\">Vacancies</th><th scope=\"col\" class=\"num\">Median wage range</th></tr></thead><tbody>");
            foreach (var j in m.JobBank.Where(j => j.Month == month).OrderByDescending(j => j.Postings))
            {
                h.Append("<tr><td>").Append(E(j.NocName)).Append(" <span class=\"muted\">").Append(E(j.NocCode)).Append("</span></td><td class=\"num\">")
                    .Append(Num(j.Postings)).Append("</td><td class=\"num\">").Append(Num(j.Vacancies)).Append("</td><td class=\"num\">")
                    .Append(j.WageMinMedian is { } lo && j.WageMaxMedian is { } hi ? $"{Money(lo)}–{Money(hi)}" : "—").Append("</td></tr>");
            }
            h.Append("</tbody></table></div>");
        }
        Close(h);
    }

    private static void Method(StringBuilder h, ReportModel m)
    {
        var sources = string.Join(" · ", m.Snapshot.BySource.Select(kv => $"{kv.Key} {Num(kv.Value)}"));
        h.Append("<footer class=\"method\"><h2>How this is made</h2><ul>")
            .Append("<li>Sources: the Adzuna job search API, public Greenhouse, Lever and Ashby job boards of Toronto tech employers, Job Bank open data, and postings saved to the inbox.</li>")
            .Append("<li>Counted: postings in GTA municipalities or remote roles open to Canada, in your target roles. The same job found on several sites counts once. By source: ")
            .Append(E(sources)).Append(".</li>")
            .Append("<li>Skills come from a curated list of names and aliases matched in the posting text. Salary is taken from structured fields first, then from Canadian ranges stated in the text.</li>")
            .Append("<li>").Append(Num(m.Snapshot.CitizenshipFlagged)).Append(" postings mention Canadian citizenship or a security clearance.</li>")
            .Append("<li>Run ").Append(E(m.LatestRunId)).Append(", generated ").Append(E(m.GeneratedAt.ToString("MMMM d, yyyy 'at' h:mm tt", Canada))).Append(".</li></ul>");
        if (m.Warnings.Count > 0)
        {
            h.Append("<h3>Data warnings</h3><ul>");
            foreach (var w in m.Warnings) h.Append("<li>").Append(E(w)).Append("</li>");
            h.Append("</ul>");
        }
        h.Append("<p>Jobs data partly by <a href=\"https://www.adzuna.ca\" target=\"_blank\" rel=\"noopener noreferrer\">Adzuna</a>.</p></footer>\n");
    }

    private static void Open(StringBuilder h, string id, string title, string note) =>
        h.Append("<section id=\"").Append(id).Append("\" aria-labelledby=\"").Append(id).Append("-h\"><div class=\"section-head\"><h2 id=\"")
            .Append(id).Append("-h\">").Append(E(title)).Append("</h2><p class=\"note\">").Append(E(note)).Append("</p></div>");

    private static void Close(StringBuilder h) => h.Append("</section>\n");

    private static void Empty(StringBuilder h, string text) => h.Append("<p class=\"empty\">").Append(E(text)).Append("</p>");

    private static string Chip(SkillLevel level) =>
        $"<span class=\"chip lvl-{level.ToString().ToLowerInvariant()}\">{LevelLabel(level)}</span>";

    private static string LevelLabel(SkillLevel level) => level switch
    {
        SkillLevel.Strong => "Strong",
        SkillLevel.Working => "Working",
        SkillLevel.Learning => "Learning",
        _ => "Gap",
    };

    private static string FamilyLabel(RoleFamily family) => family switch
    {
        RoleFamily.Dotnet => ".NET",
        RoleFamily.Fullstack => "Full stack",
        RoleFamily.SoftwareEngineer => "Software engineer",
        RoleFamily.LeadManager => "Lead / manager",
        _ => "Other",
    };

    private static string SeniorityLabel(Seniority seniority) => seniority switch
    {
        Seniority.Junior => "Junior",
        Seniority.Intermediate => "Intermediate",
        Seniority.Senior => "Senior",
        Seniority.StaffPrincipal => "Staff / principal",
        Seniority.Lead => "Lead",
        Seniority.Manager => "Manager",
        _ => "Not stated",
    };

    private static string SalaryText(SalaryRange s) =>
        s.Period == SalaryPeriod.Hour ? $"${s.Min.ToString("0", Inv)}–${s.Max.ToString("0", Inv)}/h" : $"{Money(s.YearlyMin)}–{Money(s.YearlyMax)}";

    private static string? SafeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? url : null;

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");
    private static string Num(int n) => n.ToString("N0", Canada);
    private static string Money(decimal amount) => "$" + (amount / 1000m).ToString("0", Inv) + "k";
    private static string Pct(double share) => share <= 0 ? "0%" : share < 0.005 ? "<1%" : (share * 100).ToString("0", Inv) + "%";
    private static string F(double value) => value.ToString("0.#", Inv);
    private static string WeekLabel(DateOnly day) => day.ToString("MMM d", Canada);

    private const string Css = """
        :root{--bg:#F2F4F7;--surface:#FFFFFF;--ink:#17212B;--muted:#586676;--rule:#D5DCE4;--accent:#A8132B;--accent-soft:#F5DADF;--bar:#2B5B8C;--bar-track:#E1E8F0;--good:#1C7445;--good-soft:#DAF0E3;--info:#2B5B8C;--info-soft:#DFE8F3;--warn:#99570A;--warn-soft:#F6E7D0;--display:"Archivo","Arial Narrow",system-ui,sans-serif;--body:"Source Sans 3","Segoe UI",system-ui,-apple-system,sans-serif;--mono:"IBM Plex Mono",ui-monospace,"Cascadia Mono",Consolas,monospace}
        @media (prefers-color-scheme:dark){:root:not([data-theme="light"]){color-scheme:dark;--bg:#0E141B;--surface:#151E28;--ink:#E2E8EE;--muted:#94A2B1;--rule:#27333F;--accent:#FF7385;--accent-soft:#3E1D26;--bar:#72A9DD;--bar-track:#1D2B3A;--good:#5DCB90;--good-soft:#16311F;--info:#72A9DD;--info-soft:#172838;--warn:#F0AB5A;--warn-soft:#342413}}
        :root[data-theme="dark"]{color-scheme:dark;--bg:#0E141B;--surface:#151E28;--ink:#E2E8EE;--muted:#94A2B1;--rule:#27333F;--accent:#FF7385;--accent-soft:#3E1D26;--bar:#72A9DD;--bar-track:#1D2B3A;--good:#5DCB90;--good-soft:#16311F;--info:#72A9DD;--info-soft:#172838;--warn:#F0AB5A;--warn-soft:#342413}
        *,*::before,*::after{box-sizing:border-box}
        body{margin:0;background:var(--bg);color:var(--ink);font-family:var(--body);font-size:16px;line-height:1.55}
        .report{max-width:980px;margin:0 auto;padding-inline:max(16px,4vw);padding-block:28px 56px;display:grid;gap:44px}
        h1,h2,h3{font-family:var(--display);font-stretch:87.5%;text-wrap:balance;margin:0;line-height:1.1}
        h1{font-size:clamp(2.2rem,6vw,3.4rem);font-weight:800;letter-spacing:-.01em}
        h2{font-size:1.45rem;font-weight:700}
        h3{font-size:1.05rem;font-weight:700}
        p{margin:0}
        a{color:var(--bar);text-underline-offset:2px}
        a:focus-visible{outline:2px solid var(--accent);outline-offset:2px;border-radius:2px}
        .eyebrow,.stat-label,.axis,.rank-meta,.bar-value,.num,.chip,.pill,.flag,thead th{font-family:var(--mono);font-variant-numeric:tabular-nums}
        .eyebrow{font-size:.78rem;letter-spacing:.06em;text-transform:uppercase;color:var(--muted)}
        .masthead{display:grid;gap:10px;padding-bottom:22px;border-bottom:3px solid var(--accent)}
        .lede{font-size:1.15rem;max-width:62ch}
        .stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:12px}
        .stat{background:var(--surface);border:1px solid var(--rule);border-radius:10px;padding:14px 16px;display:grid;gap:2px}
        .stat-label{font-size:.72rem;letter-spacing:.06em;text-transform:uppercase;color:var(--muted)}
        .stat-value{font-family:var(--display);font-stretch:87.5%;font-size:2rem;font-weight:800;font-variant-numeric:tabular-nums}
        .stat-note{font-size:.85rem;color:var(--muted)}
        section{display:grid;gap:14px}
        .section-head{display:grid;gap:6px}
        .note{color:var(--muted);font-size:.95rem;max-width:68ch}
        .empty{color:var(--muted);font-style:italic}
        .bars{list-style:none;margin:0;padding:0;display:grid;gap:6px}
        .bar-row{display:grid;grid-template-columns:minmax(7.5rem,12rem) minmax(0,1fr) 3.2rem 5.6rem;grid-template-areas:"name track value chip";align-items:center;gap:10px}
        .bar-name{grid-area:name;overflow-wrap:anywhere}
        .bar-track{grid-area:track;height:12px;background:var(--bar-track);border-radius:3px;overflow:hidden}
        .bar-fill{display:block;height:100%;background:var(--bar);border-radius:3px}
        .bar-value{grid-area:value;text-align:right;font-size:.85rem}
        .bar-row .chip{grid-area:chip}
        .chip{display:inline-block;font-size:.72rem;padding:2px 8px;border-radius:999px;justify-self:start;white-space:nowrap}
        .lvl-strong{background:var(--good-soft);color:var(--good)}
        .lvl-working{background:var(--info-soft);color:var(--info)}
        .lvl-learning{background:var(--warn-soft);color:var(--warn)}
        .lvl-none{background:var(--accent-soft);color:var(--accent)}
        .pair{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:28px}
        .col{display:grid;gap:10px;align-content:start}
        .ranked{margin:0;padding-left:1.6rem;display:grid;gap:8px}
        .ranked li::marker{font-family:var(--mono);color:var(--muted);font-size:.85rem}
        .rank-line{display:flex;flex-wrap:wrap;align-items:baseline;gap:6px 10px}
        .rank-name{font-weight:600}
        .rank-meta{font-size:.8rem;color:var(--muted)}
        .scroll{overflow-x:auto;border:1px solid var(--rule);border-radius:10px;background:var(--surface)}
        table{border-collapse:collapse;width:100%;min-width:520px;font-size:.95rem}
        th,td{padding:9px 12px;text-align:left;border-bottom:1px solid var(--rule)}
        thead th{font-size:.72rem;letter-spacing:.06em;text-transform:uppercase;color:var(--muted);font-weight:500}
        tbody tr:last-child td{border-bottom:0}
        .num{text-align:right;white-space:nowrap}
        .muted{color:var(--muted)}
        .up{color:var(--good)}
        .down{color:var(--accent)}
        .matches{list-style:none;margin:0;padding:0;display:grid;gap:10px}
        .match{display:flex;flex-wrap:wrap;justify-content:space-between;gap:8px 16px;padding:12px 14px;background:var(--surface);border:1px solid var(--rule);border-radius:10px}
        .match-main{display:grid;gap:3px;min-width:0;flex:1 1 280px}
        .match-title{font-weight:600;overflow-wrap:anywhere}
        .match-meta,.match-missing{font-size:.9rem;color:var(--muted)}
        .match-side{display:flex;flex-wrap:wrap;align-items:flex-start;gap:6px}
        .pill{font-size:.8rem;padding:3px 10px;border-radius:999px;background:var(--info-soft);color:var(--info);white-space:nowrap}
        .flag{font-size:.75rem;padding:3px 10px;border-radius:999px;background:var(--warn-soft);color:var(--warn);white-space:nowrap}
        .companies{list-style:none;margin:0;padding:0;display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:0 24px}
        .companies li{display:flex;justify-content:space-between;gap:12px;padding:7px 0;border-bottom:1px solid var(--rule)}
        .chart{margin:0;background:var(--surface);border:1px solid var(--rule);border-radius:10px;padding:12px}
        .chart svg{display:block;width:100%;height:auto;max-width:100%}
        .chart figcaption{font-size:.85rem;color:var(--muted);margin-top:6px}
        .grid{stroke:var(--rule);stroke-width:1;fill:none}
        .line{stroke:var(--bar);stroke-width:2.5;stroke-linejoin:round;stroke-linecap:round}
        .area{fill:var(--bar);fill-opacity:.12;stroke:none}
        .dot{fill:var(--bar);stroke:var(--surface);stroke-width:2}
        .axis{fill:var(--muted);font-size:12px}
        .method{display:grid;gap:10px;padding-top:20px;border-top:1px solid var(--rule);color:var(--muted);font-size:.92rem}
        .method ul{margin:0;padding-left:1.2rem;display:grid;gap:4px}
        .method h2{font-size:1.1rem;color:var(--ink)}
        @media (max-width:560px){.bar-row{grid-template-columns:minmax(0,1fr) auto 3rem;grid-template-areas:"name chip value" "track track track";row-gap:4px}}
        """;
}
```

`src/Processor/Reporting/ReportWriter.cs`:
```csharp
using System.Text;
using Processor.Analysis;

namespace Processor.Reporting;

// Writes data/reports/<run>.html, latest.fragment.html (for publishing) and latest.html last, each via temp file + rename.
public static class ReportWriter
{
    public static async Task<string> WriteAsync(string dataRoot, ReportModel model, CancellationToken ct)
    {
        var fragment = ReportRenderer.RenderFragment(model);
        var document = ReportRenderer.RenderDocument(fragment);
        var dir = Path.Combine(dataRoot, "reports");
        Directory.CreateDirectory(dir);

        await WriteAtomicAsync(Path.Combine(dir, $"{model.LatestRunId}.html"), document, ct);
        await WriteAtomicAsync(Path.Combine(dir, "latest.fragment.html"), fragment, ct);
        var latest = Path.Combine(dir, "latest.html");
        await WriteAtomicAsync(latest, document, ct);
        return latest;
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        File.Move(tmp, path, overwrite: true);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Processor.Tests`
Expected: renderer and writer tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Render the GTA job market report as a publishable page"
```

### Task 12: Rebuild service, CLI and the first live report

**Files:**
- Create: `src/Processor/Rebuild/RebuildService.cs`, `src/Processor/Analysis/AnalysisLoader.cs`, `src/Processor/ErrorText.cs`
- Modify: `src/Processor/Program.cs` (replace the template entirely), `src/Processor/Processor.csproj` (UserSecretsId via command)
- Create (local only, gitignored): `.env`, `config/profile.json`
- Test: `tests/Processor.Tests/Rebuild/RebuildServiceTests.cs`, `tests/Processor.Tests/ErrorTextTests.cs`

**Interfaces:**
- Consumes: everything above
- Produces:
  - `record RebuildSummary(int Runs, int Postings, int CountedPostings, IReadOnlyList<string> Warnings)`
  - `RebuildService(TrackerDb, ProcessorOptions, SkillCatalog, ILogger<RebuildService>).RunAsync(string dataRoot, CancellationToken) : Task<RebuildSummary>`
  - `AnalysisLoader.LoadAsync(TrackerDb, CancellationToken) : Task<(IReadOnlyList<AnalysisPosting> Postings, IReadOnlyList<RunInfo> Runs, IReadOnlyList<JobBankMonth> JobBank)>` — in-scope, non-duplicate postings; `Location` is the GTA city or `"Remote (Canada)"`
  - `ErrorText.Chain(Exception) : string`
  - The `Processor` executable: `Processor [--report-only]`, exit codes 0/1/2/3

- [ ] **Step 1: Write the failing tests**

`tests/Processor.Tests/ErrorTextTests.cs`:
```csharp
namespace Processor.Tests;

public class ErrorTextTests
{
    [Fact]
    public void Chain_joins_inner_messages() =>
        Assert.Equal("outer -> inner", ErrorText.Chain(new InvalidOperationException("outer", new IOException("inner"))));
}
```

`tests/Processor.Tests/Rebuild/RebuildServiceTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Processor.Analysis;
using Processor.Interpreting;
using Processor.Rebuild;
using Processor.Tests.Configuration;
using Processor.Tests.TestSupport;

namespace Processor.Tests.Rebuild;

[Collection("postgres")]
[Trait("Category", "Integration")]
public class RebuildServiceTests(PostgresFixture pg)
{
    private static readonly SkillCatalog Catalog = SkillCatalog.Load(RepoConfig.PathOf("skills.json"));

    private static RebuildService Service(Processor.Data.TrackerDb db) =>
        new(db, RepoConfig.Options(), Catalog, NullLogger<RebuildService>.Instance);

    [Fact]
    public async Task Rebuilds_the_fixture_archive_and_loads_it_for_analysis()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);
        await using var db = await pg.OpenAsync();

        var summary = await Service(db).RunAsync(tmp.Path, CancellationToken.None);

        Assert.Equal((3, 8, 6), (summary.Runs, summary.Postings, summary.CountedPostings));
        Assert.Empty(summary.Warnings);
        Assert.Equal("greenhouse:101", (await db.Postings.AsNoTracking().SingleAsync(p => p.Id == "adzuna:A3")).DuplicateOf);
        Assert.False((await db.Postings.AsNoTracking().SingleAsync(p => p.Id == "greenhouse:102")).InScope);

        var g1 = await db.Postings.AsNoTracking().Include(p => p.Sightings).Include(p => p.Skills).ThenInclude(s => s.Skill)
            .SingleAsync(p => p.Id == "greenhouse:101");
        Assert.Equal(3, g1.Sightings.Count);
        Assert.Equal(("parsed", 130000m), (g1.SalaryOrigin, g1.SalaryMin!.Value));
        Assert.Superset(new HashSet<string> { "React", "TypeScript", "C#" }, g1.Skills.Select(s => s.Skill!.Name).ToHashSet());

        var s1 = await db.Postings.AsNoTracking().SingleAsync(p => p.Id == "ashby:S1");
        Assert.True(s1.IsRemoteCanada);
        Assert.True(s1.CitizenshipOrClearance);

        var (postings, runs, jobBank) = await AnalysisLoader.LoadAsync(db, CancellationToken.None);
        Assert.Equal(6, postings.Count);
        Assert.Equal(3, runs.Count);
        Assert.Empty(jobBank);
        var inbox = postings.Single(p => p.Id == "inbox:one.txt");
        Assert.Equal("Toronto", inbox.Location);
        Assert.Equal(runs[^1].StartedAt, Assert.Single(inbox.Sightings));
        Assert.Equal("Remote (Canada)", postings.Single(p => p.Id == "ashby:S1").Location);
        Assert.Equal(RoleFamily.Dotnet, postings.Single(p => p.Id == "adzuna:A1").Family);
    }

    [Fact]
    public async Task Rebuilding_the_same_archive_twice_is_idempotent()
    {
        using var tmp = new TempDir();
        FixtureArchive.Write(tmp.Path);
        await using var db = await pg.OpenAsync();

        async Task<(int, int, int)> Counts() =>
            (await db.Postings.CountAsync(), await db.PostingSightings.CountAsync(), await db.PostingSkills.CountAsync());

        await Service(db).RunAsync(tmp.Path, CancellationToken.None);
        var first = await Counts();
        await Service(db).RunAsync(tmp.Path, CancellationToken.None);

        Assert.Equal(first, await Counts());
    }

    [Fact]
    public async Task An_empty_archive_is_reported_clearly()
    {
        using var tmp = new TempDir();
        await using var db = await pg.OpenAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).RunAsync(tmp.Path, CancellationToken.None));

        Assert.Contains("Run the archiver first", error.Message);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Processor.Tests`
Expected: build FAILS — `ErrorText`, `RebuildService`, `AnalysisLoader` missing.

- [ ] **Step 3: Implement the service, loader and error text**

`src/Processor/ErrorText.cs`:
```csharp
namespace Processor;

public static class ErrorText
{
    // Joins an exception's messages outer to inner: the useful detail (JSON line, socket error) is usually inside.
    public static string Chain(Exception ex)
    {
        var messages = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException) messages.Add(e.Message);
        return string.Join(" -> ", messages.Distinct());
    }
}
```

`src/Processor/Rebuild/RebuildService.cs`:
```csharp
using Microsoft.Extensions.Logging;
using Processor.Configuration;
using Processor.Data;
using Processor.Interpreting;
using Processor.Reading;

namespace Processor.Rebuild;

public sealed record RebuildSummary(int Runs, int Postings, int CountedPostings, IReadOnlyList<string> Warnings);

// Reads every archived run oldest-first, interprets and deduplicates postings, and replaces the database content.
public sealed class RebuildService(TrackerDb db, ProcessorOptions options, SkillCatalog catalog, ILogger<RebuildService> log)
{
    public async Task<RebuildSummary> RunAsync(string dataRoot, CancellationToken ct)
    {
        var runs = Archive.Runs(dataRoot);
        if (runs.Count == 0)
            throw new InvalidOperationException($"No completed archive runs under {Path.Combine(dataRoot, "raw")}. Run the archiver first.");

        var collector = new SightingCollector();
        var aggregates = new List<AdzunaAggregate>();
        foreach (var run in runs)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var raw in Archive.ReadPostings(run, options.CompanyName)) collector.Add(raw);
            aggregates.AddRange(Archive.ReadAggregates(run));
        }

        var inbox = InboxReader.ReadAll(Path.Combine(dataRoot, "inbox"), runs[^1].RunId);
        foreach (var raw in inbox.Postings) collector.Add(raw);

        var interpreter = new Interpreter(new ScopeRules(options.Gta), catalog);
        var postings = Deduplicator.MarkDuplicates(
            [.. collector.Entries.Select(e => interpreter.Interpret(e.Latest, e.Runs))],
            [.. runs.Select(r => r.RunId)]);
        var jobBank = JobBankReader.SummarizeDirectory(Path.Combine(dataRoot, "jobbank"), options.JobBank);

        await new RebuildWriter(db).WriteAsync(new RebuildData(runs, catalog.Skills, postings, aggregates, jobBank), ct);

        var counted = postings.Count(p => p.Scope.InScope && p.DuplicateOf is null);
        log.LogInformation("Rebuilt from {Runs} runs: {Postings} postings, {Counted} in scope after duplicates, {JobBank} Job Bank rows",
            runs.Count, postings.Count, counted, jobBank.Count);
        return new RebuildSummary(runs.Count, postings.Count, counted, inbox.Warnings);
    }
}
```

`src/Processor/Analysis/AnalysisLoader.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Processor.Data;
using Processor.Interpreting;
using Processor.Reading;

namespace Processor.Analysis;

public static class AnalysisLoader
{
    // In-scope, non-duplicate postings with their skills and sighting times, plus runs and Job Bank rows.
    public static async Task<(IReadOnlyList<AnalysisPosting> Postings, IReadOnlyList<RunInfo> Runs, IReadOnlyList<JobBankMonth> JobBank)> LoadAsync(TrackerDb db, CancellationToken ct)
    {
        var runs = await db.Runs.AsNoTracking().OrderBy(r => r.StartedAt).Select(r => new RunInfo(r.RunId, r.StartedAt)).ToListAsync(ct);
        var startedAt = runs.ToDictionary(r => r.RunId, r => r.StartedAt, StringComparer.Ordinal);

        var rows = await db.Postings.AsNoTracking()
            .Where(p => p.InScope && p.DuplicateOf == null)
            .Select(p => new
            {
                p.Id, p.Source, p.Company, p.Title, p.Url, p.City, p.IsGta, p.RoleFamily, p.Seniority,
                p.SalaryMin, p.SalaryMax, p.SalaryPeriod, p.IsFullText, p.CitizenshipOrClearance,
                Skills = p.Skills.Select(s => s.Skill!.Name).ToList(),
                Runs = p.Sightings.Select(s => s.RunId).ToList(),
            })
            .AsSplitQuery()
            .ToListAsync(ct);

        var postings = rows.Select(r => new AnalysisPosting(
            r.Id, r.Source, r.Company, r.Title, r.Url,
            r.IsGta ? r.City ?? "GTA" : "Remote (Canada)",
            TitleRules.ParseFamily(r.RoleFamily) ?? RoleFamily.Other,
            Enum.Parse<Seniority>(r.Seniority),
            r.SalaryMin is { } min && r.SalaryMax is { } max && Enum.TryParse<SalaryPeriod>(r.SalaryPeriod, out var period)
                ? new SalaryRange(min, max, period)
                : null,
            r.IsFullText, r.CitizenshipOrClearance,
            r.Skills.ToHashSet(StringComparer.Ordinal),
            [.. r.Runs.Select(id => startedAt[id]).Order()])).ToList();

        var jobBank = await db.JobBankMonths.AsNoTracking()
            .Select(j => new JobBankMonth(j.Month, j.NocCode, j.NocName, j.Postings, j.Vacancies, j.WageMinMedian, j.WageMaxMedian))
            .ToListAsync(ct);

        return (postings, runs, jobBank);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Processor.Tests`
Expected: all tests PASS, including the 3 rebuild integration tests.

- [ ] **Step 5: Wire the CLI**

```bash
dotnet user-secrets init --project src/Processor
```

`src/Processor/Program.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Processor;
using Processor.Analysis;
using Processor.Configuration;
using Processor.Data;
using Processor.Interpreting;
using Processor.Reading;
using Processor.Rebuild;
using Processor.Reporting;

const int Ok = 0, Failed = 1, ConfigInvalid = 2, DatabaseUnavailable = 3;
const string Usage = "Usage: Processor [--report-only]";

var reportOnly = args.Contains("--report-only");
var unknown = args.Where(a => a != "--report-only").ToList();
if (unknown.Count > 0)
{
    Console.Error.WriteLine($"Unknown argument(s): {string.Join(' ', unknown)}");
    Console.Error.WriteLine(Usage);
    return ConfigInvalid;
}

var root = RepoRoot.Find(Directory.GetCurrentDirectory()) ?? RepoRoot.Find(AppContext.BaseDirectory);
if (root is null)
{
    Console.Error.WriteLine("Could not find config/sources.json in this folder or any parent. Run from the repo root.");
    return ConfigInvalid;
}

ProcessorOptions options;
SkillCatalog catalog;
Profile profile;
try
{
    options = ProcessorOptions.Load(Path.Combine(root, "config", "processor.json"));
    catalog = SkillCatalog.Load(Path.Combine(root, "config", "skills.json"));
    var profilePath = Path.Combine(root, "config", "profile.json");
    if (!File.Exists(profilePath))
    {
        Console.Error.WriteLine("Config error: config/profile.json is missing. Copy config/profile.example.json to config/profile.json and set your skill levels.");
        return ConfigInvalid;
    }
    profile = Profile.Load(profilePath, catalog);
}
catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException)
{
    Console.Error.WriteLine($"Config error: {ErrorText.Chain(ex)}");
    return ConfigInvalid;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = root });
builder.Configuration.AddUserSecrets<RebuildService>(optional: true).AddEnvironmentVariables();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
var connectionString = builder.Configuration.GetConnectionString("Tracker");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Config error: no database connection string. Set it with:");
    Console.Error.WriteLine("  dotnet user-secrets set \"ConnectionStrings:Tracker\" \"Host=localhost;Port=5434;Database=tracker;Username=tracker;Password=<password from .env>\" --project src/Processor");
    return ConfigInvalid;
}
builder.Services.AddDbContext<TrackerDb>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
builder.Services.AddSingleton(options).AddSingleton(catalog).AddSingleton(profile);
builder.Services.AddScoped<RebuildService>();

using var host = builder.Build();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Processor");
using var scope = host.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<TrackerDb>();

try
{
    await db.Database.MigrateAsync(cts.Token);
}
catch (Exception ex) when (IsDatabaseUnavailable(ex))
{
    Console.Error.WriteLine($"Database unavailable: {ErrorText.Chain(ex)}");
    Console.Error.WriteLine("Start it with: docker compose up -d --wait postgres (Docker Desktop must be running).");
    return DatabaseUnavailable;
}

try
{
    var dataRoot = Path.Combine(root, "data");
    IReadOnlyList<string> warnings = reportOnly
        ? InboxReader.ReadAll(Path.Combine(dataRoot, "inbox"), "").Warnings
        : (await scope.ServiceProvider.GetRequiredService<RebuildService>().RunAsync(dataRoot, cts.Token)).Warnings;

    var (postings, runs, jobBank) = await AnalysisLoader.LoadAsync(db, cts.Token);
    if (runs.Count == 0)
    {
        Console.Error.WriteLine("The database has no runs yet. Run Processor without --report-only first.");
        return Failed;
    }

    var model = new Analyzer(profile, catalog, options.WindowDays).Build(postings, runs, jobBank, warnings, DateTimeOffset.Now);
    var latest = await ReportWriter.WriteAsync(dataRoot, model, cts.Token);
    log.LogInformation("Report written: {Path} ({InWindow} postings in the window, top gap: {Gap})",
        latest, model.Snapshot.InWindow, model.Gaps.FirstOrDefault()?.Skill ?? "none");
    return Ok;
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelled.");
    return Failed;
}
catch (Exception ex)
{
    log.LogError(ex, "Processing failed");
    Console.Error.WriteLine($"Processing failed: {ErrorText.Chain(ex)}");
    return Failed;
}

// Connection refused, host down, timeout or authentication failure: Postgres is not reachable as configured.
static bool IsDatabaseUnavailable(Exception ex)
{
    for (var e = ex; e is not null; e = e.InnerException)
        if (e is NpgsqlException or System.Net.Sockets.SocketException or TimeoutException) return true;
    return false;
}
```

Run: `dotnet build src/Processor -warnaserror && dotnet test`
Expected: build with 0 warnings; all tests PASS.

- [ ] **Step 6: Local database and secrets (values never printed or committed)**

PowerShell, from the repo root:
```powershell
$pw = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 28 | ForEach-Object { [char]$_ })
Set-Content -Path .env -Value "POSTGRES_PASSWORD=$pw" -Encoding ascii
dotnet user-secrets set "ConnectionStrings:Tracker" "Host=localhost;Port=5434;Database=tracker;Username=tracker;Password=$pw" --project src/Processor | Out-Null
docker compose up -d --wait postgres
```
Expected: the `jobtracker-postgres` container reports healthy. Then `git status --short` must not list `.env`.

- [ ] **Step 7: Draft the owner's profile**

`config/profile.json` (gitignored, so it is not committed). It's drafted from the CV and the owner's 2026-09-28 answers, and the owner reviews it:
```json
// Drafted by Claude from your CV and our 2026-09-28 conversation. Correct any level you disagree with.
// Levels: strong | working | learning | none. Skills left out count as none.
{
  "targetFamilies": ["dotnet", "fullstack", "software_engineer", "lead_manager"],
  "skills": {
    "C#": "strong",
    ".NET": "strong",
    "ASP.NET Core": "strong",
    ".NET Framework": "strong",
    "Entity Framework": "strong",
    "SQL": "strong",
    "SQL Server": "strong",
    "JavaScript": "strong",
    "HTML": "strong",
    "CSS": "strong",
    "Bootstrap": "strong",
    "jQuery": "strong",
    "REST APIs": "strong",
    "Azure DevOps": "strong",
    "CI/CD": "strong",
    "Git": "strong",
    "Responsive Design": "strong",
    "Requirements Gathering": "strong",
    "ERP": "working",
    "IIS": "working",
    "TypeScript": "working",
    "React": "working",
    "Next.js": "working",
    "Redux": "working",
    "GraphQL": "working",
    "WPF": "working",
    "Docker": "working",
    "Linux": "working",
    "Unit Testing": "working",
    "xUnit": "working",
    "Caching": "working",
    "Performance Optimization": "working",
    "Design Patterns": "working",
    "System Design": "working",
    "API Design": "working",
    "Agile": "working",
    "Scrum": "working",
    "Code Review": "working",
    "Mentoring": "working",
    "Technical Leadership": "working",
    "Stakeholder Management": "working",
    "Estimation & Planning": "working",
    "Documentation": "working",
    "Production Support": "working",
    "AI Coding Assistants": "working",
    "Computer Vision": "working",
    "EdTech": "working",
    "Cypress": "learning",
    "LLM": "learning",
    "Generative AI": "learning",
    "Prompt Engineering": "learning",
    "Python": "learning",
    "Node.js": "learning",
    "Angular": "learning",
    "MongoDB": "learning",
    "MySQL": "learning",
    "PostgreSQL": "learning",
    "Azure": "learning",
    "Microservices": "learning"
  }
}
```

- [ ] **Step 8: Live run on the real archive**

Run: `dotnet run --project src/Processor -c Release`
Expected:
- exit 0
- the log shows `Rebuilt from N runs` (N ≥ 2) and `Report written: …\data\reports\latest.html`
- `data/reports/latest.html`, `latest.fragment.html` and `<run>.html` exist

Sanity-check it with SQL:
```bash
docker exec jobtracker-postgres psql -U tracker -d tracker -c "select count(*) filter (where in_scope and duplicate_of is null) as counted, count(*) as total from postings;"
docker exec jobtracker-postgres psql -U tracker -d tracker -c "select s.name, count(*) from posting_skills ps join skills s on s.id = ps.skill_id join postings p on p.id = ps.posting_id where p.in_scope and p.duplicate_of is null and p.is_full_text group by s.name order by 2 desc limit 15;"
docker exec jobtracker-postgres psql -U tracker -d tracker -c "select role_family, seniority, count(*), percentile_cont(0.5) within group (order by (salary_min + salary_max) / 2) as median from postings where in_scope and duplicate_of is null and salary_min is not null and salary_period = 'Year' group by 1, 2 order by 1, 2;"
```
Expected, in order:
1. The counted postings are in the high hundreds to low thousands (the data profile had about 1,030 GTA Adzuna postings plus about 980 Canadian board postings).
2. The top skills include several of C#, .NET, React, TypeScript, SQL, Azure, AWS, Python and Java.
3. No median outside 50k–300k.

If a number looks wrong, investigate with superpowers:systematic-debugging before changing a rule, and record any rule change in the ledger.

- [ ] **Step 9: Check report-only and the database-unavailable path**

Run: `dotnet run --project src/Processor -c Release -- --report-only`
Expected: exit 0, and `latest.html` is regenerated.

Run (bash): `ConnectionStrings__Tracker="Host=localhost;Port=5999;Database=tracker;Username=tracker;Password=x" dotnet run --project src/Processor -c Release; echo "exit=$?"`
Expected: prints `Database unavailable: …` and `Start it with: docker compose up -d --wait postgres …`; `exit=3`.

- [ ] **Step 10: Commit**

```bash
git add -A
git status --short   # must not show .env, config/profile.json or data/
git commit -m "Wire the Processor CLI: rebuild, analyze and write the report"
```

---

### Task 13: Pipeline scheduling, documentation and publishing

**Files:**
- Create: `scripts/run-pipeline.cmd`, `docs/decisions/0004-postgres.md`, `docs/decisions/0005-full-rebuild.md`, `docs/decisions/0006-rule-based-skills-first.md`
- Modify: `scripts/register-task.ps1`, `README.md`

**Interfaces:**
- Consumes: the `Archiver` and `Processor` executables, `docker-compose.yml`
- Produces: the scheduled task `JobMarketTracker Archiver` now running the whole pipeline Tue/Fri 07:00, logging to `data/pipeline.log`; the published claude.ai page for the report

- [ ] **Step 1: Pipeline script** (write with the Write tool — it contains backslashes)

`scripts/run-pipeline.cmd`:
```bat
@echo off
rem Collect, then process. The scheduled task appends this script's output to data\pipeline.log.
setlocal
cd /d "%~dp0.."
echo ==== %DATE% %TIME% archiver
".artifacts\archiver\Archiver.exe"
echo archiver exit code %ERRORLEVEL%
echo ==== %DATE% %TIME% database
docker compose up -d --wait postgres
if errorlevel 1 echo Docker is not available or Postgres did not become healthy; the processor will report the database as unavailable.
echo ==== %DATE% %TIME% processor
".artifacts\processor\Processor.exe"
echo processor exit code %ERRORLEVEL%
endlocal
```

- [ ] **Step 2: Scheduling script** (Write tool; replace the file)

`scripts/register-task.ps1`:
```powershell
<#
.SYNOPSIS
Publishes the archiver and processor and registers (or updates) the Windows scheduled task that runs
scripts/run-pipeline.cmd every Tuesday and Friday. Re-run this script after changing the code.
#>
[CmdletBinding()]
param(
    [string]$TaskName = 'JobMarketTracker Archiver',
    [DateTime]$At = '07:00'
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dataDir = Join-Path $root 'data'
$logFile = Join-Path $dataDir 'pipeline.log'

foreach ($app in 'Archiver', 'Processor') {
    $out = Join-Path $root ".artifacts\$($app.ToLowerInvariant())"
    dotnet publish (Join-Path $root "src\$app") -c Release -o $out --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $app failed with exit code $LASTEXITCODE" }
}
New-Item -ItemType Directory -Force $dataDir | Out-Null

$pipeline = Join-Path $root 'scripts\run-pipeline.cmd'
# cmd.exe appends all output to one log so failures before any manifest or report is written stay visible.
$action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument "/c `"`"$pipeline`" >> `"$logFile`" 2>&1`"" -WorkingDirectory $root
$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Tuesday, Friday -At $At
# RunOnlyIfNetworkAvailable: after sleep, a missed run waits for the network instead of failing every request.
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -RunOnlyIfNetworkAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 3)

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
    -Description 'Collects job-market snapshots and rebuilds the GTA job market report. See README.md.' -Force | Out-Null

Write-Host "Registered '$TaskName': Tuesday and Friday at $($At.ToString('HH:mm')), catching up after missed runs."
Write-Host "Runs: $pipeline (working directory $root)"
Write-Host "Log:  $logFile"
```

- [ ] **Step 3: Decision notes**

`docs/decisions/0004-postgres.md`:
```markdown
# 0004 — PostgreSQL for the tracker database

**Status:** Accepted (2026-09-28)

## Context
Phase 1 needs a relational store for postings, skills and sightings, queried by the report now and by the phase 2 API later. The owner knows SQL Server deeply; Toronto startups and scale-ups mostly run PostgreSQL, and banks mostly run SQL Server.

## Decision
PostgreSQL 17 in Docker Compose, accessed with EF Core (Npgsql) with snake_case names. Raw Adzuna aggregates are stored as `jsonb`, and bulk loads use Postgres binary `COPY`.

## Consequences
- A second production database to work with, plus hands-on practice with Postgres-specific features (`jsonb`, `COPY`, `percentile_cont`).
- The database runs in Docker, so the pipeline depends on Docker Desktop at 07:00. The processor exits with code 3 and a clear message when Postgres is unavailable, and collection still happens.
- Alternative rejected: SQL Server. It would be faster for the owner to read, but teaches less that's new.
```

`docs/decisions/0005-full-rebuild.md`:
```markdown
# 0005 — Rebuild the database from the archive on every run

**Status:** Accepted (2026-09-28)

## Context
Interpretation rules (scope, salary parsing, the skill list) will keep improving. Every improvement should apply to all history, not only to new data.

## Decision
Each processor run reads every archived run and replaces the database contents in one transaction (`TRUNCATE`, then `COPY`). Posting ids are stable (`<source>:<source id>`), so history is preserved across rebuilds.

## Consequences
- A rule change takes effect everywhere at the next run, with no data migrations or backfills. This is the payoff of decision 0001.
- Readers never see a half-built database, and a failed rebuild leaves the previous one intact.
- Cost: the whole archive is re-read each run (about 3.5 GB of JSON after six months, roughly a minute). Readers accept `.json.gz`, so old runs can be compressed if this grows.
- Alternative rejected: incremental processing. It's faster per run, but every rule change would need a backfill mechanism.
```

`docs/decisions/0006-rule-based-skills-first.md`:
```markdown
# 0006 — A curated skill list before an LLM

**Status:** Accepted (2026-09-28)

## Context
Skill statistics drive the gap analysis. An LLM extracts skills with more context but costs money per posting, is not deterministic, and is hard to test.

## Decision
Phase 1 matches about 180 skills from `config/skills.json` (names, categories and aliases) with word-edge rules, plus case-sensitive patterns for words that are also ordinary English (React, REST, Go, R). Unit tests pin the tricky cases.

## Consequences
- The matching is free and fast, the same input always gives the same result, and it's easy to explain in an interview.
- It misses context ("5 years of Azure" and "Azure is a plus" count the same) and new tools until someone adds them to the list.
- Phase 3 adds LLM extraction for full-text postings and compares it against these rules.
```

- [ ] **Step 4: README**

In `README.md`, replace the line `**Phase 0 (current):** the archiver. Twice a week it saves raw snapshots from:` with `**Phase 0:** the archiver. Twice a week it saves raw snapshots from:`. Then insert this section after the phase 0 "Configuration" section and before "Roadmap":
```markdown
## Phase 1: processor and report

After each collection, `Processor` rebuilds a PostgreSQL database from the whole archive, measures skill demand against your profile and writes a one-page report:

- `data/reports/latest.html`: open it in a browser on this PC
- `data/reports/latest.fragment.html`: the same page, ready to publish as a private claude.ai page (ask Claude to "publish the latest report")

### Setup (once)

1. Copy `.env.example` to `.env` and choose a password.
2. `docker compose up -d --wait postgres`
3. `dotnet user-secrets set "ConnectionStrings:Tracker" "Host=localhost;Port=5434;Database=tracker;Username=tracker;Password=<same password>" --project src/Processor`
4. Copy `config/profile.example.json` to `config/profile.json` and set your skill levels (strong / working / learning / none).
5. In Docker Desktop, turn on "Start Docker Desktop when you sign in", so the 07:00 run finds the database.

### Commands

    dotnet run --project src/Processor -c Release                    # rebuild + report
    dotnet run --project src/Processor -c Release -- --report-only   # report from the current database
    dotnet test --filter "Category!=Integration"                     # fast tests without Docker

Exit codes: `0` OK · `1` processing failed (the previous report is kept) · `2` config or profile invalid · `3` database unavailable.

The scheduled task runs `scripts/run-pipeline.cmd` (archiver, then Postgres, then processor) and appends everything to `data/pipeline.log`.

Interpretation settings live in `config/processor.json` (GTA places, company names, Job Bank occupations, window) and `config/skills.json` (the skill list). Changes apply to all history at the next run.
```
In the Roadmap list, change item 1 to: `1. ~~Ingestion worker~~ Done in phase 1: processor, Postgres, skill tagging, gap analysis, report`.

- [ ] **Step 5: Re-register and run the task once**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/register-task.ps1`
Expected: both publishes succeed, and the output shows `Registered 'JobMarketTracker Archiver' …` and `Log: …\data\pipeline.log`.

Then trigger it and wait:
```powershell
Start-ScheduledTask -TaskName 'JobMarketTracker Archiver'
```
Wait for the task state to return to `Ready`, which takes several minutes. Then read `data/pipeline.log`. Expected:
- `archiver exit code 0`
- the database step with no error
- `Report written: …` and `processor exit code 0`

- [ ] **Step 6: Commit**

```bash
git add -A
git status --short   # no .env, config/profile.json, data/ or .artifacts/
git commit -m "Run archiver and processor as one scheduled pipeline; document phase 1"
```

- [ ] **Step 7: Publish the report page**

Read `data/reports/latest.fragment.html` in full, since publishing distributes it. Then publish it with the Artifact tool:
- `file_path` = that file
- `icon` = `"chart"`
- `description` = "Toronto/GTA software job market: skills in demand, salaries, hiring companies and skill gaps, rebuilt twice a week."

Give the owner the link. It's a private page. The page declares no runtime capabilities.

