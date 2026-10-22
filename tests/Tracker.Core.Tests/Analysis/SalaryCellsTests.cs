using Tracker.Analysis;
using Tracker.Interpreting;
using Tracker.Reading;

namespace Tracker.Core.Tests.Analysis;

public class SalaryCellsTests
{
    [Fact]
    public void SalaryCells_is_usable_on_its_own()
    {
        var postings = Enumerable.Range(0, 5).Select(i => new AnalysisPosting($"p{i}", "greenhouse", "Acme", "Dev", "", "Toronto",
            RoleFamily.Dotnet, Seniority.Senior, new SalaryRange(100_000 + i * 10_000, 100_000 + i * 10_000, SalaryPeriod.Year),
            true, false, new HashSet<string>(), [])).ToList();

        var cell = Assert.Single(Analyzer.SalaryCells(postings));

        Assert.Equal(120_000m, cell.Median);
    }
}
