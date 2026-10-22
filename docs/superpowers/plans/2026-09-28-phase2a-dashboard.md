# Phase 2a Interactive Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A local, always-current dashboard (overview, skill explorer, salary explorer, postings browser). It is served by an ASP.NET Core minimal API that reuses phase 1's analysis code, and runs through a .NET Aspire AppHost with OpenTelemetry, alongside a React + TypeScript (Vite) front end.

**Architecture:**
- Phase 1's code moves into a `Tracker.Core` class library shared by `Processor` and the new `Api`.
- The API keeps a `MarketSnapshot` (in-scope postings, runs, Job Bank rows, warnings) in memory in a singleton `MarketData`, which reloads when the latest run changes (checked at most once a minute).
- Pure query functions answer each endpoint.
- The Aspire AppHost connects the API to the existing Compose Postgres, starts the Vite app with the API URL injected, and shows traces, logs and metrics.

**Tech Stack:**
- .NET 10: ASP.NET Core minimal APIs, EF Core + Npgsql.
- Aspire 13.5.4: AppHost SDK, `Aspire.Hosting.JavaScript`, `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`, service-defaults template.
- Tests: xUnit, `Microsoft.AspNetCore.Mvc.Testing`, Testcontainers.
- Front end:
  - React 19.3.0, react-router 7.18.4, @tanstack/react-query 5.104.0, recharts 3.10.1
  - vite 7.3.6, @vitejs/plugin-react 5.2.0, typescript 5.9.3
  - vitest 3.2.7, @testing-library/react 16.3.3, @testing-library/jest-dom 6.10.0, jsdom 26.1.0

**Spec:** `docs/superpowers/specs/2026-09-28-phase2a-dashboard-design.md` (builds on the phase 1 spec and plan)

## Global Constraints

- **Target framework and packages.** Target `net10.0`. Aspire packages and the SDK are **13.5.4**. npm packages use the exact versions listed above, pinned without `^`.
- **Refactor.** Namespaces change from `Processor.*` to `Tracker.*`; the test namespaces become `Tracker.Core.Tests.*` and `Tracker.TestSupport`. The migration id `20260928093654_InitialCreate` is unchanged, and no new migration is created.
- **Refactor gate.** After Task 1, all 101 archiver and 238 processor tests pass, and a live `Processor --report-only` produces a `latest.fragment.html` identical to the one before the move, apart from the "generated" line.
- **Processor user-secrets** are looked up through `AddUserSecrets<Program>()`, the Processor assembly's own class.
- **The API is read-only.** It never writes to the database. The connection name is `tracker` (AppHost user-secrets `ConnectionStrings:tracker`). The AppHost uses `AddConnectionString("tracker")` and never adds a Postgres container.
- **Filter parameters:**
  - `families`: comma list; enum names or snake keys, case-insensitive. The default is the profile's target families.
  - `seniority`: comma list of enum names, case-insensitive. The default is all.
  - `where`: `all` (default), `gta` or `remote`.
  - `window`: `30` (default), `60`, `90` or `all`.
  - `text`: `full` (default), `snippet` or `all`.
  - Postings paging: `page` ≥ 1, and `pageSize` from 1 to 100 (default 25).
  - Anything unknown returns 400 problem details naming the parameter.
