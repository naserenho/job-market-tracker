using Tracker.Reading;

namespace Tracker.Core.Tests.Reading;

public class RawPostingTests
{
    [Fact]
    public void Hourly_salary_annualizes_at_2080_hours()
    {
        var s = new SalaryRange(50, 60, SalaryPeriod.Hour);
        Assert.Equal(104_000m, s.YearlyMin);
        Assert.Equal(124_800m, s.YearlyMax);
        Assert.Equal(114_400m, s.YearlyMidpoint);
        Assert.True(s.IsPlausibleWithin(30_000, 500_000));
    }

    [Theory]
    [InlineData(500, 1000, false)]       // stipend, not a salary
    [InlineData(120_000, 150_000, true)]
    [InlineData(150_000, 120_000, false)] // inverted
    [InlineData(400_000, 900_000, false)] // above the 500k guard
    public void Plausibility_guard(int min, int max, bool plausible) =>
        Assert.Equal(plausible, new SalaryRange(min, max, SalaryPeriod.Year).IsPlausibleWithin(30_000, 500_000));

    [Fact]
    public void Monthly_salary_annualizes_at_twelve_months()
    {
        var s = new SalaryRange(25_000, 35_000, SalaryPeriod.Month);
        Assert.Equal((300_000m, 420_000m), (s.YearlyMin, s.YearlyMax));
        Assert.True(s.IsPlausibleWithin(60_000, 3_000_000));
    }

    [Fact]
    public void Key_combines_source_and_id() =>
        Assert.Equal("lever:abc", new RawPosting("lever", "abc", "r1", "Co", "T", "", "", null, null, "", true, null).Key);
}
