using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tracker;
using Tracker.Analysis;
using Tracker.Configuration;
using Tracker.Data;
using Tracker.Interpreting;
using Tracker.Reading;
using Tracker.Rebuild;
using Tracker.Reporting;

namespace Processor;

// The whole program except finding the repo root and the connection string (Program.cs), so tests can run it.
// Exit codes: 0 done, 1 failed, 2 bad arguments or config, 3 the database can't be reached.
public static class ProcessorApp
{
    public const int Ok = 0, Failed = 1, ConfigInvalid = 2, DatabaseUnavailable = 3;
    private const string Usage = "Usage: Processor [--report-only]";

    public static async Task<int> RunAsync(string[] args, string? root, string? connectionString, ILoggerFactory loggers,
        TextWriter error, CancellationToken ct)
    {
        var reportOnly = args.Contains("--report-only");
        var unknown = args.Where(a => a != "--report-only").ToList();
        if (unknown.Count > 0)
        {
            error.WriteLine($"Unknown argument(s): {string.Join(' ', unknown)}");
            error.WriteLine(Usage);
            return ConfigInvalid;
        }

        if (root is null)
        {
            error.WriteLine("Could not find config/sources.json in this folder or any parent. Run from the repo root.");
            return ConfigInvalid;
        }

        ProcessorOptions options;
        Region region;
        SkillCatalog catalog;
        Profile profile;
        try
        {
            options = ProcessorOptions.Load(Path.Combine(root, "config", "processor.json"));
            region = Regions.Load(root);
            catalog = SkillCatalog.Load(Path.Combine(root, "config", "skills.json"));
            var profilePath = Path.Combine(root, "config", "profile.json");
            if (!File.Exists(profilePath))
            {
                error.WriteLine("Config error: config/profile.json is missing. Copy config/profile.example.json to config/profile.json and set your skill levels.");
                return ConfigInvalid;
            }
            profile = Profile.Load(profilePath, catalog);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException)
        {
            error.WriteLine($"Config error: {ErrorText.Chain(ex)}");
            return ConfigInvalid;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            error.WriteLine("Config error: no database connection string. Set it with:");
            error.WriteLine("  dotnet user-secrets set \"ConnectionStrings:Tracker\" \"Host=localhost;Port=5434;Database=tracker;Username=tracker;Password=<password from .env>\" --project src/Processor");
            return ConfigInvalid;
        }

        var log = loggers.CreateLogger("Processor");
        await using var db = new TrackerDb(new DbContextOptionsBuilder<TrackerDb>(TrackerDbFactory.Options(connectionString)).UseLoggerFactory(loggers).Options);

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
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or DbUpdateException or InvalidOperationException)
        {
            // The server answered and refused: "start Docker" would be the wrong advice.
            log.LogError(ex, "Database migration failed");
            error.WriteLine($"Database error: {ErrorText.Chain(ex)}");
            return Failed;
        }

        try
        {
            var dataRoot = Path.Combine(root, "data");
            var allRuns = Archive.Runs(dataRoot);
            var archiveRuns = RebuildService.ForRegion(allRuns, region);
            IReadOnlyList<string> warnings = reportOnly
                ? [.. archiveRuns.Count > 0 ? Archive.CollectionProblems(archiveRuns[^1]) : [],
                   .. RebuildService.OtherRegions(allRuns, region) is { } skipped ? [skipped] : Array.Empty<string>(),
                   .. InboxReader.ReadAll(Path.Combine(dataRoot, "inbox"), archiveRuns, region.Name).Warnings]
                : (await new RebuildService(db, region, catalog, loggers.CreateLogger<RebuildService>()).RunAsync(dataRoot, ct)).Warnings;

            var (postings, runs, jobBank) = await AnalysisLoader.LoadAsync(db, region, ct);
            if (runs.Count == 0)
            {
                error.WriteLine("The database has no runs yet. Run Processor without --report-only first.");
                return Failed;
            }

            var areas = region.Local.Areas is { } map
                ? new AreaSettings(map, $"Other {region.LocalLabel}", region.RemoteLabel, await AnalysisLoader.LoadJobBankRegionsAsync(db, ct),
                    region.LocalLabel, $"{region.LocalLabel}, no city given")
                : null;
            var model = new Analyzer(profile, catalog, options.WindowDays).Build(postings, runs, jobBank, warnings, DateTimeOffset.Now, areas);
            var latest = await ReportWriter.WriteAsync(dataRoot, model, region, ct);
            log.LogInformation("Report written: {Path} ({InWindow} postings in the window, top gap: {Gap})",
                latest, model.Snapshot.InWindow, model.Gaps.FirstOrDefault()?.Skill ?? "none");
            return Ok;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            error.WriteLine("Cancelled.");
            return Failed;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Processing failed");
            error.WriteLine($"Processing failed: {ErrorText.Chain(ex)}");
            return Failed;
        }
    }
}
