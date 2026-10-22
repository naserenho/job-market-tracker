# Fixes Batch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Clear the minor issues deferred by the reviews of phases 1, 2a, 2b and 2c, each pinned by a test that fails first.

**Architecture:** No new subsystems. Shared rules move into Tracker.Core (`CompanyName`, `DatabaseErrors`), the Processor's body moves into a testable `ProcessorApp`, and every other change is local to the file that has the defect.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, xUnit + Testcontainers, React 19, TanStack Query 5, React Router 7, Vitest.

**Spec:** `docs/superpowers/specs/2026-10-02-fixes-batch-design.md` (item numbers A1–A4, B1–B28 below refer to it).

## Global Constraints

- Secrets stay in user-secrets or environment variables. Never read, print or commit `.env`, `config/profile.json`, `config/cv.json`, `data/` or `.artifacts/`.
- Never run the Archiver. Tests never touch the network.
- `ApplicationsDb`, its migrations and the `tracker.json` format do not change. No migration in this batch.
- 0 build warnings.
- Test commands, from the repo root:
  - `dotnet test tests/Tracker.Core.Tests`, `dotnet test tests/Api.Tests`, `dotnet test tests/Processor.Tests`, `dotnet test tests/Archiver.Tests`
  - `cd src/Dashboard && npm test` and `npm run build`
- Every fix: write the test, watch it fail, change the code, watch it pass. Commit per task.

## Review Focus

1. A pasted file whose `Saved` date is later than every run, or is not a real date: it must still be counted (latest run) and never crash the rebuild. Test in Task 1.
2. A company whose whole name is noise words ("The Company Inc."): the key must not be empty, or all such companies merge. Test in Task 1.
3. A reload failure on a cold start (no data yet) must still fail the request, not serve nothing as "stale". Test in Task 3.
4. The company-create retry must not swallow a unique violation it did not cause (a second failure surfaces as the normal 409). Test in Task 4.
5. A summary that is only line breaks, or ends with one, must not leave an empty Word run or an invalid document. Test in Task 5.

---

### Task 1: Core rules (A1, A2, A3, B1 rule)

**Files:**
- Modify: `src/Tracker.Core/Interpreting/TitleRules.cs`
- Create: `src/Tracker.Core/Interpreting/CompanyName.cs`
- Modify: `src/Tracker.Core/Analysis/Analyzer.cs` (TopCompanies)
- Modify: `src/Tracker.Core/Reading/InboxReader.cs`, `src/Tracker.Core/Rebuild/RebuildService.cs`
- Create: `src/Tracker.Core/Data/DatabaseErrors.cs`; delete `src/Api/DatabaseErrors.cs`; fix usings in `src/Api/Applications/TrackerSchema.cs`, `src/Api/Endpoints/ApiExceptionHandler.cs`, `src/Api/Market/DbMarketSource.cs`
- Modify callers of `InboxReader.ReadAll`: `src/Processor/Program.cs`, `src/Api/Market/DbMarketSource.cs`; `src/Api/Applications/InboxWriter.cs` keeps `Parse(name, content, "check")`
- Test: `tests/Tracker.Core.Tests/Interpreting/TitleRulesTests.cs`, new `CompanyNameTests.cs`, `tests/Tracker.Core.Tests/Analysis/AnalyzerTests.cs`, `tests/Tracker.Core.Tests/Reading/InboxReaderTests.cs`, `tests/Tracker.Core.Tests/Rebuild/RebuildServiceTests.cs`, new `tests/Tracker.Core.Tests/Data/DatabaseErrorsTests.cs`

**Interfaces:**
- Produces:
  - `Tracker.Interpreting.CompanyName.Key(string name) : string`, `CompanyName.Display(IEnumerable<string> spellings) : string`
  - `Tracker.Reading.InboxReader.ReadAll(string inboxDir, IReadOnlyList<ArchiveRun> runs) : InboxResult`
  - `InboxReader.Parse(string fileName, string content, IReadOnlyList<ArchiveRun> runs) : (RawPosting? Posting, IReadOnlyList<string> Warnings)`; the old `Parse(fileName, content, string runId) : (RawPosting?, string?)` stays as a wrapper
  - `Tracker.Data.DatabaseErrors.IsUnavailable(Exception) : bool`

- [ ] **Step 1: Title tests (A1).** Add to `TitleRulesTests`:

```csharp
[Theory]
[InlineData("Senior Software Engineer, Security", RoleFamily.SoftwareEngineer)]
[InlineData("Staff Software Engineer, Machine Learning Platform", RoleFamily.SoftwareEngineer)]
[InlineData("Canada- Jr Software Engineer (SRE)", RoleFamily.SoftwareEngineer)]
[InlineData("Senior Software Engineer II, Ads Data Solutions Engineering", RoleFamily.SoftwareEngineer)]
[InlineData("Sr. Software Dev Engineer, Aurora Control Plane, Aurora Security", RoleFamily.SoftwareEngineer)]
[InlineData("Software Engineers (5)", RoleFamily.SoftwareEngineer)]
[InlineData("Senior Software Engineer in Test", RoleFamily.Other)]
[InlineData("Software Engineer, Testing", RoleFamily.Other)]
[InlineData("Product Security Engineer", RoleFamily.Other)]
[InlineData("Senior Developer (Windows), Product Security", RoleFamily.Other)]
[InlineData("Software Developer and IT Support", RoleFamily.Other)]
[InlineData("Senior Data Engineer", RoleFamily.Other)]
public void A_specialty_does_not_hide_an_explicit_software_title(string title, RoleFamily expected) =>
    Assert.Equal(expected, TitleRules.Family(title));
```

Run `dotnet test tests/Tracker.Core.Tests --filter A_specialty`. Expected: the six SoftwareEngineer rows FAIL (got Other).

- [ ] **Step 2: Title rule.** In `TitleRules.cs` replace the software line of `Family` and the two regexes:

```csharp
if (SoftwareEngineer().IsMatch(title) && !NeverSoftwareEngineer().IsMatch(title)
    && (ExplicitSoftwareTitle().IsMatch(title) || !SpecialtyEngineer().IsMatch(title))) return RoleFamily.SoftwareEngineer;
```

```csharp
[GeneratedRegex(@"\b(?:software|back[\s-]?end|front[\s-]?end|web|application|platform|mobile|ios|android|java|python|react|node(?:\.js)?|golang|ruby|php|typescript|javascript|angular|product)\b.{0,25}?\b(?:engineer|developer|programmer)s?\b|\b(?:developer|programmer)s?\b|^\s*(?:senior|sr\.?|staff|principal|lead)?\s*engineers?\b", RegexOptions.IgnoreCase)]
private static partial Regex SoftwareEngineer();

// "Software Engineer, Security" is a software engineer; "Security Engineer" is not.
[GeneratedRegex(@"\bsoftware\s+(?:(?:development|dev)\s+)?(?:engineer|developer)s?\b", RegexOptions.IgnoreCase)]
private static partial Regex ExplicitSoftwareTitle();

[GeneratedRegex(@"\bbusiness develop|\bsales\b|\bsupport\b|\bqa\b|\btest|\bhardware|\bmechanical|\belectrical|\bfield engineer", RegexOptions.IgnoreCase)]
private static partial Regex NeverSoftwareEngineer();

[GeneratedRegex(@"\bdata engineer|\bmachine learning|\bml engineer|\bsecurity\b|\bsite reliability|\bsre\b|\bdevops\b|\bsolutions? engineer|\bnetwork", RegexOptions.IgnoreCase)]
private static partial Regex SpecialtyEngineer();
```

Delete `NotSoftwareEngineer`. Run the whole `TitleRulesTests`. Expected: PASS (existing rows unchanged).

- [ ] **Step 3: Company key tests (A2).** Create `tests/Tracker.Core.Tests/Interpreting/CompanyNameTests.cs`:

```csharp
using Tracker.Interpreting;

namespace Tracker.Core.Tests.Interpreting;

public class CompanyNameTests
{
    [Theory]
    [InlineData("Acme", "Acme Inc.")]
    [InlineData("  acme  ", "ACME, Ltd")]
    [InlineData("The Globex Corporation", "Globex")]
    public void Variants_share_a_key(string a, string b) => Assert.Equal(CompanyName.Key(a), CompanyName.Key(b));

    [Fact]
    public void Different_companies_differ() => Assert.NotEqual(CompanyName.Key("Acme"), CompanyName.Key("Acme Labs"));

    // Review focus 2: a name made only of dropped words keeps a key of its own.
    [Fact]
    public void A_name_of_only_noise_words_keeps_its_own_key()
    {
        Assert.Equal("the company inc.", CompanyName.Key(" The Company Inc. "));
        Assert.NotEqual(CompanyName.Key("The Company Inc."), CompanyName.Key("Co Ltd"));
    }

    [Fact]
    public void The_most_frequent_spelling_is_shown_then_ordinal_order()
    {
        Assert.Equal("Acme Inc.", CompanyName.Display(["Acme", "Acme Inc.", " Acme Inc. "]));
        Assert.Equal("Acme", CompanyName.Display(["Acme Inc.", "Acme"]));
    }
}
```

