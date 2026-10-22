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
            await new RebuildService(db, RepoConfig.Toronto, Data.Settings.Catalog, NullLogger<RebuildService>.Instance)
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
