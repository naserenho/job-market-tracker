using Api.Queries;
using Tracker.Interpreting;
using static Api.Tests.TestSupport.Data;

namespace Api.Tests.Queries;

public class MetaQueriesTests
{
    [Fact]
    public void Meta_describes_runs_and_filter_options()
    {
        var meta = MetaQueries.Meta(MarketOf([P("a", ["C#"], [0], company: "Acme"), P("b", ["C#"], [0], company: "Acme"), P("c", ["C#"], [0], company: "Globex")], -10, 0), Settings, stale: true);

        Assert.Equal(("r0", 2, 10, true), (meta.LatestRunId, meta.Runs, meta.HistoryDays, meta.Stale));
        Assert.Contains("SoftwareEngineer", meta.Families);
        Assert.Contains("StaffPrincipal", meta.Seniorities);
        Assert.Contains("devops", meta.Categories);
        Assert.Equal(new[] { "Acme", "Globex" }, meta.Companies);
        Assert.NotEmpty(meta.TargetFamilies);
    }

    [Fact]
    public void Meta_of_an_empty_market_has_no_latest_run()
    {
        var meta = MetaQueries.Meta(Api.Market.MarketSnapshot.Empty(T0), Settings, stale: false);
        Assert.Equal(((string?)null, 0, 0), (meta.LatestRunId, meta.Runs, meta.HistoryDays));
    }

    [Fact]
    public void Overview_uses_the_filtered_families_and_window()
    {
        var market = MarketOf([P("a", ["C#"], [0]), P("b", ["Go"], [0], family: RoleFamily.SoftwareEngineer)], 0);
        var f = new FilterSet(new HashSet<RoleFamily> { RoleFamily.SoftwareEngineer }, null, Where.All, 30);

        var model = MetaQueries.Overview(market, f, Settings, Day(1))!;

        Assert.Equal(1, model.Snapshot.InWindow);
        Assert.Equal("Go", model.TopSkills.Single().Skill);
        Assert.Null(MetaQueries.Overview(Api.Market.MarketSnapshot.Empty(T0), f, Settings, Day(1)));
    }

    [Fact]
    public void Company_variants_are_listed_once()
    {
        var data = MarketOf([P("a", ["C#"], [0], company: "Acme"), P("b", ["C#"], [0], company: "Acme Inc."), P("c", ["C#"], [0], company: "Acme Inc.")], 0);

        Assert.Equal(new[] { "Acme Inc." }, MetaQueries.Meta(data, Settings, stale: false).Companies);
    }

    [Fact]
    public void Meta_carries_the_active_region()
    {
        var region = MetaQueries.Meta(MarketOf([], 0), Settings, stale: false).Region;

        Assert.Equal(new RegionView("toronto", "GTA Job Market", "GTA", "Remote (Canada)", "CAD", "$", "en-CA", "America/Toronto"), region);
    }
}
