# Phase 0 Raw Archiver Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A .NET 10 console app that archives raw job-market snapshots from Adzuna, public ATS job boards and Job Bank open data. Windows Task Scheduler runs it twice a week.

**Architecture:** A Generic Host console app. Three independent `IArchiveSource` implementations run in sequence under an `ArchiveRunner`. Each saves raw HTTP bodies atomically into a per-run folder under `data/raw/` and records every request in a thread-safe `ManifestBuilder`. The manifest is written as `manifest.json`, and the process exit code is derived from the per-source outcomes.

**Tech Stack:** .NET 10, Microsoft.Extensions.Hosting, Microsoft.Extensions.Http.Resilience, System.Text.Json, xUnit, PowerShell ScheduledTasks.

**Spec:** `docs/superpowers/specs/2026-09-27-phase0-raw-archiver-design.md`

## Global Constraints

- Target framework `net10.0`; solution file `JobMarketTracker.slnx`.
- Everything the app writes goes under `data/`, which is gitignored. Nothing under `data/` is ever committed.
- The Adzuna `app_key` and `app_id` must never appear in `manifest.json`, in log output or in any file in the repo. Credentials come only from .NET user-secrets (`Adzuna:AppId`, `Adzuna:AppKey`) or env vars `ADZUNA__APPID` / `ADZUNA__APPKEY`. (Adzuna's own `redirect_url` values inside saved response bodies contain the app_id. That is accepted: it's Adzuna's data, and `data/` is gitignored.)
- Adzuna: at most **80 calls per run** (`MaxCallsPerRun`), `results_per_page=50`, at most **4 pages** per query, at least **3000 ms** between calls. The aggregate endpoints (`histogram`, `top_companies`) are planned first so the budget never cuts them.
- Boards: at most **4** concurrent requests.
- Job Bank: English CSV resources only. Keep the newest **12** months on disk, gzip-compressed, and never re-download a month already present.
- Response bodies are saved byte-for-byte (Job Bank CSVs byte-for-byte inside gzip). Every write is atomic (`*.tmp` then rename).
- Exit codes: `0` means at least one source succeeded or partially succeeded; `1` means every source failed or was skipped; `2` means invalid configuration or arguments.
- User-Agent on every request: `JobMarketTracker/0.1 (personal job-market research)`.

## Review Focus

1. **Wrong or expired Adzuna key (HTTP 401 on every call).** Paging must stop after the first failed page of each query, the source must be `failed`, and the manifest must not contain the key. Owner: Task 5.
2. **Network drops halfway through a 48 MB Job Bank download.** No `.csv.gz` and no `.tmp` may remain, and the next run must retry that month. Owner: Task 4 (stream failure) and Task 7 (month not counted as on disk).
3. **An exception message that contains a full request URL.** It must be scrubbed of `app_id`/`app_key` values before it reaches the manifest. Owner: Task 3.
4. **Two runs started in the same minute,** e.g. the scheduled run plus a manual one. The second run must not overwrite the first. Owner: Task 1.
5. **A typo in `config/sources.json`:** unknown ATS, duplicate term or board, out-of-range number. The run must exit `2` with a readable message, before any HTTP call. Owner: Task 2 (validator) and Task 9 (exit code).

---

## File Structure

```
job-market-tracker/
  JobMarketTracker.slnx
  .gitignore                                  (exists; Task 10 adds .artifacts/)
  config/sources.json                         Task 2
  src/Archiver/
    Archiver.csproj                           Task 1 (Task 9: Version, UserSecretsId)
    Program.cs                                Task 9  CLI parsing, host, exit codes
    HttpSetup.cs                              Task 9  named HttpClients + resilience
    ArchiveRunner.cs                          Task 8  runs sources, writes manifest
    Inbox.cs                                  Task 8  data/inbox README + count
    Configuration/SourcesOptions.cs           Task 2  options classes
    Configuration/SourcesValidator.cs         Task 2
    Configuration/RepoRoot.cs                 Task 2
    Storage/Slug.cs                           Task 1
    Storage/RunFolder.cs                      Task 1
    Storage/AtomicFile.cs                     Task 1
    Manifest/ManifestModels.cs                Task 3  records, enums, exit codes, outcome rules
    Manifest/UrlRedactor.cs                   Task 3
    Manifest/ManifestBuilder.cs               Task 3
    Manifest/ManifestWriter.cs                Task 3
    Sources/IArchiveSource.cs                 Task 4  IArchiveSource, RunContext, SourceResult
    Sources/HttpArchiver.cs                   Task 4  GET-and-save / streamed gzip download
    Sources/AdzunaQueryPlan.cs                Task 5
    Sources/AdzunaSource.cs                   Task 5
    Sources/BoardUrls.cs                      Task 6
    Sources/JobBoardSource.cs                 Task 6
    Sources/JobBankCatalog.cs                 Task 7
    Sources/JobBankSource.cs                  Task 7
  tests/Archiver.Tests/
    TestSupport/TempDir.cs                    Task 1
    TestSupport/StubHandler.cs                Task 4
    TestSupport/TestRun.cs                    Task 4
    Storage/*Tests.cs                         Task 1
    Configuration/*Tests.cs                   Task 2
    Manifest/*Tests.cs                        Task 3
    Sources/*Tests.cs                         Tasks 4–7
    ArchiveRunnerTests.cs                     Task 8
  scripts/register-task.ps1                   Task 10
  docs/decisions/0001..0003-*.md              Task 10
  README.md                                   Task 10
```

All commands run from the repo root unless stated otherwise.

---

### Task 1: Solution scaffold and storage primitives

**Files:**
- Create: `JobMarketTracker.slnx`, `src/Archiver/Archiver.csproj`, `tests/Archiver.Tests/Archiver.Tests.csproj` (via `dotnet new`)
- Create: `src/Archiver/Storage/Slug.cs`, `src/Archiver/Storage/RunFolder.cs`, `src/Archiver/Storage/AtomicFile.cs`
- Create: `tests/Archiver.Tests/TestSupport/TempDir.cs`
- Test: `tests/Archiver.Tests/Storage/SlugTests.cs`, `RunFolderTests.cs`, `AtomicFileTests.cs`

**Interfaces:**
- Produces:
  - `Slug.From(string text) : string`
  - `RunFolder.Create(string dataRoot, DateTimeOffset startedLocal, bool dryRun) : RunFolder`
  - `RunFolder` members: `DataRoot`, `RunId`, `FullPath`, `DryRunId` (const), `AdzunaSearchFile(term, scope, page)`, `AdzunaAggregateFile(endpoint, term)`, `BoardFile(ats, slug)`, `JobBankCatalogFile()`, `ManifestFile()`, `Relative(fullPath)`
  - `AtomicFile.WriteAsync(string path, byte[] content, CancellationToken)` and `AtomicFile.WriteAsync(string path, Func<Stream, CancellationToken, Task> write, CancellationToken)`
  - `TempDir` (test helper: `Path`, `Dispose()`)

- [ ] **Step 1: Scaffold the solution**

```bash
dotnet new sln -n JobMarketTracker
dotnet new console -n Archiver -o src/Archiver -f net10.0
dotnet new xunit -n Archiver.Tests -o tests/Archiver.Tests -f net10.0
dotnet sln add src/Archiver tests/Archiver.Tests
dotnet add tests/Archiver.Tests reference src/Archiver
dotnet add src/Archiver package Microsoft.Extensions.Hosting
dotnet add src/Archiver package Microsoft.Extensions.Http.Resilience
rm tests/Archiver.Tests/UnitTest1.cs
```

Expected: `JobMarketTracker.slnx` exists and `dotnet build` succeeds.

- [ ] **Step 2: Write the test helper and failing tests**

`tests/Archiver.Tests/TestSupport/TempDir.cs`:
```csharp
namespace Archiver.Tests.TestSupport;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "archiver-tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}
```

`tests/Archiver.Tests/Storage/SlugTests.cs`:
```csharp
using Archiver.Storage;

namespace Archiver.Tests.Storage;

public class SlugTests
{
    [Theory]
    [InlineData(".net developer", "net-developer")]
    [InlineData("c# developer", "csharp-developer")]
    [InlineData("Senior  Software Engineer", "senior-software-engineer")]
    [InlineData("C++", "cplusplus")]
    [InlineData("  tech lead!! ", "tech-lead")]
    [InlineData("!!!", "")]
    public void From_makes_file_safe_slugs(string input, string expected) =>
        Assert.Equal(expected, Slug.From(input));
}
```

`tests/Archiver.Tests/Storage/RunFolderTests.cs`:
```csharp
using Archiver.Storage;
using Archiver.Tests.TestSupport;

namespace Archiver.Tests.Storage;

public class RunFolderTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 7, 0, 5, TimeSpan.FromHours(-4));

    [Fact]
    public void Create_names_folder_by_local_start_minute()
    {
        using var tmp = new TempDir();
        var folder = RunFolder.Create(tmp.Path, Start, dryRun: false);

        Assert.Equal("2026-09-29_0700", folder.RunId);
        Assert.True(Directory.Exists(Path.Combine(tmp.Path, "raw", "2026-09-29_0700")));
    }

    [Fact]
    public void Second_run_in_same_minute_gets_seconds_suffix()
    {
        using var tmp = new TempDir();
        RunFolder.Create(tmp.Path, Start, dryRun: false);
        var second = RunFolder.Create(tmp.Path, Start, dryRun: false);

        Assert.Equal("2026-09-29_070005", second.RunId);
    }

    [Fact]
    public void Dry_run_uses_fixed_folder_and_clears_it()
    {
        using var tmp = new TempDir();
        var first = RunFolder.Create(tmp.Path, Start, dryRun: true);
        File.WriteAllText(Path.Combine(first.FullPath, "stale.json"), "{}");

        var second = RunFolder.Create(tmp.Path, Start, dryRun: true);

        Assert.Equal(RunFolder.DryRunId, second.RunId);
        Assert.False(File.Exists(Path.Combine(second.FullPath, "stale.json")));
    }

    [Fact]
    public void File_paths_follow_the_spec_layout()
    {
        using var tmp = new TempDir();
        var f = RunFolder.Create(tmp.Path, Start, dryRun: false);

        Assert.Equal("raw/2026-09-29_0700/adzuna/search/net-developer-gta-p2.json", f.Relative(f.AdzunaSearchFile(".net developer", "gta", 2)));
        Assert.Equal("raw/2026-09-29_0700/adzuna/histogram/tech-lead-gta.json", f.Relative(f.AdzunaAggregateFile("histogram", "tech lead")));
        Assert.Equal("raw/2026-09-29_0700/boards/greenhouse-faire.json", f.Relative(f.BoardFile("greenhouse", "faire")));
        Assert.Equal("raw/2026-09-29_0700/jobbank/package_show.json", f.Relative(f.JobBankCatalogFile()));
        Assert.Equal("raw/2026-09-29_0700/manifest.json", f.Relative(f.ManifestFile()));
    }
}
```

`tests/Archiver.Tests/Storage/AtomicFileTests.cs`:
```csharp
using Archiver.Storage;
using Archiver.Tests.TestSupport;

namespace Archiver.Tests.Storage;

public class AtomicFileTests
{
    [Fact]
    public async Task WriteAsync_creates_directories_and_leaves_no_tmp()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "a", "b", "x.json");

        await AtomicFile.WriteAsync(path, "hi"u8.ToArray(), CancellationToken.None);

        Assert.Equal("hi", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Failed_write_leaves_neither_file_nor_tmp()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "x.json");

        await Assert.ThrowsAsync<IOException>(() => AtomicFile.WriteAsync(path, async (stream, ct) =>
        {
            await stream.WriteAsync("partial"u8.ToArray(), ct);
            throw new IOException("boom");
        }, CancellationToken.None));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test`
Expected: build FAILS because `Archiver.Storage` types don't exist yet.

- [ ] **Step 4: Implement the storage primitives**

`src/Archiver/Storage/Slug.cs`:
```csharp
using System.Text;

namespace Archiver.Storage;

public static class Slug
{
    // Lowercase letters/digits joined by single dashes; '#' and '+' are spelled out so "c#" and "c" stay distinct.
    public static string From(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (ch == '#') sb.Append("sharp");
            else if (ch == '+') sb.Append("plus");
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}
```

`src/Archiver/Storage/RunFolder.cs`:
```csharp
using System.Globalization;

namespace Archiver.Storage;

public sealed class RunFolder
{
    public const string DryRunId = "_dryrun";

    private RunFolder(string dataRoot, string runId)
    {
        DataRoot = dataRoot;
        RunId = runId;
        FullPath = Path.Combine(dataRoot, "raw", runId);
    }

    public string DataRoot { get; }
    public string RunId { get; }
    public string FullPath { get; }

    // Real runs are named by local start minute; a second run in the same minute gets seconds appended.
    // Dry runs always reuse data/raw/_dryrun and clear it first so stale files never mislead.
    public static RunFolder Create(string dataRoot, DateTimeOffset startedLocal, bool dryRun)
    {
        if (dryRun)
        {
            var dry = new RunFolder(dataRoot, DryRunId);
            if (Directory.Exists(dry.FullPath)) Directory.Delete(dry.FullPath, recursive: true);
            Directory.CreateDirectory(dry.FullPath);
            return dry;
        }

        var folder = new RunFolder(dataRoot, startedLocal.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture));
        if (Directory.Exists(folder.FullPath))
            folder = new RunFolder(dataRoot, startedLocal.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder.FullPath);
        return folder;
    }

    public string AdzunaSearchFile(string term, string scope, int page) =>
        Path.Combine(FullPath, "adzuna", "search", $"{Slug.From(term)}-{scope}-p{page}.json");

    public string AdzunaAggregateFile(string endpoint, string term) =>
        Path.Combine(FullPath, "adzuna", endpoint, $"{Slug.From(term)}-gta.json");

    public string BoardFile(string ats, string slug) =>
        Path.Combine(FullPath, "boards", $"{ats}-{Slug.From(slug)}.json");

    public string JobBankCatalogFile() => Path.Combine(FullPath, "jobbank", "package_show.json");

    public string ManifestFile() => Path.Combine(FullPath, "manifest.json");

    // Path relative to the data root with forward slashes, as stored in the manifest.
    public string Relative(string fullPath) => Path.GetRelativePath(DataRoot, fullPath).Replace('\\', '/');
}
```

`src/Archiver/Storage/AtomicFile.cs`:
```csharp
namespace Archiver.Storage;

// Writes to "<path>.tmp" then renames, so a crash never leaves a half-written file that looks complete.
public static class AtomicFile
{
    public static Task WriteAsync(string path, byte[] content, CancellationToken ct) =>
        WriteAsync(path, (stream, token) => stream.WriteAsync(content, token).AsTask(), ct);

    public static async Task WriteAsync(string path, Func<Stream, CancellationToken, Task> write, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await write(stream, ct);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            File.Delete(tmp);
            throw;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests in `SlugTests`, `RunFolderTests` and `AtomicFileTests` PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Scaffold solution and add storage primitives"
```

---

### Task 2: Configuration, validation and the committed source list

**Files:**
- Create: `src/Archiver/Configuration/SourcesOptions.cs`, `SourcesValidator.cs`, `RepoRoot.cs`
- Create: `config/sources.json`
- Test: `tests/Archiver.Tests/Configuration/SourcesValidatorTests.cs`, `RepoConfigTests.cs`

**Interfaces:**
- Consumes: `Slug.From` (Task 1)
- Produces:
  - `SourcesOptions { AdzunaOptions Adzuna; List<BoardConfig> Boards; JobBankOptions JobBank }`
  - `AdzunaOptions { string? AppId; string? AppKey; string BaseUrl; string Country; List<string> Terms; string GtaWhere; int GtaDistanceKm; int ResultsPerPage; int MaxPages; int MaxCallsPerRun; int MinDelayMs; bool HasCredentials }`
  - `BoardConfig { string Ats; string Slug }`
  - `JobBankOptions { string DatasetId; string CatalogUrl; int BackfillMonths }`
  - `SourcesValidator.Validate(SourcesOptions) : IReadOnlyList<string>` and `SourcesValidator.KnownAts`
  - `RepoRoot.Find(string startDirectory) : string?`

- [ ] **Step 1: Write the failing tests**

`tests/Archiver.Tests/Configuration/SourcesValidatorTests.cs`:
```csharp
using Archiver.Configuration;

namespace Archiver.Tests.Configuration;

public class SourcesValidatorTests
{
    private static SourcesOptions Valid() => new()
    {
        Adzuna = new AdzunaOptions { Terms = ["tech lead"] },
        Boards = [new BoardConfig { Ats = "greenhouse", Slug = "faire" }],
        JobBank = new JobBankOptions { DatasetId = "abc" },
    };

    [Fact]
    public void Valid_options_have_no_errors() => Assert.Empty(SourcesValidator.Validate(Valid()));

    [Fact]
    public void Empty_terms_are_reported()
    {
        var o = Valid();
        o.Adzuna.Terms.Clear();
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("Adzuna:Terms"));
    }

    [Fact]
    public void Terms_that_collide_as_file_names_are_reported()
    {
        var o = Valid();
        o.Adzuna.Terms.Add("Tech Lead!");
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("tech-lead"));
    }

    [Fact]
    public void Unknown_ats_is_reported()
    {
        var o = Valid();
        o.Boards.Add(new BoardConfig { Ats = "workday", Slug = "rbc" });
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("workday"));
    }

    [Fact]
    public void Duplicate_boards_are_reported()
    {
        var o = Valid();
        o.Boards.Add(new BoardConfig { Ats = "greenhouse", Slug = "Faire" });
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("more than once"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Results_per_page_must_be_1_to_50(int value)
    {
        var o = Valid();
        o.Adzuna.ResultsPerPage = value;
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("ResultsPerPage"));
    }

    [Fact]
    public void Missing_dataset_id_is_reported()
    {
        var o = Valid();
        o.JobBank.DatasetId = "";
        Assert.Contains(SourcesValidator.Validate(o), e => e.Contains("DatasetId"));
    }
}
```

`tests/Archiver.Tests/Configuration/RepoConfigTests.cs`:
```csharp
using Archiver.Configuration;
using Archiver.Tests.TestSupport;
using Microsoft.Extensions.Configuration;

namespace Archiver.Tests.Configuration;

public class RepoConfigTests
{
    [Fact]
    public void Committed_sources_json_binds_and_validates()
    {
        var root = RepoRoot.Find(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Repo root not found.");
        var options = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "config", "sources.json"))
            .Build()
            .Get<SourcesOptions>()!;

        Assert.Empty(SourcesValidator.Validate(options));
        Assert.Equal(7, options.Adzuna.Terms.Count);
        Assert.InRange(options.Boards.Count, 20, 40);
        Assert.Null(options.Adzuna.AppKey); // secrets are never committed
    }

    [Fact]
    public void Find_walks_up_to_the_folder_holding_config()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(Path.Combine(tmp.Path, "config"));
        File.WriteAllText(Path.Combine(tmp.Path, "config", "sources.json"), "{}");
        var deep = Directory.CreateDirectory(Path.Combine(tmp.Path, "src", "Archiver", "bin")).FullName;

        Assert.Equal(new DirectoryInfo(tmp.Path).FullName, RepoRoot.Find(deep));
    }

    [Fact]
    public void Find_returns_null_when_no_config_exists()
    {
        using var tmp = new TempDir();
        Assert.Null(RepoRoot.Find(tmp.Path));
    }
}
```

Note: `Find_returns_null_when_no_config_exists` assumes no `config/sources.json` exists in any parent of the temp folder, which holds for `%TEMP%`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: build FAILS because the `Archiver.Configuration` types don't exist.

- [ ] **Step 3: Implement options, validator and repo-root lookup**

`src/Archiver/Configuration/SourcesOptions.cs`:
```csharp
namespace Archiver.Configuration;

// Bound from config/sources.json; Adzuna AppId/AppKey arrive from user-secrets or env vars.
public sealed class SourcesOptions
{
    public AdzunaOptions Adzuna { get; set; } = new();
    public List<BoardConfig> Boards { get; set; } = [];
    public JobBankOptions JobBank { get; set; } = new();
}

public sealed class AdzunaOptions
{
    public string? AppId { get; set; }
    public string? AppKey { get; set; }
    public string BaseUrl { get; set; } = "https://api.adzuna.com/v1/api/jobs";
    public string Country { get; set; } = "ca";
    public List<string> Terms { get; set; } = [];
    public string GtaWhere { get; set; } = "Toronto";
    public int GtaDistanceKm { get; set; } = 50;
    public int ResultsPerPage { get; set; } = 50;
    public int MaxPages { get; set; } = 4;
    public int MaxCallsPerRun { get; set; } = 80;
    public int MinDelayMs { get; set; } = 3000;

    public bool HasCredentials => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(AppKey);
}

public sealed class BoardConfig
{
    public string Ats { get; set; } = "";
    public string Slug { get; set; } = "";
}

public sealed class JobBankOptions
{
    public string DatasetId { get; set; } = "";
    public string CatalogUrl { get; set; } = "https://open.canada.ca/data/api/action/package_show";
    public int BackfillMonths { get; set; } = 12;
}
```

`src/Archiver/Configuration/SourcesValidator.cs`:
```csharp
using Archiver.Storage;

namespace Archiver.Configuration;

public static class SourcesValidator
{
    public static readonly IReadOnlyList<string> KnownAts = ["greenhouse", "lever", "ashby"];

    public static IReadOnlyList<string> Validate(SourcesOptions options)
    {
        var errors = new List<string>();
        var a = options.Adzuna;

        if (a.Terms.Count == 0) errors.Add("Adzuna:Terms must list at least one search term.");
        foreach (var term in a.Terms.Where(t => Slug.From(t).Length == 0))
            errors.Add($"Adzuna term '{term}' has no letters or digits.");
        foreach (var dup in a.Terms.GroupBy(Slug.From).Where(g => g.Key.Length > 0 && g.Count() > 1))
            errors.Add($"Adzuna terms {string.Join(", ", dup.Select(t => $"'{t}'"))} would share the file name '{dup.Key}'.");
        if (a.ResultsPerPage is < 1 or > 50) errors.Add("Adzuna:ResultsPerPage must be between 1 and 50.");
        if (a.MaxPages < 1) errors.Add("Adzuna:MaxPages must be at least 1.");
        if (a.MaxCallsPerRun < 1) errors.Add("Adzuna:MaxCallsPerRun must be at least 1.");
        if (a.MinDelayMs < 0) errors.Add("Adzuna:MinDelayMs cannot be negative.");
        if (a.GtaDistanceKm < 1) errors.Add("Adzuna:GtaDistanceKm must be at least 1.");

        foreach (var b in options.Boards)
        {
            if (string.IsNullOrWhiteSpace(b.Slug)) errors.Add($"A '{b.Ats}' board entry has an empty slug.");
            if (!KnownAts.Contains(b.Ats)) errors.Add($"Board '{b.Slug}' has unknown ats '{b.Ats}' (expected greenhouse, lever or ashby).");
        }
        foreach (var dup in options.Boards.GroupBy(b => (b.Ats, Slug: b.Slug.ToLowerInvariant())).Where(g => g.Count() > 1))
            errors.Add($"Board {dup.Key.Ats}/{dup.Key.Slug} is listed more than once.");

        if (string.IsNullOrWhiteSpace(options.JobBank.DatasetId)) errors.Add("JobBank:DatasetId is required.");
        if (options.JobBank.BackfillMonths < 1) errors.Add("JobBank:BackfillMonths must be at least 1.");

        return errors;
    }
}
```

`src/Archiver/Configuration/RepoRoot.cs`:
```csharp
namespace Archiver.Configuration;

public static class RepoRoot
{
    // Walks up from startDirectory to the first folder containing config/sources.json.
    public static string? Find(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "config", "sources.json")))
                return dir.FullName;
        return null;
    }
}
```

- [ ] **Step 4: Create the committed source list**

Every board below was confirmed on 2026-09-27 to return HTTP 200 with at least 3 postings in Canada.

`config/sources.json`:
```json
{
  "Adzuna": {
    "Country": "ca",
    "Terms": [
      ".net developer",
      "c# developer",
      "full stack developer",
      "senior software engineer",
      "software engineer",
      "tech lead",
      "engineering manager"
    ],
    "GtaWhere": "Toronto",
    "GtaDistanceKm": 50,
    "ResultsPerPage": 50,
    "MaxPages": 4,
    "MaxCallsPerRun": 80,
    "MinDelayMs": 3000
  },
  "Boards": [
    { "Ats": "greenhouse", "Slug": "ada18" },
    { "Ats": "greenhouse", "Slug": "affirm" },
    { "Ats": "greenhouse", "Slug": "brex" },
    { "Ats": "greenhouse", "Slug": "coinbase" },
    { "Ats": "greenhouse", "Slug": "d2l" },
    { "Ats": "greenhouse", "Slug": "databricks" },
    { "Ats": "greenhouse", "Slug": "dropbox" },
    { "Ats": "greenhouse", "Slug": "faire" },
    { "Ats": "greenhouse", "Slug": "flipp" },
    { "Ats": "greenhouse", "Slug": "geotab" },
    { "Ats": "greenhouse", "Slug": "instacart" },
    { "Ats": "greenhouse", "Slug": "lyft" },
    { "Ats": "greenhouse", "Slug": "okta" },
    { "Ats": "greenhouse", "Slug": "pinterest" },
    { "Ats": "greenhouse", "Slug": "reddit" },
    { "Ats": "greenhouse", "Slug": "robinhood" },
    { "Ats": "greenhouse", "Slug": "samsara" },
    { "Ats": "greenhouse", "Slug": "stripe" },
    { "Ats": "greenhouse", "Slug": "tailscale" },
    { "Ats": "greenhouse", "Slug": "twilio" },
    { "Ats": "lever", "Slug": "achievers" },
    { "Ats": "lever", "Slug": "pointclickcare" },
    { "Ats": "lever", "Slug": "wattpad" },
    { "Ats": "lever", "Slug": "waveapps" },
    { "Ats": "ashby", "Slug": "1password" },
    { "Ats": "ashby", "Slug": "benevity" },
    { "Ats": "ashby", "Slug": "clearco" },
    { "Ats": "ashby", "Slug": "cohere" },
    { "Ats": "ashby", "Slug": "float" },
    { "Ats": "ashby", "Slug": "hopper" },
    { "Ats": "ashby", "Slug": "jobber" },
    { "Ats": "ashby", "Slug": "klue" },
    { "Ats": "ashby", "Slug": "loopio" },
    { "Ats": "ashby", "Slug": "miovision" },
    { "Ats": "ashby", "Slug": "neofinancial" },
    { "Ats": "ashby", "Slug": "ramp" },
    { "Ats": "ashby", "Slug": "snowflake" },
    { "Ats": "ashby", "Slug": "trulioo" },
    { "Ats": "ashby", "Slug": "wealthsimple" }
  ],
  "JobBank": {
    "DatasetId": "ea639e28-c0fc-48bf-b5dd-b8899bd43072",
    "BackfillMonths": 12
  }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: all configuration tests PASS (39 boards falls within 20–40).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add source configuration, validation and the GTA board list"
```

---

### Task 3: Run manifest

**Files:**
- Create: `src/Archiver/Manifest/ManifestModels.cs`, `UrlRedactor.cs`, `ManifestBuilder.cs`, `ManifestWriter.cs`
- Test: `tests/Archiver.Tests/Manifest/UrlRedactorTests.cs`, `ManifestBuilderTests.cs`

**Interfaces:**
- Consumes: `AtomicFile.WriteAsync(string, byte[], CancellationToken)` (Task 1)
- Produces:
  - `enum SourceOutcome { Succeeded, Partial, Failed, Skipped }`
  - `record RequestRecord(string Source, string Url, int? Status, long Bytes, long DurationMs, string? File, string? Error)`
  - `record SourceSummary(SourceOutcome Outcome, int Ok, int Failed, string? Note)`
  - `record RunManifest(string RunId, bool DryRun, string ToolVersion, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, IReadOnlyDictionary<string, SourceSummary> Sources, int AdzunaCalls, int AdzunaBudget, int InboxFiles, IReadOnlyList<RequestRecord> Requests)`
  - `ExitCodes.Ok/AllFailed/ConfigInvalid` (0/1/2)
  - `Outcomes.From(int ok, int failed) : SourceOutcome` and `Outcomes.ExitCode(IEnumerable<SourceOutcome>) : int`
  - `UrlRedactor.Redact(string url)` and `UrlRedactor.ScrubText(string text)`
  - `ManifestBuilder(string runId, bool dryRun, string toolVersion, DateTimeOffset startedAt, int adzunaBudget)` with `Record`, `CountAdzunaCall`, `AdzunaCalls`, `InboxFiles`, `CompleteSource(source, note?)`, `SkipSource(source, note)`, `FailSource(source, error)`, `Build(finishedAt)`
  - `ManifestWriter.WriteAsync(RunManifest, string path, CancellationToken)` and `ManifestWriter.Json` (serializer options)

- [ ] **Step 1: Write the failing tests**

`tests/Archiver.Tests/Manifest/UrlRedactorTests.cs`:
```csharp
using Archiver.Manifest;

namespace Archiver.Tests.Manifest;

public class UrlRedactorTests
{
    [Fact]
    public void Redact_removes_credential_parameters_only()
    {
        var url = "https://api.adzuna.com/v1/api/jobs/ca/search/1?app_id=ID1&app_key=KEY1&what=tech%20lead&where=Toronto";
        Assert.Equal("https://api.adzuna.com/v1/api/jobs/ca/search/1?what=tech%20lead&where=Toronto", UrlRedactor.Redact(url));
    }

    [Fact]
    public void Redact_drops_the_question_mark_when_only_credentials_were_present() =>
        Assert.Equal("https://x.test/histogram", UrlRedactor.Redact("https://x.test/histogram?app_id=a&app_key=b"));

    [Fact]
    public void Redact_leaves_urls_without_query_alone() =>
        Assert.Equal("https://boards-api.greenhouse.io/v1/boards/faire/jobs", UrlRedactor.Redact("https://boards-api.greenhouse.io/v1/boards/faire/jobs"));

    [Fact]
    public void ScrubText_masks_credentials_inside_free_text()
    {
        var scrubbed = UrlRedactor.ScrubText("Request to https://a.test/?app_id=ID1&app_key=KEY1 failed");
        Assert.DoesNotContain("ID1", scrubbed);
        Assert.DoesNotContain("KEY1", scrubbed);
        Assert.Contains("app_key=***", scrubbed);
    }
}
```

`tests/Archiver.Tests/Manifest/ManifestBuilderTests.cs`:
```csharp
using System.Text.Json;
using Archiver.Manifest;
using Archiver.Tests.TestSupport;

namespace Archiver.Tests.Manifest;

public class ManifestBuilderTests
{
    private static ManifestBuilder NewBuilder() => new("2026-09-29_0700", false, "test", DateTimeOffset.UnixEpoch, 80);

    private static RequestRecord Ok(string source) => new(source, "https://a.test/x", 200, 10, 5, "raw/x.json", null);
    private static RequestRecord Bad(string source) => new(source, "https://a.test/y", 500, 0, 5, null, "HTTP 500");

    [Theory]
    [InlineData(3, 0, SourceOutcome.Succeeded)]
    [InlineData(2, 1, SourceOutcome.Partial)]
    [InlineData(0, 2, SourceOutcome.Failed)]
    [InlineData(0, 0, SourceOutcome.Failed)]
    public void Outcome_follows_ok_and_failed_counts(int ok, int failed, SourceOutcome expected) =>
        Assert.Equal(expected, Outcomes.From(ok, failed));

    [Fact]
    public void Exit_code_is_0_when_anything_succeeded_or_partially_succeeded()
    {
        Assert.Equal(ExitCodes.Ok, Outcomes.ExitCode([SourceOutcome.Failed, SourceOutcome.Partial]));
        Assert.Equal(ExitCodes.AllFailed, Outcomes.ExitCode([SourceOutcome.Failed, SourceOutcome.Skipped]));
        Assert.Equal(ExitCodes.AllFailed, Outcomes.ExitCode([]));
    }

    [Fact]
    public void CompleteSource_counts_only_that_sources_requests()
    {
        var m = NewBuilder();
        m.Record(Ok("adzuna"));
        m.Record(Bad("adzuna"));
        m.Record(Ok("boards"));

        var summary = m.CompleteSource("adzuna");

        Assert.Equal(new SourceSummary(SourceOutcome.Partial, 1, 1, null), summary);
    }

    [Fact]
    public void FailSource_after_some_successes_is_partial_and_scrubbed()
    {
        var m = NewBuilder();
        m.Record(Ok("adzuna"));

        var summary = m.FailSource("adzuna", "crashed at https://a.test/?app_key=SECRET");

        Assert.Equal(SourceOutcome.Partial, summary.Outcome);
        Assert.DoesNotContain("SECRET", summary.Note);
    }

    [Fact]
    public void Record_redacts_urls_and_scrubs_errors()
    {
        var m = NewBuilder();
        m.Record(new RequestRecord("adzuna", "https://a.test/s?app_id=ID9&app_key=KEY9&what=x", null, 0, 1, null, "failed: https://a.test/s?app_key=KEY9"));

        var json = JsonSerializer.Serialize(m.Build(DateTimeOffset.UnixEpoch), ManifestWriter.Json);

        Assert.DoesNotContain("KEY9", json);
        Assert.DoesNotContain("ID9", json);
    }

    [Fact]
    public async Task Record_is_safe_under_concurrency()
    {
        var m = NewBuilder();
        await Parallel.ForAsync(0, 500, (i, _) => { m.Record(Ok("boards")); return ValueTask.CompletedTask; });
        Assert.Equal(500, m.Build(DateTimeOffset.UnixEpoch).Requests.Count);
    }

    [Fact]
    public async Task WriteAsync_writes_camelCase_json_with_string_enums()
    {
        using var tmp = new TempDir();
        var m = NewBuilder();
        m.Record(Ok("adzuna"));
        m.CompleteSource("adzuna");
        m.SkipSource("jobbank", "offline");
        var path = Path.Combine(tmp.Path, "manifest.json");

        await ManifestWriter.WriteAsync(m.Build(DateTimeOffset.UnixEpoch), path, CancellationToken.None);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("succeeded", doc.RootElement.GetProperty("sources").GetProperty("adzuna").GetProperty("outcome").GetString());
        Assert.Equal("skipped", doc.RootElement.GetProperty("sources").GetProperty("jobbank").GetProperty("outcome").GetString());
        Assert.Equal(80, doc.RootElement.GetProperty("adzunaBudget").GetInt32());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: build FAILS because the `Archiver.Manifest` types don't exist.

- [ ] **Step 3: Implement the manifest types**

`src/Archiver/Manifest/ManifestModels.cs`:
```csharp
namespace Archiver.Manifest;

public enum SourceOutcome { Succeeded, Partial, Failed, Skipped }

public sealed record RequestRecord(string Source, string Url, int? Status, long Bytes, long DurationMs, string? File, string? Error);

public sealed record SourceSummary(SourceOutcome Outcome, int Ok, int Failed, string? Note);

public sealed record RunManifest(
    string RunId,
    bool DryRun,
    string ToolVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyDictionary<string, SourceSummary> Sources,
    int AdzunaCalls,
    int AdzunaBudget,
    int InboxFiles,
    IReadOnlyList<RequestRecord> Requests);

public static class ExitCodes
{
    public const int Ok = 0;
    public const int AllFailed = 1;
    public const int ConfigInvalid = 2;
}

public static class Outcomes
{
    // A source that made no successful request collected nothing, so it counts as failed.
    public static SourceOutcome From(int ok, int failed) => (ok, failed) switch
    {
        (0, _) => SourceOutcome.Failed,
        (_, 0) => SourceOutcome.Succeeded,
        _ => SourceOutcome.Partial,
    };

    public static int ExitCode(IEnumerable<SourceOutcome> outcomes) =>
        outcomes.Any(o => o is SourceOutcome.Succeeded or SourceOutcome.Partial) ? ExitCodes.Ok : ExitCodes.AllFailed;
}
```

`src/Archiver/Manifest/UrlRedactor.cs`:
```csharp
using System.Text.RegularExpressions;

namespace Archiver.Manifest;

public static partial class UrlRedactor
{
    private static readonly string[] SecretParams = ["app_id", "app_key"];

    // Drops credential query parameters from a URL before it is stored.
    public static string Redact(string url)
    {
        var q = url.IndexOf('?');
        if (q < 0) return url;
        var kept = url[(q + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !SecretParams.Contains(p.Split('=')[0], StringComparer.OrdinalIgnoreCase));
        var query = string.Join('&', kept);
        return query.Length == 0 ? url[..q] : $"{url[..q]}?{query}";
    }

    // Masks credential values that appear anywhere in free text, e.g. exception messages.
    public static string ScrubText(string text) => SecretPattern().Replace(text, "$1***");

    [GeneratedRegex(@"(app_(?:id|key)=)[^&\s""']+", RegexOptions.IgnoreCase)]
    private static partial Regex SecretPattern();
}
```

`src/Archiver/Manifest/ManifestBuilder.cs`:
```csharp
namespace Archiver.Manifest;

// Collects everything a run did. Thread-safe: board downloads record in parallel.
public sealed class ManifestBuilder(string runId, bool dryRun, string toolVersion, DateTimeOffset startedAt, int adzunaBudget)
{
    private readonly Lock _gate = new();
    private readonly List<RequestRecord> _requests = [];
    private readonly Dictionary<string, SourceSummary> _sources = [];
    private int _adzunaCalls;

    public int InboxFiles { get; set; }
    public int AdzunaCalls => Volatile.Read(ref _adzunaCalls);

    public void Record(RequestRecord record)
    {
        var safe = record with
        {
            Url = UrlRedactor.Redact(record.Url),
            Error = record.Error is null ? null : UrlRedactor.ScrubText(record.Error),
        };
        lock (_gate) _requests.Add(safe);
    }

    public void CountAdzunaCall() => Interlocked.Increment(ref _adzunaCalls);

    public SourceSummary CompleteSource(string source, string? note = null)
    {
        lock (_gate)
        {
            var (ok, failed) = Count(source);
            return _sources[source] = new SourceSummary(Outcomes.From(ok, failed), ok, failed, note);
        }
    }

    public SourceSummary SkipSource(string source, string note)
    {
        lock (_gate) return _sources[source] = new SourceSummary(SourceOutcome.Skipped, 0, 0, note);
    }

    // The source threw; whatever it saved before that still counts, so earlier successes make it partial.
    public SourceSummary FailSource(string source, string error)
    {
        lock (_gate)
        {
            var (ok, failed) = Count(source);
            var outcome = ok > 0 ? SourceOutcome.Partial : SourceOutcome.Failed;
            return _sources[source] = new SourceSummary(outcome, ok, failed, UrlRedactor.ScrubText(error));
        }
    }

    public RunManifest Build(DateTimeOffset finishedAt)
    {
        lock (_gate)
        {
            return new RunManifest(runId, dryRun, toolVersion, startedAt, finishedAt,
                new Dictionary<string, SourceSummary>(_sources), AdzunaCalls, adzunaBudget, InboxFiles, [.. _requests]);
        }
    }

    private (int Ok, int Failed) Count(string source)
    {
        var ok = _requests.Count(r => r.Source == source && r.Error is null);
        var failed = _requests.Count(r => r.Source == source && r.Error is not null);
        return (ok, failed);
    }
}
```

`src/Archiver/Manifest/ManifestWriter.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Archiver.Storage;

namespace Archiver.Manifest;

public static class ManifestWriter
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static Task WriteAsync(RunManifest manifest, string path, CancellationToken ct) =>
        AtomicFile.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(manifest, Json), ct);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all manifest tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add run manifest with credential redaction and exit-code rules"
```

---

### Task 4: Source contract and HTTP archiving helper

**Files:**
- Create: `src/Archiver/Sources/IArchiveSource.cs`, `src/Archiver/Sources/HttpArchiver.cs`
- Create: `tests/Archiver.Tests/TestSupport/StubHandler.cs`, `tests/Archiver.Tests/TestSupport/TestRun.cs`
- Test: `tests/Archiver.Tests/Sources/HttpArchiverTests.cs`

**Interfaces:**
- Consumes: `RunFolder`, `AtomicFile` (Task 1); `ManifestBuilder`, `RequestRecord` (Task 3)
- Produces:
  - `interface IArchiveSource { string Name { get; } Task<SourceResult> RunAsync(RunContext ctx, CancellationToken ct); }`
  - `record RunContext(RunFolder Folder, ManifestBuilder Manifest, bool DryRun)`
  - `record SourceResult(bool Skipped, string? Note)` with `SourceResult.Ran(string? note = null)` and `SourceResult.Skip(string note)`
  - `record FetchResult(bool Ok, int? Status, byte[] Body)`
  - `HttpArchiver.GetAndSaveAsync(HttpClient http, string source, string url, string filePath, RunContext ctx, CancellationToken ct) : Task<FetchResult>`
  - `HttpArchiver.DownloadGzipAsync(HttpClient http, string source, string url, string filePath, RunContext ctx, CancellationToken ct) : Task<bool>`
  - Test helpers: `StubHandler`, `StubHttpClientFactory`, `FailingStream`, `TestRun`

- [ ] **Step 1: Write the test helpers**

`tests/Archiver.Tests/TestSupport/StubHandler.cs`:
```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Archiver.Tests.TestSupport;

public sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private readonly ConcurrentQueue<Uri> _requests = new();

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request))) { }

    public IReadOnlyList<Uri> Requests => [.. _requests];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        _requests.Enqueue(request.RequestUri!);
        return respond(request, ct);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