Add to `AnalyzerTests` (use that file's existing posting/run helpers; build two in-window postings with companies "Acme" and "Acme Inc." and one "Globex"):

```csharp
[Fact]
public void Company_variants_count_as_one_company()
{
    var model = Build([P("a:1", company: "Acme"), P("a:2", company: "Acme Inc."), P("a:3", company: "Acme Inc."), P("a:4", company: "Globex")]);

    Assert.Equal(new[] { ("Acme Inc.", 3), ("Globex", 1) }, model.Companies.Select(c => (c.Company, c.Postings)));
}
```

Run: FAIL (`CompanyName` does not exist).

- [ ] **Step 4: Company key.** Create `src/Tracker.Core/Interpreting/CompanyName.cs`:

```csharp
namespace Tracker.Interpreting;

// One company, however a source spells it: "Acme", "ACME Inc." and "The Acme Corporation" share a key.
public static class CompanyName
{
    public static string Key(string name)
    {
        var key = Deduplicator.Normalize(name);
        return key.Length > 0 ? key : name.Trim().ToLowerInvariant();
    }

    // The spelling to show for a group: the most frequent, then ordinal order.
    public static string Display(IEnumerable<string> spellings) =>
        spellings.Select(s => s.Trim()).GroupBy(s => s, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
}
```

In `Analyzer.TopCompanies`:

```csharp
[.. window.GroupBy(p => CompanyName.Key(p.Company), StringComparer.Ordinal)
    .Select(g => new CompanyCount(CompanyName.Display(g.Select(p => p.Company)), g.Count()))
    .OrderByDescending(c => c.Postings).ThenBy(c => c.Company, StringComparer.Ordinal)
    .Take(TopCompanyCount)];
```

Run: PASS.

- [ ] **Step 5: Inbox ageing tests (A3).** In `InboxReaderTests` add helpers and tests, and change the two `ReadAll(..., "r1")` calls to `ReadAll(..., Runs)`:

```csharp
private static readonly IReadOnlyList<ArchiveRun> Runs =
[
    new("r1", new DateTimeOffset(2026, 9, 1, 7, 0, 0, TimeSpan.FromHours(-4)), ""),
    new("r2", new DateTimeOffset(2026, 9, 15, 7, 0, 0, TimeSpan.FromHours(-4)), ""),
    new("r3", new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.FromHours(-4)), ""),
];

private static string File(string saved) => $"Title: Dev\nCompany: A\nLocation: Toronto\n{saved}\n\nBody.";

[Theory]
[InlineData("Saved: 2026-08-20", "r1")] // before the first run
[InlineData("Saved: 2026-09-01", "r1")] // the day of a run
[InlineData("Saved: 2026-09-02", "r2")] // between runs: the next one
[InlineData("Saved: 2026-12-25", "r3")] // review focus 1: later than every run
public void A_pasted_job_is_seen_once_in_the_first_run_on_or_after_its_saved_date(string saved, string run)
{
    var (posting, warnings) = InboxReader.Parse("a.txt", File(saved), Runs);

    Assert.Equal(run, posting!.RunId);
    Assert.Empty(warnings);
}

[Theory]
[InlineData("Saved: yesterday")]
[InlineData("Saved: 2026-13-45")]
[InlineData("Note: no saved line")]
public void Without_a_readable_saved_date_it_stays_in_the_latest_run_with_a_warning(string line)
{
    var (posting, warnings) = InboxReader.Parse("a.txt", File(line), Runs);

    Assert.Equal("r3", posting!.RunId);
    Assert.Equal("Inbox file a.txt has no 'Saved: yyyy-MM-dd' line, so it never ages out of the market data.", Assert.Single(warnings));
}

[Fact]
public void With_no_runs_yet_the_posting_still_parses() =>
    Assert.Equal("", InboxReader.Parse("a.txt", File("Saved: 2026-09-02"), []).Posting!.RunId);
```

In `RebuildServiceTests` add (same fixture pattern as its other tests; overwrite the fixture's inbox file after `FixtureArchive.Write`):

```csharp
[Fact]
public async Task A_pasted_job_saved_before_an_earlier_run_is_sighted_only_in_that_run()
{
    FixtureArchive.Write(_data.Path);
    _data.File("inbox/one.txt", "URL: https://www.linkedin.com/jobs/view/1\nCompany: Bank of Example\nTitle: Senior .NET Developer\nLocation: Toronto, ON\nSaved: 2026-09-10\n\nWe need C#, SQL Server and Azure DevOps.\n");
    await using var db = await pg.OpenAsync();

    await Service(db).RunAsync(_data.Path, CancellationToken.None);

    var runs = await db.Sightings.Where(s => s.PostingId == "inbox:one.txt").Select(s => s.RunId).ToListAsync();
    Assert.Equal(new[] { FixtureArchive.Run2 }, runs);
}
```

(Use the names that file already uses for the temp dir, fixture and service factory.) Run: FAIL (no such overload).

- [ ] **Step 6: Inbox ageing.** Replace the body of `InboxReader`:

```csharp
// data/inbox/*.txt: header lines (URL/Company/Title/Location/Saved), a blank line, then the posting text.
// A pasted job is seen once: in the first run on or after its Saved date, so it ages out of the window like a
// collected posting. Without a readable Saved date it counts as seen in the latest run for as long as the file exists.
public static class InboxReader
{
    public static InboxResult ReadAll(string inboxDir, IReadOnlyList<ArchiveRun> runs)
    {
        if (!Directory.Exists(inboxDir)) return new InboxResult([], []);
        var postings = new List<RawPosting>();
        var warnings = new List<string>();
        foreach (var file in Directory.EnumerateFiles(inboxDir, "*.txt").Order(StringComparer.Ordinal))
        {
            var (posting, fileWarnings) = Parse(Path.GetFileName(file), File.ReadAllText(file), runs);
            if (posting is not null) postings.Add(posting);
            warnings.AddRange(fileWarnings);
        }
        return new InboxResult(postings, warnings);
    }

    // For checking a file before it is written: the run does not matter.
    public static (RawPosting? Posting, string? Warning) Parse(string fileName, string content, string runId)
    {
        var (posting, warnings) = Parse(fileName, content, [new ArchiveRun(runId, DateTimeOffset.MaxValue, "")]);
        return (posting, warnings.FirstOrDefault());
    }

    public static (RawPosting? Posting, IReadOnlyList<string> Warnings) Parse(string fileName, string content, IReadOnlyList<ArchiveRun> runs)
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
            return (null, [$"Inbox file {fileName} was skipped: it needs a 'Title:' line, a blank line, then the posting text."]);

        var warnings = new List<string>();
        var missing = new[] { "Company", "Location" }.Where(h => !headers.ContainsKey(h)).ToList();
        if (missing.Count > 0) warnings.Add($"Inbox file {fileName} has no {string.Join(" or ", missing)} line.");

        var ordered = runs.OrderBy(r => r.StartedAt).ToList();
        var runId = ordered.Count == 0 ? "" : ordered[^1].RunId;
        if (DateOnly.TryParseExact(headers.GetValueOrDefault("Saved", ""), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var saved))
            runId = ordered.FirstOrDefault(r => DateOnly.FromDateTime(r.StartedAt.Date) >= saved)?.RunId ?? runId;
        else
            warnings.Add($"Inbox file {fileName} has no 'Saved: yyyy-MM-dd' line, so it never ages out of the market data.");

        return (new RawPosting("inbox", fileName, runId, headers.GetValueOrDefault("Company", "Unknown"), title,
            headers.GetValueOrDefault("URL", ""), headers.GetValueOrDefault("Location", ""), Remote: null, Country: null,
            body, IsFullText: true, Salary: null), warnings);
    }
}
```

Add `using System.Globalization;`. The string-`runId` wrapper passes one run dated `MaxValue`, so any Saved date maps to it.

Callers:
- `RebuildService`: `InboxReader.ReadAll(Path.Combine(dataRoot, "inbox"), runs)`.
- `Processor/Program.cs` report-only branch: `InboxReader.ReadAll(Path.Combine(dataRoot, "inbox"), archiveRuns).Warnings`.
- `DbMarketSource.Warnings()`: `InboxReader.ReadAll(Path.Combine(settings.DataRoot, "inbox"), runs).Warnings`.

Existing tests `Parse_reads_headers_then_body` (no Saved line, asserts a null warning) and `Missing_optional_headers...`: add `Saved: 2026-09-01` to the first one's headers; the second keeps passing (`Contains`). Run `dotnet test tests/Tracker.Core.Tests`. Expected: PASS.

- [ ] **Step 7: Shared database-error rule (B1).** Create `tests/Tracker.Core.Tests/Data/DatabaseErrorsTests.cs`:

```csharp
using System.Net.Sockets;
using Npgsql;
using Tracker.Data;

namespace Tracker.Core.Tests.Data;

public class DatabaseErrorsTests
{
    private static PostgresException Pg(string state) => new("x", "ERROR", "ERROR", state);

    [Theory]
    [InlineData("28P01")] // wrong password
    [InlineData("3D000")] // database does not exist
    [InlineData("57P03")] // starting up
    public void Login_missing_database_and_startup_are_outages(string state) => Assert.True(DatabaseErrors.IsUnavailable(Pg(state)));

    [Theory]
    [InlineData("42P01")] // undefined table
    [InlineData("42P07")] // duplicate table
    [InlineData("23505")] // unique violation
    public void Other_server_answers_are_not(string state) =>
        Assert.False(DatabaseErrors.IsUnavailable(new InvalidOperationException("wrapped", Pg(state))));

    [Fact]
    public void Connection_failures_are_outages()
    {
        Assert.True(DatabaseErrors.IsUnavailable(new NpgsqlException("refused", new SocketException())));
        Assert.True(DatabaseErrors.IsUnavailable(new TimeoutException()));
        Assert.False(DatabaseErrors.IsUnavailable(new InvalidOperationException()));
    }
}
```

Run: FAIL (type missing in Tracker.Data). Move `src/Api/DatabaseErrors.cs` to `src/Tracker.Core/Data/DatabaseErrors.cs` with `namespace Tracker.Data;` (body unchanged), add `using Tracker.Data;` to the three API callers where missing. Run `dotnet build` then both test projects. Expected: PASS, 0 warnings.

- [ ] **Step 8: Commit.** `git add -A && git commit -m "Core rules: specialty software titles, company key, pasted jobs age out, shared database-error rule"`

---

### Task 2: Processor exit codes and tests (B1, B2)

**Files:**
- Create: `src/Processor/ProcessorApp.cs`; shrink `src/Processor/Program.cs`
- Create: `tests/Processor.Tests/Processor.Tests.csproj`, `tests/Processor.Tests/ProcessorAppTests.cs`; add the project to the solution file
- Modify: README test commands (Task 8 covers wording)

**Interfaces:**
- Consumes: `Tracker.Data.DatabaseErrors.IsUnavailable`, `InboxReader.ReadAll(dir, runs)` from Task 1.
- Produces: `ProcessorApp.RunAsync(string[] args, string? root, string? connectionString, ILoggerFactory loggers, TextWriter error, CancellationToken ct) : Task<int>` and constants `ProcessorApp.Ok = 0, Failed = 1, ConfigInvalid = 2, DatabaseUnavailable = 3`.

- [ ] **Step 1: Test project.** `tests/Processor.Tests/Processor.Tests.csproj`: copy `tests/Tracker.Core.Tests/Tracker.Core.Tests.csproj`, with project references to `..\..\src\Processor\Processor.csproj` and `..\Tracker.TestSupport\Tracker.TestSupport.csproj`. Add `<Project Path="tests/Processor.Tests/Processor.Tests.csproj" />` to the solution's `/tests/` folder.

- [ ] **Step 2: Failing tests.** `tests/Processor.Tests/ProcessorAppTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Tracker.TestSupport;

namespace Processor.Tests;

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

// The whole program, minus finding the repo root and the connection string. Never touches the network or the Archiver.
[Collection("postgres")]
[Trait("Category", "Integration")]
public sealed class ProcessorAppTests(PostgresFixture pg) : IDisposable
{
    private readonly TempDir _root = new();
    private readonly StringWriter _error = new();

    private string Root(bool withProfile = true)
    {
        foreach (var file in new[] { "sources.json", "processor.json", "skills.json" })
            File.Copy(RepoConfig.PathOf(file), _root.File($"config/{file}", ""), overwrite: true);
        if (withProfile) File.Copy(RepoConfig.PathOf("profile.example.json"), _root.File("config/profile.json", ""), overwrite: true);
        FixtureArchive.Write(Path.Combine(_root.Path, "data"));
        return _root.Path;
    }

    private async Task<string> FreshDatabaseAsync()
    {
        var name = "processor_" + Guid.NewGuid().ToString("N")[..12];
        await using var conn = new NpgsqlConnection(pg.ConnectionString);
        await conn.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", conn);
        await create.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Database = name }.ConnectionString;
    }

    private Task<int> Run(string? root, string? connection, params string[] args) =>
        ProcessorApp.RunAsync(args, root, connection, NullLoggerFactory.Instance, _error, CancellationToken.None);

    [Fact]
    public async Task An_unknown_argument_is_a_config_error_with_usage()
    {
        Assert.Equal(ProcessorApp.ConfigInvalid, await Run(Root(), pg.ConnectionString, "--fast"));
        Assert.Contains("Usage: Processor [--report-only]", _error.ToString());
    }

    [Fact]
    public async Task No_repo_root_is_a_config_error() => Assert.Equal(ProcessorApp.ConfigInvalid, await Run(null, pg.ConnectionString));

    [Fact]
    public async Task A_missing_profile_names_the_example()
    {
        Assert.Equal(ProcessorApp.ConfigInvalid, await Run(Root(withProfile: false), pg.ConnectionString));
        Assert.Contains("profile.example.json", _error.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task No_connection_string_is_a_config_error(string? connection)
    {
        Assert.Equal(ProcessorApp.ConfigInvalid, await Run(Root(), connection));
        Assert.Contains("ConnectionStrings:Tracker", _error.ToString());
    }

    [Fact]
    public async Task An_unreachable_database_exits_3_with_the_docker_hint()
    {
        Assert.Equal(ProcessorApp.DatabaseUnavailable, await Run(Root(), "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2"));
        Assert.Contains("docker compose up", _error.ToString());
    }

    // B1: the server answered, so this is not an outage.
    [Fact]
    public async Task A_database_that_refuses_the_migration_exits_1_not_3()
    {
        var connection = await FreshDatabaseAsync();
        await using (var conn = new NpgsqlConnection(connection))
        {
            await conn.OpenAsync();
            await using var clash = new NpgsqlCommand("CREATE TABLE runs (x int)", conn);
            await clash.ExecuteNonQueryAsync();
        }

        Assert.Equal(ProcessorApp.Failed, await Run(Root(), connection));
        Assert.Contains("Database error", _error.ToString());
        Assert.DoesNotContain("docker compose", _error.ToString());
    }

    [Fact]
    public async Task Report_only_on_an_empty_database_says_to_rebuild_first()
    {
        Assert.Equal(ProcessorApp.Failed, await Run(Root(), await FreshDatabaseAsync(), "--report-only"));
        Assert.Contains("without --report-only", _error.ToString());
    }

    [Fact]
    public async Task A_rebuild_then_report_only_both_succeed_and_write_the_report()
    {
        var root = Root();
        var connection = await FreshDatabaseAsync();
        var latest = Path.Combine(root, "data", "reports", "latest.html");

        Assert.Equal(ProcessorApp.Ok, await Run(root, connection));
        Assert.True(File.Exists(latest));
        File.Delete(latest);
        Assert.Equal(ProcessorApp.Ok, await Run(root, connection, "--report-only"));
        Assert.True(File.Exists(latest));
        Assert.Equal("", _error.ToString());
    }

    public void Dispose() => _root.Dispose();
}
```

Run `dotnet test tests/Processor.Tests`. Expected: build FAILS (`ProcessorApp` missing).

- [ ] **Step 3: ProcessorApp.** Create `src/Processor/ProcessorApp.cs` holding the current `Program.cs` logic with these changes: the root, connection string, logger factory, error writer and token are parameters; `TrackerDb` is created with `new TrackerDb(new DbContextOptionsBuilder<TrackerDb>(TrackerDbFactory.Options(connectionString)).UseLoggerFactory(loggers).Options)`; `RebuildService` is constructed directly with `loggers.CreateLogger<RebuildService>()`; every `Console.Error.WriteLine` becomes `error.WriteLine`; cancellation checks use `ct`.

```csharp
public static class ProcessorApp
{
    public const int Ok = 0, Failed = 1, ConfigInvalid = 2, DatabaseUnavailable = 3;
    private const string Usage = "Usage: Processor [--report-only]";

    public static async Task<int> RunAsync(string[] args, string? root, string? connectionString, ILoggerFactory loggers,
        TextWriter error, CancellationToken ct)
    {
        // 1. arguments, 2. root, 3. config files, 4. connection string: as in the old Program.cs, in that order.
        // 5. migrate:
        try
        {
            await db.Database.MigrateAsync(ct);
        }
        catch (Exception ex) when (DatabaseErrors.IsUnavailable(ex))
        {
            error.WriteLine($"Database unavailable: {ErrorText.Chain(ex)}");
            error.WriteLine("Start it with: docker compose up -d --wait postgres (Docker Desktop must be running).");
            return DatabaseUnavailable;
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or Microsoft.EntityFrameworkCore.DbUpdateException or InvalidOperationException)
        {
            error.WriteLine($"Database error: {ErrorText.Chain(ex)}");
            return Failed;
        }
        // 6. rebuild or report-only, analysis, report: unchanged.
    }
}
```

The comment lines above stand for code moved verbatim from `Program.cs` (lines 19–65 and 88–119 of the current file); the private `IsDatabaseUnavailable` is deleted. `Program.cs` becomes:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tracker.Configuration;

var root = RepoRoot.Find(Directory.GetCurrentDirectory()) ?? RepoRoot.Find(AppContext.BaseDirectory);
var configuration = new ConfigurationBuilder().AddUserSecrets<Program>(optional: true).AddEnvironmentVariables().Build();
using var loggers = LoggerFactory.Create(b => b.AddConsole().AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning));
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
return await ProcessorApp.RunAsync(args, root, configuration.GetConnectionString("Tracker"), loggers, Console.Error, cts.Token);
```

Run `dotnet test tests/Processor.Tests`. Expected: PASS (9 cases). Run `dotnet build` for 0 warnings.

- [ ] **Step 4: Commit.** `git add -A && git commit -m "Processor: exit 3 only for an unreachable database; tests for --report-only and exit codes"`

---

### Task 3: API market side (A2 in the API, A4 filters, B3, B4, B5)

**Files:**
- Modify: `src/Api/Queries/MetaQueries.cs`, `SkillQueries.cs`, `PostingQueries.cs`, `SalaryQueries.cs`, `Filters.cs`; `src/Api/Endpoints/MarketEndpoints.cs`; `src/Api/Market/MarketData.cs`
- Test: `tests/Api.Tests/Queries/*Tests.cs`, `tests/Api.Tests/Endpoints/EndpointTests.cs`, `tests/Api.Tests/Market/MarketDataTests.cs`, `tests/Api.Tests/TestSupport/Fakes.cs`

**Interfaces:**
- Consumes: `CompanyName.Key`, `CompanyName.Display` (Task 1).
- Produces: `FilterParser.Skill(string? skill, SkillCatalog catalog) : string?` (throws `FilterException("skill", ...)` for an unknown name); `MarketData` constructor gains an optional last parameter `IHostApplicationLifetime? lifetime = null`; `ScriptedSource.LastToken`.

- [ ] **Step 1: Failing query tests.** Using each test file's existing `Data.P` / `Data.MarketOf` helpers:

```csharp
// MetaQueriesTests
[Fact]
public void Company_variants_are_listed_once()
{
    var data = Data.MarketOf([Data.P("a", ["C#"], [0], company: "Acme"), Data.P("b", ["C#"], [0], company: "Acme Inc."), Data.P("c", ["C#"], [0], company: "Acme Inc.")], 0);

    Assert.Equal(new[] { "Acme Inc." }, MetaQueries.Meta(data, Data.Settings, stale: false).Companies);
}

// PostingQueriesTests
[Theory]
[InlineData("Acme")]
[InlineData("  acme inc. ")]
public void The_company_filter_matches_every_spelling(string company)
{
    var data = Data.MarketOf([Data.P("a", ["C#"], [0], company: "Acme"), Data.P("b", ["C#"], [0], company: "Acme Inc."), Data.P("c", ["C#"], [0], company: "Globex")], 0);

    Assert.Equal(2, PostingQueries.Page(data, AllFilters, null, null, company, PostingSort.Newest, 1, 25, Data.Settings.Profile).Total);
}

// SalaryQueriesTests: the same two spellings give Postings == 2 from SalaryQueries.View(data, AllFilters, null, company, Data.Settings).
// SkillQueriesTests: Detail(...).Companies for "C#" is [("Acme Inc.", 2)] when companies are "Acme", "Acme Inc.", "Acme Inc.".

// FiltersTests
[Theory]
[InlineData("where", "all,gta")]
[InlineData("text", "full,snippet")]
[InlineData("sort", "newest,match")]
public void A_comma_joined_single_value_is_refused(string name, string value)
{
    var query = new QueryCollection(new Dictionary<string, StringValues> { [name] = value });

    var error = Assert.Throws<FilterException>(() =>
    {
        FilterParser.Parse(query, Data.Settings.Profile);
        FilterParser.Text(query, TextKind.Full);
        FilterParser.Sort(query);
    });
    Assert.Equal(name, error.Parameter);
}
```

(`AllFilters` stands for the filter set those files already build for "every family, all history".) Endpoint test in `EndpointTests`:

```csharp
[Fact]
public async Task An_unknown_skill_filter_is_a_400_on_postings_and_salaries()
{
    foreach (var path in new[] { "/api/postings?skill=Cobol", "/api/salaries?skill=Cobol" })
    {
        var response = await api.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("skill", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("parameter").GetString());
    }
}
```

Run `dotnet test tests/Api.Tests --filter "Company|comma|unknown_skill"`. Expected: FAIL.

- [ ] **Step 2: Implement.**

`Filters.cs`, in `FilterParser`:

```csharp
// A skill filter must name a skill in config/skills.json.
public static string? Skill(string? skill, SkillCatalog catalog) =>
    string.IsNullOrEmpty(skill) || catalog.Skills.Any(s => s.Name == skill)
        ? skill
        : throw new FilterException("skill", $"Unknown skill '{skill}'.");
```

and in `ParseEnum`, `ParseFamily`, `ParseSeniority` add `&& !value.Contains(',')` (`raw` in `ParseEnum`) to the accept condition; `ParseFamily` checks the comma before `TitleRules.ParseFamily`.

`MetaQueries.Meta` companies:

```csharp
[.. data.Postings.GroupBy(p => CompanyName.Key(p.Company), StringComparer.Ordinal)
    .Select(g => (Name: CompanyName.Display(g.Select(p => p.Company)), Count: g.Count()))
    .OrderByDescending(c => c.Count).ThenBy(c => c.Name, StringComparer.Ordinal)
    .Take(MaxCompanies).Select(c => c.Name)],
```

`SkillQueries.Detail` companies argument:

```csharp
[.. withSkill.GroupBy(p => CompanyName.Key(p.Company), StringComparer.Ordinal)
    .Select(g => new NamedCount(CompanyName.Display(g.Select(p => p.Company)), g.Count()))
    .OrderByDescending(n => n.Count).ThenBy(n => n.Name, StringComparer.Ordinal).Take(MaxRelated)],
```

and `Top` loses its comparer parameter.

`PostingQueries.Page` and `SalaryQueries.View`: before the query, `var companyKey = string.IsNullOrWhiteSpace(company) ? null : CompanyName.Key(company);` and the filter becomes `.Where(p => companyKey is null || CompanyName.Key(p.Company) == companyKey)`. `SalaryQueries.View` replaces its own unknown-skill check with `skill = FilterParser.Skill(skill, settings.Catalog);`. `MarketEndpoints` `/postings`: `skill = FilterParser.Skill(skill, settings.Catalog);` before `ReadAsync`.

Run: PASS.

- [ ] **Step 3: Failing MarketData tests (B3, B4).** In `Fakes.cs`, `ScriptedSource` records the token: add `public CancellationToken LastToken { get; private set; }` and set it at the top of `LoadIfChangedAsync`. In `MarketDataTests`:

```csharp
[Fact]
public async Task A_reload_that_fails_for_another_reason_keeps_the_data_and_waits_a_minute()
{
    var market = Create();
    _source.ThenLoad("v1", 2).Then(_ => Task.FromException<MarketLoad?>(new IOException("inbox file busy"))).ThenUnchanged();

    var first = await market.GetAsync(default);
    _clock.Now += MarketData.CheckInterval;
    var failed = await market.GetAsync(default);
    _clock.Now += TimeSpan.FromSeconds(1);
    var soonAfter = await market.GetAsync(default);

    Assert.Same(first.Data, failed.Data);
    Assert.Equal((true, true, 2), (failed.Stale, soonAfter.Stale, _source.Calls));
    _clock.Now += MarketData.CheckInterval;
    Assert.False((await market.GetAsync(default)).Stale);
}

// Review focus 3.
[Fact]
public async Task A_cold_start_that_fails_for_another_reason_still_fails_the_request()
{
    var market = Create();
    _source.Then(_ => Task.FromException<MarketLoad?>(new IOException("inbox file busy"))).ThenLoad("v1", 1);

    await Assert.ThrowsAsync<IOException>(() => market.GetAsync(default));
    Assert.Single((await market.GetAsync(default)).Data.Postings);
}

[Fact]
public async Task A_caller_that_goes_away_does_not_cancel_the_shared_load()
{
    var market = Create();
    var release = new TaskCompletionSource();
    _source.Then(async _ =>
    {
        await release.Task;
        return ScriptedSource.Load("v1", 3);
    });
    using var caller = new CancellationTokenSource();

    var request = market.GetAsync(caller.Token);
    caller.Cancel();

    Assert.False(_source.LastToken.IsCancellationRequested);
    release.SetResult();
    Assert.Equal(3, (await request).Data.Postings.Count);
}
```

Run: the first and third FAIL.

- [ ] **Step 4: MarketData.** Constructor: add `IHostApplicationLifetime? lifetime = null` and `_stopping = lifetime?.ApplicationStopping ?? CancellationToken.None;`. In `GetAsync`: `_state = state = await RefreshAsync(state, _stopping);` (the gate waits keep `ct`). In `RefreshAsync`, between the two existing catches:

```csharp
catch (Exception ex) when (current is not null && ex is not OperationCanceledException)
{
    // Not a database outage (an inbox file being saved, say). Keep serving and try again after the normal interval,
    // not on every request.
    activity?.AddException(ex);
    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    _log.LogError(ex, "Market data reload failed; serving the data loaded at {LoadedAt}", current.Data.LoadedAt);
    return current with { CheckedAt = _clock.GetUtcNow(), Stale = true };
}
```

Update the class comment: the reload runs under the application's stopping token. Run `dotnet test tests/Api.Tests`. Expected: PASS.

- [ ] **Step 5: Commit.** `git add -A && git commit -m "API market: company key everywhere, unknown skill is a 400, reloads back off and outlive their caller"`

---

### Task 4: API tracker side (A4 tracker, B6–B12)

**Files:**
- Modify: `src/Api/Applications/Rules.cs`, `MirrorStore.cs`, `TrackerMirror.cs`, `TrackerSchema.cs`, `TrackerGate.cs`, `TrackerListEndpoints.cs`, `TrackerViews.cs`, `EntryText.cs`
- Test: `tests/Api.Tests/Applications/RulesTests.cs`, `MirrorStoreTests.cs`, `TrackerMirrorTests.cs`, `TrackerRestoreTests.cs`, `TrackerListTests.cs`, new `TrackerRaceTests.cs`

**Interfaces:**
- Produces:
  - `enum MarketState { Counted, Pending, OutOfScope, Duplicate }`; `ApplicationDetail` gains `MarketState? MarketState` right after `Market`
  - `MirrorStore.IsUnreadable(Exception) : bool`
  - `TrackerSchema.Reset()`
  - `TrackerListEndpoints.CommitNewEntryAsync(ApplicationsDb db, TrackerMirror mirror, JobApplication entry, string operation, CancellationToken ct) : Task`

- [ ] **Step 1: Rules tests (A4, B6).** In `RulesTests`:

```csharp
[Fact]
public void A_comma_joined_enum_value_is_refused() =>
    Assert.Equal("status", Assert.Throws<TrackerInputException>(() => Rules.ParseEnum<ApplicationStatus>("Shortlisted,Applied", "status")).Field);

[Fact]
public void A_nul_character_is_refused_in_any_text_field()
{
    Assert.Equal("title", Assert.Throws<TrackerInputException>(() => Rules.Required("Dev\0", "title")).Field);
    Assert.Contains("NUL", Assert.Throws<TrackerInputException>(() => Rules.Optional("a\0b", "notes", Rules.MaxLongText)).Message);
}
```

In `TrackerListTests`:

```csharp
[Fact]
public async Task A_nul_character_in_a_pasted_title_is_a_400()
{
    var response = await tracker.Client.PostAsJsonAsync("/api/tracker/applications/paste", new
    {
        title = "Dev\0eloper", company = "Nulco", url = "", location = "Toronto, ON", postingText = "C#", status = "Shortlisted",
    });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Equal("title", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("field").GetString());
}
```

Run: FAIL. Then in `Rules.cs`: `ParseEnum` accept condition gains `&& !text.Contains(',')`; `Limit` starts with

```csharp
if (text.Contains('\0')) throw new TrackerInputException(field, $"{Label(field)} contains a NUL character, which can't be saved.");
```

and `NoNul` becomes `=> Limit(value, field, int.MaxValue)`. Run: PASS.

- [ ] **Step 2: Backup errors name the file (B7).** Tests:

```csharp
// MirrorStoreTests
[Fact]
public void A_file_the_process_may_not_open_counts_as_unreadable()
{
    Assert.True(MirrorStore.IsUnreadable(new UnauthorizedAccessException()));
    Assert.True(MirrorStore.IsUnreadable(new IOException()));
    Assert.False(MirrorStore.IsUnreadable(new InvalidOperationException()));
}

// TrackerRestoreTests
[Fact]
public async Task A_backup_the_database_refuses_names_the_file_and_is_kept()
{
    var sample = MirrorStoreTests.Sample();
    sample.FollowUps[0].ApplicationId = Guid.NewGuid(); // points at no entry: the restore's insert fails
    MirrorStore.Write(MirrorPath, sample);
    var before = File.ReadAllText(MirrorPath);
    await using var factory = new ApiFactory(await FreshDatabaseAsync(), _data.Path);

    var response = await factory.CreateClient().GetAsync("/api/tracker/status");

    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("Tracker backup unreadable", problem.GetProperty("title").GetString());
    Assert.Contains("tracker.json", problem.GetProperty("detail").GetString());
    Assert.Equal(before, File.ReadAllText(MirrorPath));
}
```

Run: FAIL. Implement: `MirrorStore`

```csharp
public static bool IsUnreadable(Exception ex) => ex is JsonException or IOException or NotSupportedException or UnauthorizedAccessException;
```

used in `Read`'s `catch (Exception ex) when (IsUnreadable(ex))`. `TrackerMirror.RestoreIfEmptyAsync`: wrap from `BeginTransactionAsync` to `CommitAsync` in

```csharp
try { /* existing inserts, SaveChanges, Commit */ }
catch (DbUpdateException ex) when (!DatabaseErrors.IsUnavailable(ex))
{
    db.ChangeTracker.Clear();
    throw new TrackerBackupException(
        $"{FilePath} can't be restored ({ex.InnerException?.Message ?? ex.Message}). Fix or move the file; the tracker stays closed until then so the copy isn't overwritten.", ex);
}
```

Run: PASS.

- [ ] **Step 3: Backup span and one snapshot (B8, B11).** In `TrackerMirrorTests`:

```csharp
private sealed class IsolationSpy : DbTransactionInterceptor
{
    public List<IsolationLevel> Seen { get; } = [];