- **Serialization.** JSON uses camelCase, and enums are serialized as their names.
- **Result limits.** A skill detail returns at most 50 example postings, a salary view at most 100 examples, and co-skills and companies at most 10 each. Histogram buckets are 20,000 wide. Salary medians need n ≥ 5 (phase 1's rule).
- **Database down.** The response is 503 problem details with detail text "The tracker database isn't running. Start Docker Desktop (and `docker compose up -d postgres`)." If data was loaded before, the last good snapshot is served with the header `X-Data-Stale: true` and `stale: true` in `/api/meta`.
- **Telemetry.** The ActivitySource `Tracker.MarketData` emits a span `LoadMarketData` tagged `tracker.runs` and `tracker.postings`. The Meter `Tracker.MarketData` has a counter `tracker.market_data.reloads`.
- **Front end.** Files are TypeScript with `strict` on. The filter state lives in the URL query string. The app reuses the report's palette tokens and fonts, with light and dark themes following the OS.
- **Code containing backslashes** is written with the Write/Edit tools, never shell heredocs.

## Review Focus

1. **A collection lands while the dashboard is open.** Within about 60 s of the rebuild committing, the next request must show the new run. There must be no half-loaded state and no concurrent double reload. Owner: Task 5.
2. **Postgres stops after the dashboard has loaded data.** Pages must keep working from the last snapshot and clearly say the data is stale. A cold start with no database must show the 503 message, not a blank page. Owner: Task 5 (service), Task 6 (stale header) and Task 8 (banners).
3. **Filter combinations that match nothing** (e.g. `where=remote&seniority=Junior&window=30`). Every endpoint must return empty results (never divide by zero or produce NaN), and every page must show an empty state. Owner: Task 4 (queries) and Tasks 8–10 (pages).
4. **Skill names that need URL encoding** (`C#`, `.NET`, `CI/CD`, `Estimation & Planning`, `OAuth / OIDC`). The skill-detail route and API must round-trip them. Owner: Task 6 (API) and Task 9 (links and route).
5. **The refactor silently changes numbers or loses secrets.** The report must stay identical, and Processor must still find its connection string. Owner: Task 1.

---

## File Structure

```
src/Tracker.Core/                       Task 1 (moved from src/Processor; namespaces Tracker.*)
  Analysis/Analyzer.cs                  Task 2 (SalaryCells public)
  Analysis/AnalysisLoader.cs            Task 2 (location fields)
  Data/TrackerTables.cs                 Task 5 (shared TRUNCATE/LOCK table order)
src/Processor/Program.cs                Task 1 (usings, AddUserSecrets<Program>)
src/ServiceDefaults/                    Task 3 (aspire-servicedefaults template)
src/Api/
  Api.csproj                            Task 3
  Program.cs                            Task 3 (skeleton) → Task 6 (endpoints)
  MarketSettings.cs                     Task 3
  Market/MarketSnapshot.cs              Task 4
  Market/IMarketSource.cs               Task 5 (MarketLoad, DatabaseUnavailableException)
  Market/MarketData.cs                  Task 5
  Market/DbMarketSource.cs              Task 5
  Queries/Filters.cs                    Task 4
  Queries/PostingQueries.cs             Task 4
  Queries/SkillQueries.cs               Task 4
  Queries/SalaryQueries.cs              Task 4
  Queries/MetaQueries.cs                Task 4
  Endpoints/MarketEndpoints.cs          Task 6
  Endpoints/ApiExceptionHandler.cs      Task 6
src/Dashboard/
  package.json, vite.config.ts, tsconfig.json, index.html   Task 7
  src/theme.css, src/api.ts, src/filters.ts, src/format.ts  Task 7
  src/test/setup.ts, src/test/render.tsx                    Task 7
  src/main.tsx, src/App.tsx                                 Task 8 (routes added in Tasks 9–10)
  src/components/{FilterBar,States,LevelChip,PostingList}.tsx  Task 8
  src/pages/Overview.tsx                                    Task 8
  src/pages/{Skills,SkillDetail}.tsx                        Task 9
  src/pages/{Salaries,Postings}.tsx                         Task 10
  src/**/*.test.ts(x)                                       Tasks 7–10
src/AppHost/                            Task 11 (aspire-apphost template + AppHost.cs)
tests/Tracker.TestSupport/              Task 1 (TempDir, FixtureArchive, PostgresFixture, Make, RepoConfig)
tests/Tracker.Core.Tests/               Task 1 (renamed from Processor.Tests)
tests/Api.Tests/                        Tasks 3–6
scripts/dashboard.cmd                   Task 11
docs/decisions/0007..0009-*.md, README  Task 12
```

All commands run from the repo root.

---

### Task 1: Extract `Tracker.Core` and shared test support (refactor gate)

**Files:**
- Create: `src/Tracker.Core/Tracker.Core.csproj`, `tests/Tracker.TestSupport/Tracker.TestSupport.csproj`, `tests/Tracker.TestSupport/RepoConfig.cs`, `tests/Tracker.Core.Tests/TestSupport/PostgresCollection.cs`
- Move: `src/Processor/{Analysis,Configuration,Data,Interpreting,Reading,Rebuild,Reporting}/`, `Stats.cs`, `ErrorText.cs` → `src/Tracker.Core/`; `tests/Processor.Tests` → `tests/Tracker.Core.Tests`; `TempDir.cs`, `FixtureArchive.cs`, `PostgresFixture.cs`, `Make.cs` → `tests/Tracker.TestSupport/`
- Modify: `src/Processor/Processor.csproj`, `src/Processor/Program.cs`, `tests/Tracker.Core.Tests/Tracker.Core.Tests.csproj`, `tests/Tracker.Core.Tests/Configuration/ProcessorOptionsTests.cs`, `JobMarketTracker.slnx`

**Interfaces:**
- Produces:
  - library `Tracker.Core`, with namespaces `Tracker.Analysis`, `Tracker.Configuration`, `Tracker.Data`, `Tracker.Interpreting`, `Tracker.Reading`, `Tracker.Rebuild`, `Tracker.Reporting` and `Tracker` (`Stats`, `ErrorText`);
  - library `Tracker.TestSupport` (namespace `Tracker.TestSupport`) with `TempDir`, `FixtureArchive`, `PostgresFixture`, `Make` and `RepoConfig`, the last with `Root`, `PathOf(file)` and `Options()`.

- [ ] **Step 1: Capture the report before the move**

```bash
dotnet run --project src/Processor -c Release -- --report-only
cp data/reports/latest.fragment.html "$TEMP/report-before.html"
```
Expected: `Report written: …`, exit 0.

- [ ] **Step 2: Create the projects and move the files**

```bash
dotnet new classlib -n Tracker.Core -o src/Tracker.Core -f net10.0
rm src/Tracker.Core/Class1.cs
git mv src/Processor/Analysis src/Processor/Configuration src/Processor/Data src/Processor/Interpreting src/Processor/Reading src/Processor/Rebuild src/Processor/Reporting src/Tracker.Core/
git mv src/Processor/Stats.cs src/Processor/ErrorText.cs src/Tracker.Core/
git mv tests/Processor.Tests tests/Tracker.Core.Tests
git mv tests/Tracker.Core.Tests/Processor.Tests.csproj tests/Tracker.Core.Tests/Tracker.Core.Tests.csproj
dotnet new classlib -n Tracker.TestSupport -o tests/Tracker.TestSupport -f net10.0
rm tests/Tracker.TestSupport/Class1.cs
git mv tests/Tracker.Core.Tests/TestSupport/TempDir.cs tests/Tracker.Core.Tests/TestSupport/FixtureArchive.cs tests/Tracker.Core.Tests/TestSupport/PostgresFixture.cs tests/Tracker.Core.Tests/TestSupport/Make.cs tests/Tracker.TestSupport/
```

- [ ] **Step 3: Project files**

`src/Tracker.Core/Tracker.Core.csproj` (replace the whole file):
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="EFCore.NamingConventions" Version="10.0.1" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.12" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
  </ItemGroup>

</Project>
```

`src/Processor/Processor.csproj`: keep the `PropertyGroup` exactly as it is (including `UserSecretsId`). Replace the package `ItemGroup` with the lines below. The Design package stays so `dotnet ef` can use Processor as its startup project; it's `PrivateAssets=all` in Tracker.Core and doesn't flow through.
```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.12" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Tracker.Core\Tracker.Core.csproj" />
  </ItemGroup>
```

`tests/Tracker.TestSupport/Tracker.TestSupport.csproj` (replace the whole file):
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.15.0" />
    <PackageReference Include="xunit.extensibility.core" Version="2.9.3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Tracker.Core\Tracker.Core.csproj" />
  </ItemGroup>

</Project>
```
(`PostgresFixture` uses `IAsyncLifetime` through the global `Xunit` using that used to come from the test project.)

In `tests/Tracker.Core.Tests/Tracker.Core.Tests.csproj`:
- remove the `Testcontainers.PostgreSql` package reference;
- replace the project reference with:
```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\Tracker.Core\Tracker.Core.csproj" />
    <ProjectReference Include="..\Tracker.TestSupport\Tracker.TestSupport.csproj" />
  </ItemGroup>
```

- [ ] **Step 4: Split the collection definition and RepoConfig out**

xUnit needs a collection definition in the assembly that holds the tests, so it stays in the test project. The fixture itself moves.
- In `tests/Tracker.TestSupport/PostgresFixture.cs`, delete the `[CollectionDefinition("postgres")] public sealed class PostgresCollection …;` declaration.
- Create `tests/Tracker.Core.Tests/TestSupport/PostgresCollection.cs`:
```csharp
using Tracker.TestSupport;

namespace Tracker.Core.Tests.TestSupport;

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
```

Move the `RepoConfig` class out of `tests/Tracker.Core.Tests/Configuration/ProcessorOptionsTests.cs`, so that file keeps only `ProcessorOptionsTests`. Add `using Tracker.TestSupport;` to that file's usings: it declares the old `RepoConfig` namespace rather than importing it, so the rename script won't add the using. Create `tests/Tracker.TestSupport/RepoConfig.cs`:
```csharp
using Tracker.Configuration;

namespace Tracker.TestSupport;

// Loads the committed config/ files so tests pin their real content.
public static class RepoConfig
{
    public static string Root { get; } = RepoRoot.Find(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Repo root not found.");
    public static string PathOf(string file) => Path.Combine(Root, "config", file);
    public static ProcessorOptions Options() => ProcessorOptions.Load(PathOf("processor.json"));
}
```

- [ ] **Step 5: Rename namespaces**

Write `$TEMP/rename_ns.py` with the Write tool:
```python
import pathlib, re
roots = ["src/Tracker.Core", "src/Processor", "tests/Tracker.Core.Tests", "tests/Tracker.TestSupport"]
pairs = [
    ("Processor.Tests.TestSupport", "Tracker.TestSupport"),
    ("using Processor.Tests.Configuration;", "using Tracker.TestSupport;"),
    ("Processor.Tests", "Tracker.Core.Tests"),
    ("namespace Processor;", "namespace Tracker;"),
    ("using Processor;", "using Tracker;"),
    ("Processor.Data.", "Tracker.Data."),
    ("Processor.Reading.", "Tracker.Reading."),
]
prefix = re.compile(r"\b(namespace|using) Processor\.")
for root in roots:
    for path in pathlib.Path(root).rglob("*.cs"):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        text = original = path.read_text(encoding="utf-8")
        for a, b in pairs:
            text = text.replace(a, b)
        text = prefix.sub(lambda m: f"{m.group(1)} Tracker.", text)
        lines, seen = [], set()
        for line in text.split("\n"):  # drop exact duplicate using lines created by the renames
            key = line.strip()
            if key.startswith("using ") and key.endswith(";") and key in seen:
                continue
            if key.startswith("using "):
                seen.add(key)
            lines.append(line)
        text = "\n".join(lines)
        if text != original:
            path.write_text(text, encoding="utf-8", newline="\n")
            print("renamed", path)
```
Run: `python "$TEMP/rename_ns.py"`

Then, in `src/Processor/Program.cs`, change `.AddUserSecrets<RebuildService>(optional: true)` to `.AddUserSecrets<Program>(optional: true)`. `RebuildService` now lives in `Tracker.Core`, which has no UserSecretsId.

Update `JobMarketTracker.slnx`: replace the path `tests/Processor.Tests/Processor.Tests.csproj` with `tests/Tracker.Core.Tests/Tracker.Core.Tests.csproj` (Edit tool), then:
```bash
dotnet sln add src/Tracker.Core tests/Tracker.TestSupport
```

- [ ] **Step 6: Build and run the gate**

Run: `dotnet build -warnaserror src/Processor && dotnet test`
Expected:
- 0 warnings;
- `Passed! … Total: 101` (archiver) and `Passed! … Total: 238` (Tracker.Core.Tests).

Run: `dotnet ef migrations list --project src/Tracker.Core --startup-project src/Processor --no-connect && dotnet ef migrations has-pending-model-changes --project src/Tracker.Core --startup-project src/Processor`
Expected: `20260928093654_InitialCreate` is listed, and `No changes have been made to the model since the last migration.` The rename script also rewrites the `"Processor.Data.*"` entity-name strings in the migration designer and snapshot.

- [ ] **Step 7: Check that the report is unchanged**

```bash
dotnet run --project src/Processor -c Release -- --report-only
diff <(sed -E 's/generated [^<]*/generated X/' "$TEMP/report-before.html") <(sed -E 's/generated [^<]*/generated X/' data/reports/latest.fragment.html) && echo "REPORT IDENTICAL"
```
Expected: exit 0, and `REPORT IDENTICAL`. This also proves Processor still finds its connection string, since a secrets failure would exit 2.
- The generated time sits mid-line, so the `sed` swaps only that phrase; `grep -v` would drop the whole line.
- If the diff shows changed numbers, check `data/pipeline.log` for a scheduled rebuild between Step 1 and now before debugging.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Extract Tracker.Core library and shared test support from Processor"
```

---

### Task 2: Location fields for analysis postings and public salary cells

**Files:**
- Modify: `src/Tracker.Core/Analysis/Analyzer.cs` (`AnalysisPosting` record; `SalaryCells` becomes `public static`)
- Modify: `src/Tracker.Core/Analysis/AnalysisLoader.cs`
- Test: `tests/Tracker.Core.Tests/Rebuild/RebuildServiceTests.cs`

**Interfaces:**
- Consumes: `AnalysisLoader`, `Analyzer` (phase 1)
- Produces:
  - `record AnalysisPosting(…existing 13 parameters…, bool IsGta = true, bool IsRemoteCanada = false, string? City = null)`
  - `public static IReadOnlyList<SalaryCell> Analyzer.SalaryCells(IReadOnlyList<AnalysisPosting> postings)`

- [ ] **Step 1: Write the failing test**

In `tests/Tracker.Core.Tests/Rebuild/RebuildServiceTests.cs`, in `Rebuilds_the_fixture_archive_and_loads_it_for_analysis`, add after `Assert.Equal("Remote (Canada)", postings.Single(p => p.Id == "ashby:S1").Location);`:
```csharp
        var remote = postings.Single(p => p.Id == "ashby:S1");
        Assert.Equal((false, true), (remote.IsGta, remote.IsRemoteCanada));
        var toronto = postings.Single(p => p.Id == "adzuna:A1");
        Assert.Equal((true, "Toronto"), (toronto.IsGta, toronto.City));
```
Also add a new test file `tests/Tracker.Core.Tests/Analysis/SalaryCellsTests.cs`:
```csharp
using Tracker.Analysis;
using Tracker.Interpreting;
using Tracker.Reading;

namespace Tracker.Core.Tests.Analysis;

public class SalaryCellsTests
{
    [Fact]
    public void SalaryCells_is_usable_on_its_own()
    {
        var postings = Enumerable.Range(0, 5).Select(i => new AnalysisPosting($"p{i}", "greenhouse", "Acme", "Dev", "", "Toronto",
            RoleFamily.Dotnet, Seniority.Senior, new SalaryRange(100_000 + i * 10_000, 100_000 + i * 10_000, SalaryPeriod.Year),
            true, false, new HashSet<string>(), [])).ToList();

        var cell = Assert.Single(Analyzer.SalaryCells(postings));

        Assert.Equal(120_000m, cell.Median);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Tracker.Core.Tests`
Expected: build FAILS (`IsGta` doesn't exist; `SalaryCells` is inaccessible).

- [ ] **Step 3: Implement**

In `src/Tracker.Core/Analysis/Analyzer.cs`, replace the `AnalysisPosting` declaration with:
```csharp
public sealed record AnalysisPosting(string Id, string Source, string Company, string Title, string Url, string Location,
    RoleFamily Family, Seniority Seniority, SalaryRange? Salary, bool IsFullText, bool CitizenshipOrClearance,
    IReadOnlySet<string> Skills, IReadOnlyList<DateTimeOffset> Sightings,
    bool IsGta = true, bool IsRemoteCanada = false, string? City = null);
```
and change `private static IReadOnlyList<SalaryCell> SalaryCells(` to `public static IReadOnlyList<SalaryCell> SalaryCells(`.

In `src/Tracker.Core/Analysis/AnalysisLoader.cs`:
- add `p.IsRemoteCanada` to the anonymous projection (after `p.IsGta`);
- change the `new AnalysisPosting(` call's final argument list to end with:
```csharp
            r.Skills.ToHashSet(StringComparer.Ordinal),
            [.. r.Runs.Select(id => startedAt[id]).Order()],
            r.IsGta, r.IsRemoteCanada, r.City)).ToList();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Tracker.Core.Tests`
Expected: all PASS (239 tests: 238 plus `SalaryCells_is_usable_on_its_own`).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Expose location on analysis postings and salary cells for the API"
```

---

### Task 3: Service defaults, API skeleton and settings

**Files:**
- Create (template): `src/ServiceDefaults/*`
- Create: `src/Api/Api.csproj`, `src/Api/Program.cs`, `src/Api/MarketSettings.cs`
- Create: `tests/Api.Tests/Api.Tests.csproj`, `tests/Api.Tests/MarketSettingsTests.cs`, `tests/Api.Tests/TestSupport/PostgresCollection.cs`

**Interfaces:**
- Consumes: `RepoRoot`, `ProcessorOptions`, `SkillCatalog`, `Profile` (Tracker.Core); `RepoConfig`, `TempDir` (TestSupport)
- Produces:
  - `record MarketSettings(string RepoRoot, string DataRoot, ProcessorOptions Options, SkillCatalog Catalog, Profile Profile)`
  - `static MarketSettings MarketSettings.Load(string repoRoot, string? profilePath = null, string? dataRoot = null)`, which throws `InvalidDataException` for a missing profile with the message "config/profile.json is missing. Copy config/profile.example.json to config/profile.json and set your skill levels."
  - `public partial class Program` in `Api`, for `WebApplicationFactory`

- [ ] **Step 1: Scaffold**

```bash
dotnet new install Aspire.ProjectTemplates::13.5.4
dotnet new aspire-servicedefaults -n ServiceDefaults -o src/ServiceDefaults
dotnet new web -n Api -o src/Api -f net10.0
dotnet add src/Api reference src/Tracker.Core src/ServiceDefaults
dotnet add src/Api package Aspire.Npgsql.EntityFrameworkCore.PostgreSQL --version 13.5.4
```
Expected: each command succeeds. `dotnet new web` creates `Program.cs`, `appsettings*.json` and `Properties/launchSettings.json`; Program.cs is replaced below.

Create `tests/Api.Tests/Api.Tests.csproj` by hand, with the same test package versions as Tracker.Core.Tests. A template could bring xunit v3, whose `IAsyncLifetime` would clash with TestSupport's xunit 2.9.3.
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Api\Api.csproj" />
    <ProjectReference Include="..\Tracker.TestSupport\Tracker.TestSupport.csproj" />
  </ItemGroup>

</Project>
```
Then: `dotnet sln add src/ServiceDefaults src/Api tests/Api.Tests`

- [ ] **Step 2: Write the failing tests**

`tests/Api.Tests/TestSupport/PostgresCollection.cs`:
```csharp
using Tracker.TestSupport;

namespace Api.Tests.TestSupport;

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
```

`tests/Api.Tests/MarketSettingsTests.cs`:
```csharp
using Api;
using Tracker.TestSupport;

namespace Api.Tests;

public class MarketSettingsTests
{
    [Fact]
    public void Load_reads_config_and_the_given_profile()
    {
        var settings = MarketSettings.Load(RepoConfig.Root, RepoConfig.PathOf("profile.example.json"), "D:/somewhere/data");

        Assert.Equal(RepoConfig.Root, settings.RepoRoot);
        Assert.Equal("D:/somewhere/data", settings.DataRoot);
        Assert.InRange(settings.Catalog.Skills.Count, 150, 220);
        Assert.NotEmpty(settings.Profile.TargetFamilies);
        Assert.Equal(30, settings.Options.WindowDays);
    }

    [Fact]
    public void Missing_profile_explains_how_to_create_it()
    {
        using var tmp = new TempDir();
        var error = Assert.Throws<InvalidDataException>(() => MarketSettings.Load(RepoConfig.Root, Path.Combine(tmp.Path, "profile.json")));
        Assert.Contains("profile.example.json", error.Message);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Api.Tests`
Expected: build FAILS (`MarketSettings` missing).

- [ ] **Step 4: Implement**

`src/Api/MarketSettings.cs`:
```csharp
using Tracker.Analysis;
using Tracker.Configuration;
using Tracker.Interpreting;

namespace Api;

// Everything the API reads from the repo at start-up: interpretation config, the skill list and the owner's profile.
public sealed record MarketSettings(string RepoRoot, string DataRoot, ProcessorOptions Options, SkillCatalog Catalog, Profile Profile)
{
    public static MarketSettings Load(string repoRoot, string? profilePath = null, string? dataRoot = null)
    {
        var options = ProcessorOptions.Load(Path.Combine(repoRoot, "config", "processor.json"));
        var catalog = SkillCatalog.Load(Path.Combine(repoRoot, "config", "skills.json"));
        var profileFile = profilePath ?? Path.Combine(repoRoot, "config", "profile.json");
        if (!File.Exists(profileFile))
            throw new InvalidDataException("config/profile.json is missing. Copy config/profile.example.json to config/profile.json and set your skill levels.");
        return new MarketSettings(repoRoot, dataRoot ?? Path.Combine(repoRoot, "data"), options, catalog, Profile.Load(profileFile, catalog));
    }
}
```

`src/Api/Program.cs` (skeleton; replaced in Task 6):
```csharp
using Api;
using Tracker.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var root = RepoRoot.Find(builder.Environment.ContentRootPath) ?? RepoRoot.Find(AppContext.BaseDirectory)
    ?? throw new InvalidOperationException("config/sources.json was not found in the API folder or any parent.");
builder.Services.AddSingleton(MarketSettings.Load(root, builder.Configuration["Tracker:ProfilePath"], builder.Configuration["Tracker:DataRoot"]));

var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();

public partial class Program;
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet build src/Api -warnaserror && dotnet test tests/Api.Tests`
Expected: build OK; 2 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add Aspire service defaults and the API skeleton with market settings"
```

---

### Task 4: Market snapshot, filters and query functions

**Files:**
- Create: `src/Api/Market/MarketSnapshot.cs`, `src/Api/Queries/Filters.cs`, `src/Api/Queries/PostingQueries.cs`, `src/Api/Queries/SkillQueries.cs`, `src/Api/Queries/SalaryQueries.cs`, `src/Api/Queries/MetaQueries.cs`
- Test: `tests/Api.Tests/Queries/FiltersTests.cs`, `SkillQueriesTests.cs`, `SalaryQueriesTests.cs`, `PostingQueriesTests.cs`, `MetaQueriesTests.cs`, `tests/Api.Tests/TestSupport/Data.cs`

**Interfaces:**
- Consumes: `AnalysisPosting` (with location fields), `RunInfo`, `JobBankMonth`, `Analyzer`, `Profile`, `SkillLevel`, `SkillCatalog`, `TitleRules`, `RoleFamily`, `Seniority`, `Stats` (Tracker.Core); `MarketSettings` (Task 3)
- Produces (namespace `Api.Market` / `Api.Queries`):
  - `record MarketSnapshot(IReadOnlyList<AnalysisPosting> Postings, IReadOnlyList<RunInfo> Runs, IReadOnlyList<JobBankMonth> JobBank, IReadOnlyList<string> Warnings, DateTimeOffset LoadedAt)` with `static Empty(DateTimeOffset)` and `DateTimeOffset? LatestRunAt`
  - `enum Where { All, Gta, Remote }`, `enum TextKind { Full, Snippet, All }`, `enum PostingSort { Newest, Match, Salary }`
  - `class FilterException(string parameter, string message) : Exception` with `Parameter`
  - `record FilterSet(IReadOnlySet<RoleFamily> Families, IReadOnlySet<Seniority>? Seniorities, Where Where, int? WindowDays)` with `Apply(MarketSnapshot)` and `ApplyBetween(MarketSnapshot, DateTimeOffset from, DateTimeOffset toExclusive)`
  - `static class FilterParser` with `Parse(IQueryCollection, Profile)`, `Text(IQueryCollection, TextKind)`, `Sort(IQueryCollection)` and `Int(IQueryCollection, string name, int @default, int min, int max)`
  - `record PostingRow(string Id, string Title, string Company, string Location, string Url, decimal? SalaryMin, decimal? SalaryMax, string? SalaryPeriod, double Match, DateTimeOffset FirstSeen, IReadOnlyList<string> Skills, bool CitizenshipOrClearance, string Source, bool IsFullText)`
  - `record PostingPage(int Total, int Page, int PageSize, IReadOnlyList<PostingRow> Items)`
  - `PostingQueries.ToRow(AnalysisPosting, Profile)`, `PostingQueries.Match(AnalysisPosting, Profile)`, and `PostingQueries.Page(MarketSnapshot, FilterSet, string? q, string? skill, string? company, PostingSort, int page, int pageSize, Profile)`
  - `record SkillRow(string Skill, string Category, double Share, int Count, SkillLevel Level, double? PreviousShare, double? Change)` and `record SkillList(int Postings, IReadOnlyList<SkillRow> Skills)`
  - `record NamedCount(string Name, int Count)` and `record SalaryComparison(decimal? WithMedian, int WithCount, decimal? WithoutMedian, int WithoutCount)`
  - `record SkillDetail(string Skill, string Category, SkillLevel Level, double Share, int Count, int Postings, SalaryComparison Salary, IReadOnlyList<NamedCount> CoSkills, IReadOnlyList<NamedCount> Companies, IReadOnlyList<PostingRow> Examples)`
  - `SkillQueries.List(MarketSnapshot, FilterSet, TextKind, string? category, MarketSettings)` and `SkillQueries.Detail(MarketSnapshot, FilterSet, TextKind, string skill, MarketSettings) : SkillDetail?` (null for an unknown skill)
  - `record Bucket(decimal From, decimal To, int Count)` and `record SalaryView(int Postings, int WithSalary, IReadOnlyList<SalaryCell> Cells, IReadOnlyList<Bucket> Histogram, IReadOnlyList<PostingRow> Examples)`
  - `SalaryQueries.View(MarketSnapshot, FilterSet, string? skill, string? company, MarketSettings)`, which throws `FilterException("skill", …)` for an unknown skill
  - `record MetaView(string? LatestRunId, DateTimeOffset? LatestRunAt, int Runs, int HistoryDays, IReadOnlyList<string> TargetFamilies, IReadOnlyList<string> Families, IReadOnlyList<string> Seniorities, IReadOnlyList<string> Categories, IReadOnlyList<string> Companies, bool Stale)` and `MetaQueries.Meta(MarketSnapshot, MarketSettings, bool stale)`
  - `MetaQueries.Overview(MarketSnapshot, FilterSet, MarketSettings, DateTimeOffset now) : ReportModel?` (null when there are no runs)

- [ ] **Step 1: Write the test data helper**

`tests/Api.Tests/TestSupport/Data.cs`:
```csharp
using Api;
using Api.Market;
using Tracker.Analysis;
using Tracker.Interpreting;
using Tracker.Reading;
using Tracker.TestSupport;

namespace Api.Tests.TestSupport;

// Small hand-made market: runs on day 0 (2026-09-01) and later days as each test needs.
public static class Data
{
    public static readonly DateTimeOffset T0 = new(2026, 9, 1, 7, 0, 0, TimeSpan.FromHours(-4));
    public static DateTimeOffset Day(int d) => T0.AddDays(d);

    public static MarketSettings Settings { get; } = MarketSettings.Load(RepoConfig.Root, RepoConfig.PathOf("profile.example.json"), "unused");

    public static AnalysisPosting P(string id, string[] skills, int[] seen, RoleFamily family = RoleFamily.Dotnet,
        Seniority seniority = Seniority.Senior, decimal? salary = null, bool fullText = true, string company = "Acme",
        string title = "Developer", bool gta = true, bool remote = false, string source = "greenhouse") =>
        new(id, source, company, title, $"https://x.test/{id}", gta ? "Toronto" : "Remote (Canada)", family, seniority,
            salary is { } s ? new SalaryRange(s, s, SalaryPeriod.Year) : null, fullText, false, skills.ToHashSet(),
            [.. seen.Select(Day)], gta, remote, gta ? "Toronto" : null);

    public static MarketSnapshot MarketOf(IReadOnlyList<AnalysisPosting> postings, params int[] runDays) =>
        new(postings, [.. runDays.Select(d => new RunInfo($"r{d}", Day(d)))], [], [], Day(100));
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Api.Tests/Queries/FiltersTests.cs`:
```csharp
using Api.Queries;
using Api.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Tracker.Interpreting;
using static Api.Tests.TestSupport.Data;

namespace Api.Tests.Queries;

public class FiltersTests
{
    private static IQueryCollection Q(params (string Key, string Value)[] pairs) =>
        new QueryCollection(pairs.ToDictionary(p => p.Key, p => new StringValues(p.Value)));

    [Fact]
    public void Defaults_come_from_the_profile()
    {
        var f = FilterParser.Parse(Q(), Settings.Profile);
        Assert.Equal(Settings.Profile.TargetFamilies.OrderBy(x => x), f.Families.OrderBy(x => x));
        Assert.Null(f.Seniorities);
        Assert.Equal((Where.All, 30), (f.Where, f.WindowDays!.Value));
    }

    [Fact]
    public void Families_accept_keys_and_enum_names_case_insensitively()
    {
        var f = FilterParser.Parse(Q(("families", "software_engineer,DOTNET,LeadManager")), Settings.Profile);
        Assert.Equal(new[] { RoleFamily.Dotnet, RoleFamily.SoftwareEngineer, RoleFamily.LeadManager }.Order(), f.Families.Order());
    }

    [Fact]
    public void Seniority_where_and_window_parse()
    {
        var f = FilterParser.Parse(Q(("seniority", "senior,StaffPrincipal"), ("where", "remote"), ("window", "all")), Settings.Profile);
        Assert.Equal(new[] { Seniority.Senior, Seniority.StaffPrincipal }.Order(), f.Seniorities!.Order());
        Assert.Equal(Where.Remote, f.Where);
        Assert.Null(f.WindowDays);
    }

    [Theory]
    [InlineData("families", "astronaut")]
    [InlineData("seniority", "wizard")]
    [InlineData("seniority", "3")]
    [InlineData("where", "moon")]
    [InlineData("window", "45")]
    public void Unknown_values_name_the_parameter(string key, string value)
    {
        var error = Assert.Throws<FilterException>(() => FilterParser.Parse(Q((key, value)), Settings.Profile));
        Assert.Equal(key, error.Parameter);
        Assert.Contains(value, error.Message);
    }

    [Fact]
    public void Text_sort_and_paging_parse_with_defaults_and_bounds()
    {
        Assert.Equal(TextKind.Full, FilterParser.Text(Q(), TextKind.Full));
        Assert.Equal(TextKind.Snippet, FilterParser.Text(Q(("text", "snippet")), TextKind.Full));
        Assert.Equal(PostingSort.Newest, FilterParser.Sort(Q()));
        Assert.Equal(PostingSort.Salary, FilterParser.Sort(Q(("sort", "salary"))));
        Assert.Equal(25, FilterParser.Int(Q(), "pageSize", 25, 1, 100));
        Assert.Equal("pageSize", Assert.Throws<FilterException>(() => FilterParser.Int(Q(("pageSize", "500")), "pageSize", 25, 1, 100)).Parameter);
        Assert.Equal("text", Assert.Throws<FilterException>(() => FilterParser.Text(Q(("text", "huge")), TextKind.Full)).Parameter);
    }

    [Fact]
    public void Apply_filters_by_family_seniority_place_and_window()
    {
        var market = MarketOf([
            P("in", ["C#"], [0]),
            P("old", ["C#"], [-40]),
            P("other", ["C#"], [0], family: RoleFamily.Other),
            P("junior", ["C#"], [0], seniority: Seniority.Junior),
            P("remote", ["C#"], [0], gta: false, remote: true),
        ], -40, 0);
        var f = new FilterSet(new HashSet<RoleFamily> { RoleFamily.Dotnet }, new HashSet<Seniority> { Seniority.Senior }, Where.Gta, 30);

        Assert.Equal(new[] { "in" }, f.Apply(market).Select(p => p.Id));
        Assert.Equal(new[] { "remote" }, (f with { Where = Where.Remote }).Apply(market).Select(p => p.Id));
        Assert.Equal(new[] { "in", "old" }, (f with { WindowDays = null }).Apply(market).Select(p => p.Id).Order());
    }

    [Fact]
    public void Apply_on_an_empty_market_is_empty() =>
        Assert.Empty(FilterParser.Parse(Q(), Settings.Profile).Apply(Api.Market.MarketSnapshot.Empty(T0)));
}
```

`tests/Api.Tests/Queries/SkillQueriesTests.cs`:
```csharp
using Api.Queries;
using Tracker.Analysis;
using Tracker.Interpreting;
using static Api.Tests.TestSupport.Data;

namespace Api.Tests.Queries;

public class SkillQueriesTests
{
    private static readonly FilterSet All30 = new(new HashSet<RoleFamily> { RoleFamily.Dotnet }, null, Where.All, 30);

    [Fact]
    public void List_shares_use_the_chosen_text_kind_and_show_levels()
    {
        var market = MarketOf([
            P("a", ["C#", "Docker"], [0]),
            P("b", ["C#"], [0]),
            P("s", ["Docker"], [0], fullText: false),
        ], 0);

        var full = SkillQueries.List(market, All30, TextKind.Full, null, Settings);
        var csharp = full.Skills.Single(s => s.Skill == "C#");
        Assert.Equal(2, full.Postings);
        Assert.Equal((1.0, 2, SkillLevel.Strong), (csharp.Share, csharp.Count, csharp.Level));
        Assert.Equal(0.5, full.Skills.Single(s => s.Skill == "Docker").Share);
        Assert.Equal(1.0, SkillQueries.List(market, All30, TextKind.Snippet, null, Settings).Skills.Single(s => s.Skill == "Docker").Share);
        Assert.Equal("C#", full.Skills[0].Skill); // highest share first
    }

    [Fact]
    public void List_compares_with_the_previous_window_when_history_covers_it()
    {
        var market = MarketOf([P("old", ["Docker"], [-40]), P("new", ["C#"], [0])], -60, -40, 0);

        var list = SkillQueries.List(market, All30, TextKind.Full, null, Settings);

        var docker = list.Skills.Single(s => s.Skill == "Docker");
        Assert.Equal((0.0, 1.0, -1.0), (docker.Share, docker.PreviousShare!.Value, docker.Change!.Value));
        Assert.Null(SkillQueries.List(MarketOf([P("new", ["C#"], [0])], 0), All30, TextKind.Full, null, Settings).Skills[0].PreviousShare);
    }

    [Fact]
    public void List_filters_by_category()
    {
        var list = SkillQueries.List(MarketOf([P("a", ["C#", "Docker"], [0])], 0), All30, TextKind.Full, "devops", Settings);
        Assert.All(list.Skills, s => Assert.Equal("devops", s.Category));
    }

    [Fact]
    public void Detail_compares_salary_with_and_without_the_skill_and_lists_companions()
    {
        var postings = Enumerable.Range(0, 5).Select(i => P($"k{i}", ["Kubernetes", "Go"], [0], salary: 150_000, company: i < 3 ? "Acme" : "Globex"))
            .Concat(Enumerable.Range(0, 5).Select(i => P($"n{i}", ["C#"], [0], salary: 110_000)))
            .ToList();

        var detail = SkillQueries.Detail(MarketOf(postings, 0), All30, TextKind.Full, "Kubernetes", Settings)!;

        Assert.Equal((5, 10, 0.5), (detail.Count, detail.Postings, detail.Share));
        Assert.Equal(new SalaryComparison(150_000m, 5, 110_000m, 5), detail.Salary);
        Assert.Equal(new NamedCount("Go", 5), detail.CoSkills[0]);
        Assert.Equal(new NamedCount("Acme", 3), detail.Companies[0]);
        Assert.Equal(5, detail.Examples.Count);
        Assert.Equal(SkillLevel.None, detail.Level);
    }

    [Fact]
    public void Detail_hides_medians_below_five_and_is_null_for_unknown_skills()
    {
        var detail = SkillQueries.Detail(MarketOf([P("a", ["C#"], [0], salary: 120_000)], 0), All30, TextKind.Full, "C#", Settings)!;
        Assert.Equal(new SalaryComparison(null, 1, null, 0), detail.Salary);
        Assert.Null(SkillQueries.Detail(MarketOf([], 0), All30, TextKind.Full, "Cobol", Settings));
    }

    [Fact]
    public void Nothing_matching_gives_zero_shares_not_errors()
    {
        var list = SkillQueries.List(MarketOf([], 0), All30, TextKind.Full, null, Settings);
        Assert.Equal(0, list.Postings);
        Assert.All(list.Skills, s => Assert.Equal(0, s.Share));
    }
}
```

`tests/Api.Tests/Queries/SalaryQueriesTests.cs`:
```csharp
using Api.Queries;
using Tracker.Interpreting;
using static Api.Tests.TestSupport.Data;

namespace Api.Tests.Queries;

public class SalaryQueriesTests
{
    private static readonly FilterSet All30 = new(new HashSet<RoleFamily> { RoleFamily.Dotnet, RoleFamily.SoftwareEngineer }, null, Where.All, 30);

    [Fact]
    public void View_builds_cells_histogram_and_examples()
    {
        var market = MarketOf([
            P("a", ["C#"], [0], salary: 110_000),
            P("b", ["C#"], [0], salary: 125_000),
            P("c", ["Go"], [0], salary: 175_000, family: RoleFamily.SoftwareEngineer),
            P("d", ["Go"], [0]),
        ], 0);

        var view = SalaryQueries.View(market, All30, null, null, Settings);

        Assert.Equal((4, 3), (view.Postings, view.WithSalary));
        Assert.Equal(new[] { new Bucket(100_000, 120_000, 1), new Bucket(120_000, 140_000, 1), new Bucket(140_000, 160_000, 0), new Bucket(160_000, 180_000, 1) }, view.Histogram);
        Assert.Equal("c", view.Examples[0].Id); // highest salary first
        Assert.Equal(2, view.Cells.Count);
    }

    [Fact]
    public void View_filters_by_skill_and_company()
    {
        var market = MarketOf([P("a", ["C#"], [0], salary: 110_000, company: "Acme"), P("b", ["Go"], [0], salary: 130_000, company: "Globex")], 0);
        Assert.Equal(new[] { "a" }, SalaryQueries.View(market, All30, "C#", null, Settings).Examples.Select(e => e.Id));
        Assert.Equal(new[] { "b" }, SalaryQueries.View(market, All30, null, "globex", Settings).Examples.Select(e => e.Id));
    }

    [Fact]
    public void Unknown_skill_is_a_filter_error() =>
        Assert.Equal("skill", Assert.Throws<FilterException>(() => SalaryQueries.View(MarketOf([], 0), All30, "Cobol", null, Settings)).Parameter);

    [Fact]
    public void No_salaries_gives_an_empty_histogram() =>
        Assert.Empty(SalaryQueries.View(MarketOf([P("a", ["C#"], [0])], 0), All30, null, null, Settings).Histogram);
}
```

`tests/Api.Tests/Queries/PostingQueriesTests.cs`:
```csharp
using Api.Queries;
using Tracker.Interpreting;
using static Api.Tests.TestSupport.Data;

namespace Api.Tests.Queries;

public class PostingQueriesTests
{
    private static readonly FilterSet All30 = new(new HashSet<RoleFamily> { RoleFamily.Dotnet }, null, Where.All, 30);

    private static readonly Api.Market.MarketSnapshot Market3 = MarketOf([
        P("old", ["C#", ".NET"], [-5], title: "Senior .NET Developer", company: "Acme", salary: 120_000),
        P("new", ["Kubernetes", "Go"], [0], title: "Platform Engineer", company: "Globex", salary: 160_000),
        P("mid", ["C#", "Kubernetes"], [-2], title: "Full Stack Dev", company: "Acme"),
    ], -5, -2, 0);

    [Fact]
    public void Match_is_the_share_of_skills_at_working_level_or_better() =>
        Assert.Equal(0.5, PostingQueries.Match(P("x", ["C#", "Kubernetes"], [0]), Settings.Profile)); // C# strong, Kubernetes none

    [Theory]
    [InlineData(PostingSort.Newest, "new,mid,old")]
    [InlineData(PostingSort.Match, "old,mid,new")]
    [InlineData(PostingSort.Salary, "new,old,mid")]
    public void Sorts(PostingSort sort, string expected) =>
        Assert.Equal(expected, string.Join(",", PostingQueries.Page(Market3, All30, null, null, null, sort, 1, 25, Settings.Profile).Items.Select(i => i.Id)));

    [Fact]
    public void Search_skill_and_company_filters_combine()
    {
        Assert.Equal(new[] { "old" }, PostingQueries.Page(Market3, All30, ".net dev", null, null, PostingSort.Newest, 1, 25, Settings.Profile).Items.Select(i => i.Id));
        Assert.Equal(new[] { "mid" }, PostingQueries.Page(Market3, All30, null, "Kubernetes", "acme", PostingSort.Newest, 1, 25, Settings.Profile).Items.Select(i => i.Id));
    }

    [Fact]
    public void Paging_reports_the_total()
    {
        var page = PostingQueries.Page(Market3, All30, null, null, null, PostingSort.Newest, 2, 2, Settings.Profile);
        Assert.Equal((3, 2, 2), (page.Total, page.Page, page.PageSize));
        Assert.Equal(new[] { "old" }, page.Items.Select(i => i.Id));
    }

    [Fact]
    public void Row_carries_salary_first_seen_and_sorted_skills()
    {
        var row = PostingQueries.ToRow(P("x", ["Go", "C#"], [-3, 0], salary: 100_000), Settings.Profile);
        Assert.Equal((100_000m, 100_000m, "Year"), (row.SalaryMin!.Value, row.SalaryMax!.Value, row.SalaryPeriod));
        Assert.Equal(Day(-3), row.FirstSeen);
        Assert.Equal(new[] { "C#", "Go" }, row.Skills);
    }
}
```

`tests/Api.Tests/Queries/MetaQueriesTests.cs`:
```csharp
using Api.Queries;
using Tracker.Interpreting;
using static Api.Tests.TestSupport.Data;

namespace Api.Tests.Queries;

public class MetaQueriesTests
{
    [Fact]
    public void Meta_describes_runs_and_filter_options()
    {
        var meta = MetaQueries.Meta(MarketOf([P("a", ["C#"], [0], company: "Acme"), P("b", ["C#"], [0], company: "Acme"), P("c", ["C#"], [0], company: "Globex")], -10, 0), Settings, stale: true);

        Assert.Equal(("r0", 2, 10, true), (meta.LatestRunId, meta.Runs, meta.HistoryDays, meta.Stale));
        Assert.Contains("SoftwareEngineer", meta.Families);
        Assert.Contains("StaffPrincipal", meta.Seniorities);
        Assert.Contains("devops", meta.Categories);
        Assert.Equal(new[] { "Acme", "Globex" }, meta.Companies);
        Assert.NotEmpty(meta.TargetFamilies);
    }

    [Fact]
    public void Meta_of_an_empty_market_has_no_latest_run()
    {
        var meta = MetaQueries.Meta(Api.Market.MarketSnapshot.Empty(T0), Settings, stale: false);
        Assert.Equal(((string?)null, 0, 0), (meta.LatestRunId, meta.Runs, meta.HistoryDays));
    }

    [Fact]
    public void Overview_uses_the_filtered_families_and_window()
    {
        var market = MarketOf([P("a", ["C#"], [0]), P("b", ["Go"], [0], family: RoleFamily.SoftwareEngineer)], 0);
        var f = new FilterSet(new HashSet<RoleFamily> { RoleFamily.SoftwareEngineer }, null, Where.All, 30);

        var model = MetaQueries.Overview(market, f, Settings, Day(1))!;

        Assert.Equal(1, model.Snapshot.InWindow);
        Assert.Equal("Go", model.TopSkills.Single().Skill);
        Assert.Null(MetaQueries.Overview(Api.Market.MarketSnapshot.Empty(T0), f, Settings, Day(1)));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Api.Tests`
Expected: build FAILS (the query types don't exist).

- [ ] **Step 4: Implement**

`src/Api/Market/MarketSnapshot.cs`:
```csharp
using Tracker.Analysis;
using Tracker.Reading;

namespace Api.Market;

// Everything the endpoints answer from: in-scope, non-duplicate postings plus runs, Job Bank rows and data warnings.
public sealed record MarketSnapshot(
    IReadOnlyList<AnalysisPosting> Postings,
    IReadOnlyList<RunInfo> Runs,
    IReadOnlyList<JobBankMonth> JobBank,
    IReadOnlyList<string> Warnings,
    DateTimeOffset LoadedAt)
{
    public static MarketSnapshot Empty(DateTimeOffset now) => new([], [], [], [], now);

    public DateTimeOffset? LatestRunAt => Runs.Count == 0 ? null : Runs.Max(r => r.StartedAt);
}
```

`src/Api/Queries/Filters.cs`:
```csharp
using Api.Market;
using Microsoft.AspNetCore.Http;
using Tracker.Analysis;
using Tracker.Interpreting;

namespace Api.Queries;

public enum Where { All, Gta, Remote }

public enum TextKind { Full, Snippet, All }

public enum PostingSort { Newest, Match, Salary }

public sealed class FilterException(string parameter, string message) : Exception(message)
{
    public string Parameter { get; } = parameter;
}

public sealed record FilterSet(IReadOnlySet<RoleFamily> Families, IReadOnlySet<Seniority>? Seniorities, Where Where, int? WindowDays)
{
    // Postings seen within the window up to the latest run (all history when WindowDays is null).
    public IReadOnlyList<AnalysisPosting> Apply(MarketSnapshot data) =>
        data.LatestRunAt is { } latest
            ? ApplyBetween(data, WindowDays is { } days ? latest.AddDays(-days) : DateTimeOffset.MinValue, DateTimeOffset.MaxValue)
            : [];

    public IReadOnlyList<AnalysisPosting> ApplyBetween(MarketSnapshot data, DateTimeOffset from, DateTimeOffset toExclusive) =>
        [.. data.Postings.Where(p =>
            Families.Contains(p.Family)
            && (Seniorities is null || Seniorities.Contains(p.Seniority))
            && Where switch { Where.Gta => p.IsGta, Where.Remote => p.IsRemoteCanada, _ => true }
            && p.Sightings.Any(s => s >= from && s < toExclusive))];
}

public static class FilterParser
{
    private static readonly string[] Windows = ["30", "60", "90", "all"];

    public static FilterSet Parse(IQueryCollection query, Profile profile)
    {
        var families = List(query, "families").Select(v => ParseFamily(v)).ToHashSet();
        var seniorities = List(query, "seniority").Select(ParseSeniority).ToHashSet();
        return new FilterSet(
            families.Count > 0 ? families : profile.TargetFamilies,
            seniorities.Count > 0 ? seniorities : null,
            ParseEnum(query, "where", Where.All),
            Window(query));
    }

    public static TextKind Text(IQueryCollection query, TextKind @default) => ParseEnum(query, "text", @default);

    public static PostingSort Sort(IQueryCollection query) => ParseEnum(query, "sort", PostingSort.Newest);

    public static int Int(IQueryCollection query, string name, int @default, int min, int max)
    {
        var raw = query[name].ToString();
        if (raw.Length == 0) return @default;
        if (int.TryParse(raw, out var value) && value >= min && value <= max) return value;
        throw new FilterException(name, $"'{raw}' is not a valid {name}. Use a whole number from {min} to {max}.");
    }

    private static IEnumerable<string> List(IQueryCollection query, string name) =>
        query[name].ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static RoleFamily ParseFamily(string value) =>
        TitleRules.ParseFamily(value.ToLowerInvariant())
        ?? (Enum.TryParse<RoleFamily>(value, ignoreCase: true, out var family) && Enum.IsDefined(family) && !int.TryParse(value, out _)
            ? family
            : throw new FilterException("families", $"Unknown role family '{value}'. Use {string.Join(", ", Enum.GetNames<RoleFamily>())}."));

    private static Seniority ParseSeniority(string value) =>
        Enum.TryParse<Seniority>(value, ignoreCase: true, out var seniority) && Enum.IsDefined(seniority) && !int.TryParse(value, out _)
            ? seniority
            : throw new FilterException("seniority", $"Unknown seniority '{value}'. Use {string.Join(", ", Enum.GetNames<Seniority>())}.");

    private static T ParseEnum<T>(IQueryCollection query, string name, T @default) where T : struct, Enum
    {
        var raw = query[name].ToString();
        if (raw.Length == 0) return @default;
        return Enum.TryParse<T>(raw, ignoreCase: true, out var value) && Enum.IsDefined(value) && !int.TryParse(raw, out _)
            ? value
            : throw new FilterException(name, $"Unknown {name} '{raw}'. Use {string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))}.");
    }

    private static int? Window(IQueryCollection query)
    {
        var raw = query["window"].ToString();
        if (raw.Length == 0) return 30;
        if (!Windows.Contains(raw)) throw new FilterException("window", $"Unknown window '{raw}'. Use 30, 60, 90 or all.");
        return raw == "all" ? null : int.Parse(raw);
    }
}
```

`src/Api/Queries/PostingQueries.cs`:
```csharp
using Api.Market;
using Tracker.Analysis;

namespace Api.Queries;

public sealed record PostingRow(string Id, string Title, string Company, string Location, string Url,
    decimal? SalaryMin, decimal? SalaryMax, string? SalaryPeriod, double Match, DateTimeOffset FirstSeen,
    IReadOnlyList<string> Skills, bool CitizenshipOrClearance, string Source, bool IsFullText);

public sealed record PostingPage(int Total, int Page, int PageSize, IReadOnlyList<PostingRow> Items);

public static class PostingQueries
{
    // Share of the posting's skills the owner has at working level or better (same rule as the report's matches).
    public static double Match(AnalysisPosting p, Profile profile) =>
        p.Skills.Count == 0 ? 0 : (double)p.Skills.Count(s => profile.LevelOf(s) >= SkillLevel.Working) / p.Skills.Count;

    public static PostingRow ToRow(AnalysisPosting p, Profile profile) => new(
        p.Id, p.Title, p.Company, p.Location, p.Url, p.Salary?.Min, p.Salary?.Max, p.Salary?.Period.ToString(),
        Match(p, profile), p.Sightings.Count == 0 ? DateTimeOffset.MinValue : p.Sightings.Min(),
        [.. p.Skills.Order(StringComparer.Ordinal)], p.CitizenshipOrClearance, p.Source, p.IsFullText);

    public static PostingPage Page(MarketSnapshot data, FilterSet filters, string? q, string? skill, string? company,
        PostingSort sort, int page, int pageSize, Profile profile)
    {
        var rows = filters.Apply(data)
            .Where(p => string.IsNullOrWhiteSpace(q)
                || p.Title.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)
                || p.Company.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(p => string.IsNullOrEmpty(skill) || p.Skills.Contains(skill))
            .Where(p => string.IsNullOrEmpty(company) || p.Company.Equals(company, StringComparison.OrdinalIgnoreCase))
            .Select(p => (Posting: p, Row: ToRow(p, profile)))
            .ToList();

        IEnumerable<(AnalysisPosting Posting, PostingRow Row)> ordered = sort switch
        {
            PostingSort.Match => rows.OrderByDescending(r => r.Row.Match).ThenByDescending(r => r.Row.FirstSeen),
            PostingSort.Salary => rows.OrderByDescending(r => r.Posting.Salary?.YearlyMidpoint ?? -1).ThenByDescending(r => r.Row.FirstSeen),
            _ => rows.OrderByDescending(r => r.Row.FirstSeen).ThenBy(r => r.Row.Title, StringComparer.Ordinal),
        };
        return new PostingPage(rows.Count, page, pageSize, [.. ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(r => r.Row)]);
    }
}
```

`src/Api/Queries/SkillQueries.cs`:
```csharp
using Api.Market;
using Tracker;
using Tracker.Analysis;

namespace Api.Queries;

public sealed record SkillRow(string Skill, string Category, double Share, int Count, SkillLevel Level, double? PreviousShare, double? Change);

public sealed record SkillList(int Postings, IReadOnlyList<SkillRow> Skills);

public sealed record NamedCount(string Name, int Count);

public sealed record SalaryComparison(decimal? WithMedian, int WithCount, decimal? WithoutMedian, int WithoutCount);

public sealed record SkillDetail(string Skill, string Category, SkillLevel Level, double Share, int Count, int Postings,
    SalaryComparison Salary, IReadOnlyList<NamedCount> CoSkills, IReadOnlyList<NamedCount> Companies, IReadOnlyList<PostingRow> Examples);

public static class SkillQueries
{
    public const int MaxExamples = 50, MaxRelated = 10;

    public static SkillList List(MarketSnapshot data, FilterSet filters, TextKind text, string? category, MarketSettings settings)
    {
        var current = ByText(filters.Apply(data), text);
        var previous = PreviousWindow(data, filters, text);
        var rows = settings.Catalog.Skills
            .Where(s => string.IsNullOrEmpty(category) || s.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Select(s =>
            {
                var count = current.Count(p => p.Skills.Contains(s.Name));
                var share = current.Count == 0 ? 0 : (double)count / current.Count;
                double? before = previous is { Count: > 0 } ? (double)previous.Count(p => p.Skills.Contains(s.Name)) / previous.Count : null;
                return new SkillRow(s.Name, s.Category, share, count, settings.Profile.LevelOf(s.Name), before, before is { } b ? share - b : null);
            })
            .OrderByDescending(r => r.Share).ThenBy(r => r.Skill, StringComparer.Ordinal)
            .ToList();
        return new SkillList(current.Count, rows);
    }

    public static SkillDetail? Detail(MarketSnapshot data, FilterSet filters, TextKind text, string skill, MarketSettings settings)
    {
        var definition = settings.Catalog.Skills.FirstOrDefault(s => s.Name == skill);
        if (definition is null) return null;

        var postings = ByText(filters.Apply(data), text);
        var withSkill = postings.Where(p => p.Skills.Contains(skill)).ToList();
        var withoutSkill = postings.Where(p => !p.Skills.Contains(skill)).ToList();

        return new SkillDetail(skill, definition.Category, settings.Profile.LevelOf(skill),
            postings.Count == 0 ? 0 : (double)withSkill.Count / postings.Count, withSkill.Count, postings.Count,
            new SalaryComparison(Median(withSkill), withSkill.Count(p => p.Salary is not null),
                Median(withoutSkill), withoutSkill.Count(p => p.Salary is not null)),
            Top(withSkill.SelectMany(p => p.Skills).Where(s => s != skill)),
            Top(withSkill.Select(p => p.Company.Trim()), StringComparer.OrdinalIgnoreCase),
            [.. withSkill.OrderByDescending(p => p.Sightings.Min()).Take(MaxExamples).Select(p => PostingQueries.ToRow(p, settings.Profile))]);
    }

    internal static IReadOnlyList<AnalysisPosting> ByText(IReadOnlyList<AnalysisPosting> postings, TextKind text) => text switch
    {
        TextKind.Full => [.. postings.Where(p => p.IsFullText)],
        TextKind.Snippet => [.. postings.Where(p => !p.IsFullText)],
        _ => postings,
    };

    // The same-length window just before the current one, when the archive reaches back that far.
    private static IReadOnlyList<AnalysisPosting>? PreviousWindow(MarketSnapshot data, FilterSet filters, TextKind text)
    {
        if (filters.WindowDays is not { } days || data.LatestRunAt is not { } latest || data.Runs.Count == 0) return null;
        var from = latest.AddDays(-2 * days);
        var to = latest.AddDays(-days);
        if (data.Runs.Min(r => r.StartedAt) > to) return null;
        return ByText(filters.ApplyBetween(data, from, to), text);
    }

    private static decimal? Median(IEnumerable<AnalysisPosting> postings)
    {
        var mids = postings.Where(p => p.Salary is not null).Select(p => p.Salary!.YearlyMidpoint).ToList();
        return mids.Count >= Analyzer.MinSalarySample ? Stats.Median(mids) : null;
    }

    private static IReadOnlyList<NamedCount> Top(IEnumerable<string> names, IEqualityComparer<string>? comparer = null) =>
        [.. names.GroupBy(n => n, comparer ?? StringComparer.Ordinal)
            .Select(g => new NamedCount(g.First(), g.Count()))
            .OrderByDescending(n => n.Count).ThenBy(n => n.Name, StringComparer.Ordinal)
            .Take(MaxRelated)];
}
```

`src/Api/Queries/SalaryQueries.cs`:
```csharp
using Api.Market;
using Tracker.Analysis;

namespace Api.Queries;

public sealed record Bucket(decimal From, decimal To, int Count);

public sealed record SalaryView(int Postings, int WithSalary, IReadOnlyList<SalaryCell> Cells, IReadOnlyList<Bucket> Histogram, IReadOnlyList<PostingRow> Examples);

public static class SalaryQueries
{
    public const decimal BucketWidth = 20_000;
    public const int MaxExamples = 100;

    public static SalaryView View(MarketSnapshot data, FilterSet filters, string? skill, string? company, MarketSettings settings)
    {
        if (!string.IsNullOrEmpty(skill) && settings.Catalog.Skills.All(s => s.Name != skill))
            throw new FilterException("skill", $"Unknown skill '{skill}'.");

        var postings = filters.Apply(data)
            .Where(p => string.IsNullOrEmpty(skill) || p.Skills.Contains(skill))
            .Where(p => string.IsNullOrEmpty(company) || p.Company.Equals(company, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var salaried = postings.Where(p => p.Salary is not null).ToList();

        return new SalaryView(postings.Count, salaried.Count, Analyzer.SalaryCells(salaried), Histogram(salaried),
            [.. salaried.OrderByDescending(p => p.Salary!.YearlyMidpoint).Take(MaxExamples).Select(p => PostingQueries.ToRow(p, settings.Profile))]);
    }

    private static IReadOnlyList<Bucket> Histogram(IReadOnlyList<AnalysisPosting> salaried)
    {
        if (salaried.Count == 0) return [];
        var mids = salaried.Select(p => p.Salary!.YearlyMidpoint).ToList();
        var start = Math.Floor(mids.Min() / BucketWidth) * BucketWidth;
        var end = Math.Floor(mids.Max() / BucketWidth) * BucketWidth + BucketWidth;
        var buckets = new List<Bucket>();
        for (var from = start; from < end; from += BucketWidth)
            buckets.Add(new Bucket(from, from + BucketWidth, mids.Count(m => m >= from && m < from + BucketWidth)));
        return buckets;
    }
}
```

`src/Api/Queries/MetaQueries.cs`:
```csharp
using Api.Market;
using Tracker.Analysis;
using Tracker.Interpreting;

namespace Api.Queries;

public sealed record MetaView(string? LatestRunId, DateTimeOffset? LatestRunAt, int Runs, int HistoryDays,
    IReadOnlyList<string> TargetFamilies, IReadOnlyList<string> Families, IReadOnlyList<string> Seniorities,
    IReadOnlyList<string> Categories, IReadOnlyList<string> Companies, bool Stale);

public static class MetaQueries
{
    public const int MaxCompanies = 50;

    public static MetaView Meta(MarketSnapshot data, MarketSettings settings, bool stale)
    {
        var latest = data.Runs.OrderBy(r => r.StartedAt).LastOrDefault();
        var history = data.Runs.Count == 0 ? 0 : (int)Math.Round((data.Runs.Max(r => r.StartedAt) - data.Runs.Min(r => r.StartedAt)).TotalDays);
        return new MetaView(latest?.RunId, latest?.StartedAt, data.Runs.Count, history,
            [.. settings.Profile.TargetFamilies.Order().Select(f => f.ToString())],
            Enum.GetNames<RoleFamily>(), Enum.GetNames<Seniority>(),
            [.. settings.Catalog.Skills.Select(s => s.Category).Distinct().Order(StringComparer.Ordinal)],
            [.. data.Postings.GroupBy(p => p.Company.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Take(MaxCompanies).Select(g => g.First().Company.Trim())],
            stale);
    }

    // The report's model, computed over the filtered families, seniorities, places and window.
    public static ReportModel? Overview(MarketSnapshot data, FilterSet filters, MarketSettings settings, DateTimeOffset now)
    {
        if (data.Runs.Count == 0) return null;
        var windowDays = filters.WindowDays
            ?? (int)Math.Ceiling((data.Runs.Max(r => r.StartedAt) - data.Runs.Min(r => r.StartedAt)).TotalDays) + 1;
        var postings = (filters with { WindowDays = null }).Apply(data);
        var profile = new Profile(filters.Families, settings.Profile.Skills);
        return new Analyzer(profile, settings.Catalog, windowDays).Build(postings, data.Runs, data.JobBank, data.Warnings, now);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests`
Expected: all query tests PASS. When a case fails, fix the code, not the expected value, unless the plan's expectation is itself wrong. In that case, ledger a ruling.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add dashboard filters and pure skill, salary, posting and meta queries"
```

---

### Task 5: `MarketData`: a consistent, cached, observable load

**Files:**
- Create: `src/Tracker.Core/Data/TrackerTables.cs`
- Modify: `src/Tracker.Core/Data/RebuildWriter.cs` (its `Truncate` constant)
- Create: `src/Api/Market/IMarketSource.cs`, `src/Api/Market/MarketData.cs`, `src/Api/Market/DbMarketSource.cs`
- Test: `tests/Tracker.Core.Tests/Data/TrackerTablesTests.cs`, `tests/Api.Tests/TestSupport/Fakes.cs`, `tests/Api.Tests/Market/MarketDataTests.cs`, `tests/Api.Tests/Market/DbMarketSourceTests.cs`

**Interfaces:**
- Consumes: `MarketSnapshot` (Task 4); `MarketSettings` (Task 3); `AnalysisLoader.LoadAsync`, `Archive`, `InboxReader`, `TrackerDb`, `TrackerDbFactory`, `RebuildService` (Tracker.Core); `Data` helpers (Task 4 tests)
- Produces:
  - `Tracker.Data.TrackerTables.All` (const string): the rebuilt tables, in TRUNCATE order
  - `record MarketLoad(string Version, MarketSnapshot Data)`
  - `interface IMarketSource { Task<MarketLoad?> LoadIfChangedAsync(string? knownVersion, CancellationToken ct); }`: null means unchanged; never null when `knownVersion` is null
  - `class DatabaseUnavailableException(Exception inner)`, whose message is the spec's 503 text
  - `record MarketRead(MarketSnapshot Data, bool Stale)`
  - `class MarketData(IMarketSource, TimeProvider, IMeterFactory, ILogger<MarketData>)`, with `Task<MarketRead> GetAsync(CancellationToken)`, `const string TelemetryName = "Tracker.MarketData"` and `static TimeSpan CheckInterval` (60 s)
  - `class DbMarketSource(IServiceScopeFactory, MarketSettings, TimeProvider) : IMarketSource`
  - Test support: `ManualClock : TimeProvider` (settable `Now`); `ScriptedSource : IMarketSource` with `ThenLoad(version, postings)`, `ThenUnchanged()`, `ThenDown()`, `Then(step)`, `Calls` and `static Load(version, postings)`

**Why the load is built this way:**
- The rebuild runs `TRUNCATE` and then `COPY` in one transaction.
  - A plain multi-query load could read `runs` before a rebuild and `postings` after it. The sighting run ids would then be missing from the run list (a KeyNotFoundException), or `postings` would look empty, because TRUNCATE isn't MVCC-safe.
  - The source opens a REPEATABLE READ transaction and takes ACCESS SHARE locks on every rebuilt table before its first query. It therefore either finishes before the rebuild's TRUNCATE or waits for the rebuild to commit. It never sees half of each.
  - The locks are taken in exactly the TRUNCATE order (the shared `TrackerTables.All`), so a load and a rebuild can't deadlock.
- The version is the run count plus the latest run id plus the runs rows' transaction id (`xmin`). A rebuild re-inserts every row, so even a rules-only rebuild (same runs, new skill rules) gets a new version. The version is read inside the same transaction as the data it labels.
- Only a cold start waits for a load. Once data exists, the request that wins the gate refreshes, and every other request keeps getting the current copy instead of queueing behind a reload that may be waiting for a rebuild's lock.

- [ ] **Step 1: Write the failing tests**

`tests/Tracker.Core.Tests/Data/TrackerTablesTests.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Tracker.Data;

namespace Tracker.Core.Tests.Data;

public class TrackerTablesTests
{
    [Fact]
    public void All_lists_every_mapped_table_once()
    {
        using var db = new TrackerDb(TrackerDbFactory.Options("Host=unused"));
        var mapped = db.Model.GetEntityTypes().Select(e => e.GetTableName()!).Order(StringComparer.Ordinal).ToList();
        var listed = TrackerTables.All.Split(", ").Order(StringComparer.Ordinal).ToList();
        Assert.Equal(mapped, listed);
    }
}
```

`tests/Api.Tests/TestSupport/Fakes.cs`:
```csharp
using System.Collections.Concurrent;
using Api.Market;

namespace Api.Tests.TestSupport;

public sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

// A market source that plays back scripted answers, one per call.
public sealed class ScriptedSource : IMarketSource
{
    private readonly ConcurrentQueue<Func<string?, Task<MarketLoad?>>> _steps = new();
    private int _calls;

    public int Calls => _calls;

    public ScriptedSource Then(Func<string?, Task<MarketLoad?>> step)
    {
        _steps.Enqueue(step);
        return this;
    }

    public ScriptedSource ThenLoad(string version, int postings) => Then(_ => Task.FromResult<MarketLoad?>(Load(version, postings)));
    public ScriptedSource ThenUnchanged() => Then(_ => Task.FromResult<MarketLoad?>(null));
    public ScriptedSource ThenDown() => Then(_ => Task.FromException<MarketLoad?>(new DatabaseUnavailableException(new TimeoutException())));

    public Task<MarketLoad?> LoadIfChangedAsync(string? knownVersion, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        return _steps.TryDequeue(out var step) ? step(knownVersion) : throw new InvalidOperationException("No scripted answer left.");
    }

    public static MarketLoad Load(string version, int postings) =>
        new(version, Data.MarketOf([.. Enumerable.Range(0, postings).Select(i => Data.P($"p{i}", ["C#"], [0]))], 0));
}
```

`tests/Api.Tests/Market/MarketDataTests.cs`:
```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Api.Market;
using Api.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests.Market;

public class MarketDataTests
{
    private readonly ScriptedSource _source = new();
    private readonly ManualClock _clock = new();
    private readonly IMeterFactory _meters = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();

    private MarketData Create() => new(_source, _clock, _meters, NullLogger<MarketData>.Instance);

    [Fact]
    public async Task First_request_loads_and_requests_within_a_minute_reuse_it()
    {
        var market = Create();
        _source.ThenLoad("v1", 2);

        var first = await market.GetAsync(default);
        _clock.Now += TimeSpan.FromSeconds(59);
        var second = await market.GetAsync(default);

        Assert.Same(first.Data, second.Data);
        Assert.Equal((1, false), (_source.Calls, second.Stale));
    }

    [Fact]
    public async Task After_a_minute_an_unchanged_database_keeps_the_same_data()
    {
        var market = Create();
        _source.ThenLoad("v1", 2).Then(known =>
        {
            Assert.Equal("v1", known);
            return Task.FromResult<MarketLoad?>(null);
        });

        var first = await market.GetAsync(default);
        _clock.Now += MarketData.CheckInterval;
        var second = await market.GetAsync(default);

        Assert.Same(first.Data, second.Data);
        Assert.Equal(2, _source.Calls);
    }

    [Fact]
    public async Task A_new_version_replaces_the_data()
    {
        var market = Create();
        _source.ThenLoad("v1", 2).ThenLoad("v2", 5);

        await market.GetAsync(default);
        _clock.Now += MarketData.CheckInterval;

        Assert.Equal(5, (await market.GetAsync(default)).Data.Postings.Count);
    }

    [Fact]
    public async Task Cold_start_with_the_database_down_throws_and_the_next_request_retries()
    {
        var market = Create();
        _source.ThenDown().ThenLoad("v1", 1);

        await Assert.ThrowsAsync<DatabaseUnavailableException>(() => market.GetAsync(default));
        Assert.Single((await market.GetAsync(default)).Data.Postings);
    }

    [Fact]
    public async Task Losing_the_database_serves_the_last_data_as_stale_until_it_returns()
    {
        var market = Create();
        _source.ThenLoad("v1", 2).ThenDown().ThenUnchanged();

        var first = await market.GetAsync(default);
        _clock.Now += MarketData.CheckInterval;
        var during = await market.GetAsync(default);
        _clock.Now += TimeSpan.FromSeconds(30);
        var stillStale = await market.GetAsync(default);
        _clock.Now += TimeSpan.FromSeconds(30);
        var after = await market.GetAsync(default);

        Assert.Same(first.Data, during.Data);
        Assert.Equal((true, true, false), (during.Stale, stillStale.Stale, after.Stale));
        Assert.Equal(3, _source.Calls);
    }

    [Fact]
    public async Task Concurrent_cold_requests_share_one_load()
    {
        var market = Create();
        var release = new TaskCompletionSource();
        _source.Then(async _ =>
        {
            await release.Task;
            return ScriptedSource.Load("v1", 3);
        });

        var requests = Enumerable.Range(0, 10).Select(_ => market.GetAsync(default)).ToList();
        release.SetResult();
        var reads = await Task.WhenAll(requests);

        Assert.Equal(1, _source.Calls);
        Assert.All(reads, r => Assert.Same(reads[0].Data, r.Data));
    }

    [Fact]
    public async Task Requests_during_a_slow_reload_get_the_current_data_without_waiting()
    {
        var market = Create();
        var release = new TaskCompletionSource();
        _source.ThenLoad("v1", 2).Then(async _ =>
        {
            await release.Task;
            return ScriptedSource.Load("v2", 7);
        });
        var first = await market.GetAsync(default);
        _clock.Now += MarketData.CheckInterval;

        var reloading = market.GetAsync(default);
        var meanwhile = await market.GetAsync(default).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(first.Data, meanwhile.Data);
        Assert.False(reloading.IsCompleted);
        release.SetResult();
        Assert.Equal(7, (await reloading).Data.Postings.Count);
    }

    [Fact]
    public async Task Loads_are_traced_with_counts_and_counted_once_per_new_version()
    {
        long reloads = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Scope == _meters && instrument.Name == "tracker.market_data.reloads") listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref reloads, value));
        var spans = new ConcurrentBag<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == MarketData.TelemetryName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(activityListener);
        var market = Create();
        meterListener.Start();
        _source.ThenLoad("v1", 3).ThenUnchanged();

        await market.GetAsync(default);
        _clock.Now += MarketData.CheckInterval;
        await market.GetAsync(default);

        Assert.Equal(1, Interlocked.Read(ref reloads));
        Assert.Contains(spans, s => s.OperationName == "LoadMarketData"
            && s.GetTagItem("tracker.postings") is 3 && s.GetTagItem("tracker.runs") is 1);
    }
}
```

`tests/Api.Tests/Market/DbMarketSourceTests.cs`:
```csharp
using Api.Market;
using Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Tracker.Data;
using Tracker.Rebuild;
using Tracker.TestSupport;

namespace Api.Tests.Market;

[Collection("postgres")]
[Trait("Category", "Integration")]
public sealed class DbMarketSourceTests(PostgresFixture pg) : IDisposable
{
    private readonly TempDir _data = new();

    private static DbMarketSource Source(string connectionString, string dataRoot)
    {
        var services = new ServiceCollection()
            .AddDbContext<TrackerDb>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention())
            .BuildServiceProvider();
        return new DbMarketSource(services.GetRequiredService<IServiceScopeFactory>(), Data.Settings with { DataRoot = dataRoot }, TimeProvider.System);
    }

    private async Task<DbMarketSource> RebuiltAsync()
    {
        FixtureArchive.Write(_data.Path);
        await using (var db = await pg.OpenAsync())
            await new RebuildService(db, RepoConfig.Options(), Data.Settings.Catalog, NullLogger<RebuildService>.Instance)
                .RunAsync(_data.Path, CancellationToken.None);
        return Source(pg.ConnectionString, _data.Path);
    }

    [Fact]
    public async Task Loads_the_rebuilt_market_and_reports_no_change_for_the_same_version()
    {
        var source = await RebuiltAsync();

        var load = (await source.LoadIfChangedAsync(null, CancellationToken.None))!;

        Assert.Equal((3, 6), (load.Data.Runs.Count, load.Data.Postings.Count));
        Assert.Contains(load.Data.Postings, p => p.Id == "ashby:S1" && p.IsRemoteCanada && !p.IsGta);
        Assert.Null(await source.LoadIfChangedAsync(load.Version, CancellationToken.None));
    }

    [Fact]
    public async Task A_rebuild_of_the_same_runs_changes_the_version()
    {
        var source = await RebuiltAsync();
        var before = (await source.LoadIfChangedAsync(null, CancellationToken.None))!.Version;

        await RebuiltAsync();

        Assert.NotNull(await source.LoadIfChangedAsync(before, CancellationToken.None));
    }

    [Fact]
    public async Task A_load_during_a_rebuild_waits_for_it_and_sees_only_the_committed_result()
    {
        var source = await RebuiltAsync();
        await using var conn = new NpgsqlConnection(pg.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand(
            $"TRUNCATE {TrackerTables.All}; INSERT INTO runs (run_id, started_at) VALUES ('2026-12-01_0700', '2026-12-01T12:00:00Z')", conn, tx))
            await cmd.ExecuteNonQueryAsync();

        var loading = source.LoadIfChangedAsync(null, CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(loading.IsCompleted);

        await tx.CommitAsync();
        var load = (await loading)!;
        Assert.Equal(("2026-12-01_0700", 0), (Assert.Single(load.Data.Runs).RunId, load.Data.Postings.Count));
    }

    [Fact]
    public async Task An_unreachable_database_is_reported_as_unavailable()
    {
        var source = Source("Host=127.0.0.1;Port=1;Database=tracker;Username=tracker;Password=x;Timeout=3", _data.Path);
        await Assert.ThrowsAsync<DatabaseUnavailableException>(() => source.LoadIfChangedAsync(null, CancellationToken.None));
    }

    public void Dispose() => _data.Dispose();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Api.Tests && dotnet test tests/Tracker.Core.Tests --filter TrackerTablesTests`
Expected: build FAILS (`TrackerTables`, `IMarketSource`, `MarketData` and `DbMarketSource` don't exist).

- [ ] **Step 3: Implement**

`src/Tracker.Core/Data/TrackerTables.cs`:
```csharp
namespace Tracker.Data;

public static class TrackerTables
{
    // Every table the rebuild replaces, in the order its TRUNCATE locks them. A reader that locks the same tables
    // must use this exact order, or a reader and a rebuild can deadlock.
    public const string All = "posting_skills, posting_sightings, postings, skills, runs, adzuna_aggregates, jobbank_monthly";
}
```

In `src/Tracker.Core/Data/RebuildWriter.cs`, replace the `Truncate` constant with:
```csharp
    private const string Truncate = "TRUNCATE " + TrackerTables.All;
```

`src/Api/Market/IMarketSource.cs`:
```csharp
namespace Api.Market;

public sealed record MarketLoad(string Version, MarketSnapshot Data);

public interface IMarketSource
{
    // Null when the database still holds knownVersion; otherwise the current version and its data, read from one snapshot.
    // Never null when knownVersion is null.
    Task<MarketLoad?> LoadIfChangedAsync(string? knownVersion, CancellationToken ct);
}

// Postgres can't be reached: not running, wrong port, starting up, or refusing the login.
public sealed class DatabaseUnavailableException(Exception inner)
    : Exception("The tracker database isn't running. Start Docker Desktop (and `docker compose up -d postgres`).", inner);
```

`src/Api/Market/MarketData.cs`:
```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Api.Market;

public sealed record MarketRead(MarketSnapshot Data, bool Stale);

// Keeps the analysed market in memory. At most once a minute a request checks the database version and swaps in a
// fresh copy when it changed. Only a cold start waits for a load; later requests never queue behind a reload.
public sealed class MarketData : IDisposable
{
    public const string TelemetryName = "Tracker.MarketData";
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);
    private static readonly ActivitySource Activities = new(TelemetryName);

    private readonly IMarketSource _source;
    private readonly TimeProvider _clock;
    private readonly ILogger<MarketData> _log;
    private readonly Counter<long> _reloads;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile State? _state;

    private sealed record State(MarketSnapshot Data, string Version, DateTimeOffset CheckedAt, bool Stale);

    public MarketData(IMarketSource source, TimeProvider clock, IMeterFactory meters, ILogger<MarketData> log)
    {
        _source = source;
        _clock = clock;
        _log = log;
        _reloads = meters.Create(TelemetryName).CreateCounter<long>("tracker.market_data.reloads",
            description: "Times the market data was loaded from the database.");
    }

    public async Task<MarketRead> GetAsync(CancellationToken ct)
    {
        var state = _state;
        if (state is not null && !Due(state)) return Read(state);

        if (state is null) await _gate.WaitAsync(ct);
        else if (!await _gate.WaitAsync(0, ct)) return Read(state); // another request is already refreshing

        try
        {
            state = _state;
            if (state is null || Due(state)) _state = state = await RefreshAsync(state, ct);
            return Read(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private static MarketRead Read(State state) => new(state.Data, state.Stale);

    private bool Due(State state) => _clock.GetUtcNow() - state.CheckedAt >= CheckInterval;

    private async Task<State> RefreshAsync(State? current, CancellationToken ct)
    {
        using var activity = Activities.StartActivity("LoadMarketData");
        try
        {
            var load = await _source.LoadIfChangedAsync(current?.Version, ct);
            activity?.SetTag("tracker.changed", load is not null);
            if (load is null)
                return (current ?? throw new InvalidOperationException("The market source returned nothing for a first load."))
                    with { CheckedAt = _clock.GetUtcNow(), Stale = false };

            _reloads.Add(1);
            activity?.SetTag("tracker.runs", load.Data.Runs.Count);
            activity?.SetTag("tracker.postings", load.Data.Postings.Count);
            _log.LogInformation("Market data loaded: {Runs} runs, {Postings} postings", load.Data.Runs.Count, load.Data.Postings.Count);
            return new State(load.Data, load.Version, _clock.GetUtcNow(), Stale: false);
        }
        catch (DatabaseUnavailableException ex) when (current is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _log.LogWarning(ex, "Tracker database unavailable; serving the data loaded at {LoadedAt}", current.Data.LoadedAt);
            return current with { CheckedAt = _clock.GetUtcNow(), Stale = true };
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
```

`src/Api/Market/DbMarketSource.cs`:
```csharp
using System.Data;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tracker.Analysis;
using Tracker.Data;
using Tracker.Reading;

namespace Api.Market;

// Reads the market in one REPEATABLE READ transaction that first takes ACCESS SHARE locks on every rebuilt table,
// in the rebuild's TRUNCATE order: a load finishes before a rebuild starts or waits for it to commit, never half of each.
public sealed class DbMarketSource(IServiceScopeFactory scopes, MarketSettings settings, TimeProvider clock) : IMarketSource
{
    private const string LockSql = "LOCK TABLE " + TrackerTables.All + " IN ACCESS SHARE MODE";

    // Changes on every rebuild, even one that re-reads the same runs with new rules: the rebuild re-inserts the runs
    // rows, so their transaction id (xmin) is new.
    private const string VersionSql =
        """SELECT count(*)::text || '|' || coalesce(max(run_id), '') || '|' || coalesce(min(xmin::text), '') AS "Value" FROM runs""";

    private const string NoTablesVersion = "no-tables";

    public async Task<MarketLoad?> LoadIfChangedAsync(string? knownVersion, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackerDb>();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            await db.Database.ExecuteSqlRawAsync(LockSql, ct);
            var version = await db.Database.SqlQueryRaw<string>(VersionSql).SingleAsync(ct);
            if (version == knownVersion) return null;

            var (postings, runs, jobBank) = await AnalysisLoader.LoadAsync(db, ct);
            await tx.CommitAsync(ct);
            return new MarketLoad(version, new MarketSnapshot(postings, runs, jobBank, Warnings(), clock.GetUtcNow()));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // The Processor has never run against this database, so there is nothing to show yet.
            return knownVersion == NoTablesVersion ? null : new MarketLoad(NoTablesVersion, MarketSnapshot.Empty(clock.GetUtcNow()));
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            throw new DatabaseUnavailableException(ex);
        }
    }

    // The same data warnings the report shows with --report-only: problems in the latest collection and unreadable inbox files.
    private IReadOnlyList<string> Warnings()
    {
        var runs = Archive.Runs(settings.DataRoot);
        return [.. runs.Count > 0 ? Archive.CollectionProblems(runs[^1]) : [], .. InboxReader.ReadAll(Path.Combine(settings.DataRoot, "inbox"), "").Warnings];
    }

    // Not running, wrong port, timed out, starting up or refusing the login: the "start Docker" message applies.
    // Other server errors (bad SQL, a missing column) are bugs and stay 500s.
    private static bool IsUnavailable(Exception ex)
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

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests && dotnet test tests/Tracker.Core.Tests`
Expected: all PASS. The rebuild tests in Tracker.Core.Tests still pass with the shared TRUNCATE list, and the lock test takes about 1 s.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add consistent, cached market data loading with telemetry"
```

---

### Task 6: Endpoints, problem details and integration tests

**Files:**
- Modify: `src/Api/Program.cs` (full wiring)
- Create: `src/Api/Endpoints/MarketEndpoints.cs`, `src/Api/Endpoints/ApiExceptionHandler.cs`
- Test: `tests/Api.Tests/TestSupport/ApiFactory.cs`, `tests/Api.Tests/TestSupport/ApiFixture.cs`, `tests/Api.Tests/Endpoints/EndpointTests.cs`, `tests/Api.Tests/Endpoints/UnavailableDatabaseTests.cs`

**Interfaces:**
- Consumes: everything in Tasks 3–5
- Produces:
  - HTTP: `GET /api/meta` → `MetaView`; `GET /api/overview` → `{ report: ReportModel | null }`; `GET /api/skills` → `SkillList`; `GET /api/skills/{**name}` → `SkillDetail` or 404; `GET /api/salaries` → `SalaryView`; `GET /api/postings` → `PostingPage`; `GET /health`
  - Errors are problem details: 400 carries `parameter`, 404 names the skill, 503 carries the spec message, 500 is generic. Stale data adds the header `X-Data-Stale: true`
  - Config: `ConnectionStrings:tracker`, `Tracker:ProfilePath` (optional), `Tracker:DataRoot` (optional)
  - `record OverviewView(ReportModel? Report)`

- [ ] **Step 1: Write the failing tests**

`tests/Api.Tests/TestSupport/ApiFactory.cs`:
```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Tracker.TestSupport;

namespace Api.Tests.TestSupport;

// The real API wired to a given database and archive folder, with the committed example profile (config/profile.json is gitignored).
public sealed class ApiFactory(string connectionString, string dataRoot) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:tracker", connectionString);
        builder.UseSetting("Tracker:DataRoot", dataRoot);
        builder.UseSetting("Tracker:ProfilePath", RepoConfig.PathOf("profile.example.json"));
    }
}
```

`tests/Api.Tests/TestSupport/ApiFixture.cs`:
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Tracker.Rebuild;
using Tracker.TestSupport;

namespace Api.Tests.TestSupport;

// One Postgres container rebuilt from the fixture archive, and one API in front of it, shared by the endpoint tests.
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgresFixture _pg = new();
    private readonly TempDir _data = new();
    private ApiFactory? _factory;

    public HttpClient Client { get; private set; } = null!;
    public string ConnectionString => _pg.ConnectionString;

    public async Task InitializeAsync()
    {
        await _pg.InitializeAsync();
        FixtureArchive.Write(_data.Path);
        await using (var db = await _pg.OpenAsync())
            await new RebuildService(db, RepoConfig.Options(), Data.Settings.Catalog, NullLogger<RebuildService>.Instance)
                .RunAsync(_data.Path, CancellationToken.None);
        _factory = new ApiFactory(_pg.ConnectionString, _data.Path);
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        await _pg.DisposeAsync();
        _data.Dispose();
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
```

`tests/Api.Tests/Endpoints/EndpointTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Queries;
using Api.Tests.TestSupport;
using Tracker.Analysis;
using Tracker.Data;
using Tracker.TestSupport;

namespace Api.Tests.Endpoints;

[Collection("api")]
[Trait("Category", "Integration")]
public sealed class EndpointTests(ApiFixture api)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private async Task<T> GetAsync<T>(string url)
    {
        var response = await api.Client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    [Fact]
    public async Task Meta_describes_the_rebuilt_archive()
    {
        var meta = await GetAsync<MetaView>("/api/meta");

        Assert.Equal((FixtureArchive.Run3, 3, 30, false), (meta.LatestRunId, meta.Runs, meta.HistoryDays, meta.Stale));
        Assert.Contains("Acme", meta.Companies);
        Assert.Contains("Dotnet", meta.TargetFamilies);
    }

    [Fact]
    public async Task Overview_agrees_with_the_report_analyzer_for_the_default_filters()
    {
        await using var db = new TrackerDb(TrackerDbFactory.Options(api.ConnectionString));
        var (postings, runs, jobBank) = await AnalysisLoader.LoadAsync(db, CancellationToken.None);
        var report = new Analyzer(Data.Settings.Profile, Data.Settings.Catalog, 30).Build(postings, runs, jobBank, [], DateTimeOffset.Now);

        var model = (await GetAsync<JsonElement>("/api/overview")).GetProperty("report");

        Assert.Equal(report.Snapshot.InWindow, model.GetProperty("snapshot").GetProperty("inWindow").GetInt32());
        Assert.Equal(report.TopSkills.Select(s => s.Skill), model.GetProperty("topSkills").EnumerateArray().Select(s => s.GetProperty("skill").GetString()!));
        Assert.Equal(report.Gaps.Select(s => s.Skill), model.GetProperty("gaps").EnumerateArray().Select(s => s.GetProperty("skill").GetString()!));
    }

    [Fact]
    public async Task Postings_filter_by_place_and_page()
    {
        var remote = await GetAsync<PostingPage>("/api/postings?where=remote&window=all");
        Assert.Equal("ashby:S1", Assert.Single(remote.Items).Id);

        var page = await GetAsync<PostingPage>("/api/postings?families=dotnet,fullstack,software_engineer,lead_manager,other&window=all&pageSize=2&page=2");
        Assert.Equal((6, 2, 2), (page.Total, page.Page, page.Items.Count));
    }

    [Theory]
    [InlineData("C%23", "C#")]
    [InlineData(".NET", ".NET")]
    [InlineData("CI%2FCD", "CI/CD")]
    [InlineData("Estimation%20%26%20Planning", "Estimation & Planning")]
    [InlineData("OAuth%20%2F%20OIDC", "OAuth / OIDC")]
    public async Task Skill_detail_round_trips_names_that_need_encoding(string path, string skill) =>
        Assert.Equal(skill, (await GetAsync<SkillDetail>($"/api/skills/{path}?window=all")).Skill);

    [Fact]
    public async Task Unknown_skill_is_404_and_bad_filters_are_400_naming_the_parameter()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync("/api/skills/Cobol")).StatusCode);

        var bad = await api.Client.GetAsync("/api/postings?window=45");
        var problem = await bad.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("window", problem.GetProperty("parameter").GetString());
        Assert.Contains("45", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Skills_and_salaries_answer_with_camel_case_and_enum_names()
    {
        var skills = await GetAsync<JsonElement>("/api/skills?window=all&text=all");
        Assert.Contains(skills.GetProperty("skills").EnumerateArray(), s => s.GetProperty("skill").GetString() == "C#" && s.GetProperty("count").GetInt32() > 0);

        var salaries = await api.Client.GetStringAsync("/api/salaries?window=all");
        Assert.Contains("\"withSalary\":", salaries);
        Assert.Contains("\"seniority\":\"Senior\"", salaries);
    }

    [Fact]
    public async Task A_filter_that_matches_nothing_gives_empty_answers()
    {
        const string none = "where=remote&seniority=Junior&window=30";
        Assert.Equal(0, (await GetAsync<PostingPage>($"/api/postings?{none}")).Total);
        Assert.Equal(0, (await GetAsync<SkillList>($"/api/skills?{none}")).Postings);
        Assert.Empty((await GetAsync<SalaryView>($"/api/salaries?{none}")).Histogram);
        Assert.Equal(0, (await GetAsync<JsonElement>($"/api/overview?{none}")).GetProperty("report").GetProperty("snapshot").GetProperty("inWindow").GetInt32());
    }

    [Fact]
    public async Task Health_checks_the_database() =>
        Assert.Equal("Healthy", await api.Client.GetStringAsync("/health"));
}
```

`tests/Api.Tests/Endpoints/UnavailableDatabaseTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Market;
using Api.Tests.TestSupport;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Tracker.TestSupport;

namespace Api.Tests.Endpoints;

[Trait("Category", "Integration")]
public sealed class UnavailableDatabaseTests : IDisposable
{
    private const string ClosedPort = "Host=127.0.0.1;Port=1;Database=tracker;Username=tracker;Password=x;Timeout=3";
    private readonly TempDir _data = new();

    [Fact]
    public async Task A_stopped_database_answers_503_with_how_to_start_it()
    {
        await using var factory = new ApiFactory(ClosedPort, _data.Path);

        var response = await factory.CreateClient().GetAsync("/api/meta");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("docker compose up -d postgres", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Bad_filters_are_400_even_while_the_database_is_down()
    {
        await using var factory = new ApiFactory(ClosedPort, _data.Path);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().GetAsync("/api/skills?where=moon")).StatusCode);
    }

    [Fact]
    public async Task Data_loaded_before_an_outage_is_served_with_the_stale_header()
    {
        var clock = new ManualClock();
        var source = new ScriptedSource().ThenLoad("v1", 2).ThenDown();
        await using var factory = new ApiFactory(ClosedPort, _data.Path);
        using var client = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton<IMarketSource>(source);
            s.AddSingleton<TimeProvider>(clock);
        })).CreateClient();

        var fresh = await client.GetAsync("/api/meta");
        clock.Now += MarketData.CheckInterval;
        var stale = await client.GetAsync("/api/meta");

        Assert.False(fresh.Headers.Contains("X-Data-Stale"));
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        Assert.Equal("true", Assert.Single(stale.Headers.GetValues("X-Data-Stale")));
        Assert.True((await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stale").GetBoolean());
    }

    public void Dispose() => _data.Dispose();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~Endpoints"`
Expected: the tests FAIL: `/api/*` returns 404 because no endpoints are mapped yet, and the DI graph lacks `MarketData`.

- [ ] **Step 3: Implement**

`src/Api/Endpoints/ApiExceptionHandler.cs`:
```csharp
using Api.Market;
using Api.Queries;
using Microsoft.AspNetCore.Diagnostics;

namespace Api.Endpoints;

// Known failures become problem details: bad filter → 400, database down → 503, anything else → 500 without internals.
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var result = exception switch
        {
            FilterException f => Results.Problem(statusCode: 400, title: "Invalid filter", detail: f.Message,
                extensions: new Dictionary<string, object?> { ["parameter"] = f.Parameter }),
            DatabaseUnavailableException d => Results.Problem(statusCode: 503, title: "Database unavailable", detail: d.Message),
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
}
```

`src/Api/Endpoints/MarketEndpoints.cs`:
```csharp
using Api.Market;
using Api.Queries;
using Tracker.Analysis;

namespace Api.Endpoints;

public sealed record OverviewView(ReportModel? Report);

public static class MarketEndpoints
{
    public static void MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/meta", async (HttpContext http, MarketData market, MarketSettings settings) =>
        {
            var read = await ReadAsync(http, market);
            return MetaQueries.Meta(read.Data, settings, read.Stale);
        });

        api.MapGet("/overview", async (HttpContext http, MarketData market, MarketSettings settings, TimeProvider clock) =>
        {
            var filters = FilterParser.Parse(http.Request.Query, settings.Profile);
            var read = await ReadAsync(http, market);
            return new OverviewView(MetaQueries.Overview(read.Data, filters, settings, clock.GetLocalNow()));
        });

        api.MapGet("/skills", async (HttpContext http, MarketData market, MarketSettings settings, string? category) =>
        {
            var filters = FilterParser.Parse(http.Request.Query, settings.Profile);
            var text = FilterParser.Text(http.Request.Query, TextKind.Full);
            var read = await ReadAsync(http, market);
            return SkillQueries.List(read.Data, filters, text, category, settings);
        });

        // Catch-all so names with "/" (CI/CD) match; %2F stays encoded in the path, so unescape once more.
        api.MapGet("/skills/{**name}", async (HttpContext http, MarketData market, MarketSettings settings, string name) =>
        {
            var skill = Uri.UnescapeDataString(name);
            var filters = FilterParser.Parse(http.Request.Query, settings.Profile);
            var text = FilterParser.Text(http.Request.Query, TextKind.Full);
            var read = await ReadAsync(http, market);
            return SkillQueries.Detail(read.Data, filters, text, skill, settings) is { } detail
                ? Results.Ok(detail)
                : Results.Problem(statusCode: 404, title: "Unknown skill", detail: $"'{skill}' is not in config/skills.json.");
        });

        api.MapGet("/salaries", async (HttpContext http, MarketData market, MarketSettings settings, string? skill, string? company) =>
        {
            var filters = FilterParser.Parse(http.Request.Query, settings.Profile);
            var read = await ReadAsync(http, market);
            return SalaryQueries.View(read.Data, filters, skill, company, settings);
        });

        api.MapGet("/postings", async (HttpContext http, MarketData market, MarketSettings settings, string? q, string? skill, string? company) =>
        {
            var query = http.Request.Query;
            var filters = FilterParser.Parse(query, settings.Profile);
            var sort = FilterParser.Sort(query);
            var page = FilterParser.Int(query, "page", 1, 1, 10_000);
            var pageSize = FilterParser.Int(query, "pageSize", 25, 1, 100);
            var read = await ReadAsync(http, market);
            return PostingQueries.Page(read.Data, filters, q, skill, company, sort, page, pageSize, settings.Profile);
        });
    }

    // Called after the filters are parsed, so a bad filter is a 400 even while the database is down.
    private static async Task<MarketRead> ReadAsync(HttpContext http, MarketData market)
    {
        var read = await market.GetAsync(http.RequestAborted);
        if (read.Stale) http.Response.Headers["X-Data-Stale"] = "true";
        return read;
    }
}
```

`src/Api/Program.cs` (replace the whole file):
```csharp
using System.Text.Json.Serialization;
using Api;
using Api.Endpoints;
using Api.Market;
using Microsoft.EntityFrameworkCore;
using Tracker.Configuration;
using Tracker.Data;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var root = RepoRoot.Find(builder.Environment.ContentRootPath) ?? RepoRoot.Find(AppContext.BaseDirectory)
    ?? throw new InvalidOperationException("config/sources.json was not found in the API folder or any parent.");
builder.Services.AddSingleton(MarketSettings.Load(root, builder.Configuration["Tracker:ProfilePath"], builder.Configuration["Tracker:DataRoot"]));

// Read-only use of the database the Processor rebuilds. No retries: a stopped database should answer 503 at once.
builder.AddNpgsqlDbContext<TrackerDb>("tracker",
    settings => settings.DisableRetry = true,
    options => options.UseSnakeCaseNamingConvention());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IMarketSource, DbMarketSource>();
builder.Services.AddSingleton<MarketData>();
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(MarketData.TelemetryName))
    .WithMetrics(metrics => metrics.AddMeter(MarketData.TelemetryName));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
app.UseExceptionHandler();
app.MapDefaultEndpoints();
app.MapMarketEndpoints();
app.Run();

public partial class Program;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build src/Api -warnaserror && dotnet test tests/Api.Tests`
Expected: 0 warnings; all Api.Tests PASS.
- If `-warnaserror` flags `public partial class Program;` as redundant (.NET 10 generates the class as public), delete the line and ledger a ruling.
- If the endpoint tests can't see `ConnectionStrings:tracker` (the Aspire integration throws "ConnectionString is missing"), set it through `ConfigureAppConfiguration` in `ApiFactory` as well, and ledger that too.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test`
Expected: Archiver.Tests 101, Tracker.Core.Tests 240 and Api.Tests all PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add dashboard API endpoints with problem details and integration tests"
```

---

### Task 7: Dashboard scaffold, typed API client and URL filters

**Files:**
- Create: `src/Dashboard/package.json`, `src/Dashboard/tsconfig.json`, `src/Dashboard/vite.config.ts`, `src/Dashboard/index.html`
- Create: `src/Dashboard/src/theme.css`, `src/Dashboard/src/api.ts`, `src/Dashboard/src/filters.ts`, `src/Dashboard/src/format.ts`
- Create: `src/Dashboard/src/test/setup.ts`, `src/Dashboard/src/test/render.tsx`
- Test: `src/Dashboard/src/filters.test.ts`, `src/Dashboard/src/format.test.ts`, `src/Dashboard/src/api.test.ts`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: the JSON shapes from Task 6 (camelCase, enum names)
- Produces (TypeScript):
  - `api.ts`:
    - Types: `SkillLevel`, `TextKind` (`'full' | 'snippet' | 'all'`), `PostingSort`, `Meta`, `PostingRow`, `PostingPage`, `SkillRow`, `SkillList`, `NamedCount`, `SkillDetail`, `SalaryCell`, `Bucket`, `SalaryView`, `ReportModel` (and its parts), `Overview`, `PostingParams`
    - `class ApiError(message, status)`
    - `api.meta()`, `api.overview(f)`, `api.skills(f, text, category?)`, `api.skill(name, f, text)`, `api.salaries(f, skill?, company?)`, `api.postings(f, params)`
  - `filters.ts`:
    - Types: `Where`, `TimeWindow`, `Filters { families: string[]; seniority: string[]; where: Where; window: TimeWindow }`
    - Values and functions: `DEFAULT_FILTERS`, `readFilters(params)`, `writeFilters(filters, params?)`, `apiQuery(filters, extra?)`, `linkSearch(filters, extra?)`, `useFilters(): [Filters, (next: Filters) => void]`
  - `format.ts`: `money`, `salaryText`, `pct`, `signedPts`, `dateText`, `FAMILY_LABELS`, `SENIORITY_LABELS`, `label(map, key)`
  - Test support (`test/render.tsx`): `META`, `renderRoute(url, element, path?)`, `LocationProbe` (`data-testid="location"`), `mockApi(routes): string[]`, `failure(status, detail)`

- [ ] **Step 1: Project files**

`src/Dashboard/package.json`:
```json
{
  "name": "job-market-dashboard",
  "private": true,
  "version": "0.1.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc --noEmit && vite build",
    "typecheck": "tsc --noEmit",
    "test": "vitest run"
  },
  "dependencies": {
    "@tanstack/react-query": "5.104.0",
    "react": "19.3.0",
    "react-dom": "19.3.0",
    "react-router": "7.18.4",
    "recharts": "3.10.1"
  },
  "devDependencies": {
    "@testing-library/dom": "10.4.2",
    "@testing-library/jest-dom": "6.10.0",
    "@testing-library/react": "16.3.3",
    "@types/node": "22.20.4",
    "@types/react": "19.3.0",
    "@types/react-dom": "19.3.0",
    "@vitejs/plugin-react": "5.2.0",
    "jsdom": "26.1.0",
    "typescript": "5.9.3",
    "vite": "7.3.6",
    "vitest": "3.2.7"
  }
}
```

`src/Dashboard/tsconfig.json`:
```json
{
  "compilerOptions": {
    "target": "ES2022",
    "lib": ["ES2022", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "moduleResolution": "Bundler",
    "jsx": "react-jsx",
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noFallthroughCasesInSwitch": true,
    "isolatedModules": true,
    "skipLibCheck": true,
    "noEmit": true,
    "types": ["vite/client", "node"]
  },
  "include": ["src", "vite.config.ts"]
}
```

`src/Dashboard/vite.config.ts`:
```ts
/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// Under Aspire, AddViteApp sets PORT and WithReference(api) passes the API's address.
const apiUrl = process.env.API_HTTP ?? process.env.services__api__http__0 ?? 'http://localhost:5080';

export default defineConfig({
  plugins: [react()],
  server: {
    port: Number(process.env.PORT ?? 5173),
    strictPort: true,
    proxy: { '/api': { target: apiUrl, changeOrigin: true } },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
});
```

`src/Dashboard/index.html`:
```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Job Market Tracker</title>
    <link rel="preconnect" href="https://fonts.googleapis.com" />
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
    <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Archivo:wdth,wght@87.5,700;87.5,800&family=IBM+Plex+Mono:wght@400;500&family=Source+Sans+3:wght@400;600&display=swap" />
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

Append to `.gitignore`:
```
# Dashboard dependencies and build output
node_modules/
/src/Dashboard/dist/
```

Run: `cd src/Dashboard && npm install --no-fund --no-audit && npm ls --depth=0`
Expected: installs with no `ERESOLVE`/peer errors, and `package-lock.json` is created (commit it). If a pinned version no longer resolves, use the newest patch of the same major and ledger a ruling.

- [ ] **Step 2: Theme, API client, filters and formatting**

`src/Dashboard/src/theme.css` (the report's palette and type, so the dashboard and the published page look like one product):
```css
:root {
  --bg: #F2F4F7; --surface: #FFFFFF; --ink: #17212B; --muted: #586676; --rule: #D5DCE4;
  --accent: #A8132B; --accent-soft: #F5DADF; --bar: #2B5B8C; --bar-track: #E1E8F0;
  --good: #1C7445; --good-soft: #DAF0E3; --info: #2B5B8C; --info-soft: #DFE8F3; --warn: #99570A; --warn-soft: #F6E7D0;
  --display: "Archivo", "Arial Narrow", system-ui, sans-serif;
  --body: "Source Sans 3", "Segoe UI", system-ui, -apple-system, sans-serif;
  --mono: "IBM Plex Mono", ui-monospace, "Cascadia Mono", Consolas, monospace;
  color-scheme: light;
}
@media (prefers-color-scheme: dark) {
  :root {
    color-scheme: dark;
    --bg: #0E141B; --surface: #151E28; --ink: #E2E8EE; --muted: #94A2B1; --rule: #27333F;
    --accent: #FF7385; --accent-soft: #3E1D26; --bar: #72A9DD; --bar-track: #1D2B3A;
    --good: #5DCB90; --good-soft: #16311F; --info: #72A9DD; --info-soft: #172838; --warn: #F0AB5A; --warn-soft: #342413;
  }
}
* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--ink); font-family: var(--body); font-size: 16px; line-height: 1.55; }
a { color: var(--info); }
:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
h1, h2, h3 { font-family: var(--display); font-stretch: 87.5%; text-wrap: balance; margin: 0; line-height: 1.1; }
code { font-family: var(--mono); font-size: .9em; }
fieldset { border: 0; margin: 0; padding: 0; min-width: 0; }
legend { padding: 0; }
.shell { max-width: 1180px; margin: 0 auto; padding: 0 16px 48px; }
.skip { position: absolute; left: -9999px; }
.skip:focus { left: 16px; top: 8px; z-index: 10; background: var(--surface); padding: 6px 10px; }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
.masthead { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: 8px 16px; padding-block: 20px 12px; border-bottom: 3px solid var(--ink); }
.masthead h1 { font-size: 1.6rem; font-weight: 800; }
.run-line { font-family: var(--mono); font-size: .8rem; color: var(--muted); }
.nav { display: flex; flex-wrap: wrap; gap: 4px 20px; padding-block: 10px; border-bottom: 1px solid var(--rule); }
.nav a { font-family: var(--mono); font-size: .82rem; letter-spacing: .06em; text-transform: uppercase; color: var(--muted); text-decoration: none; padding-block: 4px; border-bottom: 2px solid transparent; }
.nav a.active { color: var(--ink); border-bottom-color: var(--accent); }
.filters, .toolbar { display: flex; flex-wrap: wrap; gap: 12px 24px; align-items: flex-end; }
.filters { padding-block: 14px; border-bottom: 1px solid var(--rule); }
.field { display: grid; gap: 4px; }
.field-label, .eyebrow { font-family: var(--mono); font-size: .72rem; letter-spacing: .08em; text-transform: uppercase; color: var(--muted); }
.checks { display: flex; flex-wrap: wrap; gap: 4px 14px; }
.checks label { display: inline-flex; gap: 6px; align-items: center; }
select, input[type="search"], button { font: inherit; font-size: .95rem; color: var(--ink); background: var(--surface); border: 1px solid var(--rule); border-radius: 4px; padding: 5px 8px; }
button { cursor: pointer; }
button:disabled { opacity: .5; cursor: default; }
details.multi { position: relative; }
details.multi summary { cursor: pointer; list-style: none; border: 1px solid var(--rule); border-radius: 4px; padding: 5px 8px; background: var(--surface); }
details.multi[open] .checks { position: absolute; z-index: 5; margin-top: 4px; padding: 10px 12px; min-width: 200px; display: grid; background: var(--surface); border: 1px solid var(--rule); border-radius: 4px; }
main { display: grid; gap: 32px; padding-top: 24px; }
footer { margin-top: 40px; font-size: .85rem; }
.section { display: grid; gap: 12px; min-width: 0; }
.section h2 { font-size: 1.35rem; font-weight: 800; }
.grid-2 { display: grid; gap: 32px; grid-template-columns: repeat(auto-fit, minmax(min(100%, 380px), 1fr)); }
.stats { display: grid; gap: 1px; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); background: var(--rule); border: 1px solid var(--rule); }
.stat { display: grid; gap: 2px; padding: 12px 14px; background: var(--surface); }
.stat-value { font-family: var(--display); font-stretch: 87.5%; font-size: 2rem; font-weight: 800; font-variant-numeric: tabular-nums; }
.table-wrap { overflow-x: auto; }
table { width: 100%; border-collapse: collapse; font-size: .95rem; }
th, td { text-align: left; padding: 7px 12px 7px 0; border-bottom: 1px solid var(--rule); vertical-align: top; }
thead th { font-family: var(--mono); font-size: .72rem; font-weight: 500; letter-spacing: .08em; text-transform: uppercase; color: var(--muted); }
th button.sort { all: unset; cursor: pointer; }
th button.sort:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.num { font-family: var(--mono); font-variant-numeric: tabular-nums; text-align: right; white-space: nowrap; }
.bar { position: relative; height: 10px; min-width: 80px; margin-top: 7px; background: var(--bar-track); }
.bar > span { position: absolute; inset: 0 auto 0 0; background: var(--bar); }
.range { position: relative; height: 12px; min-width: 140px; margin-top: 6px; background: var(--bar-track); }
.range > .iqr { position: absolute; top: 0; bottom: 0; background: var(--bar); opacity: .6; }
.range > .median { position: absolute; top: -3px; bottom: -3px; width: 3px; background: var(--ink); }
.chip { font-family: var(--mono); font-size: .72rem; letter-spacing: .04em; text-transform: uppercase; padding: 2px 6px; border-radius: 3px; white-space: nowrap; }
.chip-Strong { background: var(--good-soft); color: var(--good); }
.chip-Working { background: var(--info-soft); color: var(--info); }
.chip-Learning { background: var(--warn-soft); color: var(--warn); }
.chip-None { background: var(--accent-soft); color: var(--accent); }
.flag { color: var(--warn); font-family: var(--mono); font-size: .75rem; }
.muted { color: var(--muted); }
.small { font-size: .88rem; }
.change-up { color: var(--good); }
.change-down { color: var(--accent); }
.banner { padding: 10px 14px; border-left: 4px solid var(--accent); background: var(--accent-soft); }
.banner.warn { border-left-color: var(--warn); background: var(--warn-soft); }
.empty { margin: 0; padding-block: 16px; color: var(--muted); }
.ranked { margin: 0; padding-left: 1.5rem; display: grid; gap: 6px; }
.ranked li::marker { font-family: var(--mono); color: var(--muted); font-size: .85rem; }
.pills { display: flex; flex-wrap: wrap; gap: 6px; list-style: none; margin: 0; padding: 0; }
.pills a, .pills button { display: inline-block; font-size: .9rem; padding: 2px 10px; border: 1px solid var(--rule); border-radius: 999px; text-decoration: none; color: var(--ink); background: var(--surface); }
.pills .count { font-family: var(--mono); font-size: .78rem; color: var(--muted); margin-left: 6px; }
.compare { display: grid; gap: 1px; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); background: var(--rule); border: 1px solid var(--rule); }
.chart { width: 100%; height: 280px; }
.pager { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }
```

`src/Dashboard/src/filters.ts`:
```ts
import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router';

export type Where = 'all' | 'gta' | 'remote';
export type TimeWindow = '30' | '60' | '90' | 'all';

export interface Filters {
  families: string[]; // RoleFamily names; empty = the profile's target families (the API's default)
  seniority: string[]; // Seniority names; empty = all
  where: Where;
  window: TimeWindow;
}

export const DEFAULT_FILTERS: Filters = { families: [], seniority: [], where: 'all', window: '30' };

const WHERES: readonly string[] = ['all', 'gta', 'remote'];
const WINDOWS: readonly string[] = ['30', '60', '90', 'all'];
const FILTER_KEYS = ['families', 'seniority', 'where', 'window'];

function list(value: string | null): string[] {
  return (value ?? '').split(',').map((v) => v.trim()).filter((v) => v.length > 0);
}

// Unknown where/window values fall back to the defaults; unknown families or seniorities go to the API, which names them in a 400.
export function readFilters(params: URLSearchParams): Filters {
  const where = params.get('where') ?? '';
  const win = params.get('window') ?? '';
  return {
    families: list(params.get('families')),
    seniority: list(params.get('seniority')),
    where: WHERES.includes(where) ? (where as Where) : DEFAULT_FILTERS.where,
    window: WINDOWS.includes(win) ? (win as TimeWindow) : DEFAULT_FILTERS.window,
  };
}

// Writes the filters into a copy of `params`, leaving other keys (q, sort, page…) alone and leaving defaults out.
export function writeFilters(filters: Filters, params: URLSearchParams = new URLSearchParams()): URLSearchParams {
  const out = new URLSearchParams(params);
  FILTER_KEYS.forEach((key) => out.delete(key));
  if (filters.families.length > 0) out.set('families', filters.families.join(','));
  if (filters.seniority.length > 0) out.set('seniority', filters.seniority.join(','));
  if (filters.where !== DEFAULT_FILTERS.where) out.set('where', filters.where);
  if (filters.window !== DEFAULT_FILTERS.window) out.set('window', filters.window);
  return out;
}

// Query string for an API call: the filters plus any extra parameters that have a value.
export function apiQuery(filters: Filters, extra: Record<string, string | number | undefined> = {}): string {
  const params = writeFilters(filters);
  Object.entries(extra).forEach(([key, value]) => {
    if (value !== undefined && value !== '') params.set(key, String(value));
  });
  const text = params.toString();
  return text ? `?${text}` : '';
}

// Search string for an in-app link: the current filters plus page-specific parameters.
export function linkSearch(filters: Filters, extra: Record<string, string> = {}): string {
  const params = writeFilters(filters);
  Object.entries(extra).forEach(([key, value]) => params.set(key, value));
  return params.toString();
}

export function useFilters(): [Filters, (next: Filters) => void] {
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => readFilters(params), [params]);
  const setFilters = useCallback(
    (next: Filters) =>
      setParams((current) => {
        const out = writeFilters(next, current);
        out.delete('page'); // new filters start again from the first page
        return out;
      }),
    [setParams],
  );
  return [filters, setFilters];
}
```

`src/Dashboard/src/api.ts`:
```ts
import { apiQuery, type Filters } from './filters';

export type SkillLevel = 'None' | 'Learning' | 'Working' | 'Strong';
export type TextKind = 'full' | 'snippet' | 'all';
export type PostingSort = 'newest' | 'match' | 'salary';

export interface Meta {
  latestRunId: string | null;
  latestRunAt: string | null;
  runs: number;
  historyDays: number;
  targetFamilies: string[];
  families: string[];
  seniorities: string[];
  categories: string[];
  companies: string[];
  stale: boolean;
}

export interface PostingRow {
  id: string;
  title: string;
  company: string;
  location: string;
  url: string;
  salaryMin: number | null;
  salaryMax: number | null;
  salaryPeriod: 'Year' | 'Hour' | null;
  match: number;
  firstSeen: string;
  skills: string[];
  citizenshipOrClearance: boolean;
  source: string;
  isFullText: boolean;
}

export interface PostingPage { total: number; page: number; pageSize: number; items: PostingRow[] }

export interface SkillRow {
  skill: string;
  category: string;
  share: number;
  count: number;
  level: SkillLevel;
  previousShare: number | null;
  change: number | null;
}

export interface SkillList { postings: number; skills: SkillRow[] }

export interface NamedCount { name: string; count: number }

export interface SkillDetail {
  skill: string;
  category: string;
  level: SkillLevel;
  share: number;
  count: number;
  postings: number;
  salary: { withMedian: number | null; withCount: number; withoutMedian: number | null; withoutCount: number };
  coSkills: NamedCount[];
  companies: NamedCount[];
  examples: PostingRow[];
}

export interface SalaryCell {
  family: string;
  seniority: string;
  count: number;
  median: number | null;
  p25: number | null;
  p75: number | null;
}

export interface Bucket { from: number; to: number; count: number }

export interface SalaryView { postings: number; withSalary: number; cells: SalaryCell[]; histogram: Bucket[]; examples: PostingRow[] }

export interface SalaryRange { min: number; max: number; period: 'Year' | 'Hour' }

export interface ReportModel {
  generatedAt: string;
  latestRunId: string;
  latestRunAt: string;
  windowDays: number;
  snapshot: {
    activeNow: number;
    newSinceLastRun: number;
    inWindow: number;
    fullText: number;
    snippets: number;
    withSalary: number;
    citizenshipFlagged: number;
    runs: number;
    historyDays: number;
    bySource: Record<string, number>;
  };
  topSkills: { skill: string; category: string; fullTextShare: number; snippetShare: number; count: number; level: SkillLevel }[];
  gaps: SkillScore[];
  strengths: SkillScore[];
  salaries: SalaryCell[];
  companies: { company: string; postings: number }[];
  matches: {
    title: string;
    company: string;
    url: string;
    location: string;
    score: number;
    skillCount: number;
    missing: string[];
    salary: SalaryRange | null;
    citizenshipOrClearance: boolean;
  }[];
  trends: { weekly: { weekStart: string; active: number }[]; moves: { skill: string; previous: number; current: number }[] } | null;
  warnings: string[];
}

export interface SkillScore { skill: string; category: string; demand: number; level: SkillLevel; score: number }

export interface Overview { report: ReportModel | null }

export interface PostingParams { q?: string; skill?: string; company?: string; sort?: PostingSort; page?: number; pageSize?: number }

export class ApiError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

async function get<T>(path: string): Promise<T> {
  let response: Response;
  try {
    response = await fetch(path, { headers: { Accept: 'application/json' } });
  } catch {
    throw new ApiError('The API is not reachable. Is the dashboard (scripts/dashboard.cmd) running?', 0);
  }
  if (!response.ok) {
    let message = `The API answered ${response.status}.`;
    try {
      const problem = (await response.json()) as { detail?: string; title?: string };
      message = problem.detail ?? problem.title ?? message;
    } catch {
      // Not a problem-details body; keep the status message.
    }
    throw new ApiError(message, response.status);
  }
  return (await response.json()) as T;
}

export const api = {
  meta: () => get<Meta>('/api/meta'),
  overview: (f: Filters) => get<Overview>(`/api/overview${apiQuery(f)}`),
  skills: (f: Filters, text: TextKind, category?: string) => get<SkillList>(`/api/skills${apiQuery(f, { text, category })}`),
  skill: (name: string, f: Filters, text: TextKind) =>
    get<SkillDetail>(`/api/skills/${encodeURIComponent(name)}${apiQuery(f, { text })}`),
  salaries: (f: Filters, skill?: string, company?: string) => get<SalaryView>(`/api/salaries${apiQuery(f, { skill, company })}`),
  postings: (f: Filters, p: PostingParams) => get<PostingPage>(`/api/postings${apiQuery(f, { ...p })}`),
};
```

`src/Dashboard/src/format.ts`:
```ts
export const FAMILY_LABELS: Record<string, string> = {
  Dotnet: '.NET',
  Fullstack: 'Full stack',
  SoftwareEngineer: 'Software engineer',
  LeadManager: 'Lead / manager',
  Other: 'Other',
};

export const SENIORITY_LABELS: Record<string, string> = {
  Unspecified: 'Not stated',
  Junior: 'Junior',
  Intermediate: 'Intermediate',
  Senior: 'Senior',
  StaffPrincipal: 'Staff / principal',
  Lead: 'Lead',
  Manager: 'Manager',
};

export function label(map: Record<string, string>, key: string): string {
  return map[key] ?? key;
}

// Yearly amounts in thousands ($162k); hourly amounts in dollars ($45).
export function money(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '—';
  return value >= 1000 ? `$${Math.round(value / 1000)}k` : `$${Math.round(value)}`;
}

export function salaryText(min: number | null, max: number | null, period: string | null): string {
  if (min === null || max === null) return '—';
  const range = min === max ? money(min) : `${money(min)}–${money(max)}`;
  return period === 'Hour' ? `${range}/h` : range;
}

export function pct(share: number | null | undefined): string {
  if (share === null || share === undefined || Number.isNaN(share)) return '—';
  return `${Math.round(share * 100)}%`;
}

// Change in share between two windows, in percentage points.
export function signedPts(change: number | null): string {
  if (change === null || Number.isNaN(change)) return '—';
  const pts = Math.round(change * 100);
  if (pts === 0) return '±0 pts';
  return `${pts > 0 ? '+' : '−'}${Math.abs(pts)} pts`;
}

export function dateText(iso: string | null | undefined): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleDateString('en-CA', { year: 'numeric', month: 'short', day: 'numeric', timeZone: 'America/Toronto' });
}
```

`src/Dashboard/src/test/setup.ts`:
```ts
import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, vi } from 'vitest';

// jsdom has no ResizeObserver; Recharts' ResponsiveContainer needs one to mount.
class ResizeObserverStub {
  observe() {}
  unobserve() {}
  disconnect() {}
}
globalThis.ResizeObserver ??= ResizeObserverStub as unknown as typeof ResizeObserver;

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});
```

`src/Dashboard/src/test/render.tsx`:
```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import type { ReactElement } from 'react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { vi } from 'vitest';
import type { Meta } from '../api';

export const META: Meta = {
  latestRunId: '2026-10-01_0700',
  latestRunAt: '2026-10-01T11:00:00Z',
  runs: 3,
  historyDays: 30,
  targetFamilies: ['Dotnet', 'Fullstack', 'SoftwareEngineer', 'LeadManager'],
  families: ['Dotnet', 'Fullstack', 'SoftwareEngineer', 'LeadManager', 'Other'],
  seniorities: ['Unspecified', 'Junior', 'Intermediate', 'Senior', 'StaffPrincipal', 'Lead', 'Manager'],
  categories: ['backend', 'devops', 'language'],
  companies: ['Acme', 'Globex'],
  stale: false,
};

export function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname + location.search}</output>;
}

// Renders `element` at `url` inside a router and a fresh query client (no retries), plus a probe showing the current URL.
export function renderRoute(url: string, element: ReactElement, path = '*') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[url]}>
        <Routes>
          <Route path={path} element={<>{element}<LocationProbe /></>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const FAILURE = Symbol('failure');

export function failure(status: number, detail: string) {
  return { [FAILURE]: true, status, body: { title: 'Error', status, detail } };
}

// Replaces fetch: each request gets the body of the longest matching path prefix. Returns the URLs requested.
export function mockApi(routes: Record<string, unknown>): string[] {
  const calls: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input);
    calls.push(url);
    const key = Object.keys(routes).filter((k) => url.startsWith(k)).sort((a, b) => b.length - a.length)[0];
    const value = key === undefined ? failure(500, `No mock for ${url}`) : routes[key];
    const failed = typeof value === 'object' && value !== null && FAILURE in value;
    const { status, body } = failed ? (value as { status: number; body: unknown }) : { status: 200, body: value };
    return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
  }));
  return calls;
}
```

- [ ] **Step 3: Write the tests**

`src/Dashboard/src/filters.test.ts`:
```ts
import { describe, expect, it } from 'vitest';
import { DEFAULT_FILTERS, apiQuery, linkSearch, readFilters, writeFilters, type Filters } from './filters';

describe('filters in the URL', () => {
  it('reads the defaults from an empty query', () => {
    expect(readFilters(new URLSearchParams())).toEqual(DEFAULT_FILTERS);
  });

  it('round-trips every filter', () => {
    const filters: Filters = { families: ['Dotnet', 'LeadManager'], seniority: ['Senior', 'Lead'], where: 'gta', window: '90' };
    expect(readFilters(writeFilters(filters))).toEqual(filters);
  });

  it('leaves defaults out and keeps unrelated keys', () => {
    const params = writeFilters(DEFAULT_FILTERS, new URLSearchParams('q=bank&families=Other&window=60'));
    expect(params.toString()).toBe('q=bank');
  });

  it('falls back to the defaults for place and window values the API would reject', () => {
    const f = readFilters(new URLSearchParams('where=moon&window=45'));
    expect([f.where, f.window]).toEqual(['all', '30']);
  });

  it('builds API query strings, skipping empty extras', () => {
    expect(apiQuery({ ...DEFAULT_FILTERS, where: 'remote' }, { q: 'bank', skill: undefined, company: '', page: 2 })).toBe('?where=remote&q=bank&page=2');
    expect(apiQuery(DEFAULT_FILTERS)).toBe('');
  });

  it('builds link search strings that carry the filters', () => {
    expect(linkSearch({ ...DEFAULT_FILTERS, window: 'all' }, { company: 'Acme' })).toBe('window=all&company=Acme');
  });
});
```

`src/Dashboard/src/format.test.ts`:
```ts
import { describe, expect, it } from 'vitest';
import { money, pct, salaryText, signedPts } from './format';

describe('format', () => {
  it('shows yearly money in thousands and hourly money in dollars', () => {
    expect(money(162_000)).toBe('$162k');
    expect(money(45)).toBe('$45');
    expect(money(null)).toBe('—');
  });

  it('writes salary ranges with the period', () => {
    expect(salaryText(120_000, 140_000, 'Year')).toBe('$120k–$140k');
    expect(salaryText(100_000, 100_000, 'Year')).toBe('$100k');
    expect(salaryText(45, 60, 'Hour')).toBe('$45–$60/h');
    expect(salaryText(null, null, null)).toBe('—');
  });

  it('writes shares and changes without NaN', () => {
    expect(pct(0.254)).toBe('25%');
    expect(pct(Number.NaN)).toBe('—');
    expect(signedPts(0.05)).toBe('+5 pts');
    expect(signedPts(-0.031)).toBe('−3 pts');
    expect(signedPts(0)).toBe('±0 pts');
    expect(signedPts(null)).toBe('—');
  });
});
```

`src/Dashboard/src/api.test.ts`:
```ts
import { describe, expect, it, vi } from 'vitest';
import { ApiError, api } from './api';
import { DEFAULT_FILTERS } from './filters';
import { failure, mockApi } from './test/render';

describe('api client', () => {
  it.each([
    ['C#', '/api/skills/C%23?text=full'],
    ['CI/CD', '/api/skills/CI%2FCD?text=full'],
    ['Estimation & Planning', '/api/skills/Estimation%20%26%20Planning?text=full'],
  ])('encodes the skill name %s in the path', async (name, expected) => {
    const calls = mockApi({ '/api/skills/': { skill: name } });
    await api.skill(name, DEFAULT_FILTERS, 'full');
    expect(calls[0]).toBe(expected);
  });

  it('turns problem details into an ApiError with the detail text', async () => {
    mockApi({ '/api/meta': failure(503, "The tracker database isn't running.") });
    await expect(api.meta()).rejects.toMatchObject({ status: 503, message: "The tracker database isn't running." });
  });

  it('explains when the API cannot be reached at all', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => {
      throw new TypeError('Failed to fetch');
    }));
    await expect(api.meta()).rejects.toBeInstanceOf(ApiError);
  });
});
```

- [ ] **Step 4: Run tests and type check**

Run: `cd src/Dashboard && npm test && npm run typecheck`
Expected: 3 files, 14 tests PASS; `tsc` reports no errors. These modules are new, so the red step is the first run: fix the code, not the tests, if anything fails.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Scaffold the React dashboard with a typed API client and URL filters"
```

---

### Task 8: App shell, filter bar, shared states and the Overview page

**Files:**
- Create: `src/Dashboard/src/main.tsx`, `src/Dashboard/src/App.tsx`
- Create: `src/Dashboard/src/components/FilterBar.tsx`, `States.tsx`, `LevelChip.tsx`, `PostingList.tsx`
- Create: `src/Dashboard/src/pages/Overview.tsx`
- Test: `src/Dashboard/src/components/States.test.tsx`, `FilterBar.test.tsx`, `PostingList.test.tsx`, `src/Dashboard/src/App.test.tsx`

**Interfaces:**
- Consumes: Task 7's `api`, filters, format and test support
- Produces:
  - `App` has a `<Routes>` block with `/` → `Overview` and a catch-all. Tasks 9–10 add routes there.
  - Components:
    - `FilterBar({ meta })`
    - `Loading({ what })`, `ErrorBanner({ error })`, `StaleBanner()`, `Empty({ children })`
    - `QueryState({ query: { isPending: boolean; error: unknown }, what })`
    - `LevelChip({ level })`
    - `PostingList({ rows, caption })`
    - `Overview()`

- [ ] **Step 1: Write the failing tests**

`src/Dashboard/src/components/States.test.tsx`:
```tsx
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ApiError } from '../api';
import { ErrorBanner, QueryState } from './States';

describe('shared states', () => {
  it("shows the API's message in an alert", () => {
    render(<ErrorBanner error={new ApiError("'Cobol' is not in config/skills.json.", 404)} />);
    expect(screen.getByRole('alert')).toHaveTextContent("'Cobol' is not in config/skills.json.");
  });

  it('shows loading while pending and the error once it fails', () => {
    const { rerender } = render(<QueryState query={{ isPending: true, error: null }} what="skills" />);
    expect(screen.getByRole('status')).toHaveTextContent('Loading skills');
    rerender(<QueryState query={{ isPending: false, error: new ApiError('Down', 503) }} what="skills" />);
    expect(screen.getByRole('alert')).toHaveTextContent('Down');
  });
});
```

`src/Dashboard/src/components/FilterBar.test.tsx`:
```tsx
import { fireEvent, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { META, renderRoute } from '../test/render';
import { FilterBar } from './FilterBar';

const url = () => decodeURIComponent(screen.getByTestId('location').textContent ?? '');

describe('FilterBar', () => {
  it('ticks the profile families when the URL names none', () => {
    renderRoute('/skills', <FilterBar meta={META} />);
    expect(screen.getByLabelText('.NET')).toBeChecked();
    expect(screen.getByLabelText('Other')).not.toBeChecked();
  });

  it('writes changes to the URL, keeps other keys and returns to page 1', () => {
    renderRoute('/postings?page=3&q=bank', <FilterBar meta={META} />);

    fireEvent.click(screen.getByLabelText('Other'));
    fireEvent.change(screen.getByLabelText('Where'), { target: { value: 'remote' } });
    fireEvent.change(screen.getByLabelText('Window'), { target: { value: 'all' } });

    expect(url()).toContain('families=Dotnet,Fullstack,SoftwareEngineer,LeadManager,Other');
    expect(url()).toContain('where=remote');
    expect(url()).toContain('window=all');
    expect(url()).toContain('q=bank');
    expect(url()).not.toContain('page=');
  });

  it('keeps at least one role family ticked', () => {
    renderRoute('/?families=Dotnet', <FilterBar meta={META} />);
    expect(screen.getByLabelText('.NET')).toBeDisabled();
    expect(screen.getByLabelText('Other')).toBeEnabled();
  });

  it('filters by seniority and shows the choice in the summary', () => {
    renderRoute('/', <FilterBar meta={META} />);
    fireEvent.click(screen.getByLabelText('Senior'));
    expect(url()).toContain('seniority=Senior');
    expect(screen.getByText('Seniority: Senior')).toBeInTheDocument();
  });
});
```

`src/Dashboard/src/components/PostingList.test.tsx`:
```tsx
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { PostingRow } from '../api';
import { PostingList } from './PostingList';

const row: PostingRow = {
  id: 'ashby:S1', title: 'Senior Software Engineer', company: 'Cohere', location: 'Remote (Canada)',
  url: 'https://jobs.ashbyhq.com/cohere/S1', salaryMin: 150000, salaryMax: 190000, salaryPeriod: 'Year', match: 0.5,
  firstSeen: '2026-10-01T11:00:00Z', skills: ['Go', 'Kubernetes'], citizenshipOrClearance: true, source: 'ashby', isFullText: true,
};

describe('PostingList', () => {
  it('shows salary, match and the citizenship flag with a link to the posting', () => {
    render(<PostingList rows={[row]} caption="Examples" />);
    expect(screen.getByRole('link', { name: 'Senior Software Engineer' })).toHaveAttribute('href', row.url);
    expect(screen.getByText('$150k–$190k')).toBeInTheDocument();
    expect(screen.getByText('50%')).toBeInTheDocument();
    expect(screen.getByText(/citizenship/)).toBeInTheDocument();
  });

  it('says so when nothing matches', () => {
    render(<PostingList rows={[]} caption="Examples" />);
    expect(screen.getByText(/No postings match these filters/)).toBeInTheDocument();
  });
});
```

`src/Dashboard/src/App.test.tsx`:
```tsx
import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { App } from './App';
import { META, failure, mockApi, renderRoute } from './test/render';

describe('App', () => {
  it('says when there is no data yet', async () => {
    mockApi({ '/api/meta': { ...META, runs: 0, latestRunId: null, latestRunAt: null } });
    renderRoute('/', <App />);
    expect(await screen.findByText(/No data yet/)).toBeInTheDocument();
  });

  it("shows the API's message when the database is down on a cold start", async () => {
    mockApi({ '/api/meta': failure(503, "The tracker database isn't running. Start Docker Desktop (and `docker compose up -d postgres`).") });
    renderRoute('/', <App />);
    expect(await screen.findByRole('alert')).toHaveTextContent('docker compose up -d postgres');
  });

  it('warns when the numbers come from before a database outage', async () => {
    mockApi({ '/api/meta': { ...META, stale: true, runs: 0 } });
    renderRoute('/', <App />);
    expect(await screen.findByText(/last successful load/)).toBeInTheDocument();
  });

  it('keeps the filters in the page links', async () => {
    mockApi({ '/api/meta': { ...META, runs: 0 } });
    renderRoute('/?where=gta', <App />);
    expect(await screen.findByRole('link', { name: 'Skills' })).toHaveAttribute('href', '/skills?where=gta');
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd src/Dashboard && npm test`
Expected: the new test files FAIL to import `./States`, `./FilterBar`, `./PostingList` and `./App`.

- [ ] **Step 3: Implement**

`src/Dashboard/src/components/States.tsx`:
```tsx
import type { ReactNode } from 'react';

export function Loading({ what }: { what: string }) {
  return <p className="empty" role="status">Loading {what}…</p>;
}

export function ErrorBanner({ error }: { error: unknown }) {
  const message = error instanceof Error ? error.message : 'Something went wrong.';
  return <div className="banner" role="alert">{message}</div>;
}

export function StaleBanner() {
  return (
    <div className="banner warn" role="status">
      The database isn't reachable, so these numbers are from the last successful load. Start Docker Desktop
      (and <code>docker compose up -d postgres</code>) to refresh them.
    </div>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return <p className="empty">{children}</p>;
}

// Loading or error for a query that has no data yet.
export function QueryState({ query, what }: { query: { isPending: boolean; error: unknown }; what: string }) {
  if (query.error) return <ErrorBanner error={query.error} />;
  if (query.isPending) return <Loading what={what} />;
  return null;
}
```

`src/Dashboard/src/components/LevelChip.tsx`:
```tsx
import type { SkillLevel } from '../api';

export function LevelChip({ level }: { level: SkillLevel }) {
  return <span className={`chip chip-${level}`}>{level === 'None' ? 'Gap' : level}</span>;
}
```

`src/Dashboard/src/components/PostingList.tsx`:
```tsx
import type { PostingRow } from '../api';
import { dateText, pct, salaryText } from '../format';

export function PostingList({ rows, caption }: { rows: PostingRow[]; caption: string }) {
  if (rows.length === 0) return <p className="empty">No postings match these filters.</p>;
  return (
    <div className="table-wrap">
      <table>
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr>
            <th>Role</th>
            <th>Company</th>
            <th>Where</th>
            <th className="num">Salary</th>
            <th className="num">Match</th>
            <th className="num">First seen</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.id}>
              <td>
                <a href={r.url} target="_blank" rel="noreferrer">{r.title}</a>
                {r.citizenshipOrClearance && <span className="flag"> · citizenship/clearance</span>}
                <div className="muted small">{r.skills.join(' · ')}</div>
              </td>
              <td>{r.company}</td>
              <td>{r.location}</td>
              <td className="num">{salaryText(r.salaryMin, r.salaryMax, r.salaryPeriod)}</td>
              <td className="num">{pct(r.match)}</td>
              <td className="num">{dateText(r.firstSeen)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
```

`src/Dashboard/src/components/FilterBar.tsx`:
```tsx
import type { Meta } from '../api';
import { useFilters, type Filters, type TimeWindow, type Where } from '../filters';
import { FAMILY_LABELS, SENIORITY_LABELS, label } from '../format';

const WHERE_LABELS: Record<Where, string> = { all: 'GTA + remote Canada', gta: 'GTA only', remote: 'Remote (Canada) only' };
const WINDOW_LABELS: Record<TimeWindow, string> = { '30': 'Last 30 days', '60': 'Last 60 days', '90': 'Last 90 days', all: 'All history' };

function toggle(list: string[], value: string, on: boolean): string[] {
  return on ? [...list, value] : list.filter((v) => v !== value);
}

export function FilterBar({ meta }: { meta: Meta }) {
  const [filters, setFilters] = useFilters();
  // No families in the URL means the profile's target families (the API's default), so those show as ticked.
  const families = filters.families.length > 0 ? filters.families : meta.targetFamilies;
  const update = (patch: Partial<Filters>) => setFilters({ ...filters, ...patch });
  const senioritySummary = filters.seniority.length === 0
    ? 'Seniority: all'
    : `Seniority: ${filters.seniority.map((s) => label(SENIORITY_LABELS, s)).join(', ')}`;

  return (
    <form className="filters" aria-label="Filters" onSubmit={(e) => e.preventDefault()}>
      <fieldset className="field">
        <legend className="field-label">Roles</legend>
        <div className="checks">
          {meta.families.map((f) => (
            <label key={f}>
              <input
                type="checkbox"
                checked={families.includes(f)}
                disabled={families.length === 1 && families.includes(f)}
                onChange={(e) => update({ families: toggle(families, f, e.target.checked) })}
              />
              {label(FAMILY_LABELS, f)}
            </label>
          ))}
        </div>
      </fieldset>

      <details className="multi field">
        <summary>{senioritySummary}</summary>
        <fieldset className="checks">
          <legend className="sr-only">Seniority</legend>
          {meta.seniorities.map((s) => (
            <label key={s}>
              <input
                type="checkbox"
                checked={filters.seniority.includes(s)}
                onChange={(e) => update({ seniority: toggle(filters.seniority, s, e.target.checked) })}
              />
              {label(SENIORITY_LABELS, s)}
            </label>
          ))}
        </fieldset>
      </details>

      <div className="field">
        <label className="field-label" htmlFor="filter-where">Where</label>
        <select id="filter-where" value={filters.where} onChange={(e) => update({ where: e.target.value as Where })}>
          {(Object.keys(WHERE_LABELS) as Where[]).map((w) => <option key={w} value={w}>{WHERE_LABELS[w]}</option>)}
        </select>
      </div>

      <div className="field">
        <label className="field-label" htmlFor="filter-window">Window</label>
        <select id="filter-window" value={filters.window} onChange={(e) => update({ window: e.target.value as TimeWindow })}>
          {(Object.keys(WINDOW_LABELS) as TimeWindow[]).map((w) => <option key={w} value={w}>{WINDOW_LABELS[w]}</option>)}
        </select>
      </div>
    </form>
  );
}
```

`src/Dashboard/src/App.tsx`:
```tsx
import { useQuery } from '@tanstack/react-query';
import { NavLink, Route, Routes } from 'react-router';
import { api } from './api';
import { FilterBar } from './components/FilterBar';
import { Empty, ErrorBanner, StaleBanner } from './components/States';
import { linkSearch, useFilters } from './filters';
import { dateText } from './format';
import { Overview } from './pages/Overview';

const PAGES = [
  { to: '/', label: 'Overview' },
  { to: '/skills', label: 'Skills' },
  { to: '/salaries', label: 'Salaries' },
  { to: '/postings', label: 'Postings' },
];

export function App() {
  const [filters] = useFilters();
  const meta = useQuery({ queryKey: ['meta'], queryFn: api.meta, refetchInterval: 60_000 });
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
      </nav>
      {meta.data && <FilterBar meta={meta.data} />}
      <main id="content">
        {meta.error && <ErrorBanner error={meta.error} />}
        {meta.data?.stale && <StaleBanner />}
        {meta.data && meta.data.runs === 0 && <Empty>No data yet: the first collection runs Tuesday or Friday at 07:00.</Empty>}
        {meta.data && meta.data.runs > 0 && (
          <Routes>
            <Route path="/" element={<Overview />} />
            <Route path="*" element={<Empty>There's no page here. Pick one from the menu above.</Empty>} />
          </Routes>
        )}
      </main>
      <footer className="muted">
        Jobs data partly by <a href="https://www.adzuna.ca" target="_blank" rel="noreferrer">Adzuna</a>. Other postings come from company job boards, the Job Bank and saved pastes.
      </footer>
    </div>
  );
}
```

`src/Dashboard/src/main.tsx`:
```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router';
import { ApiError } from './api';
import { App } from './App';
import './theme.css';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      // A 4xx won't change on retry; anything else (a restart, a blip) gets one more try.
      retry: (failureCount, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && failureCount < 1,
    },
  },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
```

`src/Dashboard/src/pages/Overview.tsx`:
```tsx
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';
import { Bar, BarChart, CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { api, type SkillScore } from '../api';
import { LevelChip } from '../components/LevelChip';
import { Empty, QueryState } from '../components/States';
import { linkSearch, useFilters, type Filters } from '../filters';
import { pct, salaryText } from '../format';

const TOOLTIP_STYLE = { background: 'var(--surface)', border: '1px solid var(--rule)', color: 'var(--ink)' };

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="stat">
      <span className="eyebrow">{label}</span>
      <span className="stat-value">{value.toLocaleString('en-CA')}</span>
    </div>
  );
}

function SkillRanking({ items, filters }: { items: SkillScore[]; filters: Filters }) {
  if (items.length === 0) return <Empty>Nothing to rank for these filters.</Empty>;
  return (
    <ol className="ranked">
      {items.map((s) => (
        <li key={s.skill}>
          <Link to={{ pathname: `/skills/${encodeURIComponent(s.skill)}`, search: linkSearch(filters) }}>{s.skill}</Link>{' '}
          <span className="muted small">{pct(s.demand)} of postings</span> <LevelChip level={s.level} />
        </li>
      ))}
    </ol>
  );
}

export function Overview() {
  const [filters] = useFilters();
  const query = useQuery({ queryKey: ['overview', filters], queryFn: () => api.overview(filters) });
  if (!query.data) return <QueryState query={query} what="the overview" />;

  const report = query.data.report;
  if (!report) return <Empty>No data yet: the first collection runs Tuesday or Friday at 07:00.</Empty>;
  const s = report.snapshot;
  const top = report.topSkills.slice(0, 15).map((d) => ({ skill: d.skill, share: Math.round(d.fullTextShare * 1000) / 10 }));

  return (
    <>
      <section className="section" aria-labelledby="h-snapshot">
        <h2 id="h-snapshot">Last {report.windowDays} days</h2>
        <div className="stats">
          <Stat label="Postings" value={s.inWindow} />
          <Stat label="Active now" value={s.activeNow} />
          <Stat label="New since last run" value={s.newSinceLastRun} />
          <Stat label="With salary" value={s.withSalary} />
          <Stat label="Full text" value={s.fullText} />
          <Stat label="Citizenship / clearance" value={s.citizenshipFlagged} />
        </div>
      </section>

      {s.inWindow === 0 ? (
        <Empty>No postings match these filters. Widen the window or add role families.</Empty>
      ) : (
        <>
          <section className="section" aria-labelledby="h-demand">
            <h2 id="h-demand">Most requested skills</h2>
            <p className="muted small">Share of full-text postings that mention each skill. The Skills page lists every skill.</p>
            {top.length === 0 ? <Empty>No full-text postings in this selection.</Empty> : (
              <>
              <div className="chart" style={{ height: 28 * top.length + 40 }} aria-hidden="true">
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={top} layout="vertical" margin={{ left: 8, right: 24, top: 4, bottom: 4 }}>
                    <CartesianGrid horizontal={false} stroke="var(--rule)" />
                    <XAxis type="number" unit="%" tick={{ fill: 'var(--muted)', fontSize: 12 }} stroke="var(--rule)" />
                    <YAxis type="category" dataKey="skill" width={150} interval={0} tick={{ fill: 'var(--ink)', fontSize: 13 }} stroke="var(--rule)" />
                    <Tooltip contentStyle={TOOLTIP_STYLE} />
                    <Bar dataKey="share" name="Share (%)" fill="var(--bar)" />
                  </BarChart>
                </ResponsiveContainer>
              </div>
              <table className="sr-only">
                <caption>Most requested skills, share of full-text postings</caption>
                <tbody>{top.map((d) => <tr key={d.skill}><th scope="row">{d.skill}</th><td>{d.share}%</td></tr>)}</tbody>
              </table>
              </>
            )}
          </section>

          <div className="grid-2">
            <section className="section" aria-labelledby="h-gaps">
              <h2 id="h-gaps">Biggest gaps</h2>
              <p className="muted small">Asked for often, and not yet a strength of yours.</p>
              <SkillRanking items={report.gaps} filters={filters} />
            </section>
            <section className="section" aria-labelledby="h-strengths">
              <h2 id="h-strengths">Strengths in demand</h2>
              <p className="muted small">Your skills that postings ask for most.</p>
              <SkillRanking items={report.strengths} filters={filters} />
            </section>
          </div>

          <section className="section" aria-labelledby="h-matches">
            <h2 id="h-matches">Best matches right now</h2>
            {report.matches.length === 0 ? <Empty>No active posting lists three or more skills in this selection.</Empty> : (
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr><th>Role</th><th>Company</th><th className="num">Match</th><th>Missing</th><th className="num">Salary</th></tr>
                  </thead>
                  <tbody>
                    {report.matches.map((m) => (
                      <tr key={`${m.url}|${m.title}`}>
                        <td>
                          <a href={m.url} target="_blank" rel="noreferrer">{m.title}</a>
                          {m.citizenshipOrClearance && <span className="flag"> · citizenship/clearance</span>}
                          <div className="muted small">{m.location}</div>
                        </td>
                        <td>{m.company}</td>
                        <td className="num">{pct(m.score)}</td>
                        <td className="small">{m.missing.join(', ') || '—'}</td>
                        <td className="num">{m.salary ? salaryText(m.salary.min, m.salary.max, m.salary.period) : '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          {report.trends && (
            <section className="section" aria-labelledby="h-trend">
              <h2 id="h-trend">Postings seen per week</h2>
              <div className="chart" aria-hidden="true">
                <ResponsiveContainer width="100%" height="100%">
                  <LineChart data={report.trends.weekly} margin={{ left: 8, right: 24, top: 8, bottom: 4 }}>
                    <CartesianGrid vertical={false} stroke="var(--rule)" />
                    <XAxis dataKey="weekStart" tick={{ fill: 'var(--muted)', fontSize: 12 }} stroke="var(--rule)" />
                    <YAxis allowDecimals={false} tick={{ fill: 'var(--muted)', fontSize: 12 }} stroke="var(--rule)" />
                    <Tooltip contentStyle={TOOLTIP_STYLE} />
                    <Line type="monotone" dataKey="active" name="Postings" stroke="var(--accent)" strokeWidth={2} dot={false} />
                  </LineChart>
                </ResponsiveContainer>
              </div>
              <table className="sr-only">
                <caption>Postings seen per week</caption>
                <tbody>{report.trends.weekly.map((w) => <tr key={w.weekStart}><th scope="row">Week of {w.weekStart}</th><td>{w.active}</td></tr>)}</tbody>
              </table>
            </section>
          )}

          <section className="section" aria-labelledby="h-companies">
            <h2 id="h-companies">Companies hiring</h2>
            <ul className="pills">
              {report.companies.map((c) => (
                <li key={c.company}>
                  <Link to={{ pathname: '/postings', search: linkSearch(filters, { company: c.company }) }}>
                    {c.company}<span className="count">{c.postings}</span>
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        </>
      )}

      {report.warnings.length > 0 && (
        <section className="section" aria-labelledby="h-warnings">
          <h2 id="h-warnings">Data warnings</h2>
          <ul className="small">{report.warnings.map((w) => <li key={w}>{w}</li>)}</ul>
        </section>
      )}
    </>
  );
}
```

- [ ] **Step 4: Run tests, type check and build**

Run: `cd src/Dashboard && npm test && npm run build`
Expected: all Vitest files PASS; `tsc --noEmit` has no errors; `vite build` writes `dist/` (ignored by git). Warnings about chunk size are fine.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add dashboard shell, filter bar, shared states and overview page"
```

---

### Task 9: Skill explorer (table and detail page)

**Files:**
- Create: `src/Dashboard/src/pages/Skills.tsx`, `src/Dashboard/src/pages/SkillDetail.tsx`
- Modify: `src/Dashboard/src/App.tsx` (routes)
- Test: `src/Dashboard/src/pages/Skills.test.tsx`, `src/Dashboard/src/pages/SkillDetail.test.tsx`

**Interfaces:**
- Consumes: `api.skills`, `api.skill`, `api.meta`, `linkSearch`, `useFilters`, `LevelChip`, `PostingList`, `QueryState`, `Empty` (Tasks 7–8)
- Produces: routes `/skills` (optional URL params `text` and `category`) and `/skills/:name`; `decodeSkill(raw)`

- [ ] **Step 1: Write the failing tests**

`src/Dashboard/src/pages/Skills.test.tsx`:
```tsx
import { fireEvent, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SkillList } from '../api';
import { META, mockApi, renderRoute } from '../test/render';
import { Skills } from './Skills';

const LIST: SkillList = {
  postings: 40,
  skills: [
    { skill: 'Kubernetes', category: 'devops', share: 0.3, count: 12, level: 'None', previousShare: 0.2, change: 0.1 },
    { skill: 'C#', category: 'language', share: 0.5, count: 20, level: 'Strong', previousShare: null, change: null },
    { skill: 'CI/CD', category: 'devops', share: 0.1, count: 4, level: 'Working', previousShare: 0.15, change: -0.05 },
    { skill: 'Cobol', category: 'language', share: 0, count: 0, level: 'None', previousShare: null, change: null },
  ],
};

const skillColumn = () => screen.getAllByRole('row').slice(1).map((r) => within(r).getAllByRole('cell')[0].textContent);

describe('Skills page', () => {
  it('lists skills by share, leaves out unmentioned skills you lack, and links each to its page', async () => {
    mockApi({ '/api/meta': META, '/api/skills': LIST });
    renderRoute('/skills', <Skills />);

    expect(await screen.findByRole('link', { name: 'C#' })).toHaveAttribute('href', '/skills/C%23');
    expect(skillColumn()).toEqual(['C#', 'Kubernetes', 'CI/CD']);
    expect(screen.getByRole('link', { name: 'CI/CD' })).toHaveAttribute('href', '/skills/CI%2FCD');
  });

  it('sorts by the clicked column', async () => {
    mockApi({ '/api/meta': META, '/api/skills': LIST });
    renderRoute('/skills', <Skills />);
    await screen.findByRole('link', { name: 'C#' });

    fireEvent.click(screen.getByRole('button', { name: /Skill/ }));
    expect(skillColumn()).toEqual(['C#', 'CI/CD', 'Kubernetes']);

    fireEvent.click(screen.getByRole('button', { name: /Change/ }));
    expect(skillColumn()).toEqual(['Kubernetes', 'CI/CD', 'C#']);
  });

  it('asks the API for the chosen text kind and category', async () => {
    const calls = mockApi({ '/api/meta': META, '/api/skills': LIST });
    renderRoute('/skills?text=snippet&category=devops&where=gta', <Skills />);
    await screen.findByRole('link', { name: 'C#' });
    expect(calls).toContain('/api/skills?where=gta&text=snippet&category=devops');
  });

  it('shows an empty state when no postings match', async () => {
    mockApi({ '/api/meta': META, '/api/skills': { postings: 0, skills: LIST.skills.map((s) => ({ ...s, share: 0, count: 0 })) } });
    renderRoute('/skills', <Skills />);
    expect(await screen.findByText(/No postings match these filters/)).toBeInTheDocument();
  });
});
```

`src/Dashboard/src/pages/SkillDetail.test.tsx`:
```tsx
import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SkillDetail as Detail } from '../api';
import { failure, mockApi, renderRoute } from '../test/render';
import { SkillDetail } from './SkillDetail';

const DETAIL: Detail = {
  skill: 'CI/CD', category: 'devops', level: 'Working', share: 0.25, count: 10, postings: 40,
  salary: { withMedian: 150000, withCount: 6, withoutMedian: null, withoutCount: 3 },
  coSkills: [{ name: 'C#', count: 4 }], companies: [{ name: 'Acme', count: 3 }], examples: [],
};

describe('Skill detail page', () => {
  it.each([
    ['/skills/CI%2FCD', '/api/skills/CI%2FCD?text=full'],
    ['/skills/C%23', '/api/skills/C%23?text=full'],
    ['/skills/Estimation%20%26%20Planning', '/api/skills/Estimation%20%26%20Planning?text=full'],
  ])('reads %s from the route and asks the API for that skill', async (url, apiUrl) => {
    const calls = mockApi({ '/api/skills/': DETAIL });
    renderRoute(url, <SkillDetail />, '/skills/:name');
    await screen.findByRole('heading', { name: /CI\/CD/ });
    expect(calls).toContain(apiUrl);
  });

  it('compares salaries with and without the skill and links companies to their postings', async () => {
    mockApi({ '/api/skills/': DETAIL });
    renderRoute('/skills/CI%2FCD?where=gta', <SkillDetail />, '/skills/:name');

    expect(await screen.findByText('$150k')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Acme/ })).toHaveAttribute('href', '/postings?where=gta&skill=CI%2FCD&company=Acme');
    expect(screen.getByRole('link', { name: /C#/ })).toHaveAttribute('href', '/skills/C%23?where=gta');
  });

  it("shows the API's message for an unknown skill", async () => {
    mockApi({ '/api/skills/': failure(404, "'Cobol' is not in config/skills.json.") });
    renderRoute('/skills/Cobol', <SkillDetail />, '/skills/:name');
    expect(await screen.findByRole('alert')).toHaveTextContent("'Cobol' is not in config/skills.json.");
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd src/Dashboard && npm test -- src/pages`
Expected: FAIL: `./Skills` and `./SkillDetail` don't exist.

- [ ] **Step 3: Implement**

`src/Dashboard/src/pages/Skills.tsx`:
```tsx
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api, type SkillRow, type TextKind } from '../api';
import { LevelChip } from '../components/LevelChip';
import { Empty, QueryState } from '../components/States';
import { linkSearch, useFilters } from '../filters';
import { pct, signedPts } from '../format';

type SortKey = 'skill' | 'category' | 'share' | 'level' | 'change';

const TEXT_LABELS: Record<TextKind, string> = { full: 'Full-text postings', snippet: 'Adzuna snippets', all: 'All postings' };
const LEVEL_ORDER = { None: 0, Learning: 1, Working: 2, Strong: 3 } as const;

function sortValue(row: SkillRow, key: SortKey): string | number {
  switch (key) {
    case 'skill': return row.skill.toLowerCase();
    case 'category': return row.category;
    case 'share': return row.share;
    case 'level': return LEVEL_ORDER[row.level];
    case 'change': return row.change ?? Number.NEGATIVE_INFINITY;
  }
}

function sortSkills(rows: SkillRow[], key: SortKey, descending: boolean): SkillRow[] {
  return [...rows].sort((a, b) => {
    const [x, y] = [sortValue(a, key), sortValue(b, key)];
    const order = x < y ? -1 : x > y ? 1 : 0;
    return (descending ? -order : order) || a.skill.localeCompare(b.skill);
  });
}

export function Skills() {
  const [filters] = useFilters();
  const [params, setParams] = useSearchParams();
  const text = (Object.keys(TEXT_LABELS) as TextKind[]).find((t) => t === params.get('text')) ?? 'full';
  const category = params.get('category') ?? '';
  const [sort, setSort] = useState<{ key: SortKey; descending: boolean }>({ key: 'share', descending: true });
  const meta = useQuery({ queryKey: ['meta'], queryFn: api.meta });
  const query = useQuery({
    queryKey: ['skills', filters, text, category],
    queryFn: () => api.skills(filters, text, category || undefined),
  });

  const setParam = (key: string, value: string, fallback: string) =>
    setParams((current) => {
      const out = new URLSearchParams(current);
      if (value === fallback) out.delete(key);
      else out.set(key, value);
      return out;
    });

  const header = (key: SortKey, title: string, numeric = false) => (
    <th className={numeric ? 'num' : undefined} aria-sort={sort.key === key ? (sort.descending ? 'descending' : 'ascending') : 'none'}>
      <button
        type="button"
        className="sort"
        onClick={() => setSort((s) => ({ key, descending: s.key === key ? !s.descending : key !== 'skill' && key !== 'category' }))}
      >
        {title}{sort.key === key ? (sort.descending ? ' ↓' : ' ↑') : ''}
      </button>
    </th>
  );

  // Skills nobody mentions stay listed only when you have them, so your own profile is always visible.
  const rows = query.data
    ? sortSkills(query.data.skills.filter((s) => s.count > 0 || s.level !== 'None'), sort.key, sort.descending)
    : [];
  const search = linkSearch(filters);

  return (
    <section className="section" aria-labelledby="h-skills">
      <h2 id="h-skills">Skill demand</h2>
      <div className="toolbar">
        <div className="field">
          <label className="field-label" htmlFor="skills-text">Counted in</label>
          <select id="skills-text" value={text} onChange={(e) => setParam('text', e.target.value, 'full')}>
            {(Object.keys(TEXT_LABELS) as TextKind[]).map((t) => <option key={t} value={t}>{TEXT_LABELS[t]}</option>)}
          </select>
        </div>
        <div className="field">
          <label className="field-label" htmlFor="skills-category">Category</label>
          <select id="skills-category" value={category} onChange={(e) => setParam('category', e.target.value, '')}>
            <option value="">All categories</option>
            {meta.data?.categories.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>
      </div>

      {!query.data ? (
        <QueryState query={query} what="skills" />
      ) : query.data.postings === 0 ? (
        <Empty>No postings match these filters. Widen the window, add role families or count snippets too.</Empty>
      ) : (
        <>
          <p className="muted small">
            {query.data.postings.toLocaleString('en-CA')} postings counted. Change compares with the previous window of the same length.
          </p>
          <div className="table-wrap">
            <table>
              <caption className="sr-only">Skills by share of postings</caption>
              <thead>
                <tr>
                  {header('skill', 'Skill')}
                  {header('category', 'Category')}
                  {header('share', 'Share', true)}
                  <th aria-hidden="true" />
                  <th className="num">Postings</th>
                  {header('level', 'You')}
                  {header('change', 'Change', true)}
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.skill}>
                    <td><Link to={{ pathname: `/skills/${encodeURIComponent(r.skill)}`, search }}>{r.skill}</Link></td>
                    <td className="muted">{r.category}</td>
                    <td className="num">{pct(r.share)}</td>
                    <td><div className="bar"><span style={{ width: `${Math.round(r.share * 100)}%` }} /></div></td>
                    <td className="num">{r.count}</td>
                    <td><LevelChip level={r.level} /></td>
                    <td className={`num ${r.change === null ? 'muted' : r.change > 0 ? 'change-up' : r.change < 0 ? 'change-down' : ''}`}>
                      {signedPts(r.change)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </section>
  );
}
```

`src/Dashboard/src/pages/SkillDetail.tsx`:
```tsx
import { useQuery } from '@tanstack/react-query';
import { Link, useParams, useSearchParams } from 'react-router';
import { api, type TextKind } from '../api';
import { LevelChip } from '../components/LevelChip';
import { PostingList } from '../components/PostingList';
import { Empty, QueryState } from '../components/States';
import { linkSearch, useFilters } from '../filters';
import { money, pct } from '../format';

const TEXT_KINDS: TextKind[] = ['full', 'snippet', 'all'];

// React Router decodes the path segment but keeps %2F encoded so "CI/CD" stays one segment; decode once more.
export function decodeSkill(raw: string): string {
  try {
    return decodeURIComponent(raw);
  } catch {
    return raw;
  }
}

export function SkillDetail() {
  const [filters] = useFilters();
  const [params] = useSearchParams();
  const name = decodeSkill(useParams().name ?? '');
  const text = TEXT_KINDS.find((t) => t === params.get('text')) ?? 'full';
  const query = useQuery({ queryKey: ['skill', name, filters, text], queryFn: () => api.skill(name, filters, text) });

  if (!query.data) return <QueryState query={query} what={name} />;
  const d = query.data;
  const search = linkSearch(filters);

  return (
    <>
      <section className="section" aria-labelledby="h-skill">
        <p className="eyebrow"><Link to={{ pathname: '/skills', search }}>Skills</Link> / {d.category}</p>
        <h2 id="h-skill">{d.skill} <LevelChip level={d.level} /></h2>
        <div className="stats">
          <div className="stat"><span className="eyebrow">Share of postings</span><span className="stat-value">{pct(d.share)}</span></div>
          <div className="stat"><span className="eyebrow">Postings</span><span className="stat-value">{d.count} of {d.postings}</span></div>
          <div className="stat">
            <span className="eyebrow">Median salary with it</span>
            <span className="stat-value">{money(d.salary.withMedian)}</span>
            <span className="muted small">{d.salary.withCount} postings with a salary</span>
          </div>
          <div className="stat">
            <span className="eyebrow">Median salary without it</span>
            <span className="stat-value">{money(d.salary.withoutMedian)}</span>
            <span className="muted small">{d.salary.withoutCount} postings with a salary</span>
          </div>
        </div>
        <p className="muted small">A median needs at least 5 salaries; "—" means there aren't enough yet.</p>
      </section>

      <div className="grid-2">
        <section className="section" aria-labelledby="h-co">
          <h2 id="h-co">Often asked for together</h2>
          {d.coSkills.length === 0 ? <Empty>No other skills appear with it in this selection.</Empty> : (
            <ul className="pills">
              {d.coSkills.map((c) => (
                <li key={c.name}>
                  <Link to={{ pathname: `/skills/${encodeURIComponent(c.name)}`, search }}>{c.name}<span className="count">{c.count}</span></Link>
                </li>
              ))}
            </ul>
          )}
        </section>
        <section className="section" aria-labelledby="h-companies">
          <h2 id="h-companies">Companies asking for it</h2>
          {d.companies.length === 0 ? <Empty>No company in this selection mentions it.</Empty> : (
            <ul className="pills">
              {d.companies.map((c) => (
                <li key={c.name}>
                  <Link to={{ pathname: '/postings', search: linkSearch(filters, { skill: d.skill, company: c.name }) }}>
                    {c.name}<span className="count">{c.count}</span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>

      <section className="section" aria-labelledby="h-examples">
        <h2 id="h-examples">Postings that mention it</h2>
        <PostingList rows={d.examples} caption={`Postings that mention ${d.skill}`} />
        {d.count > d.examples.length && (
          <p className="small">
            <Link to={{ pathname: '/postings', search: linkSearch(filters, { skill: d.skill }) }}>Browse all postings with {d.skill}</Link>
          </p>
        )}
      </section>
    </>
  );
}
```

In `src/Dashboard/src/App.tsx`, add the imports:
```tsx
import { SkillDetail } from './pages/SkillDetail';
import { Skills } from './pages/Skills';
```
and add the routes after the `/` route:
```tsx
            <Route path="/skills" element={<Skills />} />
            <Route path="/skills/:name" element={<SkillDetail />} />
```

- [ ] **Step 4: Run tests, type check and build**

Run: `cd src/Dashboard && npm test && npm run build`
Expected: all PASS; no type errors.
- If a link's `href` comes out double-encoded (`%2523`) or decoded (`/skills/C#`), React Router's encoding isn't what this plan assumes. Fix it in the page code, not the test, because the API test in Task 6 pins what the server needs. Ledger a ruling.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add the skill explorer: sortable demand table and per-skill detail"
```

---

### Task 10: Salary explorer and postings browser

**Files:**
- Create: `src/Dashboard/src/pages/Salaries.tsx`, `src/Dashboard/src/pages/Postings.tsx`
- Modify: `src/Dashboard/src/App.tsx` (routes)
- Test: `src/Dashboard/src/pages/Salaries.test.tsx`, `src/Dashboard/src/pages/Postings.test.tsx`

**Interfaces:**
- Consumes: `api.salaries`, `api.postings`, `api.skills`, `api.meta`, `useFilters`, `PostingList`, the states (Tasks 7–8)
- Produces: routes `/salaries` (URL params `skill`, `company`) and `/postings` (URL params `q`, `skill`, `company`, `sort`, `page`)

- [ ] **Step 1: Write the failing tests**

`src/Dashboard/src/pages/Salaries.test.tsx`:
```tsx
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SalaryView } from '../api';
import { META, mockApi, renderRoute } from '../test/render';
import { Salaries } from './Salaries';

const VIEW: SalaryView = {
  postings: 12,
  withSalary: 7,
  cells: [
    { family: 'Dotnet', seniority: 'Senior', count: 6, median: 150000, p25: 130000, p75: 170000 },
    { family: 'LeadManager', seniority: 'Manager', count: 1, median: null, p25: null, p75: null },
  ],
  histogram: [{ from: 120000, to: 140000, count: 3 }, { from: 140000, to: 160000, count: 4 }],
  examples: [],
};
const NO_SKILLS = { postings: 0, skills: [] };

describe('Salaries page', () => {
  it('shows percentiles where there are enough salaries and marks thin cells', async () => {
    mockApi({ '/api/meta': META, '/api/skills': NO_SKILLS, '/api/salaries': VIEW });
    renderRoute('/salaries', <Salaries />);

    expect(await screen.findByText('$150k')).toBeInTheDocument();
    expect(screen.getByText('n < 5')).toBeInTheDocument();
    expect(screen.getByText(/7 of 12 postings/)).toBeInTheDocument();
  });

  it('sends the chosen company to the API', async () => {
    const calls = mockApi({ '/api/meta': META, '/api/skills': NO_SKILLS, '/api/salaries': VIEW });
    renderRoute('/salaries', <Salaries />);
    await screen.findByRole('option', { name: 'Acme' });

    fireEvent.change(screen.getByLabelText('Company'), { target: { value: 'Acme' } });

    await waitFor(() => expect(calls).toContain('/api/salaries?company=Acme'));
  });

  it('says so when no posting in the selection has a salary', async () => {
    mockApi({ '/api/meta': META, '/api/skills': NO_SKILLS, '/api/salaries': { ...VIEW, withSalary: 0, cells: [], histogram: [] } });
    renderRoute('/salaries', <Salaries />);
    expect(await screen.findByText(/No salaried postings match these filters/)).toBeInTheDocument();
  });
});
```

`src/Dashboard/src/pages/Postings.test.tsx`:
```tsx
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { PostingPage } from '../api';
import { META, mockApi, renderRoute } from '../test/render';
import { Postings } from './Postings';

const page = (n: number, total = 60): PostingPage => ({
  total, page: n, pageSize: 25,
  items: [{
    id: `p${n}`, title: `Developer ${n}`, company: 'Acme', location: 'Toronto', url: 'https://x.test', salaryMin: null, salaryMax: null,
    salaryPeriod: null, match: 0.5, firstSeen: '2026-10-01T11:00:00Z', skills: ['C#'], citizenshipOrClearance: false, source: 'greenhouse', isFullText: true,
  }],
});

describe('Postings page', () => {
  it('searches titles and companies and starts again from page 1', async () => {
    const calls = mockApi({ '/api/meta': META, '/api/postings': page(3) });
    renderRoute('/postings?page=3', <Postings />);
    await screen.findByText('Page 3 of 3');

    fireEvent.change(screen.getByLabelText('Title or company'), { target: { value: 'bank' } });
    fireEvent.click(screen.getByRole('button', { name: 'Search' }));

    await waitFor(() => expect(calls).toContain('/api/postings?q=bank&sort=newest&page=1&pageSize=25'));
    expect(screen.getByTestId('location').textContent).toBe('/postings?q=bank');
  });

  it('pages through the results', async () => {
    const calls = mockApi({ '/api/meta': META, '/api/postings': page(1) });
    renderRoute('/postings', <Postings />);
    await screen.findByText('Page 1 of 3');

    fireEvent.click(screen.getByRole('button', { name: 'Next' }));

    await waitFor(() => expect(calls).toContain('/api/postings?sort=newest&page=2&pageSize=25'));
    expect(screen.getByText('Page 2 of 3')).toBeInTheDocument();
  });

  it('shows and clears a skill filter that came from another page', async () => {
    const calls = mockApi({ '/api/meta': META, '/api/postings': page(1, 1) });
    renderRoute('/postings?skill=CI%2FCD', <Postings />);

    const chip = await screen.findByRole('button', { name: 'Remove skill filter CI/CD' });
    expect(calls).toContain('/api/postings?skill=CI%2FCD&sort=newest&page=1&pageSize=25');
    fireEvent.click(chip);
    expect(screen.getByTestId('location').textContent).toBe('/postings');
  });

  it('shows the empty state', async () => {
    mockApi({ '/api/meta': META, '/api/postings': { total: 0, page: 1, pageSize: 25, items: [] } });
    renderRoute('/postings?where=remote&seniority=Junior', <Postings />);
    expect(await screen.findByText(/No postings match these filters/)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd src/Dashboard && npm test -- src/pages`
Expected: FAIL: `./Salaries` and `./Postings` don't exist.

- [ ] **Step 3: Implement**

`src/Dashboard/src/pages/Salaries.tsx`:
```tsx
import { useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router';
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { api, type SalaryCell } from '../api';
import { PostingList } from '../components/PostingList';
import { Empty, QueryState } from '../components/States';
import { useFilters } from '../filters';
import { FAMILY_LABELS, SENIORITY_LABELS, label, money } from '../format';

const TOOLTIP_STYLE = { background: 'var(--surface)', border: '1px solid var(--rule)', color: 'var(--ink)' };

// 25th–75th percentile band with a median tick, on one scale shared by every row.
function RangeBar({ cell, lo, hi }: { cell: SalaryCell; lo: number; hi: number }) {
  if (cell.p25 === null || cell.p75 === null || cell.median === null) return null;
  const span = hi - lo || 1;
  const at = (value: number) => `${((value - lo) / span) * 100}%`;
  return (
    <div className="range" aria-hidden="true">
      <span className="iqr" style={{ left: at(cell.p25), width: `${((cell.p75 - cell.p25) / span) * 100}%` }} />
      <span className="median" style={{ left: at(cell.median) }} />
    </div>
  );
}

export function Salaries() {
  const [filters] = useFilters();
  const [params, setParams] = useSearchParams();
  const skill = params.get('skill') ?? '';
  const company = params.get('company') ?? '';
  const meta = useQuery({ queryKey: ['meta'], queryFn: api.meta });
  const skills = useQuery({ queryKey: ['skills', filters, 'all', ''], queryFn: () => api.skills(filters, 'all') });
  const query = useQuery({
    queryKey: ['salaries', filters, skill, company],
    queryFn: () => api.salaries(filters, skill || undefined, company || undefined),
  });

  const setParam = (key: string, value: string) =>
    setParams((current) => {
      const out = new URLSearchParams(current);
      if (value) out.set(key, value);
      else out.delete(key);
      return out;
    });

  const skillOptions = (skills.data?.skills ?? [])
    .filter((s) => s.count > 0 || s.skill === skill)
    .map((s) => s.skill)
    .sort((a, b) => a.localeCompare(b));
  const companyOptions = [...new Set([...(meta.data?.companies ?? []), ...(company ? [company] : [])])];

  const view = query.data;
  const measured = view?.cells.filter((c) => c.p25 !== null && c.p75 !== null) ?? [];
  const lo = measured.length > 0 ? Math.min(...measured.map((c) => c.p25 as number)) : 0;
  const hi = measured.length > 0 ? Math.max(...measured.map((c) => c.p75 as number)) : 0;
  const bands = view?.histogram.map((b) => ({ band: `${money(b.from)}–${money(b.to)}`, count: b.count })) ?? [];

  return (
    <section className="section" aria-labelledby="h-salaries">
      <h2 id="h-salaries">Salaries</h2>
      <div className="toolbar">
        <div className="field">
          <label className="field-label" htmlFor="salaries-skill">Skill</label>
          <select id="salaries-skill" value={skill} onChange={(e) => setParam('skill', e.target.value)}>
            <option value="">Any skill</option>
            {(skillOptions.includes(skill) || !skill ? skillOptions : [skill, ...skillOptions]).map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>
        <div className="field">
          <label className="field-label" htmlFor="salaries-company">Company</label>
          <select id="salaries-company" value={company} onChange={(e) => setParam('company', e.target.value)}>
            <option value="">Any company</option>
            {companyOptions.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>
      </div>

      {!view ? (
        <QueryState query={query} what="salaries" />
      ) : view.withSalary === 0 ? (
        <Empty>No salaried postings match these filters. Ontario postings must show pay from 2026, so this fills in as new postings arrive.</Empty>
      ) : (
        <>
          <p className="muted small">
            {view.withSalary} of {view.postings} postings state a salary. Amounts are yearly midpoints in CAD; hourly rates count 2,080 hours a year.
          </p>
          <div className="chart" aria-hidden="true">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={bands} margin={{ left: 8, right: 16, top: 8, bottom: 4 }}>
                <CartesianGrid vertical={false} stroke="var(--rule)" />
                <XAxis dataKey="band" tick={{ fill: 'var(--muted)', fontSize: 12 }} stroke="var(--rule)" />
                <YAxis allowDecimals={false} tick={{ fill: 'var(--muted)', fontSize: 12 }} stroke="var(--rule)" />
                <Tooltip contentStyle={TOOLTIP_STYLE} />
                <Bar dataKey="count" name="Postings" fill="var(--bar)" />
              </BarChart>
            </ResponsiveContainer>
          </div>
          <table className="sr-only">
            <caption>Postings by yearly salary band</caption>
            <tbody>{bands.map((b) => <tr key={b.band}><th scope="row">{b.band}</th><td>{b.count}</td></tr>)}</tbody>
          </table>

          <div className="table-wrap">
            <table>
              <caption className="sr-only">Salary percentiles by role and seniority</caption>
              <thead>
                <tr>
                  <th>Role</th>
                  <th>Seniority</th>
                  <th className="num">Salaries</th>
                  <th className="num">25th</th>
                  <th className="num">Median</th>
                  <th className="num">75th</th>
                  <th aria-hidden="true" />
                </tr>
              </thead>
              <tbody>
                {view.cells.map((c) => (
                  <tr key={`${c.family}-${c.seniority}`}>
                    <td>{label(FAMILY_LABELS, c.family)}</td>
                    <td>{label(SENIORITY_LABELS, c.seniority)}</td>
                    <td className="num">{c.count}</td>
                    <td className="num">{money(c.p25)}</td>
                    <td className="num">{c.median === null ? <span className="muted">n &lt; 5</span> : money(c.median)}</td>
                    <td className="num">{money(c.p75)}</td>
                    <td><RangeBar cell={c} lo={lo} hi={hi} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <h3>Highest-paying postings</h3>
          <PostingList rows={view.examples} caption="Salaried postings, highest first" />
        </>
      )}
    </section>
  );
}
```

`src/Dashboard/src/pages/Postings.tsx`:
```tsx
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useSearchParams } from 'react-router';
import { api, type PostingSort } from '../api';
import { PostingList } from '../components/PostingList';
import { ErrorBanner, Loading } from '../components/States';
import { useFilters } from '../filters';

const PAGE_SIZE = 25;
const SORTS: Record<PostingSort, string> = { newest: 'Newest first', match: 'Best match first', salary: 'Highest salary first' };

export function Postings() {
  const [filters] = useFilters();
  const [params, setParams] = useSearchParams();
  const q = params.get('q') ?? '';
  const skill = params.get('skill') ?? '';
  const company = params.get('company') ?? '';
  const sort = (Object.keys(SORTS) as PostingSort[]).find((s) => s === params.get('sort')) ?? 'newest';
  const page = Math.max(1, Number.parseInt(params.get('page') ?? '1', 10) || 1);
  const [draft, setDraft] = useState(q);
  const meta = useQuery({ queryKey: ['meta'], queryFn: api.meta });
  const query = useQuery({
    queryKey: ['postings', filters, q, skill, company, sort, page],
    queryFn: () => api.postings(filters, {
      q: q || undefined, skill: skill || undefined, company: company || undefined, sort, page, pageSize: PAGE_SIZE,
    }),
    placeholderData: keepPreviousData,
  });

  // Any change except the page itself starts again from page 1.
  const set = (patch: Record<string, string>) =>
    setParams((current) => {
      const out = new URLSearchParams(current);
      Object.entries(patch).forEach(([key, value]) => (value ? out.set(key, value) : out.delete(key)));
      if (!('page' in patch)) out.delete('page');
      return out;
    });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    set({ q: draft.trim() });
  };

  const data = query.data;
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  const companies = [...new Set([...(meta.data?.companies ?? []), ...(company ? [company] : [])])];

  return (
    <section className="section" aria-labelledby="h-postings">
      <h2 id="h-postings">Postings</h2>
      <form className="toolbar" role="search" onSubmit={submit}>
        <div className="field">
          <label className="field-label" htmlFor="postings-q">Title or company</label>
          <input id="postings-q" type="search" value={draft} placeholder="e.g. bank, platform" onChange={(e) => setDraft(e.target.value)} />
        </div>
        <button type="submit">Search</button>
        <div className="field">
          <label className="field-label" htmlFor="postings-company">Company</label>
          <select id="postings-company" value={company} onChange={(e) => set({ company: e.target.value })}>
            <option value="">All companies</option>
            {companies.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>
        <div className="field">
          <label className="field-label" htmlFor="postings-sort">Sort</label>
          <select id="postings-sort" value={sort} onChange={(e) => set({ sort: e.target.value === 'newest' ? '' : e.target.value })}>
            {(Object.keys(SORTS) as PostingSort[]).map((s) => <option key={s} value={s}>{SORTS[s]}</option>)}
          </select>
        </div>
      </form>

      {skill && (
        <ul className="pills" aria-label="Active filters">
          <li>
            <button type="button" aria-label={`Remove skill filter ${skill}`} onClick={() => set({ skill: '' })}>Skill: {skill} ×</button>
          </li>
        </ul>
      )}

      {query.error && <ErrorBanner error={query.error} />}
      {!data ? (
        !query.error && <Loading what="postings" />
      ) : (
        <>
          <p className="muted small">{data.total.toLocaleString('en-CA')} postings</p>
          <PostingList rows={data.items} caption="Postings" />
          {data.total > data.pageSize && (
            <nav className="pager" aria-label="Result pages">
              <button type="button" disabled={page <= 1} onClick={() => set({ page: String(page - 1) })}>Previous</button>
              <span className="num">Page {page} of {pages}</span>
              <button type="button" disabled={page >= pages} onClick={() => set({ page: String(page + 1) })}>Next</button>
            </nav>
          )}
        </>
      )}
    </section>
  );
}
```

In `src/Dashboard/src/App.tsx`, add the imports:
```tsx
import { Postings } from './pages/Postings';
import { Salaries } from './pages/Salaries';
```
and the routes after the skill routes:
```tsx
            <Route path="/salaries" element={<Salaries />} />
            <Route path="/postings" element={<Postings />} />
```

- [ ] **Step 4: Run tests, type check and build**

Run: `cd src/Dashboard && npm test && npm run build`
Expected: all Vitest files PASS (11 files); no type errors; the build succeeds.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add the salary explorer and the postings browser"
```

---

### Task 11: Aspire AppHost, launcher script and the live check

**Files:**
- Create (template): `src/AppHost/*`
- Modify: `src/AppHost/AppHost.cs`, `src/AppHost/Properties/launchSettings.json`
- Create: `scripts/dashboard.cmd`

**Interfaces:**
- Consumes: the `Api` project (Task 6), `src/Dashboard` (Tasks 7–10), and the AppHost user-secret `ConnectionStrings:tracker`
- Produces:
  - Aspire resources `tracker` (connection string), `api` (project, health check on `/alive`) and `dashboard` (Vite on port 5190)
  - `scripts\dashboard.cmd`

- [ ] **Step 1: Scaffold**

```bash
dotnet new aspire-apphost -n AppHost -o src/AppHost
dotnet sln add src/AppHost
dotnet add src/AppHost reference src/Api
dotnet add src/AppHost package Aspire.Hosting.JavaScript --version 13.5.4
```
Expected: `src/AppHost/AppHost.csproj` uses `Aspire.AppHost.Sdk/13.5.4` and has its own `UserSecretsId`; `AppHost.cs`, `aspire.config.json` and `Properties/launchSettings.json` exist.

- [ ] **Step 2: AppHost**

`src/AppHost/AppHost.cs` (replace the whole file):
```csharp
var builder = DistributedApplication.CreateBuilder(args);

// The Compose Postgres that the scheduled Processor rebuilds (port 5434). The connection string lives in this project's
// user-secrets as ConnectionStrings:tracker; Aspire doesn't start or own the database.
var tracker = builder.AddConnectionString("tracker");

// Liveness only (/alive): with the database down the API still runs and answers 503 or stale data, so the dashboard starts anyway.
var api = builder.AddProject<Projects.Api>("api")
    .WithReference(tracker)
    .WithHttpHealthCheck("/alive");

// Vite dev server on a fixed port so the page can be bookmarked; it proxies /api to the API address Aspire passes in.
builder.AddViteApp("dashboard", "../Dashboard")
    .WithReference(api)
    .WaitFor(api)
    .WithEndpoint("http", endpoint => endpoint.Port = 5190, createIfNotExists: false);

builder.Build().Run();
```

In `src/AppHost/Properties/launchSettings.json`, add this line to the `http` profile's `environmentVariables`. It lets the AppHost run over plain HTTP on this PC without trusting a dev certificate.
```json
        "ASPIRE_ALLOW_UNSECURED_TRANSPORT": "true",
```

`scripts/dashboard.cmd` (save with CRLF line endings, like `run-pipeline.cmd`):
```bat
@echo off
rem Starts the local dashboard: Postgres (Compose), then the Aspire AppHost with the API and the Vite app.
rem Dashboard: http://localhost:5190 - the Aspire dashboard login link is printed below. Ctrl+C stops everything.
setlocal
cd /d "%~dp0.."
docker compose up -d --wait postgres
if errorlevel 1 (
  echo Postgres did not start. Is Docker Desktop running?
  exit /b 1
)
dotnet run --project src\AppHost --launch-profile http
```

Run: `dotnet build src/AppHost -warnaserror`
Expected: build succeeds with 0 warnings.

- [ ] **Step 3: Give the AppHost the database connection string without printing it**

The Processor's user-secrets already hold the connection string (`ConnectionStrings:Tracker`). Write `$SCRATCH/copy_db_secret.py` with the Write tool and run it from the repo root. It must never print the value.
```python
import json, subprocess

listed = subprocess.run(["dotnet", "user-secrets", "list", "--json", "--project", "src/Processor"],
                        capture_output=True, text=True, check=True).stdout
body = listed[listed.index("//BEGIN") + len("//BEGIN"):listed.index("//END")]
value = json.loads(body)["ConnectionStrings:Tracker"]
subprocess.run(["dotnet", "user-secrets", "set", "ConnectionStrings:tracker", value, "--project", "src/AppHost"],
               capture_output=True, text=True, check=True)
print("Copied the Processor's connection string to the AppHost user-secrets (value not shown).")
```
Expected: the one "Copied…" line. If the Processor has no such secret, stop and ask the owner to run `! dotnet user-secrets set "ConnectionStrings:tracker" "<connection string>" --project src/AppHost` themselves.

- [ ] **Step 4: Live check against the real database**

1. `docker compose up -d --wait postgres`
2. Start `dotnet run --project src/AppHost --launch-profile http` in the background, writing to `<workspace>/apphost.log`. Wait with a Monitor until-loop, for up to 5 minutes, until `curl -sf http://localhost:5190/api/meta` succeeds. The first start runs `npm install`.
   - If port 5190 never answers but `apphost.log` shows the dashboard on another port, the Vite endpoint isn't named `http`. Find its name in the log, fix the `WithEndpoint` call and ledger a ruling.
3. Check each page's API through the Vite proxy:
   ```bash
   curl -s http://localhost:5190/ | grep -c 'id="root"'                                           # 1
   curl -s http://localhost:5190/api/meta                                                         # runs ≥ 3, stale false
   curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:5190/api/skills/C%23"                # 200
   curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:5190/api/skills/CI%2FCD"             # 200
   curl -s "http://localhost:5190/api/postings?where=remote&seniority=Junior&window=30"           # 200, a (probably zero) total
   curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:5190/api/postings?window=45"        # 400
   for u in meta overview skills salaries "postings?pageSize=100" "skills/C%23"; do
     curl -s -o /dev/null -w "$u %{size_download}\n" "http://localhost:5190/api/$u"   # each under 500000 bytes (spec)
   done
   ```
4. Check that the dashboard agrees with the report.
   - Run `dotnet run --project src/Processor -c Release -- --report-only` and note `N postings in the window, top gap: X` from its log line.
   - Then `curl -s http://localhost:5190/api/overview` must show `report.snapshot.inWindow == N` and `report.gaps[0].skill == X`.
   - Any difference is a bug. Debug it with superpowers:systematic-debugging.
5. Check stale data (Review Focus 2).
   - Run `docker compose stop postgres`.
   - Wait with a Monitor until-loop, for up to 90 s, until `curl -si http://localhost:5190/api/meta` shows `X-Data-Stale: true` together with HTTP 200.
   - Run `docker compose start postgres`, then wait up to 90 s until the header is gone.
6. Stop the AppHost's background task. `curl http://localhost:5190` must then fail.
7. Ledger this line: `Task 11: live check passed (inWindow N = report N, top gap X)`. The Aspire dashboard's trace view can't be checked without a browser, so list "open the Aspire dashboard and look at a GET /api/overview trace with its Npgsql and LoadMarketData spans" as an owner check in the final message.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add the Aspire AppHost and dashboard launcher"
```

---

### Task 12: Decision notes, README, and the refactored scheduled task

**Files:**
- Create: `docs/decisions/0007-aspire-local-orchestration.md`, `docs/decisions/0008-shared-core-in-memory-api.md`, `docs/decisions/0009-react-typescript-vite.md`
- Modify: `README.md`

- [ ] **Step 1: Decision notes**

`docs/decisions/0007-aspire-local-orchestration.md`:
```markdown
# 0007 — .NET Aspire to run the dashboard locally

**Status:** Accepted (2026-09-28)

## Context
Phase 2a adds two processes next to the database: an ASP.NET Core API and a Vite dev server. They need each other's addresses and a connection string, and it should be possible to see what happens inside them: requests, SQL and errors. Observability is also a skill the market data asks for.

## Decision
A .NET Aspire 13 AppHost starts the API and the Vite app and runs the Aspire dashboard.
- It passes the API's address to Vite and the connection string to the API.
- The API uses the service-defaults project for OpenTelemetry traces, metrics and logs, and for health checks.
- The AppHost points at the existing Compose Postgres with `AddConnectionString` instead of starting its own container. The scheduled Processor rebuilds that database twice a week, and that has to keep working when the dashboard isn't running.

## Consequences
- One command (`scripts\dashboard.cmd`) starts everything, and a trace shows each request down to its SQL.
- The same service defaults carry over to Azure Container Apps in phase 4.
- Docker Compose remains for the database only, so there are two ways of starting things. The README says which is which.
- On this PC the AppHost runs over plain HTTP (`ASPIRE_ALLOW_UNSECURED_TRANSPORT`), which avoids trusting a dev certificate for a local-only tool.
```

`docs/decisions/0008-shared-core-in-memory-api.md`:
```markdown
# 0008 — One analysis library, answered from memory

**Status:** Accepted (2026-09-28)

## Context
The report and the dashboard must never disagree about a number. The data is small (a few thousand postings) and changes twice a week.

## Decision
Phase 1's code moves into a `Tracker.Core` library that both the Processor and the API reference. The API loads the analysed postings into memory once (`MarketData`) and answers every filter with plain C# functions, reusing `Analyzer` for the overview.

At most once a minute, a request checks a cheap database version and reloads if it changed. The version is the run count, the latest run and the transaction id of the runs rows, so even a rules-only rebuild counts as a change.

**Consistency.** A load reads everything in one REPEATABLE READ transaction. That transaction first takes ACCESS SHARE locks on the rebuilt tables, in the same order as the rebuild's TRUNCATE. As a result:
- a load never mixes data from before and after a rebuild;
- a load can't deadlock with a rebuild.

**Database down.** The API keeps serving the last load and marks it stale.

**Alternatives considered:**
- **SQL per endpoint:** repeats the analysis rules in SQL, and they drift from the report.
- **A static JSON export:** isn't live, and needs an export step after every change.

## Consequences
- Every filter is a function that can be unit-tested without a database. The endpoint tests compare the API with `Analyzer` directly.
- Memory grows with history. That's fine at tens of thousands of postings; heavy queries can move into SQL later if needed.
- Editing `config/skills.json` or `config/profile.json` needs a dashboard restart, because a database reload doesn't reread them.
```

`docs/decisions/0009-react-typescript-vite.md`:
```markdown
# 0009 — React, TypeScript and Vite for the dashboard

**Status:** Accepted (2026-09-28)

## Context
The dashboard is an interactive client for a JSON API on this PC. React and TypeScript appear in many GTA full-stack postings.

## Decision
React 19 with TypeScript, built by Vite, using:
- TanStack Query for fetching and caching;
- React Router, with every filter kept in the URL;
- Recharts for the charts;
- Vitest with Testing Library for tests.

**Alternatives considered:**
- **Next.js:** server rendering and file routing add nothing to a single-user local tool whose API is already .NET.
- **Blazor:** would keep everything in C#, but React is asked for far more often in postings.

## Consequences
- The types in `api.ts` mirror the C# records, so a renamed field fails the TypeScript build instead of the page.
- Filters in the URL make every view bookmarkable, and back/forward work.
- There are two toolchains (dotnet and npm). Aspire starts both; the scheduled task needs neither.
```

- [ ] **Step 2: README**

In `README.md`, insert this section before `## Roadmap`:
```markdown
## Phase 2a: interactive dashboard

A local dashboard over the same database, always current. It has an overview, a skill explorer, a salary explorer and a postings browser. The static report stays the phone view.

### Setup (once)

1. Node.js 22.12 or later.
2. `dotnet user-secrets set "ConnectionStrings:tracker" "Host=localhost;Port=5434;Database=tracker;Username=tracker;Password=<same password>" --project src/AppHost`

### Run

    scripts\dashboard.cmd        # Postgres, then the API, the Vite app and the Aspire dashboard

- Dashboard: http://localhost:5190. Filters live in the URL, so any view can be bookmarked.
- Aspire dashboard (traces, logs, metrics): open the login link printed in the console.
- New data shows within a minute of the scheduled rebuild. If Postgres stops, pages keep the last data and say it's stale.
- Restart after editing `config/skills.json` or `config/profile.json`.

### Tests

    dotnet test                                     # all .NET tests (integration tests need Docker)
    cd src/Dashboard && npm test && npm run build   # dashboard tests, type check and build
```
and replace the Roadmap's second line with:
```markdown
2. ~~API + dashboard~~ Done in phase 2a: API + React dashboard (.NET Aspire, OpenTelemetry). Next: 2b shortlist and application tracker, 2c CV tailoring per posting
```

- [ ] **Step 3: Republish the scheduled task's binaries and check them**

The scheduled task runs the published copies in `.artifacts/`, which still hold the pre-refactor Processor.
```powershell
powershell -ExecutionPolicy Bypass -File scripts/register-task.ps1
```
Expected: both apps publish, and the task is registered again (Tue/Fri 07:00).

Then run the **published** processor in report-only mode, from the repo root. Never run the Archiver: it uses the Adzuna quota.
```bash
.artifacts/processor/Processor.exe --report-only; echo "exit $?"
```
Expected: `Report written: …` and `exit 0`. That proves the published build finds its user-secrets (`AddUserSecrets<Program>`) and the database.

- [ ] **Step 4: Full verification**

Run: `dotnet build -warnaserror && dotnet test && (cd src/Dashboard && npm test && npm run build)`
Expected:
- 0 warnings;
- Archiver.Tests 101, Tracker.Core.Tests 240 and Api.Tests all pass;
- 11 Vitest files pass;
- the dashboard builds.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Document phase 2a decisions and dashboard usage"
```