// Yields a prefix, then fails like a dropped connection.
public sealed class FailingStream(byte[] prefix) : Stream
{
    private int _position;

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= prefix.Length) throw new IOException("Connection reset by peer.");
        var n = Math.Min(count, prefix.Length - _position);
        Array.Copy(prefix, _position, buffer, offset, n);
        _position += n;
        return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
```

`tests/Archiver.Tests/TestSupport/TestRun.cs`:
```csharp
using Archiver.Manifest;
using Archiver.Sources;
using Archiver.Storage;

namespace Archiver.Tests.TestSupport;

// A run folder + manifest + context on a temp data root. Pass dataRoot to simulate a later run on the same disk.
public sealed class TestRun : IDisposable
{
    private static int _minute;
    private readonly bool _ownsRoot;

    public TestRun(bool dryRun = false, string? dataRoot = null)
    {
        _ownsRoot = dataRoot is null;
        DataRoot = dataRoot ?? Path.Combine(Path.GetTempPath(), "archiver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataRoot);
        var start = new DateTimeOffset(2026, 9, 29, 7, 0, 0, TimeSpan.Zero).AddMinutes(Interlocked.Increment(ref _minute));
        Folder = RunFolder.Create(DataRoot, start, dryRun);
        Manifest = new ManifestBuilder(Folder.RunId, dryRun, "test", start, adzunaBudget: 80);
        Context = new RunContext(Folder, Manifest, dryRun);
    }

    public string DataRoot { get; }
    public RunFolder Folder { get; }
    public ManifestBuilder Manifest { get; }
    public RunContext Context { get; }

    public IReadOnlyList<RequestRecord> Requests => Manifest.Build(DateTimeOffset.UnixEpoch).Requests;

    public void Dispose()
    {
        if (!_ownsRoot) return;
        try { Directory.Delete(DataRoot, recursive: true); } catch (IOException) { }
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Archiver.Tests/Sources/HttpArchiverTests.cs`:
```csharp
using System.IO.Compression;
using System.Net;
using Archiver.Sources;
using Archiver.Tests.TestSupport;

namespace Archiver.Tests.Sources;

public class HttpArchiverTests
{
    [Fact]
    public async Task GetAndSave_writes_body_verbatim_and_records_redacted_url()
    {
        using var run = new TestRun();
        var http = new HttpClient(new StubHandler(_ => StubHandler.Json("{\"a\": 1}")));
        var file = Path.Combine(run.Folder.FullPath, "x.json");

        var result = await HttpArchiver.GetAndSaveAsync(http, "adzuna", "https://a.test/s?app_id=I&app_key=K&what=x", file, run.Context, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("{\"a\": 1}", File.ReadAllText(file));
        var record = Assert.Single(run.Requests);
        Assert.Equal("https://a.test/s?what=x", record.Url);
        Assert.Equal(200, record.Status);
        Assert.Equal(run.Folder.Relative(file), record.File);
        Assert.Null(record.Error);
    }

    [Fact]
    public async Task GetAndSave_does_not_save_error_responses()
    {
        using var run = new TestRun();
        var http = new HttpClient(new StubHandler(_ => StubHandler.Json("{}", HttpStatusCode.ServiceUnavailable)));
        var file = Path.Combine(run.Folder.FullPath, "x.json");

        var result = await HttpArchiver.GetAndSaveAsync(http, "boards", "https://a.test/b", file, run.Context, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.False(File.Exists(file));
        Assert.Equal("HTTP 503", Assert.Single(run.Requests).Error);
    }

    [Fact]
    public async Task GetAndSave_records_exceptions_without_leaking_credentials()
    {
        using var run = new TestRun();
        var http = new HttpClient(new StubHandler(_ => throw new HttpRequestException("failed for https://a.test/?app_key=SECRET")));

        var result = await HttpArchiver.GetAndSaveAsync(http, "adzuna", "https://a.test/?app_key=SECRET", Path.Combine(run.Folder.FullPath, "x.json"), run.Context, CancellationToken.None);

        Assert.False(result.Ok);
        var record = Assert.Single(run.Requests);
        Assert.DoesNotContain("SECRET", record.Error);
        Assert.DoesNotContain("SECRET", record.Url);
    }

    [Fact]
    public async Task DownloadGzip_round_trips_the_exact_bytes()
    {
        using var run = new TestRun();
        var original = new byte[] { 0xFF, 0xFE, 0x57, 0x00, 0x09, 0x00, 0x41, 0x00 }; // UTF-16 BOM + "W\tA"
        var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(original) }));
        var file = Path.Combine(run.DataRoot, "jobbank", "2026-08_x.csv.gz");

        var ok = await HttpArchiver.DownloadGzipAsync(http, "jobbank", "https://files.test/x.csv", file, run.Context, CancellationToken.None);

        Assert.True(ok);
        await using var gz = new GZipStream(File.OpenRead(file), CompressionMode.Decompress);
        using var copy = new MemoryStream();
        await gz.CopyToAsync(copy);
        Assert.Equal(original, copy.ToArray());
        Assert.Equal(original.Length, Assert.Single(run.Requests).Bytes);
    }

    [Fact]
    public async Task DownloadGzip_leaves_nothing_behind_when_the_stream_breaks()
    {
        using var run = new TestRun();
        var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new FailingStream(new byte[100_000])),
        }));
        var file = Path.Combine(run.DataRoot, "jobbank", "2026-08_x.csv.gz");

        var ok = await HttpArchiver.DownloadGzipAsync(http, "jobbank", "https://files.test/x.csv", file, run.Context, CancellationToken.None);

        Assert.False(ok);
        Assert.False(File.Exists(file));
        Assert.False(File.Exists(file + ".tmp"));
        Assert.Contains("Connection reset", Assert.Single(run.Requests).Error);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test`
Expected: build FAILS because `Archiver.Sources` types don't exist.

- [ ] **Step 4: Implement the contract and helper**

`src/Archiver/Sources/IArchiveSource.cs`:
```csharp
using Archiver.Manifest;
using Archiver.Storage;

