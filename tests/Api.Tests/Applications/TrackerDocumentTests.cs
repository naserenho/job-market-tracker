using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Applications;
using Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Applications;

[Collection("tracker")]
[Trait("Category", "Integration")]
public sealed class TrackerDocumentTests(TrackerFixture tracker)
{
    private static MultipartFormDataContent Upload(byte[] bytes, string fileName, string? kind = "Cv", string? sentOn = "2027-03-01")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new("application/octet-stream");
        form.Add(file, "file", fileName);
        if (kind is not null) form.Add(new StringContent(kind), "kind");
        if (sentOn is not null) form.Add(new StringContent(sentOn), "sentOn");
        return form;
    }

    private string FolderOf(Guid entryId) => Path.Combine(tracker.DataRoot, "tracker", "files", entryId.ToString("N"));

    [Fact]
    public async Task Documents_upload_download_and_delete()
    {
        var entry = await tracker.PasteAsync("Docco");
        var bytes = "%PDF-1.4 tailored CV"u8.ToArray();

        var created = await tracker.Client.PostAsync($"/api/tracker/applications/{entry.Id}/documents", Upload(bytes, "..\\CV (RBC).pdf"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<NewEntry>(TrackerFixture.Json))!.Id;
        var doc = Assert.Single((await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{entry.Id}")).Documents);
        Assert.Equal((id, "CV (RBC)", "CV-RBC-.pdf", DocumentKind.Cv, new DateOnly(2027, 3, 1), (long)bytes.Length),
            (doc.Id, doc.Label, doc.FileName, doc.Kind, doc.SentOn, doc.SizeBytes));
        var onDisk = Path.Combine(FolderOf(entry.Id), $"{id:N}-CV-RBC-.pdf");
        Assert.True(File.Exists(onDisk));

        var download = await tracker.Client.GetAsync($"/api/tracker/documents/{id}");
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Contains("CV-RBC-.pdf", download.Content.Headers.ContentDisposition!.ToString());

        Assert.Equal(HttpStatusCode.NoContent, (await tracker.Client.DeleteAsync($"/api/tracker/documents/{id}")).StatusCode);
        Assert.False(File.Exists(onDisk));
        Assert.Equal(HttpStatusCode.NotFound, (await tracker.Client.GetAsync($"/api/tracker/documents/{id}")).StatusCode);
    }

    [Theory]
    [InlineData("tool.exe", 10, "PDF, DOCX")]
    [InlineData("CV.pdf", 0, "empty")]
    [InlineData("CV.pdf", Rules.MaxFileBytes + 1, "10 MB")]
    public async Task Unsuitable_files_are_refused_and_nothing_is_stored(string name, long size, string message)
    {
        var entry = await tracker.PasteAsync($"Refuse {name} {size}");

        var response = await tracker.Client.PostAsync($"/api/tracker/applications/{entry.Id}/documents", Upload(new byte[size], name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("file", problem.GetProperty("field").GetString());
        Assert.Contains(message, problem.GetProperty("detail").GetString());
        Assert.False(Directory.Exists(FolderOf(entry.Id)));
    }

    // On real Kestrel (30 MB default body limit): a large scan must get the 10 MB message, not a 500 or a dropped connection.
    [Fact]
    public async Task Oversized_uploads_are_refused_before_the_body_is_read()
    {
        var entry = await tracker.PasteAsync("Bigfile Co");
        await using var factory = new ApiFactory(tracker.ConnectionString, tracker.DataRoot);
        factory.UseKestrel(0);
        factory.StartServer();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TrackerGate.ClientHeader, "tests");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tracker/applications/{entry.Id}/documents")
        {
            Content = Upload(new byte[40 * 1024 * 1024], "scan.pdf"),
        };
        request.Headers.ExpectContinue = true;

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("file", problem.GetProperty("field").GetString());
        Assert.Contains("10 MB", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Deleting_an_entry_removes_its_files_follow_ups_and_backup_record()
    {
        var entry = await tracker.PasteAsync("Deleteco");
        await tracker.Client.PostAsync($"/api/tracker/applications/{entry.Id}/documents", Upload("hello"u8.ToArray(), "notes.txt", kind: "Other"));
        await tracker.PostAsync<NewEntry>($"/api/tracker/applications/{entry.Id}/follow-ups", new { action = "Chase", dueOn = "2026-10-09" });
        Assert.True(Directory.Exists(FolderOf(entry.Id)));

        var deleted = await tracker.Client.DeleteAsync($"/api/tracker/applications/{entry.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.False(Directory.Exists(FolderOf(entry.Id)));
        Assert.Equal(HttpStatusCode.NotFound, (await tracker.Client.GetAsync($"/api/tracker/applications/{entry.Id}")).StatusCode);
        await using var db = tracker.OpenDb();
        Assert.False(await db.FollowUps.AnyAsync(f => f.ApplicationId == entry.Id));
        Assert.DoesNotContain(MirrorStore.Read(Path.Combine(tracker.DataRoot, "tracker", "tracker.json"))!.Applications, a => a.Id == entry.Id);
    }

    [Fact]
    public async Task Generated_documents_are_marked_in_the_entry()
    {
        var entry = await tracker.PasteAsync("Genco");
        await tracker.Client.PostAsync($"/api/tracker/applications/{entry.Id}/documents", Upload("%PDF"u8.ToArray(), "Final CV.pdf"));
        await using (var db = tracker.OpenDb())
        {
            db.Documents.Add(new ApplicationDocument
            {
                Id = Guid.NewGuid(), ApplicationId = entry.Id, Kind = DocumentKind.Cv, Label = "Tailored CV", FileName = "cv.docx",
                ContentType = Rules.ContentType("cv.docx"), SizeBytes = 1, SentOn = new DateOnly(2026, 10, 2), CreatedAt = DateTimeOffset.UtcNow,
                CvChoices = """{"headline":"Dev","summary":"","bulletIds":[]}""",
            });
            await db.SaveChangesAsync();
        }

        var documents = (await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{entry.Id}")).Documents;

        Assert.Equal(new[] { ("Final CV", false), ("Tailored CV", true) }, documents.Select(d => (d.Label, d.Generated)).OrderBy(d => d.Label));
    }

    [Fact]
    public async Task Uploads_need_the_dashboard_header_and_an_existing_entry()
    {
        using var outsider = tracker.CreateClient(withHeader: false);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.PostAsync($"/api/tracker/applications/{Guid.NewGuid()}/documents", Upload([1], "CV.pdf"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await tracker.Client.PostAsync($"/api/tracker/applications/{Guid.NewGuid()}/documents", Upload([1], "CV.pdf"))).StatusCode);
    }
}
