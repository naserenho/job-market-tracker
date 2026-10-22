using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Applications;
using Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Tracker.Data;
using Tracker.TestSupport;

namespace Api.Tests.Applications;

[Collection("tracker")]
[Trait("Category", "Integration")]
public sealed class TrackerSchemaTests(TrackerFixture tracker)
{
    [Fact]
    public async Task Status_answers_once_the_tracker_tables_exist()
    {
        var status = await tracker.GetAsync<JsonElement>("/api/tracker/status");

        Assert.True(status.GetProperty("migrated").GetBoolean());
        await using var db = tracker.OpenDb();
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_InitialApplications"));
    }

    [Fact]
    public async Task Tracker_rows_survive_a_market_rebuild()
    {
        await tracker.GetAsync<JsonElement>("/api/tracker/status");
        var id = Guid.NewGuid();
        await using (var db = tracker.OpenDb())
        {
            var now = DateTimeOffset.UtcNow;
            var company = new Company { Id = Guid.NewGuid(), Name = "Survivor Inc", NameKey = "survivor inc", CreatedAt = now, UpdatedAt = now };
            db.Companies.Add(company);
            db.Applications.Add(new JobApplication { Id = id, CompanyId = company.Id, Title = "Survivor role", Status = ApplicationStatus.Applied, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }

        await tracker.RebuildAsync();

        await using var after = tracker.OpenDb();
        Assert.Equal("Survivor role", (await after.Applications.SingleAsync(a => a.Id == id)).Title);
    }

    [Fact]
    public void Tracker_tables_are_not_in_the_rebuild_list()
    {
        using var db = tracker.OpenDb();
        var rebuilt = TrackerTables.All.Split(", ");
        Assert.All(db.Model.GetEntityTypes().Select(e => e.GetTableName()!), table => Assert.DoesNotContain(table, rebuilt));
    }

    [Fact]
    public async Task Requests_for_other_host_names_are_refused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tracker/status");
        request.Headers.Host = "attacker.example";

        Assert.Equal(HttpStatusCode.BadRequest, (await tracker.Client.SendAsync(request)).StatusCode);
    }
}

[Trait("Category", "Integration")]
public sealed class TrackerUnavailableTests : IDisposable
{
    private readonly TempDir _data = new();

    [Fact]
    public async Task A_stopped_database_answers_503_on_tracker_requests()
    {
        await using var factory = new ApiFactory("Host=127.0.0.1;Port=1;Database=tracker;Username=tracker;Password=x;Timeout=3", _data.Path);

        var response = await factory.CreateClient().GetAsync("/api/tracker/status");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("docker compose up -d postgres", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }

    public void Dispose() => _data.Dispose();
}
