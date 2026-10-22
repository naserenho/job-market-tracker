using Api.Cv;
using Microsoft.EntityFrameworkCore;

namespace Api.Applications;

public sealed record TrackerStatusView(bool Migrated, DateTimeOffset? LastBackupAt, string? LastBackupError,
    IReadOnlyDictionary<string, int> Counts, int Overdue);

public static class TrackerEndpoints
{
    public static void MapTrackerEndpoints(this IEndpointRouteBuilder app)
    {
        var tracker = app.MapGroup("/api/tracker").AddEndpointFilter<TrackerGate>();

        tracker.MapGet("/status", async (ApplicationsDb db, TrackerMirror mirror, TrackerSchema schema, TimeProvider clock, CancellationToken ct) =>
        {
            var statuses = await db.Applications.AsNoTracking().Select(a => a.Status).ToListAsync(ct);
            var today = Tracking.Today(clock);
            var overdue = await db.FollowUps.AsNoTracking().CountAsync(f => f.DoneOn == null && f.DueOn < today, ct);
            return new TrackerStatusView(schema.Ready, mirror.LastBackupAt, mirror.LastBackupError,
                statuses.GroupBy(s => s).ToDictionary(g => g.Key.ToString(), g => g.Count()), overdue);
        });

        tracker.MapTrackerLists();
        tracker.MapTrackerEdits();
        tracker.MapTrackerDocuments();
        tracker.MapCvEndpoints();
    }
}