    public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
        DbTransaction result, CancellationToken cancellationToken = default)
    {
        Seen.Add(result.IsolationLevel);
        return ValueTask.FromResult(result);
    }
}

[Fact]
public async Task The_copy_is_read_from_one_snapshot_inside_a_traced_span()
{
    await tracker.GetAsync<JsonElement>("/api/tracker/status");
    var spy = new IsolationSpy();
    var spans = new ConcurrentBag<Activity>();
    using var listener = new ActivityListener
    {
        ShouldListenTo = s => s.Name == TrackerTelemetry.Name,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        ActivityStopped = spans.Add,
    };
    ActivitySource.AddActivityListener(listener);
    await using var db = new ApplicationsDb(new DbContextOptionsBuilder<ApplicationsDb>(ApplicationsDb.Options(tracker.ConnectionString)).AddInterceptors(spy).Options);
    var mirror = Mirror();

    await mirror.WriteAsync(db);

    Assert.Null(mirror.LastBackupError);
    Assert.Equal(new[] { IsolationLevel.RepeatableRead }, spy.Seen);
    Assert.Contains(spans, s => s.OperationName == "tracker.backup" && s.Status != ActivityStatusCode.Error);
}

[Fact]
public async Task A_failed_copy_marks_its_span_as_an_error()
{
    // as A_failed_copy_is_reported_without_throwing, with the same listener: the "tracker.backup" span has Status Error.
}
```

(Write the second test in full with the listener block repeated.) Run: FAIL. Implement in `WriteAsync`:

```csharp
using var activity = TrackerTelemetry.Source.StartActivity("tracker.backup");
await _gate.WaitAsync();
try
{
    // One snapshot: a write landing between two of these reads can't leave the copy half old, half new.
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
    var file = /* the six reads, unchanged */;
    await tx.CommitAsync();
    MirrorStore.Write(FilePath, file);
    activity?.SetTag("tracker.applications", file.Applications.Count);
    ...
}
catch (Exception ex)
{
    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    ...
}
```

Run: PASS.

- [ ] **Step 4: Company and posting races (B9).** Create `tests/Api.Tests/Applications/TrackerRaceTests.cs`:

```csharp
[Collection("tracker")]
[Trait("Category", "Integration")]
public sealed class TrackerRaceTests(TrackerFixture tracker) : IDisposable
{
    private readonly TempDir _root = new();

