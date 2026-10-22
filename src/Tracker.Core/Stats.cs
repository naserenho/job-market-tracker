namespace Tracker;

public static class Stats
{
    // Linear interpolation between closest ranks; `sorted` must be ascending and non-empty.
    public static decimal Percentile(IReadOnlyList<decimal> sorted, double p)
    {
        if (sorted.Count == 0) throw new ArgumentException("No values.", nameof(sorted));
        var rank = (decimal)p * (sorted.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
    }

    public static decimal? Median(IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? null : Percentile(sorted, 0.5);
    }
}
