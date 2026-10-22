using Microsoft.EntityFrameworkCore;

namespace Api.Applications;

internal static class Tracking
{
    public const string MissingEntry = "That entry doesn't exist (it may have been deleted).";

    // The owner's calendar date on this PC; follow-ups and timeline dates use it.
    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    // The entry, tracked for changes, or a 404.
    public static async Task<JobApplication> FindEntryAsync(ApplicationsDb db, Guid id, CancellationToken ct) =>
        await db.Applications.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new TrackerNotFoundException(MissingEntry);

    // Saves, counts the change, then refreshes the JSON copy. The copy is written even if the caller has gone away:
    // the database already holds the change.
    public static async Task CommitAsync(ApplicationsDb db, TrackerMirror mirror, string operation, CancellationToken ct)
    {
        using var activity = TrackerTelemetry.Source.StartActivity(operation);
        await db.SaveChangesAsync(ct);
        TrackerTelemetry.Changes.Add(1, new KeyValuePair<string, object?>("operation", operation));
        await mirror.WriteAsync(db);
    }
}