    private TrackerMirror Mirror() => new(Data.Settings with { DataRoot = _root.Path }, tracker.Clock, NullLogger<TrackerMirror>.Instance);

    private JobApplication Entry(Company company, string? postingId) =>
        TrackerListEndpoints.CreateEntry(company, "Developer", "", "Toronto, ON", "C#", ApplicationStatus.Shortlisted, EntryOrigin.Pasted, postingId, tracker.Clock, "test");

    [Fact]
    public async Task Losing_the_race_to_create_a_company_uses_the_winners_company()
    {
        await tracker.GetAsync<JsonElement>("/api/tracker/status");
        await using var mine = tracker.OpenDb();
        await using var theirs = tracker.OpenDb();
        var myCompany = await TrackerListEndpoints.FindOrCreateCompanyAsync(mine, "Raceco", tracker.Clock, default);
        var entry = Entry(myCompany, null);
        mine.Applications.Add(entry);
        var winner = await TrackerListEndpoints.FindOrCreateCompanyAsync(theirs, "RaceCo", tracker.Clock, default);
        await theirs.SaveChangesAsync();

        await TrackerListEndpoints.CommitNewEntryAsync(mine, Mirror(), entry, "paste", default);

        await using var check = tracker.OpenDb();
        Assert.Equal(winner.Id, (await check.Applications.SingleAsync(a => a.Id == entry.Id)).CompanyId);
        Assert.Equal(1, await check.Companies.CountAsync(c => c.NameKey == "raceco"));
    }