namespace Archiver.Sources;

public interface IArchiveSource
{
    string Name { get; }

    // Records every request in ctx.Manifest. Throws only for bugs; expected failures are recorded, not thrown.
    Task<SourceResult> RunAsync(RunContext ctx, CancellationToken ct);
}

public sealed record RunContext(RunFolder Folder, ManifestBuilder Manifest, bool DryRun);

public sealed record SourceResult(bool Skipped, string? Note)
{
    public static SourceResult Ran(string? note = null) => new(false, note);
    public static SourceResult Skip(string note) => new(true, note);
}
```

`src/Archiver/Sources/HttpArchiver.cs`:
```csharp
using System.Diagnostics;
using System.IO.Compression;
using Archiver.Manifest;
using Archiver.Storage;

namespace Archiver.Sources;

public sealed record FetchResult(bool Ok, int? Status, byte[] Body);

// Every outbound request goes through here so each one is saved atomically and lands in the manifest exactly once.
public static class HttpArchiver
{
    public static async Task<FetchResult> GetAndSaveAsync(HttpClient http, string source, string url, string filePath, RunContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await http.GetAsync(url, ct);
            var body = await response.Content.ReadAsByteArrayAsync(ct);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                ctx.Manifest.Record(new RequestRecord(source, url, status, body.Length, sw.ElapsedMilliseconds, null, $"HTTP {status}"));
                return new FetchResult(false, status, body);
            }

