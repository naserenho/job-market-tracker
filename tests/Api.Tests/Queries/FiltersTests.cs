using Api.Queries;
using Api.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Tracker.Interpreting;
using static Api.Tests.TestSupport.Data;

namespace Api.Tests.Queries;

public class FiltersTests
{
    private static IQueryCollection Q(params (string Key, string Value)[] pairs) =>
        new QueryCollection(pairs.ToDictionary(p => p.Key, p => new StringValues(p.Value)));

    [Fact]
    public void Defaults_come_from_the_profile()
    {
        var f = FilterParser.Parse(Q(), Settings.Profile);
        Assert.Equal(Settings.Profile.TargetFamilies.OrderBy(x => x), f.Families.OrderBy(x => x));
        Assert.Null(f.Seniorities);
        Assert.Equal((Where.All, 30), (f.Where, f.WindowDays!.Value));
    }

    [Fact]
    public void Families_accept_keys_and_enum_names_case_insensitively()
    {
        var f = FilterParser.Parse(Q(("families", "software_engineer,DOTNET,LeadManager")), Settings.Profile);
        Assert.Equal(new[] { RoleFamily.Dotnet, RoleFamily.SoftwareEngineer, RoleFamily.LeadManager }.Order(), f.Families.Order());
    }

    [Fact]
    public void Seniority_where_and_window_parse()
    {
        var f = FilterParser.Parse(Q(("seniority", "senior,StaffPrincipal"), ("where", "remote"), ("window", "all")), Settings.Profile);
        Assert.Equal(new[] { Seniority.Senior, Seniority.StaffPrincipal }.Order(), f.Seniorities!.Order());
        Assert.Equal(Where.Remote, f.Where);
        Assert.Null(f.WindowDays);
    }

    [Theory]
    [InlineData("families", "astronaut")]
    [InlineData("seniority", "wizard")]
    [InlineData("seniority", "3")]
    [InlineData("where", "moon")]
    [InlineData("window", "45")]
    public void Unknown_values_name_the_parameter(string key, string value)
    {
        var error = Assert.Throws<FilterException>(() => FilterParser.Parse(Q((key, value)), Settings.Profile));
        Assert.Equal(key, error.Parameter);
        Assert.Contains(value, error.Message);
    }

    [Fact]
    public void Text_sort_and_paging_parse_with_defaults_and_bounds()
    {
        Assert.Equal(TextKind.Full, FilterParser.Text(Q(), TextKind.Full));
        Assert.Equal(TextKind.Snippet, FilterParser.Text(Q(("text", "snippet")), TextKind.Full));
        Assert.Equal(PostingSort.Newest, FilterParser.Sort(Q()));
        Assert.Equal(PostingSort.Salary, FilterParser.Sort(Q(("sort", "salary"))));
        Assert.Equal(25, FilterParser.Int(Q(), "pageSize", 25, 1, 100));
        Assert.Equal("pageSize", Assert.Throws<FilterException>(() => FilterParser.Int(Q(("pageSize", "500")), "pageSize", 25, 1, 100)).Parameter);
        Assert.Equal("text", Assert.Throws<FilterException>(() => FilterParser.Text(Q(("text", "huge")), TextKind.Full)).Parameter);
    }

    [Fact]
    public void Apply_filters_by_family_seniority_place_and_window()
    {
        var market = MarketOf([
            P("in", ["C#"], [0]),
            P("old", ["C#"], [-40]),
            P("other", ["C#"], [0], family: RoleFamily.Other),
            P("junior", ["C#"], [0], seniority: Seniority.Junior),
            P("remote", ["C#"], [0], gta: false, remote: true),
        ], -40, 0);
        var f = new FilterSet(new HashSet<RoleFamily> { RoleFamily.Dotnet }, new HashSet<Seniority> { Seniority.Senior }, Where.Local, 30);

        Assert.Equal(new[] { "in" }, f.Apply(market).Select(p => p.Id));
        Assert.Equal(new[] { "remote" }, (f with { Where = Where.Remote }).Apply(market).Select(p => p.Id));
        Assert.Equal(new[] { "in", "old" }, (f with { WindowDays = null }).Apply(market).Select(p => p.Id).Order());
    }

    [Fact]
    public void Apply_on_an_empty_market_is_empty() =>
        Assert.Empty(FilterParser.Parse(Q(), Settings.Profile).Apply(Api.Market.MarketSnapshot.Empty(T0)));

    // Enum.TryParse reads "all,gta" as a bitwise OR, which can land on a defined value.
    [Theory]
    [InlineData("where", "all,gta")]
    [InlineData("text", "full,snippet")]
    [InlineData("sort", "newest,match")]
    public void A_comma_joined_single_value_is_refused(string name, string value)
    {
        var query = Q((name, value));

        var error = Assert.Throws<FilterException>(() =>
        {
            FilterParser.Parse(query, Settings.Profile);
            FilterParser.Text(query, TextKind.Full);
            FilterParser.Sort(query);
        });
        Assert.Equal(name, error.Parameter);
    }

    // Links bookmarked before regions existed say where=gta.
    [Theory]
    [InlineData("gta")]
    [InlineData("GTA")]
    [InlineData("local")]
    public void Gta_is_the_old_name_for_local(string value) =>
        Assert.Equal(Where.Local, FilterParser.Parse(Q(("where", value)), Settings.Profile).Where);
}
