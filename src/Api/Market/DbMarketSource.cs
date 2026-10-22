using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tracker.Analysis;
using Tracker.Data;
using Tracker.Reading;
using Tracker.Rebuild;

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

            var (postings, runs, jobBank) = await AnalysisLoader.LoadAsync(db, settings.Region, ct);
            await tx.CommitAsync(ct);
            return new MarketLoad(version, new MarketSnapshot(postings, runs, jobBank, Warnings(runs), clock.GetUtcNow()));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // An older version's tables without one a later migration adds: the Processor has to apply it.
            if (await RunsTableExistsAsync(db, ct)) throw new MarketSchemaOutdatedException(ex);
            // The Processor has never run against this database, so there is nothing to show yet.
            return knownVersion == NoTablesVersion ? null : new MarketLoad(NoTablesVersion, MarketSnapshot.Empty(clock.GetUtcNow()));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedColumn)
        {
            throw new MarketSchemaOutdatedException(ex);
        }
        catch (Exception ex) when (DatabaseErrors.IsUnavailable(ex))
        {
            throw new DatabaseUnavailableException(ex);
        }
    }

    // Runs outside the failed transaction, which the using block has already rolled back.
    private static async Task<bool> RunsTableExistsAsync(TrackerDb db, CancellationToken ct) =>
        await db.Database.SqlQueryRaw<bool>("""SELECT to_regclass('runs') IS NOT NULL AS "Value" """).SingleAsync(ct);

    // The same data warnings the report shows with --report-only: problems in the latest collection and unreadable inbox files.
    // The data in the database came from the Processor's last rebuild. After switching region (or with Tracker:Region),
    // it can still be another region's until the Processor runs again: show that first.
    private IReadOnlyList<string> Warnings(IReadOnlyList<RunInfo> builtFrom)
    {
        var all = Archive.Runs(settings.DataRoot);
        var runs = RebuildService.ForRegion(all, settings.Region);
        var latest = builtFrom.OrderBy(r => r.StartedAt).LastOrDefault()?.RunId;
        var builtFor = all.FirstOrDefault(r => r.RunId == latest)?.Region;
        string[] wrongRegion = builtFor is not null && !builtFor.Equals(settings.Region.Name, StringComparison.OrdinalIgnoreCase)
            ? [$"This data was built for region '{builtFor}'. Run the Processor (scripts/run-pipeline.cmd) to rebuild it for {settings.Region.Name}."]
            : [];
        return [.. wrongRegion, .. runs.Count > 0 ? Archive.CollectionProblems(runs[^1]) : [],
            .. RebuildService.OtherRegions(all, settings.Region) is { } skipped ? [skipped] : Array.Empty<string>(),
            .. InboxReader.ReadAll(Path.Combine(settings.DataRoot, "inbox"), runs, settings.Region.Name).Warnings];
    }
}