            await AtomicFile.WriteAsync(filePath, body, ct);
            ctx.Manifest.Record(new RequestRecord(source, url, status, body.Length, sw.ElapsedMilliseconds, ctx.Folder.Relative(filePath), null));
            return new FetchResult(true, status, body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            ctx.Manifest.Record(new RequestRecord(source, url, null, 0, sw.ElapsedMilliseconds, null, ex.Message));
            return new FetchResult(false, null, []);
        }
    }

    // Streams a large body straight into a gzip file without buffering it in memory.
    public static async Task<bool> DownloadGzipAsync(HttpClient http, string source, string url, string filePath, RunContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        long bytes = 0;
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                ctx.Manifest.Record(new RequestRecord(source, url, status, 0, sw.ElapsedMilliseconds, null, $"HTTP {status}"));
                return false;
            }

            await AtomicFile.WriteAsync(filePath, async (file, token) =>
            {
                await using var gzip = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true);
                await using var body = await response.Content.ReadAsStreamAsync(token);
                var buffer = new byte[81920];
                int read;
                while ((read = await body.ReadAsync(buffer, token)) > 0)
                {
                    await gzip.WriteAsync(buffer.AsMemory(0, read), token);
                    bytes += read;
                }
            }, ct);

            ctx.Manifest.Record(new RequestRecord(source, url, status, bytes, sw.ElapsedMilliseconds, ctx.Folder.Relative(filePath), null));
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            ctx.Manifest.Record(new RequestRecord(source, url, null, bytes, sw.ElapsedMilliseconds, null, ex.Message));
            return false;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: all `HttpArchiverTests` PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add source contract and atomic HTTP archiving helper"
```

---

### Task 5: Adzuna source

**Files:**
- Create: `src/Archiver/Sources/AdzunaQueryPlan.cs`, `src/Archiver/Sources/AdzunaSource.cs`
- Test: `tests/Archiver.Tests/Sources/AdzunaQueryPlanTests.cs`, `AdzunaSourceTests.cs`

**Interfaces:**
- Consumes: `AdzunaOptions`, `SourcesOptions` (Task 2); `IArchiveSource`, `RunContext`, `SourceResult`, `HttpArchiver.GetAndSaveAsync` (Task 4); `RunFolder.AdzunaSearchFile/AdzunaAggregateFile` (Task 1); `ManifestBuilder.CountAdzunaCall` (Task 3)
- Produces:
  - `enum AdzunaKind { Histogram, TopCompanies, Search }`
  - `record AdzunaRequest(AdzunaKind Kind, string Term, string Scope, int Page)`
  - `AdzunaQueryPlan.Gta` = `"gta"`, `AdzunaQueryPlan.Remote` = `"remote"`
  - `AdzunaQueryPlan.Build(AdzunaOptions) : IReadOnlyList<AdzunaRequest>` and `AdzunaQueryPlan.Url(AdzunaOptions, AdzunaRequest) : string`
  - `AdzunaSource(IHttpClientFactory, IOptions<SourcesOptions>) : IArchiveSource` with `SourceName` = `"adzuna"`, `HttpClientName` = `"adzuna"`, and `static int CountResults(byte[] body)`

- [ ] **Step 1: Write the failing tests**

`tests/Archiver.Tests/Sources/AdzunaQueryPlanTests.cs`:
```csharp
using System.Web;
using Archiver.Configuration;
using Archiver.Sources;

namespace Archiver.Tests.Sources;

public class AdzunaQueryPlanTests
{
    private static AdzunaOptions Options() => new() { AppId = "ID1", AppKey = "KEY1", Terms = [".net developer", "tech lead"], MaxPages = 3 };

    [Fact]
    public void Plan_puts_aggregates_first_then_searches_page_by_page()
    {
        var plan = AdzunaQueryPlan.Build(Options());

        Assert.Equal(2 + 2 + 3 * 2 * 2, plan.Count);
        Assert.All(plan.Take(2), r => Assert.Equal(AdzunaKind.Histogram, r.Kind));
        Assert.All(plan.Skip(2).Take(2), r => Assert.Equal(AdzunaKind.TopCompanies, r.Kind));
        var searches = plan.Skip(4).ToList();
        Assert.All(searches.Take(4), r => Assert.Equal(1, r.Page)); // every term and scope gets page 1 before any page 2
        Assert.Equal(new AdzunaRequest(AdzunaKind.Search, ".net developer", "gta", 1), searches[0]);
        Assert.Equal(new AdzunaRequest(AdzunaKind.Search, ".net developer", "remote", 1), searches[1]);
    }

    [Fact]
    public void Gta_search_url_has_location_distance_and_paging()
    {
        var url = new Uri(AdzunaQueryPlan.Url(Options(), new AdzunaRequest(AdzunaKind.Search, ".net developer", "gta", 2)));
        var q = HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("/v1/api/jobs/ca/search/2", url.AbsolutePath);
        Assert.Equal(".net developer", q["what"]);
        Assert.Equal("Toronto", q["where"]);
        Assert.Equal("50", q["distance"]);
        Assert.Equal("50", q["results_per_page"]);
        Assert.Equal("ID1", q["app_id"]);
        Assert.Equal("KEY1", q["app_key"]);
    }

    [Fact]
    public void Remote_search_url_adds_remote_and_has_no_location()
    {
        var url = new Uri(AdzunaQueryPlan.Url(Options(), new AdzunaRequest(AdzunaKind.Search, "c# developer", "remote", 1)));
        var q = HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("c# developer remote", q["what"]);
        Assert.Null(q["where"]);
        Assert.Null(q["distance"]);
    }

    [Fact]
    public void Aggregate_urls_hit_their_endpoints_for_the_gta()
    {
        var hist = new Uri(AdzunaQueryPlan.Url(Options(), new AdzunaRequest(AdzunaKind.Histogram, "tech lead", "gta", 0)));
        var top = new Uri(AdzunaQueryPlan.Url(Options(), new AdzunaRequest(AdzunaKind.TopCompanies, "tech lead", "gta", 0)));

        Assert.Equal("/v1/api/jobs/ca/histogram", hist.AbsolutePath);
        Assert.Equal("/v1/api/jobs/ca/top_companies", top.AbsolutePath);
        Assert.Equal("Toronto", HttpUtility.ParseQueryString(top.Query)["where"]);
    }
}
```

`tests/Archiver.Tests/Sources/AdzunaSourceTests.cs`:
```csharp
using System.Net;
using Archiver.Configuration;
using Archiver.Manifest;
using Archiver.Sources;
using Archiver.Tests.TestSupport;
using Microsoft.Extensions.Options;

namespace Archiver.Tests.Sources;

public class AdzunaSourceTests
{
    private static SourcesOptions Config(int maxCalls = 80) => new()
    {
        Adzuna = new AdzunaOptions
        {
            AppId = "ID1", AppKey = "KEY1",
            Terms = ["dotnet developer", "tech lead"],
            MaxPages = 3, MaxCallsPerRun = maxCalls, MinDelayMs = 0,
        },
    };

    // Search pages return `perPage(page)` results; aggregates return a small object.
    private static StubHandler Adzuna(Func<int, int> perPage) => new(req =>
    {
        var path = req.RequestUri!.AbsolutePath;
        if (!path.Contains("/search/")) return StubHandler.Json("{\"histogram\":{}}");
        var n = perPage(int.Parse(path[(path.LastIndexOf('/') + 1)..]));
        return StubHandler.Json($"{{\"count\":999,\"results\":[{string.Join(',', Enumerable.Repeat("{}", n))}]}}");
    });

    private static AdzunaSource Source(StubHandler handler, SourcesOptions config) =>
        new(new StubHttpClientFactory(handler), Options.Create(config));

