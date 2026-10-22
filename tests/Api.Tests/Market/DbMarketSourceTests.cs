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
            await new RebuildService(db, RepoConfig.Toronto, Data.Settings.Catalog, NullLogger<RebuildService>.Instance)
                .RunAsync(_data.Path, CancellationToken.None);
        return Source(pg.ConnectionString, _data.Path);
    }

    [Fact]
    public async Task Loads_the_rebuilt_market_and_reports_no_change_for_the_same_version()
    {
        var source = await RebuiltAsync();

        var load = (await source.LoadIfChangedAsync(null, CancellationToken.None))!;

        Assert.Equal((3, 6), (load.Data.Runs.Count, load.Data.Postings.Count));
        Assert.Contains(load.Data.Postings, p => p.Id == "ashby:S1" && p.IsRemoteInCountry && !p.IsLocal);
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