    [Fact]
    public async Task Losing_the_race_to_shortlist_a_posting_is_a_conflict_with_the_winners_id()
    {
        await tracker.GetAsync<JsonElement>("/api/tracker/status");
        await using var mine = tracker.OpenDb();
        await using var theirs = tracker.OpenDb();
        var theirEntry = Entry(await TrackerListEndpoints.FindOrCreateCompanyAsync(theirs, "Shortco", tracker.Clock, default), "race:1");
        var myEntry = Entry(await TrackerListEndpoints.FindOrCreateCompanyAsync(mine, "Otherco", tracker.Clock, default), "race:1");
        mine.Applications.Add(myEntry);
        theirs.Applications.Add(theirEntry);
        await theirs.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<TrackerConflictException>(() => TrackerListEndpoints.CommitNewEntryAsync(mine, Mirror(), myEntry, "shortlist", default));

        Assert.Equal(theirEntry.Id, conflict.ExistingId);
    }

    public void Dispose() => _root.Dispose();
}
```

Run: FAIL (method missing). Implement in `TrackerListEndpoints` and call it from both POST handlers instead of `Tracking.CommitAsync`:

```csharp
// Saves a new entry. Two requests can pass the "already there?" checks at the same moment; the database's unique
// indexes decide. A lost company race is retried once with the winner's company; a lost posting race is a 409 that
// names the winner's entry. Anything else surfaces unchanged.
internal static async Task CommitNewEntryAsync(ApplicationsDb db, TrackerMirror mirror, JobApplication entry, string operation, CancellationToken ct)
{
    try
    {
        await Tracking.CommitAsync(db, mirror, operation, ct);
    }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
    {
        if (pg.ConstraintName == "ix_applications_posting_id" && entry.PostingId is { } postingId)
        {
            db.ChangeTracker.Clear();
            var existing = await db.Applications.AsNoTracking().Where(a => a.PostingId == postingId).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
            throw new TrackerConflictException("This posting is already in your tracker.", existing);
        }
        if (pg.ConstraintName != "ix_companies_name_key" || db.Entry(entry.Company!).State != EntityState.Added) throw;
        var mine = entry.Company!;
        db.Entry(mine).State = EntityState.Detached;
        var theirs = await db.Companies.FirstAsync(c => c.NameKey == mine.NameKey, ct);
        entry.Company = theirs;
        entry.CompanyId = theirs.Id;
        await Tracking.CommitAsync(db, mirror, operation, ct);
    }
}
```

Review focus 4 is the `throw;` branch and the unguarded second `CommitAsync`: add a third test where the unique violation is on an unrelated second save (two entries with the same posting id added in one context, company already saved) and assert `TrackerConflictException`. Run: PASS.

- [ ] **Step 5: Recover from lost tables (B10).** In `TrackerRestoreTests`:

```csharp
[Fact]
public async Task Tables_lost_while_the_api_runs_come_back_from_the_backup_on_the_next_request()
{
    var connection = await FreshDatabaseAsync();
    await using var factory = new ApiFactory(connection, _data.Path);
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(TrackerGate.ClientHeader, "tests");
    var pasted = await client.PostAsJsonAsync("/api/tracker/applications/paste", new
    {
        title = "Developer", company = "Lostco", url = "", location = "Toronto, ON", postingText = "C#", status = "Applied",
    });
    Assert.Equal(HttpStatusCode.Created, pasted.StatusCode);
    await using (var conn = new NpgsqlConnection(connection))
    {
        await conn.OpenAsync();
        await using var drop = new NpgsqlCommand(
            """DROP TABLE application_documents, contacts, follow_ups, application_events, applications, companies, "__applications_migrations" """, conn);
        await drop.ExecuteNonQueryAsync();
    }

    var first = await client.GetAsync("/api/tracker/status");
    var second = await client.GetFromJsonAsync<JsonElement>("/api/tracker/status");

    Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
    Assert.Contains("try again", (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    Assert.Equal(1, second.GetProperty("counts").GetProperty("Applied").GetInt32());
}
```

Run: FAIL (500 twice). Implement: `TrackerSchema.Reset() => _ready = false;` and in `TrackerGate.InvokeAsync`:

```csharp
await schema.EnsureReadyAsync(context.HttpContext.RequestAborted);
try
{
    return await next(context);
}
catch (Exception ex) when (TablesMissing(ex))
{
    // The tables were there when this API started and are gone now (a lost Docker volume). The next request
    // recreates them and restores from the backup copy.
    schema.Reset();
    return Results.Problem(statusCode: 503, title: "Tracker tables missing",
        detail: "The tracker's tables were missing, so they are being recreated from the backup copy. Please try again.");
}
```

```csharp
private static bool TablesMissing(Exception ex)
{
    for (var e = ex; e is not null; e = e.InnerException)
        if (e is PostgresException { SqlState: PostgresErrorCodes.UndefinedTable }) return true;
    return false;
}
```

Run: PASS.

- [ ] **Step 6: Market state (B12).** In `TrackerListTests`:

```csharp
[Fact]
public async Task The_entry_says_where_its_pasted_job_stands_in_the_market_data()
{
    var counted = await tracker.PasteAsync("Stateco");
    var outside = await tracker.PostAsync<NewEntry>("/api/tracker/applications/paste", new
    {
        title = "Developer", company = "Farco", url = "", location = "Bellevue, Washington", postingText = "C#", status = "Shortlisted",
    });

    Assert.Equal(MarketState.Pending, (await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{counted.Id}")).MarketState);
    await tracker.RebuildAsync();

    Assert.Equal(MarketState.Counted, (await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{counted.Id}")).MarketState);
    Assert.Equal(MarketState.OutOfScope, (await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{outside.Id}")).MarketState);
}
```

Run: FAIL. Implement: `TrackerViews.cs` adds `public enum MarketState { Counted, Pending, OutOfScope, Duplicate }` and the `MarketState? MarketState` member after `MarketFacts? Market`. In `EntryText.cs` add to `EntryTexts`:

```csharp
// Where the entry's posting stands after the latest rebuild. Pending: no rebuild has seen it yet.
public static async Task<MarketState?> StateAsync(JobApplication entry, bool inSnapshot, TrackerDb marketDb, CancellationToken ct)
{
    if (entry.PostingId is not { } postingId) return null;
    if (inSnapshot) return MarketState.Counted;
    try
    {
        var row = await marketDb.Postings.AsNoTracking().Where(p => p.Id == postingId)
            .Select(p => new { p.InScope, p.DuplicateOf }).FirstOrDefaultAsync(ct);
        return row is null ? MarketState.Pending
            : row.DuplicateOf is not null ? MarketState.Duplicate
            : row.InScope ? MarketState.Counted : MarketState.OutOfScope;
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return MarketState.Pending;
    }
}
```

and the detail endpoint passes `await EntryTexts.StateAsync(entry, posting is not null, marketDb, ct)`. Run `dotnet test tests/Api.Tests`. Expected: PASS.

- [ ] **Step 7: Commit.** `git add -A && git commit -m "API tracker: NUL and comma input, named backup errors, traced one-snapshot backup, create races, lost-table recovery, market state"`

---

### Task 5: CV (B14, B17, B18, B19)

**Files:**
- Modify: `src/Api/Cv/CvDocx.cs`, `src/Api/Cv/MasterCvFile.cs`
- Test: `tests/Api.Tests/Cv/CvDocxTests.cs`, `MasterCvTests.cs`, `CvEndpointTests.cs`

- [ ] **Step 1: Docx tests.** In `CvDocxTests`:

```csharp
private static WordprocessingDocument Render(string text) => Open(CvDocx.Render([CvParagraph.Plain(CvStyle.Paragraph, text)]));

[Fact]
public void Line_breaks_in_a_paragraph_become_word_line_breaks()
{
    using var doc = Render("First line.\r\nSecond line.\nThird line.");
    var paragraph = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().Single();

    Assert.Equal(new[] { "First line.", "Second line.", "Third line." }, paragraph.Descendants<Text>().Select(t => t.Text));
    Assert.Equal(2, paragraph.Descendants<Break>().Count());
    Assert.Empty(Errors(doc));
}

// Review focus 5.
[Theory]
[InlineData("\n\n")]
[InlineData("Ends with a break.\n")]
public void Stray_line_breaks_never_make_an_invalid_document(string text)
{
    using var doc = Render(text);
    Assert.Empty(Errors(doc));
    Assert.DoesNotContain(doc.MainDocumentPart!.Document!.Body!.Descendants<Text>(), t => t.Text.Contains('\n'));
}

[Fact]
public void Half_an_emoji_is_dropped_and_whole_ones_are_kept()
{
    using var doc = Render("Half \uD83D here, low \uDE00 there, whole \U0001F600.");

    Assert.Equal("Half  here, low  there, whole \U0001F600.", doc.MainDocumentPart!.Document!.Body!.InnerText);
    Assert.Empty(Errors(doc));
}
```

Run: FAIL (one Text with LF; the surrogate test throws or keeps the halves).

- [ ] **Step 2: CvDocx.** Replace `TextOf` and its three call sites:

```csharp
// A run's content: text, with a Word line break for each line break in the source.
private static IEnumerable<OpenXmlElement> Content(string value)
{
    var lines = Clean(value).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    for (var i = 0; i < lines.Length; i++)
    {
        if (i > 0) yield return new Break();
        if (lines[i].Length > 0) yield return new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve };
    }
}

// Characters XML can't hold are dropped: control characters from a pasted summary, and half of a surrogate pair.
// Whole pairs (emoji) are kept.
private static string Clean(string value)
{
    var kept = new StringBuilder(value.Length);
    for (var i = 0; i < value.Length; i++)
    {
        var c = value[i];
        if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) kept.Append(c).Append(value[++i]);
        else if (!char.IsSurrogate(c) && XmlConvert.IsXmlChar(c)) kept.Append(c);
    }
    return kept.ToString();
}

private static Run RunOf(string text, RunProperties? properties = null)
{
    var run = new Run();
    if (properties is not null) run.Append(properties);
    run.Append(Content(text));
    return run;
}
```

Call sites: `new Hyperlink(RunOf(run.Text, new RunProperties(new RunStyle { Val = "Hyperlink" }))) { Id = link.Id }` and `paragraph.Append(RunOf(run.Text, run.Bold ? new RunProperties(new Bold()) : null))`. Run the whole `CvDocxTests`. Expected: PASS, including `Body_text_reads_in_page_order`.

- [ ] **Step 3: Date-order tests (B17).** In `MasterCvTests`:

```csharp
[Fact]
public void An_end_before_its_start_and_a_project_end_without_a_start_are_problems()
{
    var load = LoadText("""
        { "person": { "name": "Sam", "email": "sam@example.com" }, "headline": "Developer",
          "summaries": [ { "id": "s", "text": "Hi." } ],
          "roles": [ { "id": "r", "title": "Dev", "company": "Co", "start": "2023-10", "end": "2021-05", "bullets": [ { "id": "b", "text": "Did work." } ] } ],
          "projects": [ { "id": "p", "title": "Tool", "end": "2024-01", "bullets": [ { "id": "pb", "text": "Built it." } ] } ] }
        """);

    Assert.Equal(new[] { "roles[0].end: 2021-05 is before the start, 2023-10.", "projects[0].start is required when end is set." }, load.Problems);
}
```

Run: FAIL. In `ReadSection` after reading `end`:

```csharp
if (start is { } from && end is { } to && to < from) Problem($"{path}.end: {to:yyyy-MM} is before the start, {from:yyyy-MM}.");
if (start is null && end is not null && !role) Problem($"{path}.start is required when end is set.");
```

Run: PASS.

- [ ] **Step 4: Integration test gaps (B19).** In `CvEndpointTests`:

```csharp
[Fact]
public async Task The_same_bullet_twice_is_refused()
{
    var entry = await PasteAsync("Twiceco", "We need C#.");

    var response = await Generate(entry.Id, new { headline = "Dev", summary = "", bulletIds = new[] { "acme-lead", "acme-lead" } });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Equal("bulletIds", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("field").GetString());
    Assert.False(Directory.Exists(FolderOf(entry.Id)));
}

[Fact]
public async Task Text_saved_on_a_market_entry_wins_over_the_collected_posting()
{
    var entry = await tracker.PostAsync<NewEntry>("/api/tracker/applications", new { postingId = "adzuna:A1" });
    var detail = await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{entry.Id}");
    var saved = await tracker.Client.PutAsJsonAsync($"/api/tracker/applications/{entry.Id}/posting-text",
        new { version = detail.Version, postingText = "The full posting: Kubernetes, Kafka and React." });
    Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);

    var proposal = await tracker.GetAsync<CvProposal>($"/api/tracker/applications/{entry.Id}/cv");

    Assert.Equal("entry", proposal.TextSource);
    Assert.Contains(proposal.PostingSkills, s => s.Skill == "Kubernetes");
}

[Fact]
public async Task A_path_like_company_name_gives_a_safe_file_name()
{
    var entry = await PasteAsync(@"..\..\Evil/Co: <CV>", "We need C#.");

    var response = await Generate(entry.Id, new { headline = "Dev", summary = "", bulletIds = Chosen });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var made = (await response.Content.ReadFromJsonAsync<GeneratedCv>(TrackerFixture.Json))!;
    Assert.Equal("Alex Example - CV - ....EvilCo CV.docx", made.FileName);
    var file = Assert.Single(Directory.GetFiles(FolderOf(entry.Id)));
    Assert.Equal(FolderOf(entry.Id), Path.GetDirectoryName(file));
}
```

These pin behaviour that should already hold. If one fails, that is a real defect: fix it test-first and ledger a ruling. If the market entry test clashes with another test that shortlists `adzuna:A1`, use the id that test file leaves free and ledger it. Run `dotnet test tests/Api.Tests`. Expected: PASS.

- [ ] **Step 5: Commit.** `git add -A && git commit -m "CV: Word line breaks, lone surrogates dropped, date order checked, three integration tests"`

---

### Task 6: Dashboard, market pages (B20, B21, B22, B25, B26, B27)

**Files:**
- Modify: `src/Dashboard/src/api.ts`, `src/pages/Postings.tsx`, `src/pages/SkillDetail.tsx` (comment only), `vite.config.ts`
- Test: `src/api.test.ts`, `src/pages/Postings.test.tsx`, new `src/pages/Overview.test.tsx`, new `src/viteConfig.test.ts`

- [ ] **Step 1: Failing tests.**

`api.test.ts`:

```ts
it('says the API is not reachable when the dev proxy answers 500 without problem details', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 500 })));
  await expect(api.meta()).rejects.toMatchObject({ status: 500, message: expect.stringMatching(/not reachable/) });
});

