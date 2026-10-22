using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Queries;
using Api.Tests.TestSupport;
using Tracker.Analysis;
using Tracker.Data;
using Tracker.TestSupport;

namespace Api.Tests.Endpoints;

[Collection("api")]
[Trait("Category", "Integration")]
public sealed class EndpointTests(ApiFixture api)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private async Task<T> GetAsync<T>(string url)
    {
        var response = await api.Client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    [Fact]
    public async Task Meta_describes_the_rebuilt_archive()
    {
        var meta = await GetAsync<MetaView>("/api/meta");

        Assert.Equal((FixtureArchive.Run3, 3, 30, false), (meta.LatestRunId, meta.Runs, meta.HistoryDays, meta.Stale));
        Assert.Contains("Acme", meta.Companies);
        Assert.Contains("Dotnet", meta.TargetFamilies);
    }

    [Fact]
    public async Task Overview_agrees_with_the_report_analyzer_for_the_default_filters()
    {
        await using var db = new TrackerDb(TrackerDbFactory.Options(api.ConnectionString));
        var (postings, runs, jobBank) = await AnalysisLoader.LoadAsync(db, RepoConfig.Toronto, CancellationToken.None);
        var report = new Analyzer(Data.Settings.Profile, Data.Settings.Catalog, 30).Build(postings, runs, jobBank, [], DateTimeOffset.Now);

        var model = (await GetAsync<JsonElement>("/api/overview")).GetProperty("report");

        Assert.Equal(report.Snapshot.InWindow, model.GetProperty("snapshot").GetProperty("inWindow").GetInt32());
        Assert.Equal(report.TopSkills.Select(s => s.Skill), model.GetProperty("topSkills").EnumerateArray().Select(s => s.GetProperty("skill").GetString()!));
        Assert.Equal(report.Gaps.Select(s => s.Skill), model.GetProperty("gaps").EnumerateArray().Select(s => s.GetProperty("skill").GetString()!));
    }

    [Fact]
    public async Task Postings_filter_by_place_and_page()
    {
        var remote = await GetAsync<PostingPage>("/api/postings?where=remote&window=all");
        Assert.Equal("ashby:S1", Assert.Single(remote.Items).Id);

        var page = await GetAsync<PostingPage>("/api/postings?families=dotnet,fullstack,software_engineer,lead_manager,other&window=all&pageSize=2&page=2");
        Assert.Equal((6, 2, 2), (page.Total, page.Page, page.Items.Count));
    }

    [Theory]
    [InlineData("C%23", "C#")]
    [InlineData(".NET", ".NET")]
    [InlineData("CI%2FCD", "CI/CD")]
    [InlineData("Estimation%20%26%20Planning", "Estimation & Planning")]
    [InlineData("OAuth%20%2F%20OIDC", "OAuth / OIDC")]
    public async Task Skill_detail_round_trips_names_that_need_encoding(string path, string skill) =>
        Assert.Equal(skill, (await GetAsync<SkillDetail>($"/api/skills/{path}?window=all")).Skill);

    [Fact]
    public async Task Unknown_skill_is_404_and_bad_filters_are_400_naming_the_parameter()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync("/api/skills/Cobol")).StatusCode);

        var bad = await api.Client.GetAsync("/api/postings?window=45");
        var problem = await bad.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("window", problem.GetProperty("parameter").GetString());
        Assert.Contains("45", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Skills_and_salaries_answer_with_camel_case_and_enum_names()
    {
        var skills = await GetAsync<JsonElement>("/api/skills?window=all&text=all");
        Assert.Contains(skills.GetProperty("skills").EnumerateArray(), s => s.GetProperty("skill").GetString() == "C#" && s.GetProperty("count").GetInt32() > 0);

        var salaries = await api.Client.GetStringAsync("/api/salaries?window=all");
        Assert.Contains("\"withSalary\":", salaries);
        Assert.Contains("\"seniority\":\"Senior\"", salaries);
    }

    [Fact]
    public async Task A_filter_that_matches_nothing_gives_empty_answers()
    {
        const string none = "where=remote&seniority=Junior&window=30";
        Assert.Equal(0, (await GetAsync<PostingPage>($"/api/postings?{none}")).Total);
        Assert.Equal(0, (await GetAsync<SkillList>($"/api/skills?{none}")).Postings);
        Assert.Empty((await GetAsync<SalaryView>($"/api/salaries?{none}")).Histogram);
        Assert.Equal(0, (await GetAsync<JsonElement>($"/api/overview?{none}")).GetProperty("report").GetProperty("snapshot").GetProperty("inWindow").GetInt32());
    }

    [Fact]
    public async Task Health_checks_the_database() =>
        Assert.Equal("Healthy", await api.Client.GetStringAsync("/health"));

    [Fact]
    public async Task An_unknown_skill_filter_is_a_400_on_postings_and_salaries()
    {
        foreach (var path in new[] { "/api/postings?skill=Cobol", "/api/salaries?skill=Cobol" })
        {
            var response = await api.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("skill", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("parameter").GetString());
        }
    }

    [Fact]
    public async Task Meta_names_the_region_and_old_gta_links_still_filter()
    {
        var meta = await GetAsync<MetaView>("/api/meta");
        var gta = await GetAsync<PostingPage>("/api/postings?where=gta");
        var local = await GetAsync<PostingPage>("/api/postings?where=local");

        Assert.Equal(("GTA", "Remote (Canada)"), (meta.Region.LocalLabel, meta.Region.RemoteLabel));
        Assert.Equal(local.Total, gta.Total);
        Assert.True(local.Total > 0);
    }

    // Tracker:Region picks another region without editing config/sources.json.
    [Fact]
    public async Task The_region_can_be_chosen_in_settings()
    {
        await using var factory = new ApiFactory(api.ConnectionString, Path.GetTempPath())
            .WithWebHostBuilder(b => b.UseSetting("Tracker:Region", "abu-dhabi"));

        var meta = await factory.CreateClient().GetFromJsonAsync<MetaView>("/api/meta", Json);

        Assert.Equal(("Abu Dhabi Job Market", "AED", "en-AE"), (meta!.Region.Title, meta.Region.Currency, meta.Region.Culture));
    }

    // Switching region without running the Processor shows the old region's numbers under the new labels: say so.
    [Fact]
    public async Task Data_built_for_another_region_is_flagged()
    {
        using var archive = new TempDir();
        FixtureArchive.Write(archive.Path); // the same Toronto runs the shared database was built from
        await using var factory = new ApiFactory(api.ConnectionString, archive.Path)
            .WithWebHostBuilder(b => b.UseSetting("Tracker:Region", "abu-dhabi"));

        var overview = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/overview", Json);

        var warnings = overview.GetProperty("report").GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToList();
        Assert.Contains(warnings, w => w!.Contains("built for region 'toronto'") && w.Contains("abu-dhabi"));
    }

    [Fact]
    public async Task Data_built_for_the_active_region_is_not_flagged()
    {
        var overview = await GetAsync<JsonElement>("/api/overview");

        Assert.DoesNotContain(overview.GetProperty("report").GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("built for region"));
    }
}