    [Fact]
    public async Task Stops_paging_a_query_after_a_short_page()
    {
        using var run = new TestRun();
        var handler = Adzuna(page => page == 1 ? 50 : 10);

        await Source(handler, Config()).RunAsync(run.Context, CancellationToken.None);

        // 4 aggregates + 4 (term, scope) queries x 2 pages each
        Assert.Equal(12, handler.Requests.Count);
        Assert.True(File.Exists(run.Folder.AdzunaSearchFile("dotnet developer", "gta", 2)));
        Assert.False(File.Exists(run.Folder.AdzunaSearchFile("dotnet developer", "gta", 3)));
        Assert.Equal(12, run.Manifest.AdzunaCalls);
    }

    [Fact]
    public async Task Budget_caps_calls_and_keeps_aggregates()
    {
        using var run = new TestRun();
        var handler = Adzuna(_ => 50);

        var result = await Source(handler, Config(maxCalls: 5)).RunAsync(run.Context, CancellationToken.None);

        Assert.Equal(5, handler.Requests.Count);
        Assert.All(handler.Requests.Take(2), u => Assert.EndsWith("/histogram", u.AbsolutePath));
        Assert.All(handler.Requests.Skip(2).Take(2), u => Assert.EndsWith("/top_companies", u.AbsolutePath));
        Assert.Contains("budget", result.Note);
    }

