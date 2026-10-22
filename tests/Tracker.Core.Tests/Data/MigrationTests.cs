using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Tracker.Data;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Data;

[Collection("postgres")]
[Trait("Category", "Integration")]
public class MigrationTests(PostgresFixture pg)
{
    // The column rename must keep the values: right after the upgrade, --report-only still reads every posting's scope.
    [Fact]
    public async Task Renaming_the_scope_columns_keeps_their_values()
    {
        var name = "migrate_" + Guid.NewGuid().ToString("N")[..12];
        await using (var conn = new NpgsqlConnection(pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", conn);
            await create.ExecuteNonQueryAsync();
        }
        var connection = new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Database = name }.ConnectionString;
        await using var db = new TrackerDb(TrackerDbFactory.Options(connection));
        await db.GetService<IMigrator>().MigrateAsync("20260928093654_InitialCreate");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO runs (run_id, started_at) VALUES ('r1', now());
            INSERT INTO postings (id, source, source_id, company, title, url, location_text, city, is_gta, is_remote_canada, in_scope,
                role_family, seniority, salary_origin, description, is_full_text, citizenship_or_clearance, first_seen_run, last_seen_run)
            VALUES ('x:1', 'x', '1', 'Co', 'Dev', '', 'Toronto', 'Toronto', true, false, true,
                'dotnet', 'Senior', 'none', '', true, false, 'r1', 'r1');
            """);

        await db.Database.MigrateAsync();

        var posting = await db.Postings.AsNoTracking().SingleAsync(p => p.Id == "x:1");
        Assert.Equal((true, false), (posting.IsLocal, posting.IsRemoteInCountry));
    }
}
