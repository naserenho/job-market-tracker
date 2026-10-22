using Tracker.Interpreting;
using Tracker.Reading;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Interpreting;

// The Ontario region counts a location anywhere in the province as local, not only the places it lists.
public class ScopeRulesOntarioTests
{
    private static readonly ScopeRules Rules = new(RepoConfig.Ontario);

    private static RawPosting Posting(string location, string source = "greenhouse", bool? remote = null, string? country = null) =>
        new(source, "1", "r1", "Co", "Software Engineer", "", location, remote, country, "", true, null);

    [Theory]
    [InlineData("Ottawa, ON", null, "greenhouse", "Ottawa")]
    [InlineData("Kanata, Ontario, Canada", null, "greenhouse", "Ottawa")]
    [InlineData("Waterloo, Ontario, Canada", null, "greenhouse", "Waterloo")]
    [InlineData("London, ON", null, "greenhouse", "London")]
    [InlineData("Sudbury, ON", null, "greenhouse", "Sudbury")]
    [InlineData("Timmins, Ontario", null, "greenhouse", "Timmins")]
    [InlineData("Ontario, Canada", null, "greenhouse", "Ontario")]
    [InlineData("ON, Canada", null, "greenhouse", "Ontario")]
    [InlineData("Remote - Ontario", null, "greenhouse", "Ontario")]
    [InlineData("Canada, Ontario, Timmins", "CA", "adzuna", "Timmins")]
    [InlineData("Toronto, Ontario, CA", null, "greenhouse", "Toronto")]
    [InlineData("Ontario, CA", "CA", "lever", "Ontario")]
    public void Local_anywhere_in_Ontario(string location, string? country, string source, string city)
    {
        var scope = Rules.Classify(Posting(location, source, country: country));
        Assert.True(scope.IsLocal);
        Assert.Equal(city, scope.City);
    }

    [Theory]
    [InlineData("London, UK")]
    [InlineData("London, England")]
    [InlineData("Waterloo, IA")]
    [InlineData("Ottawa, KS")]
    [InlineData("Ontario, CA")]
    [InlineData("Ontario, CA, USA")]
    [InlineData("Ontario, California")]
    [InlineData("Ontario, OR")]
    [InlineData("Ontario, NY")]
    [InlineData("Vancouver, BC")]
    [InlineData("Remote - Canada")]
    [InlineData("Hybrid - on site")]
    // The US Ontarios in their other spellings (final review I1).
    [InlineData("Ontario, USA")]
    [InlineData("Ontario, United States")]
    [InlineData("Ontario, OH")]
    [InlineData("Ontario, Ohio")]
    [InlineData("Ontario, Calif.")]
    [InlineData("Ontario County, NY")]
    [InlineData("Hybrid - Ontario, CA")]
    [InlineData("USA - Ontario, CA")]
    // Names shared with places abroad need Canadian context (final review I2).
    [InlineData("New Orleans")]
    [InlineData("New Orleans, Louisiana")]
    [InlineData("Orléans, France")]
    [InlineData("Niagara Falls, New York")]
    [InlineData("Sudbury, Massachusetts")]
    [InlineData("Richmond Hill, Georgia")]
    public void Not_local(string location) =>
        Assert.False(Rules.Classify(Posting(location)).IsLocal);

    // "CA" after a town in Ontario means Canada; only "Ontario, CA" alone is the US city.
    [Theory]
    [InlineData("Ontario, CA, Canada", "Ontario")]
    [InlineData("Timmins, Ontario, CA", "Timmins")]
    [InlineData("Timmins, ON, CA", "Timmins")]
    [InlineData("Toronto, Ontario, CA", "Toronto")]
    [InlineData("Orleans, ON", "Ottawa")]
    [InlineData("Niagara Falls, Ontario", "Niagara Falls")]
    public void Stays_local_with_canadian_context(string location, string city)
    {
        var scope = Rules.Classify(Posting(location));
        Assert.True(scope.IsLocal);
        Assert.Equal(city, scope.City);
    }

    [Fact]
    public void Remote_in_canada_is_unchanged() =>
        Assert.True(Rules.Classify(Posting("Remote - Canada")).IsRemoteInCountry);
}