    [Fact]
    public async Task Skips_without_calling_when_credentials_are_missing()
    {
        using var run = new TestRun();
        var handler = Adzuna(_ => 50);
        var config = Config();
        config.Adzuna.AppKey = null;

        var result = await Source(handler, config).RunAsync(run.Context, CancellationToken.None);

        Assert.True(result.Skipped);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Dry_run_makes_exactly_one_search_call()
    {
        using var run = new TestRun(dryRun: true);
        var handler = Adzuna(_ => 50);

        await Source(handler, Config()).RunAsync(run.Context, CancellationToken.None);

        var only = Assert.Single(handler.Requests);
        Assert.EndsWith("/search/1", only.AbsolutePath);
    }

    [Fact]
    public async Task Rejected_key_fails_every_query_once_and_never_stores_the_key()
    {
        using var run = new TestRun();
        var handler = new StubHandler(_ => StubHandler.Json("{\"exception\":\"AUTH_FAIL\"}", HttpStatusCode.Unauthorized));

        await Source(handler, Config()).RunAsync(run.Context, CancellationToken.None);

        // 4 aggregates + page 1 only for each of the 4 (term, scope) queries
        Assert.Equal(8, handler.Requests.Count);
        Assert.Equal(SourceOutcome.Failed, run.Manifest.CompleteSource("adzuna").Outcome);
        Assert.All(run.Requests, r => Assert.DoesNotContain("KEY1", r.Url));
    }

    [Theory]
    [InlineData("{\"results\":[{},{}]}", 2)]
    [InlineData("{\"count\":0}", 0)]
    [InlineData("not json", 0)]
    public void CountResults_reads_the_results_array(string body, int expected) =>
        Assert.Equal(expected, AdzunaSource.CountResults(System.Text.Encoding.UTF8.GetBytes(body)));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: build FAILS because `AdzunaQueryPlan` and `AdzunaSource` don't exist.

- [ ] **Step 3: Implement the query plan and source**

`src/Archiver/Sources/AdzunaQueryPlan.cs`:
```csharp
using Archiver.Configuration;

namespace Archiver.Sources;

public enum AdzunaKind { Histogram, TopCompanies, Search }

public sealed record AdzunaRequest(AdzunaKind Kind, string Term, string Scope, int Page);

public static class AdzunaQueryPlan
{
    public const string Gta = "gta";
    public const string Remote = "remote";

    // Aggregates first so the budget can never cut them; searches page-major so every query gets page 1 first.
    public static IReadOnlyList<AdzunaRequest> Build(AdzunaOptions o)
    {
        var plan = new List<AdzunaRequest>();
        plan.AddRange(o.Terms.Select(t => new AdzunaRequest(AdzunaKind.Histogram, t, Gta, 0)));
        plan.AddRange(o.Terms.Select(t => new AdzunaRequest(AdzunaKind.TopCompanies, t, Gta, 0)));
        for (var page = 1; page <= o.MaxPages; page++)
            foreach (var term in o.Terms)
                foreach (var scope in new[] { Gta, Remote })
                    plan.Add(new AdzunaRequest(AdzunaKind.Search, term, scope, page));
        return plan;
    }

    public static string Url(AdzunaOptions o, AdzunaRequest r)
    {
        var what = r.Scope == Remote ? $"{r.Term} remote" : r.Term;
        var query = new List<string>
        {
            $"app_id={E(o.AppId)}",
            $"app_key={E(o.AppKey)}",
            $"what={E(what)}",
            "content-type=application/json",
        };
        if (r.Scope == Gta) query.Add($"where={E(o.GtaWhere)}");

        string path;
        switch (r.Kind)
        {
            case AdzunaKind.Search:
                path = $"search/{r.Page}";
                query.Add($"results_per_page={o.ResultsPerPage}");
                if (r.Scope == Gta) query.Add($"distance={o.GtaDistanceKm}");
                break;
            case AdzunaKind.Histogram:
                path = "histogram";
                break;
            default:
                path = "top_companies";
                break;
        }
        return $"{o.BaseUrl.TrimEnd('/')}/{o.Country}/{path}?{string.Join('&', query)}";
    }

    private static string E(string? value) => Uri.EscapeDataString(value ?? "");
}
```

`src/Archiver/Sources/AdzunaSource.cs`:
```csharp
using System.Text.Json;
using Archiver.Configuration;
using Microsoft.Extensions.Options;

namespace Archiver.Sources;

public sealed class AdzunaSource(IHttpClientFactory httpFactory, IOptions<SourcesOptions> options) : IArchiveSource
{
    public const string SourceName = "adzuna";
    public const string HttpClientName = "adzuna";

    public string Name => SourceName;

    public async Task<SourceResult> RunAsync(RunContext ctx, CancellationToken ct)
    {
        var o = options.Value.Adzuna;
        if (!o.HasCredentials)
            return SourceResult.Skip("Adzuna AppId/AppKey not configured (see README: dotnet user-secrets).");

        var http = httpFactory.CreateClient(HttpClientName);
        var plan = AdzunaQueryPlan.Build(o);
        if (ctx.DryRun) plan = [.. plan.Where(r => r.Kind == AdzunaKind.Search).Take(1)];

        var exhausted = new HashSet<(string Term, string Scope)>();
        var calls = 0;
        var notMade = 0;
        foreach (var request in plan)
        {
            if (request.Kind == AdzunaKind.Search && exhausted.Contains((request.Term, request.Scope))) continue;
            if (calls >= o.MaxCallsPerRun) { notMade++; continue; }
            if (calls > 0 && o.MinDelayMs > 0) await Task.Delay(o.MinDelayMs, ct);

            calls++;
            ctx.Manifest.CountAdzunaCall();
            var file = request.Kind switch
            {
                AdzunaKind.Search => ctx.Folder.AdzunaSearchFile(request.Term, request.Scope, request.Page),
                AdzunaKind.Histogram => ctx.Folder.AdzunaAggregateFile("histogram", request.Term),
                _ => ctx.Folder.AdzunaAggregateFile("top_companies", request.Term),
            };
            var result = await HttpArchiver.GetAndSaveAsync(http, SourceName, AdzunaQueryPlan.Url(o, request), file, ctx, ct);

            // A failed or short page is the last one for that query.
            if (request.Kind == AdzunaKind.Search && (!result.Ok || CountResults(result.Body) < o.ResultsPerPage))
                exhausted.Add((request.Term, request.Scope));
        }

        return SourceResult.Ran(notMade > 0 ? $"{notMade} planned calls not made: budget of {o.MaxCallsPerRun} reached." : null);
    }

    public static int CountResults(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("results", out var results)
                && results.ValueKind == JsonValueKind.Array
                ? results.GetArrayLength()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all Adzuna tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add Adzuna source with budgeted, page-major query plan"
```

---

### Task 6: Job board source (Greenhouse, Lever, Ashby)

**Files:**
- Create: `src/Archiver/Sources/BoardUrls.cs`, `src/Archiver/Sources/JobBoardSource.cs`
- Test: `tests/Archiver.Tests/Sources/BoardUrlsTests.cs`, `JobBoardSourceTests.cs`

**Interfaces:**
- Consumes: `BoardConfig`, `SourcesOptions` (Task 2); `IArchiveSource`, `RunContext`, `SourceResult`, `HttpArchiver.GetAndSaveAsync` (Task 4); `RunFolder.BoardFile` (Task 1)
- Produces:
  - `BoardUrls.For(BoardConfig) : string` (throws `ArgumentException` for an unknown ATS)
  - `BoardUrls.CountJobs(string ats, byte[] body) : int?`
  - `record BoardCheck(BoardConfig Board, int? Status, int? Jobs, string? Error)` with `bool Ok`
  - `JobBoardSource(IHttpClientFactory, IOptions<SourcesOptions>) : IArchiveSource` with `SourceName` = `"boards"`, `HttpClientName` = `"boards"`, `MaxParallel` = `4`, and `VerifyAsync(CancellationToken) : Task<IReadOnlyList<BoardCheck>>`

- [ ] **Step 1: Write the failing tests**

`tests/Archiver.Tests/Sources/BoardUrlsTests.cs`:
```csharp
using System.Text;
using Archiver.Configuration;
using Archiver.Sources;

namespace Archiver.Tests.Sources;

public class BoardUrlsTests
{
    [Theory]
    [InlineData("greenhouse", "faire", "https://boards-api.greenhouse.io/v1/boards/faire/jobs?content=true")]
    [InlineData("lever", "wattpad", "https://api.lever.co/v0/postings/wattpad?mode=json")]
    [InlineData("ashby", "cohere", "https://api.ashbyhq.com/posting-api/job-board/cohere?includeCompensation=true")]
    public void For_builds_the_public_board_url(string ats, string slug, string expected) =>
        Assert.Equal(expected, BoardUrls.For(new BoardConfig { Ats = ats, Slug = slug }));

    [Fact]
    public void For_rejects_unknown_ats() =>
        Assert.Throws<ArgumentException>(() => BoardUrls.For(new BoardConfig { Ats = "workday", Slug = "rbc" }));

    [Theory]
    [InlineData("greenhouse", "{\"jobs\":[{},{},{}]}", 3)]
    [InlineData("ashby", "{\"jobs\":[]}", 0)]
    [InlineData("lever", "[{},{}]", 2)]
    [InlineData("lever", "{\"ok\":false}", null)]
    [InlineData("greenhouse", "<html>", null)]
    public void CountJobs_understands_each_ats_shape(string ats, string body, int? expected) =>
        Assert.Equal(expected, BoardUrls.CountJobs(ats, Encoding.UTF8.GetBytes(body)));
}
```

`tests/Archiver.Tests/Sources/JobBoardSourceTests.cs`:
```csharp
using System.Net;
using Archiver.Configuration;
using Archiver.Manifest;
using Archiver.Sources;
using Archiver.Tests.TestSupport;
using Microsoft.Extensions.Options;

namespace Archiver.Tests.Sources;

public class JobBoardSourceTests
{
    private static SourcesOptions Config(params (string Ats, string Slug)[] boards) => new()
    {
        Boards = [.. boards.Select(b => new BoardConfig { Ats = b.Ats, Slug = b.Slug })],
    };

    private static JobBoardSource Source(StubHandler handler, SourcesOptions config) =>
        new(new StubHttpClientFactory(handler), Options.Create(config));

    [Fact]
    public async Task Saves_every_board_and_keeps_going_after_a_failure()
    {
        using var run = new TestRun();
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath.Contains("gone")
            ? StubHandler.Json("{}", HttpStatusCode.NotFound)
            : StubHandler.Json("{\"jobs\":[]}"));

        await Source(handler, Config(("greenhouse", "faire"), ("greenhouse", "gone"), ("ashby", "cohere"))).RunAsync(run.Context, CancellationToken.None);

        Assert.True(File.Exists(run.Folder.BoardFile("greenhouse", "faire")));
        Assert.True(File.Exists(run.Folder.BoardFile("ashby", "cohere")));
        Assert.False(File.Exists(run.Folder.BoardFile("greenhouse", "gone")));
        Assert.Equal(SourceOutcome.Partial, run.Manifest.CompleteSource("boards").Outcome);
    }

    [Fact]
    public async Task Never_runs_more_than_four_requests_at_once()
    {
        using var run = new TestRun();
        int current = 0, peak = 0;
        var handler = new StubHandler(async (_, ct) =>
        {
            var now = Interlocked.Increment(ref current);
            InterlockedMax(ref peak, now);
            await Task.Delay(50, ct);
            Interlocked.Decrement(ref current);
            return StubHandler.Json("{\"jobs\":[]}");
        });
        var boards = Enumerable.Range(0, 12).Select(i => ("greenhouse", $"co{i}")).ToArray();

        await Source(handler, Config(boards)).RunAsync(run.Context, CancellationToken.None);

        Assert.Equal(12, handler.Requests.Count);
        Assert.InRange(peak, 2, JobBoardSource.MaxParallel);
    }

    [Fact]
    public async Task Dry_run_fetches_only_the_first_board()
    {
        using var run = new TestRun(dryRun: true);
        var handler = new StubHandler(_ => StubHandler.Json("{\"jobs\":[]}"));

        await Source(handler, Config(("greenhouse", "faire"), ("lever", "wattpad"))).RunAsync(run.Context, CancellationToken.None);

        Assert.Contains("faire", Assert.Single(handler.Requests).AbsolutePath);
    }

    [Fact]
    public async Task Skips_when_no_boards_are_configured()
    {
        using var run = new TestRun();
        var result = await Source(new StubHandler(_ => StubHandler.Json("{}")), Config()).RunAsync(run.Context, CancellationToken.None);
        Assert.True(result.Skipped);
    }

    [Fact]
    public async Task Verify_reports_job_counts_and_problems_in_config_order()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath switch
        {
            var p when p.Contains("faire") => StubHandler.Json("{\"jobs\":[{},{}]}"),
            var p when p.Contains("wattpad") => StubHandler.Json("<html>not json</html>"),
            _ => StubHandler.Json("{}", HttpStatusCode.NotFound),
        });

        var checks = await Source(handler, Config(("greenhouse", "faire"), ("lever", "wattpad"), ("ashby", "gone"))).VerifyAsync(CancellationToken.None);

        Assert.Equal(new[] { "faire", "wattpad", "gone" }, checks.Select(c => c.Board.Slug));
        Assert.True(checks[0].Ok);
        Assert.Equal(2, checks[0].Jobs);
        Assert.False(checks[1].Ok);
        Assert.False(checks[2].Ok);
        Assert.Equal(404, checks[2].Status);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, seen) != seen) { }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: build FAILS because `BoardUrls` and `JobBoardSource` don't exist.

- [ ] **Step 3: Implement board URLs and the source**

`src/Archiver/Sources/BoardUrls.cs`:
```csharp
using System.Text.Json;
using Archiver.Configuration;

namespace Archiver.Sources;

public static class BoardUrls
{
    public static string For(BoardConfig board)
    {
        var slug = Uri.EscapeDataString(board.Slug);
        return board.Ats switch
        {
            "greenhouse" => $"https://boards-api.greenhouse.io/v1/boards/{slug}/jobs?content=true",
            "lever" => $"https://api.lever.co/v0/postings/{slug}?mode=json",
            "ashby" => $"https://api.ashbyhq.com/posting-api/job-board/{slug}?includeCompensation=true",
            _ => throw new ArgumentException($"Unknown ATS '{board.Ats}'.", nameof(board)),
        };
    }

    // Number of postings in a board response, or null when the body is not the shape that ATS returns.
    public static int? CountJobs(string ats, byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (ats == "lever")
                return root.ValueKind == JsonValueKind.Array ? root.GetArrayLength() : null;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("jobs", out var jobs)
                && jobs.ValueKind == JsonValueKind.Array
                ? jobs.GetArrayLength()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

`src/Archiver/Sources/JobBoardSource.cs`:
```csharp
using Archiver.Configuration;
using Microsoft.Extensions.Options;

namespace Archiver.Sources;

public sealed record BoardCheck(BoardConfig Board, int? Status, int? Jobs, string? Error)
{
    public bool Ok => Error is null;
}

public sealed class JobBoardSource(IHttpClientFactory httpFactory, IOptions<SourcesOptions> options) : IArchiveSource
{
    public const string SourceName = "boards";
    public const string HttpClientName = "boards";
    public const int MaxParallel = 4;

    public string Name => SourceName;

    public async Task<SourceResult> RunAsync(RunContext ctx, CancellationToken ct)
    {
        var boards = options.Value.Boards;
        if (boards.Count == 0) return SourceResult.Skip("No boards configured.");

        var selected = ctx.DryRun ? boards.Take(1).ToList() : boards;
        var http = httpFactory.CreateClient(HttpClientName);
        await Parallel.ForEachAsync(selected, new ParallelOptions { MaxDegreeOfParallelism = MaxParallel, CancellationToken = ct },
            async (board, token) => await HttpArchiver.GetAndSaveAsync(http, SourceName, BoardUrls.For(board), ctx.Folder.BoardFile(board.Ats, board.Slug), ctx, token));
        return SourceResult.Ran();
    }

    // Checks every configured board without saving anything; used by --verify-boards.
    public async Task<IReadOnlyList<BoardCheck>> VerifyAsync(CancellationToken ct)
    {
        var boards = options.Value.Boards;
        var http = httpFactory.CreateClient(HttpClientName);
        var results = new BoardCheck[boards.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, boards.Count), new ParallelOptions { MaxDegreeOfParallelism = MaxParallel, CancellationToken = ct },
            async (i, token) => results[i] = await CheckAsync(http, boards[i], token));
        return results;
    }

    private static async Task<BoardCheck> CheckAsync(HttpClient http, BoardConfig board, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(BoardUrls.For(board), ct);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode) return new BoardCheck(board, status, null, $"HTTP {status}");
            var jobs = BoardUrls.CountJobs(board.Ats, await response.Content.ReadAsByteArrayAsync(ct));
            return new BoardCheck(board, status, jobs, jobs is null ? "Unexpected response shape" : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new BoardCheck(board, null, null, ex.Message);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all board tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add Greenhouse/Lever/Ashby board source with verify mode"
```

---

### Task 7: Job Bank source

**Files:**
- Create: `src/Archiver/Sources/JobBankCatalog.cs`, `src/Archiver/Sources/JobBankSource.cs`
- Test: `tests/Archiver.Tests/Sources/JobBankCatalogTests.cs`, `JobBankSourceTests.cs`

**Interfaces:**
- Consumes: `JobBankOptions`, `SourcesOptions` (Task 2); `IArchiveSource`, `RunContext`, `SourceResult`, `HttpArchiver.GetAndSaveAsync/DownloadGzipAsync` (Task 4); `RunFolder.JobBankCatalogFile`, `RunFolder.DataRoot` (Task 1); `RequestRecord` (Task 3)
- Produces:
  - `record JobBankResource(string Id, DateOnly Month, string Url)` with `string FileName` → `"yyyy-MM_<id>.csv.gz"`
  - `JobBankCatalog.TryParseMonth(string? name, out DateOnly month) : bool`
  - `JobBankCatalog.ParseEnglishCsvs(byte[] packageShowJson) : IReadOnlyList<JobBankResource>`
  - `JobBankCatalog.MonthsOnDisk(string directory) : IReadOnlySet<DateOnly>`
  - `JobBankCatalog.SelectToDownload(IEnumerable<JobBankResource> all, IReadOnlySet<DateOnly> monthsOnDisk, int backfillMonths) : IReadOnlyList<JobBankResource>`
  - `JobBankSource(IHttpClientFactory, IOptions<SourcesOptions>) : IArchiveSource` with `SourceName` = `"jobbank"` and `HttpClientName` = `"jobbank"`

- [ ] **Step 1: Write the failing tests**

`tests/Archiver.Tests/Sources/JobBankCatalogTests.cs`:
```csharp
using System.Text.Json;
using Archiver.Sources;
using Archiver.Tests.TestSupport;

namespace Archiver.Tests.Sources;

public class JobBankCatalogTests
{
    internal static byte[] Catalog(params (string Id, string Name, string Lang, string Format)[] resources) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            success = true,
            result = new
            {
                resources = resources.Select(r => new { id = r.Id, name = r.Name, format = r.Format, language = new[] { r.Lang }, url = $"https://files.test/{r.Id}.csv" }),
            },
        });

    [Theory]
    [InlineData("August 2026 Job Postings Advertised on Canada's National Job Bank Website", 2026, 8)]
    [InlineData("January 2023 Job Postings", 2023, 1)]
    public void TryParseMonth_reads_the_leading_month_and_year(string name, int year, int month)
    {
        Assert.True(JobBankCatalog.TryParseMonth(name, out var parsed));
        Assert.Equal(new DateOnly(year, month, 1), parsed);
    }

    [Theory]
    [InlineData("Sept 2026 Job Postings")]
    [InlineData("Job Postings")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseMonth_rejects_anything_else(string? name) =>
        Assert.False(JobBankCatalog.TryParseMonth(name, out _));

    [Fact]
    public void ParseEnglishCsvs_keeps_only_english_csv_with_a_month()
    {
        var json = Catalog(
            ("en-aug", "August 2026 Job Postings", "en", "CSV"),
            ("fr-aug", "August 2026 Job Postings", "fr", "CSV"),
            ("en-xls", "July 2026 Job Postings", "en", "XLSX"),
            ("en-odd", "Data dictionary", "en", "CSV"));

        var parsed = JobBankCatalog.ParseEnglishCsvs(json);

        var only = Assert.Single(parsed);
        Assert.Equal(new JobBankResource("en-aug", new DateOnly(2026, 8, 1), "https://files.test/en-aug.csv"), only);
        Assert.Equal("2026-08_en-aug.csv.gz", only.FileName);
    }

    [Fact]
    public void SelectToDownload_takes_newest_months_not_on_disk()
    {
        var all = new[]
        {
            new JobBankResource("a", new DateOnly(2026, 8, 1), "u"),
            new JobBankResource("b", new DateOnly(2026, 7, 1), "u"),
            new JobBankResource("c", new DateOnly(2026, 6, 1), "u"),
            new JobBankResource("d", new DateOnly(2026, 5, 1), "u"),
        };

        var todo = JobBankCatalog.SelectToDownload(all, new HashSet<DateOnly> { new(2026, 7, 1) }, backfillMonths: 3);

        Assert.Equal(new[] { "a", "c" }, todo.Select(r => r.Id));
    }

    [Fact]
    public void MonthsOnDisk_ignores_tmp_and_foreign_files()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "2026-08_a.csv.gz"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "2026-07_b.csv.gz.tmp"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "notes.txt"), "");

        Assert.Equal(new[] { new DateOnly(2026, 8, 1) }, JobBankCatalog.MonthsOnDisk(tmp.Path));
        Assert.Empty(JobBankCatalog.MonthsOnDisk(Path.Combine(tmp.Path, "missing")));
    }
}
```

`tests/Archiver.Tests/Sources/JobBankSourceTests.cs`:
```csharp
using System.IO.Compression;
using System.Net;
using System.Text;
using Archiver.Configuration;
using Archiver.Manifest;
using Archiver.Sources;
using Archiver.Tests.TestSupport;
using Microsoft.Extensions.Options;

namespace Archiver.Tests.Sources;

public class JobBankSourceTests
{
    private static readonly byte[] CatalogJson = JobBankCatalogTests.Catalog(
        ("en-aug", "August 2026 Job Postings", "en", "CSV"),
        ("en-jul", "July 2026 Job Postings", "en", "CSV"),
        ("en-jun", "June 2026 Job Postings", "en", "CSV"),
        ("fr-aug", "August 2026 Job Postings", "fr", "CSV"));

    private static JobBankSource Source(StubHandler handler, int backfill = 2) =>
        new(new StubHttpClientFactory(handler), Options.Create(new SourcesOptions
        {
            JobBank = new JobBankOptions { DatasetId = "ds1", CatalogUrl = "https://catalog.test/package_show", BackfillMonths = backfill },
        }));

    private static StubHandler Healthy() => new(req => req.RequestUri!.Host == "catalog.test"
        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(CatalogJson) }
        : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"col\n{req.RequestUri.AbsolutePath}") });

    [Fact]
    public async Task Downloads_newest_months_gzipped_and_saves_the_catalog()
    {
        using var run = new TestRun();
        var handler = Healthy();

        await Source(handler).RunAsync(run.Context, CancellationToken.None);

        var dir = Path.Combine(run.DataRoot, "jobbank");
        Assert.Equal(new[] { "2026-07_en-jul.csv.gz", "2026-08_en-aug.csv.gz" }, Directory.GetFiles(dir).Select(Path.GetFileName).Order());
        Assert.True(File.Exists(run.Folder.JobBankCatalogFile()));
        await using var gz = new GZipStream(File.OpenRead(Path.Combine(dir, "2026-08_en-aug.csv.gz")), CompressionMode.Decompress);
        Assert.Equal("col\n/en-aug.csv", await new StreamReader(gz, Encoding.UTF8).ReadToEndAsync());
        Assert.Contains("ds1", handler.Requests[0].Query);
    }

    [Fact]
    public async Task A_later_run_downloads_nothing_new()
    {
        using var first = new TestRun();
        await Source(Healthy()).RunAsync(first.Context, CancellationToken.None);
        using var second = new TestRun(dataRoot: first.DataRoot);
        var handler = Healthy();

        var result = await Source(handler).RunAsync(second.Context, CancellationToken.None);

        Assert.Single(handler.Requests); // catalog only
        Assert.Contains("Up to date", result.Note);
    }

    [Fact]
    public async Task Dry_run_reads_the_catalog_but_downloads_no_csv()
    {
        using var run = new TestRun(dryRun: true);
        var handler = Healthy();

        await Source(handler).RunAsync(run.Context, CancellationToken.None);

        Assert.Single(handler.Requests);
        Assert.False(Directory.Exists(Path.Combine(run.DataRoot, "jobbank")));
    }

    [Fact]
    public async Task Catalog_failure_makes_the_source_fail()
    {
        using var run = new TestRun();
        var handler = new StubHandler(_ => StubHandler.Json("{}", HttpStatusCode.InternalServerError));

        await Source(handler).RunAsync(run.Context, CancellationToken.None);

        Assert.Single(handler.Requests);
        Assert.Equal(SourceOutcome.Failed, run.Manifest.CompleteSource("jobbank").Outcome);
    }

    [Fact]
    public async Task Broken_download_is_retried_on_the_next_run()
    {
        using var first = new TestRun();
        var breaking = new StubHandler(req => req.RequestUri!.Host == "catalog.test"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(CatalogJson) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingStream(new byte[1000])) });
        await Source(breaking, backfill: 1).RunAsync(first.Context, CancellationToken.None);

        Assert.Empty(JobBankCatalog.MonthsOnDisk(Path.Combine(first.DataRoot, "jobbank")));
        Assert.Equal(SourceOutcome.Partial, first.Manifest.CompleteSource("jobbank").Outcome);

        using var second = new TestRun(dataRoot: first.DataRoot);
        await Source(Healthy(), backfill: 1).RunAsync(second.Context, CancellationToken.None);
        Assert.Equal(new[] { new DateOnly(2026, 8, 1) }, JobBankCatalog.MonthsOnDisk(Path.Combine(first.DataRoot, "jobbank")));
    }

    [Fact]
    public async Task Unexpected_catalog_shape_is_recorded_not_thrown()
    {
        using var run = new TestRun();
        var handler = new StubHandler(_ => StubHandler.Json("{\"success\":true}"));

        await Source(handler).RunAsync(run.Context, CancellationToken.None);

        Assert.Contains(run.Requests, r => r.Error?.Contains("catalog") == true);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: build FAILS because `JobBankCatalog` and `JobBankSource` don't exist.

- [ ] **Step 3: Implement the catalog logic and source**

`src/Archiver/Sources/JobBankCatalog.cs`:
```csharp
using System.Globalization;
using System.Text.Json;

namespace Archiver.Sources;

public sealed record JobBankResource(string Id, DateOnly Month, string Url)
{
    public string FileName => $"{Month:yyyy-MM}_{Id}.csv.gz";
}

public static class JobBankCatalog
{
    private const string FileSuffix = ".csv.gz";

    // Resource names look like "August 2026 Job Postings Advertised on Canada's National Job Bank Website".
    public static bool TryParseMonth(string? name, out DateOnly month)
    {
        month = default;
        var parts = (name ?? "").Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        if (!DateTime.TryParseExact($"{parts[0]} {parts[1]}", "MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        month = new DateOnly(parsed.Year, parsed.Month, 1);
        return true;
    }

    // Throws JsonException/KeyNotFoundException/InvalidOperationException when the catalog isn't CKAN package_show output.
    public static IReadOnlyList<JobBankResource> ParseEnglishCsvs(byte[] packageShowJson)
    {
        using var doc = JsonDocument.Parse(packageShowJson);
        var list = new List<JobBankResource>();
        foreach (var r in doc.RootElement.GetProperty("result").GetProperty("resources").EnumerateArray())
        {
            if (!string.Equals(Str(r, "format"), "CSV", StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsEnglish(r)) continue;
            if (Str(r, "id") is { Length: > 0 } id && Str(r, "url") is { Length: > 0 } url && TryParseMonth(Str(r, "name"), out var month))
                list.Add(new JobBankResource(id, month, url));
        }
        return list;
    }

    public static IReadOnlySet<DateOnly> MonthsOnDisk(string directory)
    {
        var months = new HashSet<DateOnly>();
        if (!Directory.Exists(directory)) return months;
        foreach (var name in Directory.EnumerateFiles(directory).Select(Path.GetFileName))
        {
            if (name is null || !name.EndsWith(FileSuffix, StringComparison.Ordinal) || name.Length < 8) continue;
            if (DateOnly.TryParseExact(name[..7], "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
                months.Add(month);
        }
        return months;
    }

    // Keeps the newest `backfillMonths` months on disk; anything missing among them is downloaded.
    public static IReadOnlyList<JobBankResource> SelectToDownload(IEnumerable<JobBankResource> all, IReadOnlySet<DateOnly> monthsOnDisk, int backfillMonths) =>
        all.GroupBy(r => r.Month)
            .Select(g => g.First())
            .OrderByDescending(r => r.Month)
            .Take(backfillMonths)
            .Where(r => !monthsOnDisk.Contains(r.Month))
            .ToList();

    private static string? Str(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool IsEnglish(JsonElement r) =>
        r.TryGetProperty("language", out var lang)
        && lang.ValueKind == JsonValueKind.Array
        && lang.EnumerateArray().Any(l => l.ValueKind == JsonValueKind.String && l.GetString() == "en");
}
```

`src/Archiver/Sources/JobBankSource.cs`:
```csharp
using System.Text.Json;
using Archiver.Configuration;
using Archiver.Manifest;
using Microsoft.Extensions.Options;

namespace Archiver.Sources;

public sealed class JobBankSource(IHttpClientFactory httpFactory, IOptions<SourcesOptions> options) : IArchiveSource
{
    public const string SourceName = "jobbank";
    public const string HttpClientName = "jobbank";

    public string Name => SourceName;

    public async Task<SourceResult> RunAsync(RunContext ctx, CancellationToken ct)
    {
        var o = options.Value.JobBank;
        var http = httpFactory.CreateClient(HttpClientName);
        var catalogUrl = $"{o.CatalogUrl}?id={Uri.EscapeDataString(o.DatasetId)}";

        var catalog = await HttpArchiver.GetAndSaveAsync(http, SourceName, catalogUrl, ctx.Folder.JobBankCatalogFile(), ctx, ct);
        if (!catalog.Ok) return SourceResult.Ran("Catalog request failed; no monthly files checked.");

        IReadOnlyList<JobBankResource> resources;
        try
        {
            resources = JobBankCatalog.ParseEnglishCsvs(catalog.Body);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            ctx.Manifest.Record(new RequestRecord(SourceName, catalogUrl, catalog.Status, catalog.Body.Length, 0, null, $"Unexpected catalog format: {ex.Message}"));
            return SourceResult.Ran("Catalog format changed; no monthly files checked.");
        }

        if (ctx.DryRun) return SourceResult.Ran($"Dry run: {resources.Count} English monthly CSVs listed, none downloaded.");

        var dir = Path.Combine(ctx.Folder.DataRoot, "jobbank");
        var todo = JobBankCatalog.SelectToDownload(resources, JobBankCatalog.MonthsOnDisk(dir), o.BackfillMonths);
        foreach (var resource in todo) // one at a time: each file is ~50 MB
            await HttpArchiver.DownloadGzipAsync(http, SourceName, resource.Url, Path.Combine(dir, resource.FileName), ctx, ct);

        return SourceResult.Ran(todo.Count == 0 ? "Up to date; no new monthly files." : $"Attempted {todo.Count} monthly file(s).");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all Job Bank tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add Job Bank open-data source with gzip monthly backfill"
```

---

### Task 8: Manual inbox and the run orchestrator

**Files:**
- Create: `src/Archiver/Inbox.cs`, `src/Archiver/ArchiveRunner.cs`
- Test: `tests/Archiver.Tests/ArchiveRunnerTests.cs`

**Interfaces:**
- Consumes: `RunFolder.Create` (Task 1); `SourcesOptions` (Task 2); `ManifestBuilder`, `ManifestWriter`, `Outcomes` (Task 3); `IArchiveSource`, `RunContext`, `SourceResult` (Task 4)
- Produces:
  - `Inbox.EnsureAndCount(string dataRoot) : int`
  - `ArchiveRunner(IEnumerable<IArchiveSource>, IOptions<SourcesOptions>, TimeProvider, ILogger<ArchiveRunner>)` with `RunAsync(string dataRoot, bool dryRun, CancellationToken) : Task<int>` (returns the exit code)

- [ ] **Step 1: Write the failing tests**

`tests/Archiver.Tests/ArchiveRunnerTests.cs`:
```csharp
using System.Text.Json;
using Archiver.Configuration;
using Archiver.Manifest;
using Archiver.Sources;
using Archiver.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Archiver.Tests;

public class ArchiveRunnerTests
{
    private sealed class FakeSource(string name, Func<RunContext, Task<SourceResult>> run) : IArchiveSource
    {
        public string Name => name;
        public Task<SourceResult> RunAsync(RunContext ctx, CancellationToken ct) => run(ctx);
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static ArchiveRunner Runner(params IArchiveSource[] sources) =>
        new(sources, Options.Create(new SourcesOptions()), new FixedTime(new DateTimeOffset(2026, 9, 29, 7, 0, 0, TimeSpan.Zero)), NullLogger<ArchiveRunner>.Instance);

    private static FakeSource Succeeds(string name) => new(name, ctx =>
    {
        ctx.Manifest.Record(new RequestRecord(name, "https://a.test/?app_key=SECRET", 200, 10, 1, "raw/x.json", null));
        return Task.FromResult(SourceResult.Ran());
    });

    private static FakeSource Throws(string name) => new(name, _ => throw new InvalidOperationException("kaboom"));

    private static FakeSource Skips(string name) => new(name, _ => Task.FromResult(SourceResult.Skip("not today")));

    [Fact]
    public async Task Writes_manifest_and_returns_0_when_any_source_succeeds()
    {
        using var tmp = new TempDir();

        var code = await Runner(Succeeds("adzuna"), Throws("boards"), Skips("jobbank")).RunAsync(tmp.Path, dryRun: false, CancellationToken.None);

        Assert.Equal(ExitCodes.Ok, code);
        var json = File.ReadAllText(Path.Combine(tmp.Path, "raw", "2026-09-29_0700", "manifest.json"));
        using var doc = JsonDocument.Parse(json);
        var sources = doc.RootElement.GetProperty("sources");
        Assert.Equal("succeeded", sources.GetProperty("adzuna").GetProperty("outcome").GetString());
        Assert.Equal("failed", sources.GetProperty("boards").GetProperty("outcome").GetString());
        Assert.Equal("kaboom", sources.GetProperty("boards").GetProperty("note").GetString());
        Assert.Equal("skipped", sources.GetProperty("jobbank").GetProperty("outcome").GetString());
        Assert.DoesNotContain("SECRET", json);
    }

    [Fact]
    public async Task Returns_1_when_nothing_was_collected()
    {
        using var tmp = new TempDir();
        var code = await Runner(Throws("adzuna"), Skips("boards")).RunAsync(tmp.Path, dryRun: false, CancellationToken.None);
        Assert.Equal(ExitCodes.AllFailed, code);
    }

    [Fact]
    public async Task Dry_run_writes_to_the_dryrun_folder()
    {
        using var tmp = new TempDir();
        await Runner(Succeeds("adzuna")).RunAsync(tmp.Path, dryRun: true, CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(tmp.Path, "raw", "_dryrun", "manifest.json")));
    }

    [Fact]
    public async Task Creates_inbox_readme_and_counts_txt_files()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(Path.Combine(tmp.Path, "inbox"));
        File.WriteAllText(Path.Combine(tmp.Path, "inbox", "a.txt"), "x");
        File.WriteAllText(Path.Combine(tmp.Path, "inbox", "b.txt"), "x");
        File.WriteAllText(Path.Combine(tmp.Path, "inbox", "c.md"), "x");

        await Runner(Succeeds("adzuna")).RunAsync(tmp.Path, dryRun: false, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(tmp.Path, "inbox", "README.md")));
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(tmp.Path, "raw", "2026-09-29_0700", "manifest.json")));
        Assert.Equal(2, doc.RootElement.GetProperty("inboxFiles").GetInt32());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: build FAILS because `ArchiveRunner` and `Inbox` don't exist.

- [ ] **Step 3: Implement the inbox and runner**

`src/Archiver/Inbox.cs`:
```csharp
namespace Archiver;

// data/inbox holds postings pasted by hand (LinkedIn, Indeed, bank career sites); phase 1 parses them.
public static class Inbox
{
    private const string ReadmeText = """
        # Job posting inbox

        Paste one job posting per `.txt` file here. Phase 1 of the tracker reads them.

        Suggested file name: `2026-10-01-company-job-title.txt`

        Start the file with these header lines, then a blank line, then the full posting text:

            URL: https://www.linkedin.com/jobs/view/...
            Company: Example Corp
            Title: Senior .NET Developer
            Location: Toronto, ON (Hybrid)
            Saved: 2026-10-01

        Only `.txt` files are counted. This folder is never committed to git.
        """;

    public static int EnsureAndCount(string dataRoot)
    {
        var dir = Path.Combine(dataRoot, "inbox");
        Directory.CreateDirectory(dir);
        var readme = Path.Combine(dir, "README.md");
        if (!File.Exists(readme)) File.WriteAllText(readme, ReadmeText);
        return Directory.EnumerateFiles(dir, "*.txt").Count();
    }
}
```

`src/Archiver/ArchiveRunner.cs`:
```csharp
using System.Reflection;
using Archiver.Configuration;
using Archiver.Manifest;
using Archiver.Sources;
using Archiver.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Archiver;

public sealed class ArchiveRunner(IEnumerable<IArchiveSource> sources, IOptions<SourcesOptions> options, TimeProvider time, ILogger<ArchiveRunner> log)
{
    private static readonly string ToolVersion =
        typeof(ArchiveRunner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    public async Task<int> RunAsync(string dataRoot, bool dryRun, CancellationToken ct)
    {
        var started = time.GetLocalNow();
        var folder = RunFolder.Create(dataRoot, started, dryRun);
        var manifest = new ManifestBuilder(folder.RunId, dryRun, ToolVersion, started, options.Value.Adzuna.MaxCallsPerRun)
        {
            InboxFiles = Inbox.EnsureAndCount(dataRoot),
        };
        var ctx = new RunContext(folder, manifest, dryRun);
        log.LogInformation("Run {RunId} started{DryRun}", folder.RunId, dryRun ? " (dry run)" : "");

        // Sources run one after another: keeps Adzuna pacing simple and the log readable.
        foreach (var source in sources)
        {
            log.LogInformation("Source {Source}: starting", source.Name);
            try
            {
                var result = await source.RunAsync(ctx, ct);
                if (result.Skipped) manifest.SkipSource(source.Name, result.Note ?? "Skipped.");
                else manifest.CompleteSource(source.Name, result.Note);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Source {Source} crashed", source.Name);
                manifest.FailSource(source.Name, ex.Message);
            }
        }

        var finished = manifest.Build(time.GetLocalNow());
        await ManifestWriter.WriteAsync(finished, folder.ManifestFile(), ct);
        foreach (var (name, s) in finished.Sources)
            log.LogInformation("Source {Source}: {Outcome} ({Ok} ok, {Failed} failed) {Note}", name, s.Outcome, s.Ok, s.Failed, s.Note);
        log.LogInformation("Manifest: {Path}", folder.ManifestFile());

        return Outcomes.ExitCode(finished.Sources.Values.Select(s => s.Outcome));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all runner tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add run orchestrator and manual inbox"
```

---

### Task 9: Host wiring, CLI and a live dry run

**Files:**
- Create: `src/Archiver/HttpSetup.cs`
- Modify: `src/Archiver/Program.cs` (replace the template content entirely)
- Modify: `src/Archiver/Archiver.csproj` (add `<Version>0.1.0</Version>`; `UserSecretsId` is added by the `user-secrets init` command)

**Interfaces:**
- Consumes: everything above; `AdzunaSource.HttpClientName`, `JobBoardSource.HttpClientName`, `JobBankSource.HttpClientName`, `SourcesValidator.Validate`, `RepoRoot.Find`, `ExitCodes`
- Produces: the `Archiver` executable, with `--dry-run` and `--verify-boards`

- [ ] **Step 1: Enable user-secrets and set the version**

```bash
dotnet user-secrets init --project src/Archiver
```

Then, in `src/Archiver/Archiver.csproj` inside the first `<PropertyGroup>`, add:
```xml
    <Version>0.1.0</Version>
```

- [ ] **Step 2: Store the Adzuna credentials outside the repo**

Use the App ID and App Key the user supplied in the conversation. They go only into user-secrets (`%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`) and never into any repo file:
```bash
dotnet user-secrets set "Adzuna:AppId" "<app id from user>" --project src/Archiver
dotnet user-secrets set "Adzuna:AppKey" "<app key from user>" --project src/Archiver
```
Expected: `Successfully saved Adzuna:AppId to the secret store.` (and the same for AppKey). Then check that the repo contains neither value: run `git grep -n "<app key from user>"` and expect no output.

- [ ] **Step 3: Implement HTTP client registration**

`src/Archiver/HttpSetup.cs`:
```csharp
using Archiver.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Archiver;

public static class HttpSetup
{
    private const string UserAgent = "JobMarketTracker/0.1 (personal job-market research)";

    // The standard resilience handler retries 429/5xx with exponential backoff + jitter and honours Retry-After.
    public static IServiceCollection AddArchiverHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient(AdzunaSource.HttpClientName, ConfigureDefaults)
            .AddStandardResilienceHandler();

        // Big boards (Stripe, Databricks) return several MB with full descriptions.
        services.AddHttpClient(JobBoardSource.HttpClientName, ConfigureDefaults)
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(4);
            });

        // Monthly CSVs are ~50 MB; the body is streamed after headers arrive.
        services.AddHttpClient(JobBankSource.HttpClientName, ConfigureDefaults)
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(8);
            });

        return services;
    }

    private static void ConfigureDefaults(HttpClient client)
    {
        client.Timeout = Timeout.InfiniteTimeSpan; // timeouts are owned by the resilience pipeline
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }
}
```

- [ ] **Step 4: Implement Program.cs**

`src/Archiver/Program.cs`:
```csharp
using Archiver;
using Archiver.Configuration;
using Archiver.Manifest;
using Archiver.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

const string Usage = "Usage: Archiver [--dry-run | --verify-boards]";

var dryRun = args.Contains("--dry-run");
var verifyBoards = args.Contains("--verify-boards");
var unknown = args.Where(a => a is not ("--dry-run" or "--verify-boards")).ToList();
if (unknown.Count > 0 || (dryRun && verifyBoards))
{
    Console.Error.WriteLine(unknown.Count > 0 ? $"Unknown argument(s): {string.Join(' ', unknown)}" : "Pick one mode.");
    Console.Error.WriteLine(Usage);
    return ExitCodes.ConfigInvalid;
}

var root = RepoRoot.Find(Directory.GetCurrentDirectory()) ?? RepoRoot.Find(AppContext.BaseDirectory);
if (root is null)
{
    Console.Error.WriteLine("Could not find config/sources.json in this folder or any parent. Run from the repo root.");
    return ExitCodes.ConfigInvalid;
}

IHost host;
try
{
    // Host args are not passed through: our flags are not configuration keys.
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = root });
    builder.Configuration
        .AddJsonFile(Path.Combine(root, "config", "sources.json"), optional: false, reloadOnChange: false)
        .AddUserSecrets<ArchiveRunner>(optional: true)
        .AddEnvironmentVariables();
    builder.Logging
        .AddFilter("System.Net.Http.HttpClient", LogLevel.Warning)
        .AddFilter("Polly", LogLevel.Warning);

    builder.Services.Configure<SourcesOptions>(builder.Configuration);
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddArchiverHttpClients();
    builder.Services.AddSingleton<JobBoardSource>();
    builder.Services.AddSingleton<IArchiveSource, AdzunaSource>();
    builder.Services.AddSingleton<IArchiveSource>(sp => sp.GetRequiredService<JobBoardSource>());
    builder.Services.AddSingleton<IArchiveSource, JobBankSource>(); // last: slowest, largest downloads
    builder.Services.AddSingleton<ArchiveRunner>();
    host = builder.Build();

    var errors = SourcesValidator.Validate(host.Services.GetRequiredService<IOptions<SourcesOptions>>().Value);
    if (errors.Count > 0)
    {
        foreach (var error in errors) Console.Error.WriteLine($"Config error: {error}");
        return ExitCodes.ConfigInvalid;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Config error: {ex.Message}");
    return ExitCodes.ConfigInvalid;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

if (verifyBoards)
{
    var checks = await host.Services.GetRequiredService<JobBoardSource>().VerifyAsync(cts.Token);
    foreach (var c in checks)
        Console.WriteLine($"{(c.Ok ? "OK  " : "FAIL")} {c.Board.Ats}/{c.Board.Slug}: {(c.Ok ? $"{c.Jobs} jobs" : c.Error)}");
    Console.WriteLine($"{checks.Count(c => c.Ok)}/{checks.Count} boards OK");
    return checks.All(c => c.Ok) ? ExitCodes.Ok : ExitCodes.AllFailed;
}

return await host.Services.GetRequiredService<ArchiveRunner>().RunAsync(Path.Combine(root, "data"), dryRun, cts.Token);
```

- [ ] **Step 5: Build and run the unit tests**

Run: `dotnet build src/Archiver -warnaserror && dotnet test`
Expected: the app builds with 0 warnings, and all tests PASS.

- [ ] **Step 6: Check the configuration-error path**

Run: `dotnet run --project src/Archiver -- --bogus`
Expected: prints `Unknown argument(s): --bogus` and the usage line; the exit code (`echo $?`) is `2`.

- [ ] **Step 7: Live board verification**

Run: `dotnet run --project src/Archiver -c Release -- --verify-boards`
Expected: one line per board, ending in `39/39 boards OK`, exit code 0. Remove any board that FAILs from `config/sources.json`, re-run until every board passes, and note the removals in the commit message.

- [ ] **Step 8: Live dry run**

Run: `dotnet run --project src/Archiver -c Release -- --dry-run`
Expected: the log shows `adzuna: Succeeded (1 ok, 0 failed)`, `boards: Succeeded (1 ok, 0 failed)` and `jobbank: Succeeded (1 ok, 0 failed) Dry run: … English monthly CSVs listed…`; exit code 0. Then check the credentials weren't written anywhere they shouldn't be:
```bash
grep -c "app_key" data/raw/_dryrun/manifest.json   # expect 0
ls data/raw/_dryrun/adzuna/search/                # expect net-developer-gta-p1.json
```

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "Wire host, resilient HTTP clients and CLI modes"
```

---

### Task 10: Scheduling, docs, first real run

**Files:**
- Create: `scripts/register-task.ps1`, `README.md`, `docs/decisions/0001-store-raw-parse-later.md`, `docs/decisions/0002-official-apis-only.md`, `docs/decisions/0003-dotnet-console-not-script.md`
- Modify: `.gitignore` (add `.artifacts/`)

**Interfaces:**
- Consumes: the `Archiver` executable (Task 9)
- Produces: the scheduled task `JobMarketTracker Archiver` (Tue and Fri 07:00), logging to `data/archiver.log`

- [ ] **Step 1: Ignore publish output**

Append to `.gitignore`:
```
# Published archiver used by the scheduled task
.artifacts/
```

- [ ] **Step 2: Write the scheduling script**

`scripts/register-task.ps1`:
```powershell
<#
.SYNOPSIS
Publishes the archiver and registers (or updates) the Windows scheduled task that runs it
every Tuesday and Friday. Re-run this script after changing the code.
#>
[CmdletBinding()]
param(
    [string]$TaskName = 'JobMarketTracker Archiver',
    [DateTime]$At = '07:00'
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root '.artifacts\archiver'
$dataDir = Join-Path $root 'data'
$logFile = Join-Path $dataDir 'archiver.log'

dotnet publish (Join-Path $root 'src\Archiver') -c Release -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
New-Item -ItemType Directory -Force $dataDir | Out-Null

$exe = Join-Path $publishDir 'Archiver.exe'
# cmd.exe appends console output to a log so crashes before the manifest is written are still visible.
$action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument "/c `"`"$exe`" >> `"$logFile`" 2>&1`"" -WorkingDirectory $root
$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Tuesday, Friday -At $At
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 2)

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
    -Description 'Archives raw job-market snapshots (Adzuna, job boards, Job Bank). See README.md.' -Force | Out-Null

Write-Host "Registered '$TaskName': Tuesday and Friday at $($At.ToString('HH:mm')), catching up after missed runs."
Write-Host "Runs: $exe (working directory $root)"
Write-Host "Log:  $logFile"
```

- [ ] **Step 3: Write the decision notes**

`docs/decisions/0001-store-raw-parse-later.md`:
```markdown
# 0001 — Store raw responses, parse later

**Status:** Accepted (2026-09-27)

## Context
Trend analysis needs history, and history only exists if we collect it from now on. The parsing rules (skill taxonomy, title normalization, dedupe) will change many times in phase 1 and after.

## Decision
Phase 0 saves every response byte-for-byte into dated run folders and does no parsing. Phase 1 builds its database by re-processing the whole archive.

## Consequences
- A parsing bug never loses data: fix the parser and re-run it over the archive.
- Collection could start weeks before the processing pipeline existed.
- Costs disk space (Job Bank CSVs are ~50 MB/month, stored gzipped) and duplicate postings across runs. Dedupe happens in phase 1.
- Alternative rejected: parsing on ingest into a database. It's faster to query on day one, but every schema mistake is permanent.
```

`docs/decisions/0002-official-apis-only.md`:
```markdown
# 0002 — Official APIs and open data only; no scraping

**Status:** Accepted (2026-09-27)

## Context
LinkedIn and Indeed carry most Toronto tech postings. Both forbid scraping in their terms and actively block bots. Most bank careers sites run on Workday, whose JSON endpoints are undocumented.

## Decision
Collect only from documented, public sources:
- the Adzuna API (personal research is permitted; Adzuna must be credited as the source)
- public Greenhouse, Lever and Ashby job-board APIs
- Job Bank open data (Government of Canada)

Postings from LinkedIn, Indeed and bank sites enter through `data/inbox/`, pasted by hand.

## Consequences
- No legal or account risk, and the sources don't break when a site changes its HTML.
- Adzuna gives breadth but only description snippets. Boards and the inbox give full text. Phase 1 must track which source each statistic comes from.
- Coverage of bank roles depends on Adzuna and manual pasting.
```

`docs/decisions/0003-dotnet-console-not-script.md`:
```markdown
# 0003 — A .NET console app instead of a quick script

**Status:** Accepted (2026-09-27)

## Context
A Python or PowerShell script could fetch these URLs in 50 lines. But the archiver is the first component of a system that phase 1 grows into an ingestion worker, and the project doubles as a portfolio piece in the target stack.

## Decision
A .NET 10 Generic Host console app with DI, `IHttpClientFactory` and `Microsoft.Extensions.Http.Resilience`.

## Consequences
- Retries, backoff, `Retry-After` handling and timeouts come from a standard library rather than hand-written code.
- The same host and DI setup carries over to the phase 1 worker and the phase 4 Azure Functions deployment.
- More ceremony than a script, which is accepted for the reasons above.
```

- [ ] **Step 4: Write the README**

`README.md`:
```markdown
# Job Market Tracker

A personal tracker for the Toronto / GTA software job market. It shows which skills employers ask for, what they pay, and where your gaps are.

**Phase 0 (current):** the archiver. Twice a week it saves raw snapshots from:

| Source | What | Where it lands |
|---|---|---|
| [Adzuna API](https://developer.adzuna.com/) | GTA + remote-Canada search results, salary histograms, top companies | `data/raw/<run>/adzuna/` |
| Greenhouse / Lever / Ashby | Full public job boards of ~40 Toronto-relevant tech companies | `data/raw/<run>/boards/` |
| [Job Bank open data](https://open.canada.ca/data/en/dataset/ea639e28-c0fc-48bf-b5dd-b8899bd43072) | Monthly national postings CSV (newest 12 months, gzipped) | `data/jobbank/` |
| Manual inbox | Postings pasted from LinkedIn / Indeed / bank sites | `data/inbox/*.txt` |

Every run writes `data/raw/<run>/manifest.json`, recording each request, what failed and the Adzuna call count. `data/` is never committed.

Jobs data partly by [Adzuna](https://www.adzuna.ca).

## Setup

    dotnet user-secrets set "Adzuna:AppId"  "<your app id>"  --project src/Archiver
    dotnet user-secrets set "Adzuna:AppKey" "<your app key>" --project src/Archiver

## Commands (from the repo root)

    dotnet test                                                  # unit tests
    dotnet run --project src/Archiver -c Release -- --verify-boards   # check every board still answers
    dotnet run --project src/Archiver -c Release -- --dry-run         # one request per source → data/raw/_dryrun
    dotnet run --project src/Archiver -c Release                      # full run
    powershell -File scripts/register-task.ps1                        # publish + schedule Tue/Fri 07:00

Exit codes: `0` = something was collected, `1` = every source failed, `2` = bad config or arguments.

## Configuration

`config/sources.json` holds the search terms, paging and budget (80 Adzuna calls per run), the board list and the Job Bank dataset. Credentials only ever come from user-secrets or the `ADZUNA__APPID` / `ADZUNA__APPKEY` env vars.

## Roadmap

1. Ingestion worker: dedupe, normalize, skill tagging, Postgres in Docker, CLI report
2. API + dashboard + gap analysis (.NET Aspire, OpenTelemetry), snapshot export
3. LLM skill extraction (Azure OpenAI via Microsoft.Extensions.AI) + inbox parsing
4. Azure deployment (Container Apps, Functions timer, Bicep/Terraform, GitHub Actions, Key Vault)

Design decisions: [`docs/decisions/`](docs/decisions/).
```

- [ ] **Step 5: First real run**

Run: `dotnet run --project src/Archiver -c Release`
Expected (takes several minutes because of the Adzuna pacing and the 12 Job Bank downloads):
- exit code 0
- the log shows `adzuna: Succeeded` with at most 80 calls
- `boards: Succeeded` (or `Partial` with the failures named)
- `jobbank: Succeeded … Attempted 12 monthly file(s).`

Then:
```bash
ls data/raw/                        # one dated run folder (plus _dryrun)
ls data/jobbank/ | wc -l            # 12
grep -c "app_key" data/raw/*/manifest.json   # 0 for every file
```

- [ ] **Step 6: Register the scheduled task**

Run: `powershell -ExecutionPolicy Bypass -File scripts/register-task.ps1`
Expected: `Registered 'JobMarketTracker Archiver': Tuesday and Friday at 07:00…`. Confirm with:
```powershell
Get-ScheduledTask -TaskName 'JobMarketTracker Archiver' | Get-ScheduledTaskInfo
```
Expected: `NextRunTime` is the coming Tuesday or Friday at 07:00.

- [ ] **Step 7: Final check and commit**

Run: `dotnet test && git status --short`
Expected: all tests pass; `git status` shows only the Task 10 files (nothing under `data/` or `.artifacts/`).

```bash
git add -A
git commit -m "Add scheduling script, README and decision notes"
```