it('keeps the status for other errors without a body', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 404 })));
  await expect(api.meta()).rejects.toMatchObject({ message: 'The API answered 404.' });
});
```

`Postings.test.tsx` (add `Link` from `react-router` to the imports):

```tsx
it('follows the address when you go back or forward', async () => {
  mockApi({ '/api/meta': META, '/api/postings': page(1) });
  renderRoute('/postings?q=bank', <><Postings /><Link to="/postings?q=azure">history</Link></>);
  expect(await screen.findByLabelText('Title or company')).toHaveValue('bank');

  fireEvent.click(screen.getByRole('link', { name: 'history' }));

  await waitFor(() => expect(screen.getByLabelText('Title or company')).toHaveValue('azure'));
});

it('goes back to the last page when the results shrink', async () => {
  mockApi({ '/api/meta': META, '/api/postings': page(3, 30) });
  renderRoute('/postings?page=3', <Postings />);

  expect(await screen.findByText('Page 2 of 2')).toBeInTheDocument();
  await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/postings?page=2'));
});
```

`Overview.test.tsx`:

```tsx
import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ReportModel } from '../api';
import { mockApi, renderRoute } from '../test/render';
import { Overview } from './Overview';

const REPORT: ReportModel = {
  generatedAt: '2026-10-01T12:00:00Z', latestRunId: '2026-10-01_0700', latestRunAt: '2026-10-01T11:00:00Z', windowDays: 30,
  snapshot: {
    activeNow: 40, newSinceLastRun: 5, inWindow: 120, fullText: 80, snippets: 40, withSalary: 60, citizenshipFlagged: 3, runs: 3, historyDays: 30,
    bySource: { greenhouse: 80, adzuna: 40 },
  },
  topSkills: [{ skill: 'Python', category: 'language', fullTextShare: 0.4, snippetShare: 0.1, count: 32, level: 'None' }],
  gaps: [{ skill: 'CI/CD', category: 'devops', demand: 0.3, level: 'Learning', score: 0.2 }],
  strengths: [{ skill: 'C#', category: 'language', demand: 0.2, level: 'Strong', score: 0.2 }],
  salaries: [],
  companies: [{ company: 'Acme Inc.', postings: 7 }],
  matches: [{
    title: 'Senior .NET Developer', company: 'Acme Inc.', url: 'https://x.test/1', location: 'Toronto', score: 0.75, skillCount: 4,
    missing: ['Kubernetes'], salary: { min: 130000, max: 150000, period: 'Year' }, citizenshipOrClearance: true, postingId: 'greenhouse:101',
  }],
  trends: null,
  warnings: ['Inbox file a.txt has no Location line.'],
};

