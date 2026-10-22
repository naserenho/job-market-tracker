using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Applications;
using Api.Tests.TestSupport;

namespace Api.Tests.Applications;

[Collection("tracker")]
[Trait("Category", "Integration")]
public sealed class TrackerListTests(TrackerFixture tracker)
{
    [Fact]
    public async Task Shortlisting_a_market_posting_copies_it_and_starts_the_timeline()
    {
        var created = await tracker.PostAsync<NewEntry>("/api/tracker/applications", new { postingId = "greenhouse:101" });

        var detail = await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{created.Id}");
        Assert.Equal(("Senior Full Stack Developer", "Acme", ApplicationStatus.Shortlisted, EntryOrigin.Market),
            (detail.Title, detail.Company.Name, detail.Status, detail.Origin));
        var start = Assert.Single(detail.Events);
        Assert.Equal(((ApplicationStatus?)null, ApplicationStatus.Shortlisted, new DateOnly(2026, 10, 2)), (start.FromStatus, start.ToStatus, start.OnDate));
        Assert.NotNull(detail.Market);
        Assert.True(detail.Market!.Live);
        Assert.NotEmpty(detail.PostingText);
        var index = await tracker.GetAsync<Dictionary<string, IndexEntry>>("/api/tracker/index");
        Assert.Equal(created.Id, index["greenhouse:101"].Id);
    }

