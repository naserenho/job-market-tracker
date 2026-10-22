using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Api.Applications;

public static class TrackerTelemetry
{
    public const string Name = "Tracker.Applications";
    public static readonly ActivitySource Source = new(Name);
    private static readonly Meter Meter = new(Name);
    public static readonly Counter<long> Changes = Meter.CreateCounter<long>("tracker.applications.changes",
        description: "Changes saved to the application tracker.");
}
