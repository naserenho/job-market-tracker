using Microsoft.EntityFrameworkCore;
using Tracker.Data;
using Testcontainers.PostgreSql;

namespace Tracker.TestSupport;

// One throwaway Postgres 17 container per test run (Docker must be running).
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

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
