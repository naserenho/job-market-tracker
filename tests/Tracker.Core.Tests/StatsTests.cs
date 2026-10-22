namespace Tracker.Core.Tests;

public class StatsTests
{
    [Fact]
    public void Percentile_interpolates()
    {
        decimal[] sorted = [100, 200, 300, 400];
        Assert.Equal(250m, Stats.Percentile(sorted, 0.5));
        Assert.Equal(175m, Stats.Percentile(sorted, 0.25));
        Assert.Equal(100m, Stats.Percentile(sorted, 0));
        Assert.Equal(400m, Stats.Percentile(sorted, 1));
    }

    [Fact]
    public void Median_of_nothing_is_null()
    {
        Assert.Null(Stats.Median([]));
        Assert.Equal(3m, Stats.Median([5, 1, 3]));
    }
}