describe('Overview page', () => {
  it('shows the snapshot, rankings, matches, companies and warnings', async () => {
    mockApi({ '/api/overview': { report: REPORT }, '/api/tracker/index': {} });
    renderRoute('/?where=gta', <Overview />);

    expect(await screen.findByRole('heading', { name: 'Last 30 days' })).toBeInTheDocument();
    expect(screen.getByText('120')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'CI/CD' })).toHaveAttribute('href', '/skills/CI%2FCD?where=gta');
    expect(screen.getByRole('link', { name: 'Senior .NET Developer' })).toHaveAttribute('href', 'https://x.test/1');
    expect(screen.getByText(/citizenship\/clearance/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Acme Inc\./ })).toHaveAttribute('href', '/postings?where=gta&company=Acme+Inc.');
    expect(screen.getByText('Inbox file a.txt has no Location line.')).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Shortlist "Senior .NET Developer"' })).toBeInTheDocument();
  });

  it('says when nothing has been collected yet', async () => {
    mockApi({ '/api/overview': { report: null } });
    renderRoute('/', <Overview />);
    expect(await screen.findByText(/No data yet/)).toBeInTheDocument();
  });

  it('says when the filters match nothing', async () => {
    mockApi({ '/api/overview': { report: { ...REPORT, snapshot: { ...REPORT.snapshot, inWindow: 0 }, warnings: [] } } });
    renderRoute('/', <Overview />);
    expect(await screen.findByText(/No postings match these filters/)).toBeInTheDocument();
  });
});
```

`viteConfig.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import config from '../vite.config';

// Only this PC's browser, on the dashboard's own address, may talk to the dev server: no other host name (DNS
// rebinding) and no other website (CORS). Pinned here so a Vite upgrade can't change it silently.
describe('dev server', () => {
  it('pins host, allowed hosts and CORS', () => {
    expect(config.server).toMatchObject({ host: 'localhost', allowedHosts: [], cors: false, strictPort: true });
  });
});
```

Run `npm test`. Expected: the API, Postings and config tests FAIL; the Overview tests pass or fail only on test details (they pin existing behaviour: fix the test, not the page, unless the page is wrong).

- [ ] **Step 2: Implement.**

`api.ts`:

```ts
const UNREACHABLE = 'The API is not reachable. Is the dashboard (scripts/dashboard.cmd) running?';
```

used in the `catch` of `fetch`, and in the `!response.ok` branch:

```ts
let message = '';
...
if (text) message = text;
...
// The dev proxy answers 500 with no problem details when the API itself is down.
if (!message) message = response.status >= 500 ? UNREACHABLE : `The API answered ${response.status}.`;
```

`Postings.tsx`:

```tsx
const [draft, setDraft] = useState(q);
const [seenQ, setSeenQ] = useState(q);
if (q !== seenQ) { // the address changed under the box (back, forward, a link): show what is being searched
  setSeenQ(q);
  setDraft(q);
}
...
const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
const shown = Math.min(page, pages);
useEffect(() => {
  // The results shrank while this page was open (a new run, a narrower filter): fall back to the last page.
  if (data && page > pages) {
    setParams((current) => {
      const out = new URLSearchParams(current);
      if (pages > 1) out.set('page', String(pages)); else out.delete('page');
      return out;
    }, { replace: true });
  }
}, [data, page, pages, setParams]);
```

and the pager uses `shown` for the text and both `disabled` checks and both clicks. `vite.config.ts` server block:

```ts
server: {
  host: 'localhost',
  port: Number(process.env.PORT ?? 5173),
  strictPort: true,
  allowedHosts: [], // localhost and IP addresses only (Vite's default, pinned)
  cors: false, // the dashboard calls /api on its own origin; no other site needs to read this server
  proxy: { '/api': { target: apiUrl, changeOrigin: true } },
},
```

`SkillDetail.tsx` comment: `// React Router decodes the path segment, %2F included, so "CI/CD" arrives whole. Decoding once more is harmless and covers a name that was encoded twice.`

Run `npm test && npm run build`. Expected: PASS, build clean. If Aspire's dashboard start needs a different `host` (check `scripts/dashboard.cmd` starts and `/` answers through Aspire's proxy during the Task 8 live check), keep `allowedHosts` and `cors` and ledger the ruling.

- [ ] **Step 3: Commit.** `git add -A && git commit -m "Dashboard market pages: API-down message, search box follows history, page clamp, Overview tests, pinned dev-server settings"`

---

### Task 7: Dashboard, tracker pages (A3 wording, B12, B13, B15, B16, B23, B24)

**Files:**
- Modify: `src/Dashboard/src/tracker/api.ts`, `ShortlistButton.tsx`, `SaveError.tsx`, `TrackerPage.tsx`, `useTrackerMutation.ts`, `entry/EntryPage.tsx`, `cv/tailor.ts`, `cv/CoveragePanel.tsx`, `cv/TailorPage.tsx`
- Test: `ShortlistButton.test.tsx`, `entry/EntryPage.test.tsx`, `TrackerPage.test.tsx`, `cv/TailorPage.test.tsx`, `cv/tailor.test.ts`

**Interfaces:**
- Consumes: `marketState` on the entry detail (Task 4).
- Produces: `ApplicationDetail.marketState: 'Counted' | 'Pending' | 'OutOfScope' | 'Duplicate' | null`; `findable(proposal, chosen): Set<string>` in `tailor.ts`; `useTrackerMutation(write, onDone?, onFail?)`.

- [ ] **Step 1: Failing tests.**

`ShortlistButton.test.tsx`:

```tsx
it('shows why a posting could not be shortlisted', async () => {
  mockApi({ '/api/tracker/index': {}, 'POST /api/tracker/applications': failure(404, "Posting 'x:9' isn't in the current market data.") });
  renderRoute('/postings', <ShortlistButton postingId="x:9" title="Gone Job" />);

  const button = await screen.findByRole('button', { name: 'Shortlist "Gone Job"' });
  await waitFor(() => expect(button).toBeEnabled());
  fireEvent.click(button);

  expect(await screen.findByRole('alert')).toHaveTextContent("isn't in the current market data");
});
```

`EntryPage.test.tsx` (add `marketState: 'Counted'` to `ENTRY`):

```tsx
it('offers no Reload when a company rename clashes with another company', async () => {
  mockApi({
    '/api/tracker/applications/a1': ENTRY,
    'PATCH /api/tracker/companies/c1': failure(409, "Another company in your tracker is already called 'TD'.", { id: 'c2' }),
  });
  open();

  fireEvent.change(await screen.findByLabelText('Company name'), { target: { value: 'TD' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save company' }));

  expect(await screen.findByText(/already called 'TD'/)).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Reload' })).not.toBeInTheDocument();
});

it.each([
  ['Pending', /joins after the next rebuild/],
  ['OutOfScope', /outside the GTA and remote-Canada/],
  ['Duplicate', /under another posting of the same job/],
] as const)('explains a pasted job that is %s in the market data', async (marketState, text) => {
  mockApi({ '/api/tracker/applications/a1': { ...ENTRY, origin: 'Pasted', postingId: 'inbox:2027-02-25-rbc-dev.txt', market: null, marketState } });
  open();
  expect(await screen.findByText(text)).toBeInTheDocument();
});

it('says when a counted pasted job was pasted, not whether it is live', async () => {
  mockApi({ '/api/tracker/applications/a1': { ...ENTRY, origin: 'Pasted', postingId: 'inbox:2027-02-25-rbc-dev.txt' } });
  open();

  expect(await screen.findByText(/75% skill match · pasted/)).toBeInTheDocument();
  expect(screen.queryByText(/still live|not in the latest run/)).not.toBeInTheDocument();
});
```

