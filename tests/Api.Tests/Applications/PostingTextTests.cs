using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Applications;
using Api.Endpoints;
using Api.Tests.TestSupport;

namespace Api.Tests.Applications;

[Collection("tracker")]
[Trait("Category", "Integration")]
public sealed class PostingTextTests(TrackerFixture tracker)
{
    private string Inbox => Path.Combine(tracker.DataRoot, "inbox");

    private Task<HttpResponseMessage> PutText(Guid id, uint version, string text) =>
        tracker.Client.PutAsJsonAsync($"/api/tracker/applications/{id}/posting-text", new { version, postingText = text }, TrackerFixture.Json);

    private Task<ApplicationDetail> Detail(Guid id) => tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{id}");

    [Fact]
    public async Task Text_pasted_onto_a_market_entry_replaces_the_snippet_without_touching_the_inbox()
    {
        var entry = await tracker.PostAsync<NewEntry>("/api/tracker/applications", new { postingId = "adzuna:A1" });
        var before = await Detail(entry.Id);
        Assert.Equal("Build services in C# and .NET on Azure.", before.PostingText);
        var inboxFiles = Directory.GetFiles(Inbox).Order().ToList();

        var saved = await PutText(entry.Id, before.Version, "Full posting: C#, .NET, Azure DevOps, React and SQL Server.");

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var after = await Detail(entry.Id);
        Assert.StartsWith("Full posting:", after.PostingText);
        Assert.Equal(inboxFiles, Directory.GetFiles(Inbox).Order().ToList());

        await PutText(entry.Id, after.Version, "   ");
        Assert.Equal("Build services in C# and .NET on Azure.", (await Detail(entry.Id)).PostingText);
    }

    [Fact]
    public async Task A_pasted_entry_keeps_its_inbox_file_as_first_pasted()
    {
        var entry = await tracker.PasteAsync("Textco");
        var detail = await Detail(entry.Id);
        var file = Directory.GetFiles(Inbox, "*textco*").Single();
        var original = File.ReadAllText(file);

        await PutText(entry.Id, detail.Version, "A longer version of the job text with Kubernetes.");

        Assert.Equal(original, File.ReadAllText(file));
        Assert.Contains("Kubernetes", (await Detail(entry.Id)).PostingText);
    }

    [Fact]
    public async Task A_stale_version_gets_the_reload_message()
    {
        var entry = await tracker.PasteAsync("Staletext");
        var detail = await Detail(entry.Id);
        await PutText(entry.Id, detail.Version, "First save.");

        var stale = await PutText(entry.Id, detail.Version, "Second save from an old tab.");

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ApiExceptionHandler.StaleEdit, (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(100_001, "longer than 100000")]
    [InlineData(-1, "NUL")]
    public async Task Unsavable_text_is_refused_with_the_field(int length, string message)
    {
        var entry = await tracker.PasteAsync($"Badtext {length}");
        var detail = await Detail(entry.Id);

        var response = await PutText(entry.Id, detail.Version, length < 0 ? "C#\0 and Azure" : new string('x', length));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("postingText", problem.GetProperty("field").GetString());
        Assert.Contains(message, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Saving_text_needs_the_dashboard_header()
    {
        using var outsider = tracker.CreateClient(withHeader: false);

        var response = await outsider.PutAsJsonAsync($"/api/tracker/applications/{Guid.NewGuid()}/posting-text", new { version = 1, postingText = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
