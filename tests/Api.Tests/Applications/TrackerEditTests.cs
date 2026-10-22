using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Applications;
using Api.Endpoints;
using Api.Tests.TestSupport;

namespace Api.Tests.Applications;

[Collection("tracker")]
[Trait("Category", "Integration")]
public sealed class TrackerEditTests(TrackerFixture tracker)
{
    private Task<ApplicationDetail> Detail(Guid id) => tracker.GetAsync<ApplicationDetail>($"/api/tracker/applications/{id}");

    private static ApplicationEdit EditOf(ApplicationDetail d) => new(d.Version, d.Title, d.Url, d.Location, d.FoundVia, d.WorkModel.ToString(),
        d.OfficeDays, d.TeamSize, d.WorkingHours, d.SalaryPosted, d.SalaryAsked, d.SalaryOffered, d.Notes);

    private static async Task<string?> FieldOf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("field").GetString();
    }

    [Fact]
    public async Task Edits_need_the_current_version()
    {
        var entry = await tracker.PasteAsync("Versionco");
        var detail = await Detail(entry.Id);

        var saved = await tracker.Client.PatchAsJsonAsync($"/api/tracker/applications/{entry.Id}",
            EditOf(detail) with { TeamSize = "8 people", WorkModel = "hybrid", OfficeDays = "3 days" }, TrackerFixture.Json);
        var stale = await tracker.Client.PatchAsJsonAsync($"/api/tracker/applications/{entry.Id}",
            EditOf(detail) with { TeamSize = "9 people" }, TrackerFixture.Json);

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ApiExceptionHandler.StaleEdit, (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
        var after = await Detail(entry.Id);
        Assert.Equal(("8 people", WorkModel.Hybrid, "3 days"), (after.TeamSize, after.WorkModel, after.OfficeDays));
        Assert.NotEqual(detail.Version, after.Version);
    }

    [Fact]
    public async Task Edits_validate_every_field()
    {
        var entry = await tracker.PasteAsync("Validco");
        var detail = await Detail(entry.Id);
        var url = $"/api/tracker/applications/{entry.Id}";

        Assert.Equal("title", await FieldOf(await tracker.Client.PatchAsJsonAsync(url, EditOf(detail) with { Title = " " }, TrackerFixture.Json)));
        Assert.Equal("url", await FieldOf(await tracker.Client.PatchAsJsonAsync(url, EditOf(detail) with { Url = "javascript:alert(1)" }, TrackerFixture.Json)));
        Assert.Equal("workModel", await FieldOf(await tracker.Client.PatchAsJsonAsync(url, EditOf(detail) with { WorkModel = "sometimes" }, TrackerFixture.Json)));
    }

    [Fact]
    public async Task Status_changes_add_to_the_timeline()
    {
        var entry = await tracker.PasteAsync("Statusco");

        var moved = await tracker.Client.PostAsJsonAsync($"/api/tracker/applications/{entry.Id}/status",
            new { status = "Applied", on = "2026-09-30", note = "Sent through Workday" });
        var future = await tracker.Client.PostAsJsonAsync($"/api/tracker/applications/{entry.Id}/status", new { status = "Screening", on = "2026-12-01" });

        Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        Assert.Equal("on", await FieldOf(future));
        var detail = await Detail(entry.Id);
        Assert.Equal(ApplicationStatus.Applied, detail.Status);
        var applied = Assert.Single(detail.Events, e => e.ToStatus == ApplicationStatus.Applied);
        Assert.Equal((ApplicationStatus.Shortlisted, new DateOnly(2026, 9, 30), "Sent through Workday"), (applied.FromStatus!.Value, applied.OnDate, applied.Note));
    }

    [Fact]
    public async Task Follow_ups_can_be_added_completed_and_deleted()
    {
        var entry = await tracker.PasteAsync("Followco");
        var added = await tracker.PostAsync<NewEntry>($"/api/tracker/applications/{entry.Id}/follow-ups", new { action = "Email Sam", dueOn = "2026-10-06" });

        Assert.Equal("dueOn", await FieldOf(await tracker.Client.PostAsJsonAsync($"/api/tracker/applications/{entry.Id}/follow-ups", new { action = "No date" })));

        var done = await tracker.Client.PatchAsJsonAsync($"/api/tracker/applications/{entry.Id}/follow-ups/{added.Id}",
            new { action = "Email Sam", dueOn = "2026-10-06", doneOn = "2026-10-02" });
        Assert.Equal(HttpStatusCode.NoContent, done.StatusCode);
        Assert.Equal(new DateOnly(2026, 10, 2), Assert.Single((await Detail(entry.Id)).FollowUps).DoneOn);
        Assert.DoesNotContain(await tracker.GetAsync<List<UpNextItem>>("/api/tracker/up-next"), i => i.FollowUpId == added.Id);

        var removed = await tracker.Client.DeleteAsync($"/api/tracker/applications/{entry.Id}/follow-ups/{added.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty((await Detail(entry.Id)).FollowUps);
    }

    [Fact]
    public async Task Contacts_validate_email_and_links()
    {
        var entry = await tracker.PasteAsync("Contactco");
        var url = $"/api/tracker/applications/{entry.Id}/contacts";

        Assert.Equal("name", await FieldOf(await tracker.Client.PostAsJsonAsync(url, new { name = " " })));
        Assert.Equal("email", await FieldOf(await tracker.Client.PostAsJsonAsync(url, new { name = "Sam", email = "sam at contactco" })));
        Assert.Equal("linkedIn", await FieldOf(await tracker.Client.PostAsJsonAsync(url, new { name = "Sam", linkedIn = "linkedin.com/in/sam" })));

        var added = await tracker.PostAsync<NewEntry>(url,
            new { name = "Sam Lee", role = "Recruiter", email = "sam@contactco.com", linkedIn = "https://www.linkedin.com/in/samlee" });
        var edited = await tracker.Client.PatchAsJsonAsync($"{url}/{added.Id}", new { name = "Sam Lee", role = "Hiring manager", email = "sam@contactco.com" });

        Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);
        var contact = Assert.Single((await Detail(entry.Id)).Contacts);
        Assert.Equal(("Sam Lee", "Hiring manager", ""), (contact.Name, contact.Role, contact.LinkedIn));
        Assert.Equal(HttpStatusCode.NoContent, (await tracker.Client.DeleteAsync($"{url}/{added.Id}")).StatusCode);
        Assert.Empty((await Detail(entry.Id)).Contacts);
    }

    [Fact]
    public async Task Company_details_are_shared_and_names_stay_unique()
    {
        var first = await tracker.PasteAsync("Northwind Test");
        var second = await tracker.PasteAsync("  northwind   TEST ");
        var other = await tracker.PasteAsync("Otherwind Test");
        var company = (await Detail(first.Id)).Company;

        Assert.Equal(company.Id, (await Detail(second.Id)).Company.Id);
        Assert.Equal(2, company.Applications);

        var saved = await tracker.Client.PatchAsJsonAsync($"/api/tracker/companies/{company.Id}",
            new CompanyEdit(company.Version, "Northwind Test", "https://northwind.example", "Toronto (King St W)", "5,000+", "20 days + 5 personal", "", ""),
            TrackerFixture.Json);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal("20 days + 5 personal", (await Detail(second.Id)).Company.AnnualLeave);

        var otherCompany = (await Detail(other.Id)).Company;
        var clash = await tracker.Client.PatchAsJsonAsync($"/api/tracker/companies/{otherCompany.Id}",
            new CompanyEdit(otherCompany.Version, "NORTHWIND test", "", "", "", "", "", ""), TrackerFixture.Json);
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal(company.Id.ToString(), (await clash.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task Unknown_ids_are_404()
    {
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await tracker.Client.GetAsync($"/api/tracker/applications/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tracker.Client.PostAsJsonAsync($"/api/tracker/applications/{id}/status", new { status = "Applied" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tracker.Client.DeleteAsync($"/api/tracker/applications/{id}/follow-ups/{Guid.NewGuid()}")).StatusCode);
    }
}
