using System.Web;
using Archiver.Configuration;
using Archiver.Sources;

namespace Archiver.Tests.Sources;

public class AdzunaQueryPlanTests
{
    private static AdzunaOptions Options() => new()
    {
        AppId = "ID1", AppKey = "KEY1", Country = "ca", Where = "Toronto", DistanceKm = 50, Terms = [".net developer", "tech lead"], MaxPages = 3,
    };

    [Fact]
    public void Plan_puts_aggregates_first_then_searches_page_by_page()
    {
        var plan = AdzunaQueryPlan.Build(Options());

        Assert.Equal(2 + 2 + 3 * 2 * 2, plan.Count);
        Assert.All(plan.Take(2), r => Assert.Equal(AdzunaKind.Histogram, r.Kind));
        Assert.All(plan.Skip(2).Take(2), r => Assert.Equal(AdzunaKind.TopCompanies, r.Kind));
        var searches = plan.Skip(4).ToList();
        Assert.All(searches.Take(4), r => Assert.Equal(1, r.Page)); // every term and scope gets page 1 before any page 2
        Assert.Equal(new AdzunaRequest(AdzunaKind.Search, ".net developer", "gta", 1), searches[0]);
        Assert.Equal(new AdzunaRequest(AdzunaKind.Search, ".net developer", "remote", 1), searches[1]);
    }

    [Fact]
    public void Gta_search_url_has_location_distance_and_paging()
    {
        var url = new Uri(AdzunaQueryPlan.Url(Options(), new AdzunaRequest(AdzunaKind.Search, ".net developer", "gta", 2)));
        var q = HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("/v1/api/jobs/ca/search/2", url.AbsolutePath);
        Assert.Equal(".net developer", q["what"]);
        Assert.Equal("Toronto", q["where"]);
        Assert.Equal("50", q["distance"]);
        Assert.Equal("50", q["results_per_page"]);
        Assert.Equal("ID1", q["app_id"]);
        Assert.Equal("KEY1", q["app_key"]);
    }

    // A province-wide region (Ontario) leaves DistanceKm out: Adzuna searches the whole place.
    [Fact]
    public void Url_leaves_out_distance_when_not_set()
    {
        var o = Options();
        o.Where = "Ontario";
        o.DistanceKm = 0;
        var q = HttpUtility.ParseQueryString(new Uri(AdzunaQueryPlan.Url(o, new AdzunaRequest(AdzunaKind.Search, ".net developer", "gta", 1))).Query);

        Assert.Equal("Ontario", q["where"]);
        Assert.Null(q["distance"]);
    }

    [Fact]
    public void Remote_search_url_adds_remote_and_has_no_location()
    {
        var url = new Uri(AdzunaQueryPlan.Url(Options(), new AdzunaRequest(AdzunaKind.Search, "c# developer", "remote", 1)));
        var q = HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("c# developer remote", q["what"]);
        Assert.Null(q["where"]);
        Assert.Null(q["distance"]);
    }

    [Fact]
    public void Aggregate_urls_hit_their_endpoints_for_the_gta()
    {
        var hist = new Uri(AdzunaQueryPlan.Url(Options(), new AdzunaRequest(AdzunaKind.Histogram, "tech lead", "gta", 0)));
        var top = new Uri(AdzunaQueryPlan.Url(Options(), new AdzunaRequest(AdzunaKind.TopCompanies, "tech lead", "gta", 0)));

        Assert.Equal("/v1/api/jobs/ca/histogram", hist.AbsolutePath);
        Assert.Equal("/v1/api/jobs/ca/top_companies", top.AbsolutePath);
        Assert.Equal("Toronto", HttpUtility.ParseQueryString(top.Query)["where"]);
    }
}