    [Fact]
    public async Task Shortlisting_twice_returns_the_existing_entry()
    {
        var first = await tracker.PostAsync<NewEntry>("/api/tracker/applications", new { postingId = "ashby:S1" });

        var again = await tracker.Client.PostAsJsonAsync("/api/tracker/applications", new { postingId = "ashby:S1" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(first.Id.ToString(), (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task Unknown_postings_are_404_and_writes_without_the_header_are_403()
    {
        Assert.Equal(HttpStatusCode.NotFound,
            (await tracker.Client.PostAsJsonAsync("/api/tracker/applications", new { postingId = "nope:1" })).StatusCode);
        using var outsider = tracker.CreateClient(withHeader: false);

        var refused = await outsider.PostAsJsonAsync("/api/tracker/applications", new { postingId = "lever:L1" });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("must come from the dashboard", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_pasted_job_goes_to_the_inbox_and_links_to_the_market_after_the_next_rebuild()
    {
        var created = await tracker.PostAsync<NewEntry>("/api/tracker/applications/paste", new
        {
            title = "Senior .NET Developer\n(Payments)", company = "Bank of Testing", url = "https://careers.example.com/42",
            location = "Toronto, ON", postingText = "We build payments in C#, Azure and SQL Server.", status = "Applied",
        });

        var before = await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{created.Id}");
        Assert.Equal(("Senior .NET Developer (Payments)", ApplicationStatus.Applied, EntryOrigin.Pasted), (before.Title, before.Status, before.Origin));
        Assert.StartsWith("inbox:", before.PostingId);
        Assert.Null(before.Market);
        var file = Path.Combine(tracker.DataRoot, "inbox", before.PostingId!["inbox:".Length..]);
        Assert.Contains("\nRegion: toronto\n", File.ReadAllText(file));
        Assert.Contains("Company: Bank of Testing\nTitle: Senior .NET Developer (Payments)\n", File.ReadAllText(file));

        await tracker.RebuildAsync();

        var after = await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{created.Id}");
        Assert.NotNull(after.Market);
        Assert.True(after.Market!.Live);
        Assert.Contains("C#", after.Market.Skills);
    }

    [Fact]
    public async Task The_list_sorts_by_next_follow_up_and_filters_by_status_and_text()
    {
        var applied = await tracker.PasteAsync("Sortco Alpha", "Applied");
        var waiting = await tracker.PasteAsync("Sortco Beta");
        await using (var db = tracker.OpenDb())
        {
            db.FollowUps.Add(new FollowUp { Id = Guid.NewGuid(), ApplicationId = waiting.Id, Action = "Call the recruiter",
                DueOn = new DateOnly(2026, 10, 5), CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var byNext = await tracker.GetAsync<List<ApplicationRow>>("/api/tracker/applications?q=sortco");
        var onlyApplied = await tracker.GetAsync<List<ApplicationRow>>("/api/tracker/applications?q=SORTCO&status=applied");
        var byCompany = await tracker.GetAsync<List<ApplicationRow>>("/api/tracker/applications?q=sortco&sort=company");

        Assert.Equal(new[] { waiting.Id, applied.Id }, byNext.Select(r => r.Id));
        Assert.Equal("Call the recruiter", byNext[0].Next!.Action);
        Assert.Equal(applied.Id, Assert.Single(onlyApplied).Id);
        Assert.Equal(new[] { applied.Id, waiting.Id }, byCompany.Select(r => r.Id));
    }

    [Fact]
    public async Task Up_next_lists_overdue_and_coming_follow_ups_only()
    {
        var entry = await tracker.PasteAsync("Upnext Co");
        await using (var db = tracker.OpenDb())
        {
            var now = DateTimeOffset.UtcNow;
            db.FollowUps.AddRange(
                new FollowUp { Id = Guid.NewGuid(), ApplicationId = entry.Id, Action = "Overdue call", DueOn = new DateOnly(2026, 9, 30), CreatedAt = now },
                new FollowUp { Id = Guid.NewGuid(), ApplicationId = entry.Id, Action = "Soon call", DueOn = new DateOnly(2026, 10, 5), CreatedAt = now },
                new FollowUp { Id = Guid.NewGuid(), ApplicationId = entry.Id, Action = "Later call", DueOn = new DateOnly(2026, 10, 30), CreatedAt = now },
                new FollowUp { Id = Guid.NewGuid(), ApplicationId = entry.Id, Action = "Done call", DueOn = new DateOnly(2026, 10, 1),
                    DoneOn = new DateOnly(2026, 10, 1), CreatedAt = now });
            await db.SaveChangesAsync();
        }

        var items = (await tracker.GetAsync<List<UpNextItem>>("/api/tracker/up-next")).Where(i => i.ApplicationId == entry.Id).ToList();

        Assert.Equal(new[] { ("Overdue call", true), ("Soon call", false) }, items.Select(i => (i.Action, i.Overdue)));
        Assert.Equal("Upnext Co", items[0].Company);
        Assert.True((await tracker.GetAsync<TrackerStatusView>("/api/tracker/status")).Overdue >= 1);
    }

    [Theory]
    [InlineData("status=wizard", "status")]
    [InlineData("sort=salary", "sort")]
    public async Task Bad_list_parameters_name_the_field(string query, string field)
    {
        var response = await tracker.Client.GetAsync($"/api/tracker/applications?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(field, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("field").GetString());
    }

    [Fact]
    public async Task Paste_validation_names_the_field_and_leaves_no_inbox_file()
    {
        var inbox = Path.Combine(tracker.DataRoot, "inbox");
        var before = Directory.GetFiles(inbox).Length;

        var response = await tracker.Client.PostAsJsonAsync("/api/tracker/applications/paste",
            new { title = "Dev", company = "Nofile Co", postingText = "   ", url = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("postingText", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("field").GetString());
        Assert.Equal(before, Directory.GetFiles(inbox).Length);
    }

    [Fact]
    public async Task The_backup_copy_follows_each_change()
    {
        var created = await tracker.PasteAsync("Mirror Follow Co");

        var copy = MirrorStore.Read(Path.Combine(tracker.DataRoot, "tracker", "tracker.json"))!;

        Assert.Contains(copy.Applications, a => a.Id == created.Id);
        Assert.Contains(copy.Companies, c => c.Name == "Mirror Follow Co");
    }

    [Fact]
    public async Task Companies_are_listed_for_suggestions()
    {
        await tracker.PasteAsync("Suggest Co");

        Assert.Contains(await tracker.GetAsync<List<CompanyOption>>("/api/tracker/companies"), c => c.Name == "Suggest Co");
    }

    [Fact]
    public async Task A_nul_character_in_a_pasted_title_is_a_400()
    {
        var response = await tracker.Client.PostAsJsonAsync("/api/tracker/applications/paste", new
        {
            title = "Dev\0eloper", company = "Nulco", url = "", location = "Toronto, ON", postingText = "C#", status = "Shortlisted",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("title", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("field").GetString());
    }

    [Fact]
    public async Task The_entry_says_where_its_pasted_job_stands_in_the_market_data()
    {
        var counted = await tracker.PasteAsync("Stateco");
        var outside = await tracker.PostAsync<NewEntry>("/api/tracker/applications/paste", new
        {
            title = "Developer", company = "Farco", url = "", location = "Bellevue, Washington", postingText = "C#", status = "Shortlisted",
        });

        Assert.Equal(MarketState.Pending, (await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{counted.Id}")).MarketState);
        await tracker.RebuildAsync();

        Assert.Equal(MarketState.Counted, (await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{counted.Id}")).MarketState);
        Assert.Equal(MarketState.OutOfScope, (await tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{outside.Id}")).MarketState);
    }
}
