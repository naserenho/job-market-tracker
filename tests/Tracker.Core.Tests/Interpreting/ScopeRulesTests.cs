using Tracker.Configuration;
using Tracker.Interpreting;
using Tracker.Reading;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Interpreting;

public class ScopeRulesTests
{
    private static readonly ScopeRules Rules = new(RepoConfig.Toronto);

    private static RawPosting Posting(string location, string source = "greenhouse", bool? remote = null, string? country = null, string title = "Software Engineer", string description = "") =>
        new(source, "1", "r1", "Co", title, "", location, remote, country, description, true, null);

    [Theory]
    [InlineData("Toronto, Ontario, Canada", "Toronto")]
    [InlineData("Toronto, ON, CA", "Toronto")]
    [InlineData("Oakville, Ontario - Canada; Toronto, Ontario - Canada; Waterloo, Ontario - Canada", "Oakville")]
    [InlineData("Bellevue, Washington; Chicago, Illinois; Toronto, Ontario, Canada", "Toronto")]
    [InlineData("Kitchener-Waterloo, ON; Toronto, ON", "Toronto")]
    [InlineData("Mississauga, Ontario", "Mississauga")]
    [InlineData("Burlington, Ontario", "Burlington")]
    public void Gta_locations_resolve_to_their_municipality(string location, string city)
    {
        var scope = Rules.Classify(Posting(location));
        Assert.True(scope.IsLocal);
        Assert.Equal(city, scope.City);
        Assert.True(scope.InScope);
    }

    [Fact]
    public void Adzuna_neighbourhoods_and_regions_map_to_municipalities()
    {
        Assert.Equal("Vaughan", Rules.Classify(Posting("Woodbridge, York region; Canada, Ontario, York region", "adzuna", country: "CA")).City);
        Assert.Equal("Toronto", Rules.Classify(Posting("Regent Park, City of Toronto; Canada, Ontario, Toronto", "adzuna", country: "CA")).City);
        Assert.Equal("Peel Region", Rules.Classify(Posting("Canada, Ontario, Peel region", "adzuna", country: "CA")).City);
    }

    [Theory]
    [InlineData("Burlington, Massachusetts")]
    [InlineData("Aurora, Colorado")]
    [InlineData("Vancouver, British Columbia, Canada")]
    [InlineData("New York, New York")]
    public void Non_gta_places_are_not_gta(string location) =>
        Assert.False(Rules.Classify(Posting(location)).IsLocal);

    [Fact]
    public void Remote_open_to_canada_is_in_scope()
    {
        Assert.True(Rules.Classify(Posting("Remote (United States | Canada)", "ashby", remote: true)).IsRemoteInCountry);
        Assert.True(Rules.Classify(Posting("Remote - Canada")).IsRemoteInCountry);
        Assert.True(Rules.Classify(Posting("Canada", "lever", remote: true, country: "CA")).IsRemoteInCountry);
    }

    [Fact]
    public void Remote_outside_canada_is_out_of_scope()
    {
        var scope = Rules.Classify(Posting("Remote, USA", "lever", remote: true, country: "US"));
        Assert.False(scope.IsRemoteInCountry);
        Assert.False(scope.InScope);
    }

    [Fact]
    public void Adzuna_remote_is_read_from_the_snippet()
    {
        Assert.True(Rules.Classify(Posting("Canada; Canada", "adzuna", country: "CA", description: "This is a fully remote role for Canadian residents.")).IsRemoteInCountry);
        Assert.False(Rules.Classify(Posting("Canada; Canada", "adzuna", country: "CA", description: "Hybrid, 3 days in office.")).IsRemoteInCountry);
    }

    [Theory]
    [InlineData("Remote - US; Vancouver, BC", null)]
    [InlineData("Remote, USA; Montreal, QC", null)]
    [InlineData("San Francisco, CA (Remote); Vancouver, BC", null)]
    [InlineData("Amsterdam, NL", true)]
    [InlineData("Lima, PE", true)]
    [InlineData("Remote - NL", null)]
    public void A_remote_option_outside_canada_is_not_remote_canada(string location, bool? remote) =>
        Assert.False(Rules.Classify(Posting(location, remote: remote)).IsRemoteInCountry);

