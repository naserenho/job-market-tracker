using System.Text.Json;
using Api.Applications;
using Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Tracker.TestSupport;

namespace Api.Tests.Applications;

// Two requests can both pass the "is it there already?" check before either saves. Each test plays the loser:
// "mine" prepares its save, "theirs" commits first.
[Collection("tracker")]
[Trait("Category", "Integration")]
public sealed class TrackerRaceTests(TrackerFixture tracker) : IDisposable
{
    private readonly TempDir _root = new();

    private TrackerMirror Mirror() => new(Data.Settings with { DataRoot = _root.Path }, tracker.Clock, NullLogger<TrackerMirror>.Instance);

    private JobApplication Entry(Company company, string? postingId) =>
        TrackerListEndpoints.CreateEntry(company, "Developer", "", "Toronto, ON", "C#", ApplicationStatus.Shortlisted, EntryOrigin.Pasted,
            postingId, tracker.Clock, "test");

    [Fact]
    public async Task Losing_the_race_to_create_a_company_uses_the_winners_company()
    {
        await tracker.GetAsync<JsonElement>("/api/tracker/status");
        await using var mine = tracker.OpenDb();
        await using var theirs = tracker.OpenDb();
        var entry = Entry(await TrackerListEndpoints.FindOrCreateCompanyAsync(mine, "Raceco", tracker.Clock, default), null);
        mine.Applications.Add(entry);
        var winner = await TrackerListEndpoints.FindOrCreateCompanyAsync(theirs, "RaceCo", tracker.Clock, default);
        await theirs.SaveChangesAsync();

        await TrackerListEndpoints.CommitNewEntryAsync(mine, Mirror(), entry, "paste", default);

        await using var check = tracker.OpenDb();
        Assert.Equal(winner.Id, (await check.Applications.SingleAsync(a => a.Id == entry.Id)).CompanyId);
        Assert.Equal(1, await check.Companies.CountAsync(c => c.NameKey == "raceco"));
    }

    [Fact]
    public async Task Losing_the_race_to_shortlist_a_posting_is_a_conflict_with_the_winners_id()
    {
        await tracker.GetAsync<JsonElement>("/api/tracker/status");
        await using var mine = tracker.OpenDb();
        await using var theirs = tracker.OpenDb();
        var theirEntry = Entry(await TrackerListEndpoints.FindOrCreateCompanyAsync(theirs, "Shortco", tracker.Clock, default), "race:1");
        var myEntry = Entry(await TrackerListEndpoints.FindOrCreateCompanyAsync(mine, "Otherco", tracker.Clock, default), "race:1");
        mine.Applications.Add(myEntry);
        theirs.Applications.Add(theirEntry);
        await theirs.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<TrackerConflictException>(
            () => TrackerListEndpoints.CommitNewEntryAsync(mine, Mirror(), myEntry, "shortlist", default));

        Assert.Equal(theirEntry.Id, conflict.ExistingId);
        await using var check = tracker.OpenDb();
        Assert.False(await check.Companies.AnyAsync(c => c.NameKey == "otherco")); // nothing of the losing save was kept
    }

    // The retry is for a lost company race only: when the retried save hits the posting index too, that is the 409.
    [Fact]
    public async Task Losing_both_races_at_once_ends_in_the_posting_conflict()
    {
        await tracker.GetAsync<JsonElement>("/api/tracker/status");
        await using var mine = tracker.OpenDb();
        await using var theirs = tracker.OpenDb();
        var myEntry = Entry(await TrackerListEndpoints.FindOrCreateCompanyAsync(mine, "Bothco", tracker.Clock, default), "race:2");
        mine.Applications.Add(myEntry);
        var theirEntry = Entry(await TrackerListEndpoints.FindOrCreateCompanyAsync(theirs, "Bothco", tracker.Clock, default), "race:2");
        theirs.Applications.Add(theirEntry);
        await theirs.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<TrackerConflictException>(
            () => TrackerListEndpoints.CommitNewEntryAsync(mine, Mirror(), myEntry, "shortlist", default));

        Assert.Equal(theirEntry.Id, conflict.ExistingId);
    }

    public void Dispose() => _root.Dispose();
}
