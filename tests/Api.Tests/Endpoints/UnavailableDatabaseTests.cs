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
