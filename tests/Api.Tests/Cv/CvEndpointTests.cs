using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Applications;
using Api.Cv;
using Api.Tests.TestSupport;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Api.Tests.Cv;

// Runs against the committed example master CV (Alex Example); see ApiFactory.
[Collection("tracker")]
[Trait("Category", "Integration")]
public sealed class CvEndpointTests(TrackerFixture tracker)
{
    private static readonly string[] Chosen = ["acme-lead", "acme-cicd", "globex-react"];

    private Task<HttpResponseMessage> Generate(Guid id, object body) =>
        tracker.Client.PostAsJsonAsync($"/api/tracker/applications/{id}/cv", body, TrackerFixture.Json);

    private Task<NewEntry> PasteAsync(string company, string text) =>
        tracker.PostAsync<NewEntry>("/api/tracker/applications/paste", new
        {
            title = "Senior .NET Developer", company, url = "", location = "Toronto, ON", postingText = text, status = "Shortlisted",
        });

    private string FolderOf(Guid entryId) => Path.Combine(tracker.DataRoot, "tracker", "files", entryId.ToString("N"));

    private static List<string> TextOf(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx), false);
        return [.. doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().Select(p => p.InnerText)];
    }

    [Fact]
    public async Task The_example_master_cv_is_ready()
    {
        var status = await tracker.GetAsync<CvStatusView>("/api/tracker/cv");

        Assert.Equal((true, 0), (status.Ready, status.Problems.Count));
    }

    [Fact]
    public async Task The_proposal_for_a_pasted_job_suggests_matching_bullets()
    {
        var entry = await PasteAsync("Proposeco", "We need C#, Azure DevOps CI/CD pipelines and React.");

        var proposal = await tracker.GetAsync<CvProposal>($"/api/tracker/applications/{entry.Id}/cv");

        Assert.Equal(("entry", false, "Proposeco"), (proposal.TextSource, proposal.Thin, proposal.Company));
        Assert.Contains(proposal.PostingSkills, s => s.Skill == ".NET" && s.Weight == 2);
        Assert.Equal("dotnet", proposal.SuggestedSummaryId);
        Assert.Equal(new[] { "acme-cicd", "acme-lead" }, proposal.Suggestion.BulletIds.Take(2));
        Assert.Contains("globex-react", proposal.Suggestion.BulletIds);
        Assert.Null(proposal.Saved);
    }

    [Fact]
    public async Task A_snippet_from_the_market_is_thin()
    {
        var entry = await tracker.PostAsync<NewEntry>("/api/tracker/applications", new { postingId = "adzuna:A2" });

        var proposal = await tracker.GetAsync<CvProposal>($"/api/tracker/applications/{entry.Id}/cv");

        Assert.Equal(("market", true), (proposal.TextSource, proposal.Thin));
    }

    // A later rebuild can mark a shortlisted Adzuna snippet as a duplicate (reposted, or found in full elsewhere). It then
    // leaves the market snapshot but keeps its text, and must still count as a snippet.
    [Fact]
    public async Task A_snippet_marked_as_a_duplicate_is_still_thin()
    {
        var id = Guid.NewGuid();
        await tracker.GetAsync<CvStatusView>("/api/tracker/cv"); // the first tracker request creates the tables
        await using (var db = tracker.OpenDb())
        {
            var now = DateTimeOffset.UtcNow;
            var company = new Company { Id = Guid.NewGuid(), Name = "Dupco", NameKey = "dupco", CreatedAt = now, UpdatedAt = now };
            db.Companies.Add(company);
            db.Applications.Add(new JobApplication
            {
                Id = id, CompanyId = company.Id, PostingId = "adzuna:A3", Title = "Senior .NET Developer (Azure)",
                Status = ApplicationStatus.Shortlisted, Origin = EntryOrigin.Market, CreatedAt = now, UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        var proposal = await tracker.GetAsync<CvProposal>($"/api/tracker/applications/{id}/cv");

        Assert.Equal("market", proposal.TextSource);
        Assert.True(proposal.PostingSkills.Count >= CvTailor.ThinSkillCount, "thin only because the text is a snippet");
        Assert.True(proposal.Thin);
    }

    [Fact]
    public async Task Generating_saves_the_file_with_its_choices_and_backs_it_up()
    {
        var entry = await PasteAsync("Genco Two", "C#, Azure DevOps and React.");

        var response = await Generate(entry.Id, new { headline = "Senior .NET Developer", summary = "Builds .NET systems.", bulletIds = Chosen });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<GeneratedCv>(TrackerFixture.Json))!;
        Assert.Equal("Alex Example - CV - Genco Two.docx", created.FileName);
        var document = Assert.Single((await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{entry.Id}")).Documents);
        Assert.Equal((created.DocumentId, "Tailored CV", DocumentKind.Cv, true, new DateOnly(2026, 10, 2)),
            (document.Id, document.Label, document.Kind, document.Generated, document.SentOn));

        var download = await tracker.Client.GetAsync($"/api/tracker/documents/{created.DocumentId}");
        Assert.Equal(CvDocx.ContentType, download.Content.Headers.ContentType!.MediaType);
        var text = TextOf(await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("Alex Example", text[0]);
        Assert.Contains("Set up Azure DevOps CI/CD pipelines that cut a release from a day to an hour.", text);
        Assert.DoesNotContain("Tuned SQL Server queries and indexes, halving report load times.", text);

        var backup = MirrorStore.Read(Path.Combine(tracker.DataRoot, "tracker", "tracker.json"))!;
        Assert.Contains("acme-cicd", backup.Documents.Single(d => d.Id == created.DocumentId).CvChoices);

        var again = await tracker.GetAsync<CvProposal>($"/api/tracker/applications/{entry.Id}/cv");
        Assert.Equal((new DateOnly(2026, 10, 2), "Builds .NET systems."), (again.Saved!.GeneratedOn, again.Saved.Summary));
        Assert.Equal(Chosen, again.Saved.BulletIds);
        Assert.Empty(again.Saved.DroppedIds);
    }

    [Fact]
    public async Task Saved_choices_report_bullets_that_left_the_cv()
    {
        var entry = await PasteAsync("Dropco", "C# and React.");
        await using (var db = tracker.OpenDb())
        {
            db.Documents.Add(new ApplicationDocument
            {
                Id = Guid.NewGuid(), ApplicationId = entry.Id, Kind = DocumentKind.Cv, Label = "Tailored CV", FileName = "cv.docx",
                ContentType = CvDocx.ContentType, SizeBytes = 1, SentOn = new DateOnly(2026, 9, 30), CreatedAt = DateTimeOffset.UtcNow,
                CvChoices = CvChoicesJson.Write(new CvChoices("Dev", "", ["acme-lead", "gone-1"])),
            });
            await db.SaveChangesAsync();
        }

        var proposal = await tracker.GetAsync<CvProposal>($"/api/tracker/applications/{entry.Id}/cv");

        Assert.Equal(new[] { "acme-lead" }, proposal.Saved!.BulletIds);
        Assert.Equal(new[] { "gone-1" }, proposal.Saved.DroppedIds);
    }

    [Theory]
    [InlineData("", "", "headline")]
    [InlineData("Dev\0", "", "headline")]
    [InlineData("Dev", "long", "summary")]
    [InlineData("Dev", "", "bulletIds")]
    public async Task Bad_requests_are_refused_and_nothing_is_stored(string headline, string summary, string field)
    {
        var entry = await PasteAsync($"Refuseco {field} {headline.Length}", "C#.");
        object body = field == "bulletIds"
            ? new { headline, summary, bulletIds = new[] { "acme-lead", "nope" } }
            : new { headline, summary = summary == "long" ? new string('x', 2001) : summary, bulletIds = Array.Empty<string>() };

        var response = await Generate(entry.Id, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(field, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("field").GetString());
        Assert.False(Directory.Exists(FolderOf(entry.Id)));
    }

    [Fact]
    public async Task A_missing_master_cv_is_reported_and_blocks_tailoring()
    {
        var entry = await PasteAsync("Nocvco", "C#.");
        await using var factory = new ApiFactory(tracker.ConnectionString, tracker.DataRoot, Path.Combine(tracker.DataRoot, "no-such-cv.json"));
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<CvStatusView>("/api/tracker/cv", TrackerFixture.Json);
        var proposal = await client.GetAsync($"/api/tracker/applications/{entry.Id}/cv");

        Assert.Equal((false, MasterCvFile.Missing), (status!.Ready, Assert.Single(status.Problems)));
        Assert.Equal(HttpStatusCode.BadRequest, proposal.StatusCode);
        Assert.Equal("cv", (await proposal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("field").GetString());
    }

    [Fact]
    public async Task Generating_needs_the_dashboard_header()
    {
        using var outsider = tracker.CreateClient(withHeader: false);

        var response = await outsider.PostAsJsonAsync($"/api/tracker/applications/{Guid.NewGuid()}/cv", new { headline = "Dev" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_the_entry_removes_its_generated_cv()
    {
        var entry = await PasteAsync("Gonecvco", "C#.");
        Assert.Equal(HttpStatusCode.Created, (await Generate(entry.Id, new { headline = "Dev", summary = "", bulletIds = Chosen })).StatusCode);
        Assert.True(Directory.Exists(FolderOf(entry.Id)));

        await tracker.Client.DeleteAsync($"/api/tracker/applications/{entry.Id}");

        Assert.False(Directory.Exists(FolderOf(entry.Id)));
    }

    [Fact]
    public async Task The_full_cv_holds_every_bullet()
    {
        var response = await tracker.Client.GetAsync("/api/tracker/cv/full");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Alex Example - CV.docx", response.Content.Headers.ContentDisposition!.ToString());
        var text = TextOf(await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("Mentored two junior developers through code reviews and pairing.", text);
        Assert.Contains("Built a .NET 10 and React dashboard over PostgreSQL, run with .NET Aspire and traced with OpenTelemetry.", text);
    }

    [Fact]
    public async Task An_unknown_entry_is_a_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await tracker.Client.GetAsync($"/api/tracker/applications/{Guid.NewGuid()}/cv")).StatusCode);

    [Fact]
    public async Task The_same_bullet_twice_is_refused()
    {
        var entry = await PasteAsync("Twiceco", "We need C#.");

        var response = await Generate(entry.Id, new { headline = "Dev", summary = "", bulletIds = new[] { "acme-lead", "acme-lead" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("bulletIds", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("field").GetString());
        Assert.False(Directory.Exists(FolderOf(entry.Id)));
    }

    [Fact]
    public async Task Text_saved_on_a_market_entry_wins_over_the_collected_posting()
    {
        var entry = await tracker.PostAsync<NewEntry>("/api/tracker/applications", new { postingId = "inbox:one.txt" });
        var detail = await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{entry.Id}");
        var saved = await tracker.Client.PutAsJsonAsync($"/api/tracker/applications/{entry.Id}/posting-text",
            new { version = detail.Version, postingText = "The full posting: Kubernetes, Kafka and React." });
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);

        var proposal = await tracker.GetAsync<CvProposal>($"/api/tracker/applications/{entry.Id}/cv");

        Assert.Equal("entry", proposal.TextSource);
        Assert.Contains(proposal.PostingSkills, s => s.Skill == "Kubernetes");
        Assert.DoesNotContain(proposal.PostingSkills, s => s.Skill == "SQL Server"); // only in the collected text
    }

    [Fact]
    public async Task A_path_like_company_name_gives_a_safe_file_name()
    {
        var entry = await PasteAsync(@"..\..\Evil/Co: <CV>", "We need C#.");

        var response = await Generate(entry.Id, new { headline = "Dev", summary = "", bulletIds = Chosen });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var made = (await response.Content.ReadFromJsonAsync<GeneratedCv>(TrackerFixture.Json))!;
        Assert.Equal("Alex Example - CV - ....EvilCo CV.docx", made.FileName);
        var file = Assert.Single(Directory.GetFiles(FolderOf(entry.Id)));
        Assert.Equal(FolderOf(entry.Id), Path.GetDirectoryName(file));
        Assert.EndsWith(made.FileName, file);
    }
}
