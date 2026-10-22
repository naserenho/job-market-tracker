using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Api.Applications;
using Api.Tests.TestSupport;
using Npgsql;
using Tracker.TestSupport;

namespace Api.Tests.Applications;

// Each test gets its own empty database on the shared server, as if the Docker volume had just been lost.
[Collection("postgres")]
[Trait("Category", "Integration")]
public sealed class TrackerRestoreTests(PostgresFixture pg) : IDisposable
{
    private readonly TempDir _data = new();

    private string MirrorPath => Path.Combine(_data.Path, "tracker", "tracker.json");

    private async Task<string> FreshDatabaseAsync()
    {
        var name = "restore_" + Guid.NewGuid().ToString("N")[..12];
        await using (var conn = new NpgsqlConnection(pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", conn);
            await create.ExecuteNonQueryAsync();
        }
        return new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Database = name }.ConnectionString;
    }

    [Fact]
    public async Task Empty_tables_are_restored_from_the_backup_copy_on_first_use()
    {
        var sample = MirrorStoreTests.Sample();
        MirrorStore.Write(MirrorPath, sample);
        await using var factory = new ApiFactory(await FreshDatabaseAsync(), _data.Path);

        var status = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/tracker/status");

        Assert.Equal(1, status.GetProperty("counts").GetProperty("Interviewing").GetInt32());
        Assert.Equal(0, status.GetProperty("overdue").GetInt32()); // its follow-up is due in March 2027
    }

    [Fact]
    public async Task Restored_market_entries_open_before_the_next_rebuild()
    {
        var sample = MirrorStoreTests.Sample(); // a market entry (greenhouse:101) on a database with no market tables yet
        MirrorStore.Write(MirrorPath, sample);
        await using var factory = new ApiFactory(await FreshDatabaseAsync(), _data.Path);

        var response = await factory.CreateClient().GetAsync($"/api/tracker/applications/{sample.Applications[0].Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("", detail.GetProperty("postingText").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("market").ValueKind);
    }

    [Fact]
    public async Task Backups_from_before_generated_cvs_restore()
    {
        var sample = MirrorStoreTests.Sample();
        var json = JsonNode.Parse(JsonSerializer.Serialize(sample, MirrorStore.Json))!;
        foreach (var document in json["documents"]!.AsArray()) document!.AsObject().Remove("cvChoices");
        Directory.CreateDirectory(Path.GetDirectoryName(MirrorPath)!);
        File.WriteAllText(MirrorPath, json.ToJsonString());
        await using var factory = new ApiFactory(await FreshDatabaseAsync(), _data.Path);

        var detail = await factory.CreateClient().GetFromJsonAsync<JsonElement>($"/api/tracker/applications/{sample.Applications[0].Id}");

        var restored = Assert.Single(detail.GetProperty("documents").EnumerateArray());
        Assert.False(restored.GetProperty("generated").GetBoolean());
    }

    [Fact]
    public async Task A_corrupt_backup_keeps_the_tracker_closed_and_is_never_overwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MirrorPath)!);
        File.WriteAllText(MirrorPath, "{ not json");
        await using var factory = new ApiFactory(await FreshDatabaseAsync(), _data.Path);

        var response = await factory.CreateClient().GetAsync("/api/tracker/status");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("tracker.json", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
        Assert.Equal("{ not json", File.ReadAllText(MirrorPath));
    }

    [Fact]
    public async Task A_backup_the_database_refuses_names_the_file_and_is_kept()
    {
        var sample = MirrorStoreTests.Sample();
        sample.FollowUps[0].ApplicationId = Guid.NewGuid(); // points at no entry: the restore's insert fails
        MirrorStore.Write(MirrorPath, sample);
        var before = File.ReadAllText(MirrorPath);
        await using var factory = new ApiFactory(await FreshDatabaseAsync(), _data.Path);

        var response = await factory.CreateClient().GetAsync("/api/tracker/status");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Tracker backup unreadable", problem.GetProperty("title").GetString());
        Assert.Contains("tracker.json", problem.GetProperty("detail").GetString());
        Assert.Equal(before, File.ReadAllText(MirrorPath));
    }

    // A lost Docker volume while the dashboard is open: no restart needed.
    [Fact]
    public async Task Tables_lost_while_the_api_runs_come_back_from_the_backup_on_the_next_request()
    {
        var connection = await FreshDatabaseAsync();
        await using var factory = new ApiFactory(connection, _data.Path);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TrackerGate.ClientHeader, "tests");
        var pasted = await client.PostAsJsonAsync("/api/tracker/applications/paste", new
        {
            title = "Developer", company = "Lostco", url = "", location = "Toronto, ON", postingText = "C#", status = "Applied",
        });
        Assert.Equal(HttpStatusCode.Created, pasted.StatusCode);
        await using (var conn = new NpgsqlConnection(connection))
        {
            await conn.OpenAsync();
            await using var drop = new NpgsqlCommand(
                """DROP TABLE application_documents, contacts, follow_ups, application_events, applications, companies, "__applications_migrations" """, conn);
            await drop.ExecuteNonQueryAsync();
        }

        var first = await client.GetAsync("/api/tracker/status");
        var second = await client.GetFromJsonAsync<JsonElement>("/api/tracker/status");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Contains("try again", (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
        Assert.Equal(1, second.GetProperty("counts").GetProperty("Applied").GetInt32());
    }

    public void Dispose() => _data.Dispose();
}
