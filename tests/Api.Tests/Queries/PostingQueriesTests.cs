using Api.Queries;
using Tracker.Interpreting;
using static Api.Tests.TestSupport.Data;

namespace Api.Tests.Queries;

public class PostingQueriesTests
{
    private static readonly FilterSet All30 = new(new HashSet<RoleFamily> { RoleFamily.Dotnet }, null, Where.All, 30);

    private static readonly Api.Market.MarketSnapshot Market3 = MarketOf([
        P("old", ["C#", ".NET"], [-5], title: "Senior .NET Developer", company: "Acme", salary: 120_000),
        P("new", ["Kubernetes", "Go"], [0], title: "Platform Engineer", company: "Globex", salary: 160_000),
        P("mid", ["C#", "Kubernetes"], [-2], title: "Full Stack Dev", company: "Acme"),
    ], -5, -2, 0);

    [Fact]
    public void Match_is_the_share_of_skills_at_working_level_or_better() =>
        Assert.Equal(0.5, PostingQueries.Match(P("x", ["C#", "Kubernetes"], [0]), Settings.Profile)); // C# strong, Kubernetes none

    [Theory]
    [InlineData(PostingSort.Newest, "new,mid,old")]
    [InlineData(PostingSort.Match, "old,mid,new")]
    [InlineData(PostingSort.Salary, "new,old,mid")]
    public void Sorts(PostingSort sort, string expected) =>
        Assert.Equal(expected, string.Join(",", PostingQueries.Page(Market3, All30, null, null, null, sort, 1, 25, Settings.Profile).Items.Select(i => i.Id)));

    [Fact]
    public void Search_skill_and_company_filters_combine()
    {
        Assert.Equal(new[] { "old" }, PostingQueries.Page(Market3, All30, ".net dev", null, null, PostingSort.Newest, 1, 25, Settings.Profile).Items.Select(i => i.Id));
        Assert.Equal(new[] { "mid" }, PostingQueries.Page(Market3, All30, null, "Kubernetes", "acme", PostingSort.Newest, 1, 25, Settings.Profile).Items.Select(i => i.Id));
    }

    [Fact]
    public void Paging_reports_the_total()
    {
        var page = PostingQueries.Page(Market3, All30, null, null, null, PostingSort.Newest, 2, 2, Settings.Profile);
        Assert.Equal((3, 2, 2), (page.Total, page.Page, page.PageSize));
        Assert.Equal(new[] { "old" }, page.Items.Select(i => i.Id));
    }

    [Fact]
    public void Match_sort_puts_postings_with_too_few_skills_to_judge_last()
    {
        var market = MarketOf([
            P("thin", ["C#"], [0]),                                        // 100%, but one skill says little
            P("rich", ["C#", ".NET", "SQL Server", "Kubernetes"], [-1]),   // 75% of four skills
        ], -1, 0);

        var ids = PostingQueries.Page(market, All30, null, null, null, PostingSort.Match, 1, 25, Settings.Profile).Items.Select(i => i.Id);

        Assert.Equal(new[] { "rich", "thin" }, ids);
    }

    [Fact]
    public void Row_carries_salary_first_seen_and_sorted_skills()
    {
        var row = PostingQueries.ToRow(P("x", ["Go", "C#"], [-3, 0], salary: 100_000), Settings.Profile);
        Assert.Equal((100_000m, 100_000m, "Year"), (row.SalaryMin!.Value, row.SalaryMax!.Value, row.SalaryPeriod));
        Assert.Equal(Day(-3), row.FirstSeen);
        Assert.Equal(new[] { "C#", "Go" }, row.Skills);
    }

    [Theory]
    [InlineData("Acme")]
    [InlineData("  acme inc. ")]
    public void The_company_filter_matches_every_spelling(string company)
    {
        var data = MarketOf([P("a", ["C#"], [0], company: "Acme"), P("b", ["C#"], [0], company: "Acme Inc."), P("c", ["C#"], [0], company: "Globex")], 0);

        Assert.Equal(2, PostingQueries.Page(data, All30, null, null, company, PostingSort.Newest, 1, 25, Settings.Profile).Total);
    }
}
