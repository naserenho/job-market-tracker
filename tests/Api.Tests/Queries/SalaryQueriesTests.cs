using Api.Queries;
using Tracker.Interpreting;
using static Api.Tests.TestSupport.Data;

namespace Api.Tests.Queries;

public class SalaryQueriesTests
{
    private static readonly FilterSet All30 = new(new HashSet<RoleFamily> { RoleFamily.Dotnet, RoleFamily.SoftwareEngineer }, null, Where.All, 30);

    [Fact]
    public void View_builds_cells_histogram_and_examples()
    {
        var market = MarketOf([
            P("a", ["C#"], [0], salary: 110_000),
            P("b", ["C#"], [0], salary: 125_000),
            P("c", ["Go"], [0], salary: 175_000, family: RoleFamily.SoftwareEngineer),
            P("d", ["Go"], [0]),
        ], 0);

        var view = SalaryQueries.View(market, All30, null, null, Settings);

        Assert.Equal((4, 3), (view.Postings, view.WithSalary));
        Assert.Equal(new[] { new Bucket(100_000, 120_000, 1), new Bucket(120_000, 140_000, 1), new Bucket(140_000, 160_000, 0), new Bucket(160_000, 180_000, 1) }, view.Histogram);
        Assert.Equal("c", view.Examples[0].Id); // highest salary first
        Assert.Equal(2, view.Cells.Count);
    }

    [Fact]
    public void View_filters_by_skill_and_company()
    {
        var market = MarketOf([P("a", ["C#"], [0], salary: 110_000, company: "Acme"), P("b", ["Go"], [0], salary: 130_000, company: "Globex")], 0);
        Assert.Equal(new[] { "a" }, SalaryQueries.View(market, All30, "C#", null, Settings).Examples.Select(e => e.Id));
        Assert.Equal(new[] { "b" }, SalaryQueries.View(market, All30, null, "globex", Settings).Examples.Select(e => e.Id));
    }

    [Fact]
    public void Unknown_skill_is_a_filter_error() =>
        Assert.Equal("skill", Assert.Throws<FilterException>(() => SalaryQueries.View(MarketOf([], 0), All30, "Cobol", null, Settings)).Parameter);

    [Fact]
    public void No_salaries_gives_an_empty_histogram() =>
        Assert.Empty(SalaryQueries.View(MarketOf([P("a", ["C#"], [0])], 0), All30, null, null, Settings).Histogram);

    [Theory]
    [InlineData("Acme")]
    [InlineData("  acme inc. ")]
    public void The_company_filter_matches_every_spelling(string company)
    {
        var data = MarketOf([P("a", ["C#"], [0], company: "Acme"), P("b", ["C#"], [0], company: "Acme Inc."), P("c", ["C#"], [0], company: "Globex")], 0);

        Assert.Equal(2, SalaryQueries.View(data, All30, null, company, Settings).Postings);
    }
}
