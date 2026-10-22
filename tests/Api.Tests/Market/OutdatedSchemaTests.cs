using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Tracker.Data;
using Tracker.TestSupport;

namespace Api.Tests.Market;

// The dashboard started after an upgrade but before the Processor applied the new migration (the columns still have
// their old names): say what to run instead of a generic error.
[Collection("postgres")]
[Trait("Category", "Integration")]
public sealed class OutdatedSchemaTests(PostgresFixture pg) : IDisposable
{
    private readonly TempDir _data = new();

    // InitialCreate: the columns still have their old names. RenameRegionColumns: the Job Bank region table is missing.
    [Theory]
    [InlineData("20260928093654_InitialCreate")]
    [InlineData("20261003080632_RenameRegionColumns")]
    public async Task Market_tables_from_an_older_version_ask_for_a_processor_run(string migration)
    {
        var name = "outdated_" + Guid.NewGuid().ToString("N")[..12];
        await using (var conn = new NpgsqlConnection(pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", conn);
            await create.ExecuteNonQueryAsync();
        }
        var connection = new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Database = name }.ConnectionString;
        await using (var db = new TrackerDb(TrackerDbFactory.Options(connection)))
        {
            await db.GetService<IMigrator>().MigrateAsync(migration);
            await db.Database.ExecuteSqlRawAsync("INSERT INTO runs (run_id, started_at) VALUES ('r1', now());");
        }
        await using var factory = new ApiFactory(connection, _data.Path);

        var response = await factory.CreateClient().GetAsync("/api/meta");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Processor", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }

    public void Dispose() => _data.Dispose();
}
