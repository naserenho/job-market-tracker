using Api.Queries;
using Tracker.Analysis;
using Tracker.Interpreting;
using static Api.Tests.TestSupport.Data;

namespace Api.Tests.Queries;

public class SkillQueriesTests
{
    private static readonly FilterSet All30 = new(new HashSet<RoleFamily> { RoleFamily.Dotnet }, null, Where.All, 30);

    [Fact]
    public void List_shares_use_the_chosen_text_kind_and_show_levels()
    {
        var market = MarketOf([
            P("a", ["C#", "Docker"], [0]),
            P("b", ["C#"], [0]),
            P("s", ["Docker"], [0], fullText: false),
        ], 0);

        var full = SkillQueries.List(market, All30, TextKind.Full, null, Settings);
        var csharp = full.Skills.Single(s => s.Skill == "C#");
        Assert.Equal(2, full.Postings);
        Assert.Equal((1.0, 2, SkillLevel.Strong), (csharp.Share, csharp.Count, csharp.Level));
        Assert.Equal(0.5, full.Skills.Single(s => s.Skill == "Docker").Share);
        Assert.Equal(1.0, SkillQueries.List(market, All30, TextKind.Snippet, null, Settings).Skills.Single(s => s.Skill == "Docker").Share);
        Assert.Equal("C#", full.Skills[0].Skill); // highest share first
    }

    [Fact]
    public void List_compares_with_the_previous_window_when_history_covers_it()
    {
        var market = MarketOf([P("old", ["Docker"], [-40]), P("new", ["C#"], [0])], -60, -40, 0);

        var list = SkillQueries.List(market, All30, TextKind.Full, null, Settings);

        var docker = list.Skills.Single(s => s.Skill == "Docker");
        Assert.Equal((0.0, 1.0, -1.0), (docker.Share, docker.PreviousShare!.Value, docker.Change!.Value));
        Assert.Null(SkillQueries.List(MarketOf([P("new", ["C#"], [0])], 0), All30, TextKind.Full, null, Settings).Skills[0].PreviousShare);
    }

    [Fact]
    public void No_change_is_shown_while_history_only_partly_covers_the_previous_window()
    {
        // Window 30 days: the previous window is days -60 to -30, but collection only started on day -40.
        var market = MarketOf([P("old", ["Docker"], [-40]), P("new", ["C#"], [0])], -40, 0);

        var list = SkillQueries.List(market, All30, TextKind.Full, null, Settings);

        Assert.All(list.Skills, s => Assert.Null(s.PreviousShare));
    }

    [Fact]
    public void List_filters_by_category()
    {
        var list = SkillQueries.List(MarketOf([P("a", ["C#", "Docker"], [0])], 0), All30, TextKind.Full, "devops", Settings);
        Assert.All(list.Skills, s => Assert.Equal("devops", s.Category));
    }

    [Fact]
    public void Detail_compares_salary_with_and_without_the_skill_and_lists_companions()
    {
        var postings = Enumerable.Range(0, 5).Select(i => P($"k{i}", ["Kubernetes", "Go"], [0], salary: 150_000, company: i < 3 ? "Acme" : "Globex"))
            .Concat(Enumerable.Range(0, 5).Select(i => P($"n{i}", ["C#"], [0], salary: 110_000)))
            .ToList();

        var detail = SkillQueries.Detail(MarketOf(postings, 0), All30, TextKind.Full, "Kubernetes", Settings)!;

        Assert.Equal((5, 10, 0.5), (detail.Count, detail.Postings, detail.Share));
        Assert.Equal(new SalaryComparison(150_000m, 5, 110_000m, 5), detail.Salary);
        Assert.Equal(new NamedCount("Go", 5), detail.CoSkills[0]);
        Assert.Equal(new NamedCount("Acme", 3), detail.Companies[0]);
        Assert.Equal(5, detail.Examples.Count);
        Assert.Equal(SkillLevel.None, detail.Level);
    }

    [Fact]
    public void Detail_hides_medians_below_five_and_is_null_for_unknown_skills()
    {
        var detail = SkillQueries.Detail(MarketOf([P("a", ["C#"], [0], salary: 120_000)], 0), All30, TextKind.Full, "C#", Settings)!;
        Assert.Equal(new SalaryComparison(null, 1, null, 0), detail.Salary);
        Assert.Null(SkillQueries.Detail(MarketOf([], 0), All30, TextKind.Full, "Cobol", Settings));
    }

    [Fact]
    public void Nothing_matching_gives_zero_shares_not_errors()
    {
        var list = SkillQueries.List(MarketOf([], 0), All30, TextKind.Full, null, Settings);
        Assert.Equal(0, list.Postings);
        Assert.All(list.Skills, s => Assert.Equal(0, s.Share));
    }

    [Fact]
    public void Detail_groups_company_spellings()
    {
        var data = MarketOf([P("a", ["C#"], [0], company: "Acme"), P("b", ["C#"], [0], company: "Acme Inc."), P("c", ["C#"], [0], company: "Acme Inc.")], 0);

        Assert.Equal(new[] { new NamedCount("Acme Inc.", 3) }, SkillQueries.Detail(data, All30, TextKind.Full, "C#", Settings)!.Companies);
    }
}