    [Theory]
    [InlineData("Scarborough, UK")]
    [InlineData("Oakville, CT")]
    [InlineData("Richmond Hill, GA")]
    [InlineData("Markham, IL")]
    [InlineData("Whitby, England")]
    [InlineData("Toronto, OH")]
    public void Foreign_towns_sharing_a_gta_name_are_not_gta(string location) =>
        Assert.False(Rules.Classify(Posting(location)).IsLocal);

    [Fact]
    public void Structured_remote_with_a_canadian_address_is_remote_canada() =>
        Assert.True(Rules.Classify(Posting("Vancouver; Calgary", "ashby", remote: true, country: "CA")).IsRemoteInCountry);

    [Fact]
    public void Non_gta_city_is_kept_as_the_first_location_segment() =>
        Assert.Equal("Vancouver", Rules.Classify(Posting("Vancouver, British Columbia, Canada")).City);

    // The Toronto file must rebuild exactly the patterns this class used to hard-code.
    [Fact]
    public void Toronto_patterns_equal_the_old_hand_written_ones()
    {
        var p = Rules.Patterns;
        Assert.Equal(@"\b(?:canada|canadian|ontario|british columbia|alberta|quebec|québec|nova scotia|manitoba|saskatchewan|new brunswick|newfoundland|prince edward island)\b", p.InCountryWords);
        Assert.Equal(@"(?:,|-)\s*(?:ON|BC|AB|QC|NS|MB|NB)\b", p.InCountryCodes);
        Assert.Equal(@"\b(?:USA|U\.S\.A?\.?|United States|UK|U\.K\.|United Kingdom|England|Scotland|Wales|Ireland|Australia|New Zealand|India)\b|,\s*(?-i:AL|AK|AZ|AR|CA|CO|CT|DE|FL|GA|HI|ID|IL|IN|IA|KS|KY|LA|ME|MD|MA|MI|MN|MS|MO|MT|NE|NV|NH|NJ|NM|NY|NC|ND|OH|OK|OR|PA|RI|SC|SD|TN|TX|UT|VT|VA|WA|WV|WI|WY|DC)\b", p.Foreign);
    }

    private static readonly ScopeRules AbuDhabi = new(RepoConfig.AbuDhabi);

    [Theory]
    [InlineData("Abu Dhabi, United Arab Emirates", "Abu Dhabi", true, false)]
    [InlineData("Al Ain, Abu Dhabi, UAE", "Al Ain", true, false)]
    [InlineData("Yas Island", "Abu Dhabi", true, false)]
    [InlineData("Asia; Taiwan, Taipei; Hong Kong; UAE, Abu Dhabi", "Abu Dhabi", true, false)]
    [InlineData("Dubai, UAE", "Dubai", false, false)]
    [InlineData("Remote - UAE", "Remote - UAE", false, true)]
    [InlineData("Remote, KSA; Remote, UAE", "Remote", false, true)]
    [InlineData("Remote - Dubai", "Remote - Dubai", false, true)]
    [InlineData("Riyadh, Saudi Arabia", "Riyadh", false, false)]
    [InlineData("Al Ain; Muscat, Oman", "Al Ain", false, false)] // Al Ain needs UAE context in its own segment
    public void Abu_Dhabi_scope(string location, string city, bool local, bool remote)
    {
        var scope = AbuDhabi.Classify(Posting(location));

        Assert.Equal((city, local, remote), (scope.City, scope.IsLocal, scope.IsRemoteInCountry));
    }

    [Fact]
    public void A_structured_country_code_is_the_regions()
    {
        Assert.True(AbuDhabi.IsInCountry(Posting("Somewhere", country: "AE")));
        Assert.False(AbuDhabi.IsInCountry(Posting("Somewhere", country: "CA")));
        Assert.True(AbuDhabi.Classify(Posting("Anywhere", remote: true, country: "AE")).IsRemoteInCountry);
    }
}
