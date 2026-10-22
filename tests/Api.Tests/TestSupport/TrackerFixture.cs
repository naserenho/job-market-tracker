using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Applications;
using Api.Market;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tracker.Rebuild;
using Tracker.TestSupport;

namespace Api.Tests.TestSupport;

// One Postgres container and archive folder for the tracker tests, rebuilt from the fixture archive, with the API in
// front on a manual clock (today = 2 October 2026).
public sealed class TrackerFixture : IAsyncLifetime
{
    private readonly PostgresFixture _pg = new();
    private readonly TempDir _folder = new();
    private WebApplicationFactory<Program>? _factory;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public ManualClock Clock { get; } = new() { Now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero) };
    public HttpClient Client { get; private set; } = null!;
    public string ConnectionString => _pg.ConnectionString;
    public string DataRoot => _folder.Path;

    public async Task InitializeAsync()
    {
        await _pg.InitializeAsync();
        FixtureArchive.Write(DataRoot);
        await RebuildAsync();
        _factory = new ApiFactory(_pg.ConnectionString, DataRoot)
            .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(Clock)));
        Client = CreateClient(withHeader: true);
    }

    public HttpClient CreateClient(bool withHeader)
    {
        var client = _factory!.CreateClient();
        if (withHeader) client.DefaultRequestHeaders.Add(TrackerGate.ClientHeader, "tests");
        return client;
    }

    public ApplicationsDb OpenDb() => new(ApplicationsDb.Options(ConnectionString));

    // The scheduled Processor's destructive rebuild (TRUNCATE + COPY of the market tables), then one market check
    // interval so the API notices it.
    public async Task RebuildAsync()
    {
        await using (var db = await _pg.OpenAsync())
            await new RebuildService(db, RepoConfig.Toronto, Data.Settings.Catalog, NullLogger<RebuildService>.Instance)
                .RunAsync(DataRoot, CancellationToken.None);
        Clock.Now += MarketData.CheckInterval;
    }

    public async Task<T> GetAsync<T>(string url)
    {
        var response = await Client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, $"GET {url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await Client.PostAsJsonAsync(url, body, Json);
        Assert.True(response.IsSuccessStatusCode, $"POST {url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    // A pasted job in the GTA, one per company name the test chooses.
    public Task<NewEntry> PasteAsync(string company, string status = "Shortlisted") =>
        PostAsync<NewEntry>("/api/tracker/applications/paste", new
        {
            title = $"Developer at {company}", company, url = "https://careers.example.com/" + Uri.EscapeDataString(company),
            location = "Toronto, ON", postingText = "Build services in C# and Azure.", status,
        });

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        await _pg.DisposeAsync();
        _folder.Dispose();
    }
}

[CollectionDefinition("tracker")]
public sealed class TrackerCollection : ICollectionFixture<TrackerFixture>;