`TrackerPage.test.tsx`: one row with `postingId: 'inbox:x.txt', live: false` shows "Pasted" and not "Not in latest run" (build it with that file's existing row helper).

`tailor.test.ts`:

```ts
it('finds the skills an unticked bullet shows', () => {
  expect([...findable(PROPOSAL, ['a1', 'a2', 'g1'])]).toEqual(['Kubernetes']);
  expect(findable(PROPOSAL, ['a1', 'a2', 'a3', 'g1']).size).toBe(0);
});
```

`TailorPage.test.tsx`:

```tsx
const withAzureBullet = {
  ...PROPOSAL,
  sections: PROPOSAL.sections.map((s) => s.id !== 'acme' ? s : {
    ...s, bullets: s.bullets.map((b) => b.id === 'a3' ? { ...b, skills: ['Kubernetes', 'Azure'] } : b),
  }),
};

it('points a listed skill at the unticked bullet that proves it', async () => {
  mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': withAzureBullet });
  open();

  fireEvent.click(within(await screen.findByRole('heading', { level: 4, name: /^Listed/ }).then((h) => h.parentElement as HTMLElement)).getByRole('button', { name: 'Azure' }));

  expect(screen.getByRole('checkbox', { name: 'Moved services to Kubernetes.' }).closest('li')).toHaveClass('highlight');
});

it('keeps a listed skill as plain text when no unticked bullet shows it', async () => {
  mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': PROPOSAL });
  open();
  await screen.findByRole('checkbox', { name: 'Ran Docker in production.' });

  expect(within(group('Listed')).queryByRole('button', { name: 'Azure' })).not.toBeInTheDocument();
  expect(within(group('Listed')).getByText('Azure')).toBeInTheDocument();
});

it('drops the highlight once its skill is in a ticked bullet', async () => {
  mockApi({ '/api/tracker/cv': READY, '/api/tracker/applications/a1/cv': PROPOSAL });
  open();

  fireEvent.click(await screen.findByRole('button', { name: 'Kubernetes' }));
  const bullet = screen.getByRole('checkbox', { name: 'Moved services to Kubernetes.' });
  fireEvent.click(bullet);
  fireEvent.click(screen.getByRole('checkbox', { name: 'Moved services to Kubernetes.' }));

  expect(screen.getByRole('checkbox', { name: 'Moved services to Kubernetes.' }).closest('li')).not.toHaveClass('highlight');
});

it('drops a bullet that left cv.json and lets you generate again', async () => {
  let current = PROPOSAL;
  const withoutA2 = { ...PROPOSAL, sections: PROPOSAL.sections.map((s) => ({ ...s, bullets: s.bullets.filter((b) => b.id !== 'a2') })) };
  let refused = false;
  mockApi({
    '/api/tracker/cv': READY,
    '/api/tracker/applications/a1/cv': () => current,
    'POST /api/tracker/applications/a1/cv': () => {
      if (refused) return reply(201, { documentId: 'd9', fileName: 'CV.docx' });
      refused = true;
      current = withoutA2;
      return failure(400, "Bullet 'a2' isn't in config/cv.json any more. Reload the page to see your current CV.", { field: 'bulletIds' });
    },
  });
  open();

  fireEvent.click(await screen.findByRole('button', { name: 'Generate .docx' }));
  expect(await screen.findByText(/1 bullet you had ticked is no longer in cv.json/)).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Generate .docx' }));

  await waitFor(() => expect(startDownload).toHaveBeenCalled());
  expect(sentBody('POST /api/tracker/applications/a1/cv')).toMatchObject({ bulletIds: ['a1', 'g1'] });
});
```

(The third test ticks then unticks: after the tick the skill is "In a bullet" and the highlight must clear, and it must not come back when the bullet is unticked.) Run `npm test`. Expected: the new tests FAIL.

- [ ] **Step 2: Implement.**

`tracker/api.ts`: `export type MarketState = 'Counted' | 'Pending' | 'OutOfScope' | 'Duplicate';` and `marketState: MarketState | null;` on `ApplicationDetail` after `market`.

`ShortlistButton.tsx`: return a fragment with the button (without `title`) followed by `{add.error && <span className="field-error" role="alert">{add.error.message}</span>}`.

`SaveError.tsx`:

```tsx
// A version conflict (someone saved first) is fixed by reloading. A conflict that names another record (`id`), such
// as a company name already in use, is not: the user has to change what they typed.
const conflict = error instanceof ApiError && error.status === 409 && typeof error.details.id !== 'string';
```

`EntryPage.tsx` facts:

```tsx
const pasted = entry.postingId?.startsWith('inbox:') ?? false;
const STATE_TEXT = {
  Pending: "Not in the market data yet: it joins after the next rebuild if it's in the GTA or remote in Canada.",
  OutOfScope: 'Not counted in the market data: the rebuild placed it outside the GTA and remote-Canada roles.',
  Duplicate: 'Counted in the market data under another posting of the same job.',
} as const;
...
{entry.market ? (
  <span>
    {pct(entry.market.match)} skill match · {pasted ? `pasted ${dateText(entry.market.firstSeen)}` : entry.market.live ? 'still live' : 'not in the latest run'}
    {!pasted && <> · first seen {dateText(entry.market.firstSeen)}</>}
  </span>
) : entry.marketState && entry.marketState !== 'Counted' ? (
  <span>{STATE_TEXT[entry.marketState]}</span>
) : null}
```

(`STATE_TEXT` is a module constant.) The existing "shows the entry with its market facts" test still reads "75% skill match · still live · first seen …".

`TrackerPage.tsx` posting cell: after the `null` case add `: r.postingId.startsWith('inbox:') ? <span className="muted">Pasted</span>`.

`useTrackerMutation.ts`: third parameter `onFail?: (error: Error, input: TInput) => void`, passed as `onError: onFail`.

`tailor.ts`:

```ts
// Skills an unticked bullet shows: their "Listed" and "Available" chips can point at those bullets.
export function findable(proposal: CvProposal, chosen: string[]): Set<string> {
  const picked = new Set(chosen);
  return new Set(proposal.sections.flatMap((s) => s.bullets).filter((b) => !picked.has(b.id)).flatMap((b) => b.skills));
}
```

`CoveragePanel.tsx`: new prop `findable: Set<string>`; the Listed hint becomes `'Only your skills list or a certification shows it. A button means an unticked bullet shows it too: click to find it.'`; the chip is a button when `(state === 'available' || state === 'listed') && findable.has(s.skill)`.

`TailorPage.tsx`, in `Tailor`:

```tsx
const queryClient = useQueryClient();
const known = new Set(proposal.sections.flatMap((s) => s.bullets.map((b) => b.id)));
// A bullet can leave cv.json while this page is open; the refetched proposal no longer has it.
const current = { ...choices, bulletIds: choices.bulletIds.filter((id) => known.has(id)) };
const gone = choices.bulletIds.length - current.bulletIds.length;
const generate = useTrackerMutation(
  (picked: CvChoices) => tracker.generateCv(proposal.entryId, picked),
  (result) => { onMade({ ...result, entryId: proposal.entryId }); startDownload(tracker.documentUrl(result.documentId)); },
  () => void queryClient.invalidateQueries({ queryKey: ['tracker', 'cv', 'proposal', proposal.entryId] }),
);
const states = coverage(proposal, current.bulletIds);
const pointable = findable(proposal, current.bulletIds);
const active = highlight && pointable.has(highlight) && states.get(highlight) !== 'bullet' ? highlight : null;
const highlighted = active ? bulletsShowing(proposal, active) : new Set<string>();
```

Every later use of `choices` for reading (`length`, `summaryId`, `SectionPicker choices=`, `generate.mutate`) uses `current`; setters spread `current`. `CoveragePanel` gets `findable={pointable}` and `highlight={active}`. Below the buttons:

```tsx
{gone > 0 ? (
  <div className="banner warn" role="status">
    {gone} {gone === 1 ? 'bullet' : 'bullets'} you had ticked {gone === 1 ? 'is' : 'are'} no longer in cv.json and {gone === 1 ? 'was' : 'were'} removed. Check the CV, then generate again.
  </div>
) : <SaveError error={generate.error} />}
```

Run `npm test && npm run build`. Expected: PASS.

- [ ] **Step 3: Commit.** `git add -A && git commit -m "Dashboard tracker pages: visible shortlist errors, honest pasted-job wording, listed skills point at bullets, stale bullets dropped"`

---

### Task 8: Docs and whole-suite check (B28)

**Files:**
- Modify: `README.md`
- Create: `docs/decisions/0014-pasted-jobs-age-and-company-key.md`

- [ ] **Step 1: README.** In Setup, before the dashboard test command: "Run `npm ci` in `src/Dashboard` once on a fresh clone." Add `dotnet test tests/Processor.Tests` beside the other test commands. Add a "Fixes batch (October 2026)" section with one line per user-visible change: specialty software titles count as software engineer; company spellings are grouped; pasted jobs age out (and need a `Saved:` line to do so); the Processor's exit code 3 means "database unreachable" only.

- [ ] **Step 2: ADR 0014.** Context: pasted jobs never aged and inflated "active now"; company spellings split counts. Decision: one sighting in the first run on or after the Saved date; `CompanyName.Key` built on the deduplicator's normalisation. Alternatives considered: keep pasted jobs live for a fixed 30 days (assumes a posting lifetime nobody measured); age by file timestamp (changes when files are copied or restored). Consequences: a pasted job leaves "best matches" after the next collection; files without `Saved` warn in the report; two different companies whose names differ only by "Inc"/"Ltd"/"The" merge.

- [ ] **Step 3: Whole suite.** Run all four `dotnet test` projects, `dotnet build` (0 warnings), `npm test`, `npm run build`. Expected: all PASS.

- [ ] **Step 4: Commit.** `git add -A && git commit -m "Docs: fixes batch in the README, ADR 0014"`

---

## After the last task

1. Final whole-branch review by a fresh reviewer on the most capable model (worktree isolation; it must not run the Archiver or read private config), then one fix pass.
2. Live check on the owner's database, API on a spare port: Overview, a Tailor page, the tracker list.
3. Ask before merging. After the merge: note the pre-merge `--report-only` total, re-run `scripts/register-task.ps1`, run the published Processor (full rebuild, never the Archiver), and report the new total with the SQL counts from the spec's "Checking the numbers" section.
