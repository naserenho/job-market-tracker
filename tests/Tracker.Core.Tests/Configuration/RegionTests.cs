using System.Text.Json;
using System.Text.Json.Nodes;
using Tracker.Configuration;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Configuration;

public class RegionTests
{
    [Fact]
    public void The_region_named_in_sources_json_loads()
    {
        var name = Regions.ActiveName(RepoConfig.Root);
        Assert.Equal(name, Regions.Load(RepoConfig.Root, name).Name);
    }

    [Fact]
    public void Both_shipped_regions_load()
    {
        var toronto = RepoConfig.Toronto;
        Assert.Equal(("toronto", "CA", "GTA", "Remote (Canada)", "CAD"),
            (toronto.Name, toronto.CountryCode, toronto.LocalLabel, toronto.RemoteLabel, toronto.Salary.Currency));
        Assert.NotNull(toronto.Adzuna);
        Assert.NotNull(toronto.JobBank);

        var abuDhabi = RepoConfig.AbuDhabi;
        Assert.Equal(("abu-dhabi", "AE", "Abu Dhabi", "AED"), (abuDhabi.Name, abuDhabi.CountryCode, abuDhabi.LocalLabel, abuDhabi.Salary.Currency));
        Assert.Null(abuDhabi.Adzuna);
        Assert.Null(abuDhabi.JobBank);
        Assert.NotEmpty(abuDhabi.Boards);
    }

    // A misspelt member must never be ignored silently: the region would quietly lose that setting.
    [Theory]
    [InlineData("\"Placess\": {}", "Placess")]
    [InlineData("\"Title\": 5", "Title")]
    public void A_typo_or_wrong_type_names_the_member(string member, string expected)
    {
        using var tmp = new TempDir();
        var text = File.ReadAllText(RepoConfig.PathOf("regions/toronto.json")).TrimEnd();
        var path = tmp.File("regions/x.json", text[..^1].TrimEnd() + $",\n  {member}\n}}");

        var error = Assert.Throws<InvalidDataException>(() => Regions.LoadFile(path));

        Assert.Contains(expected, error.Message);
        Assert.Contains("x.json", error.Message);
    }

    [Fact]
    public void An_unknown_region_lists_the_available_ones()
    {
        var error = Assert.Throws<InvalidDataException>(() => Regions.Load(RepoConfig.Root, "atlantis"));

        Assert.Contains("abu-dhabi", error.Message);
        Assert.Contains("toronto", error.Message);
    }

    [Theory]
    [InlineData("Culture", "\"xx-NOPE\"")]
    [InlineData("TimeZone", "\"Mars/Base\"")]
    [InlineData("CitizenshipPatterns", "[\"(unclosed\"]")]
    [InlineData("LocalLabel", "\"\"")]
    public void Invalid_values_are_config_errors(string member, string value)
    {
        using var tmp = new TempDir();
        var json = JsonNode.Parse(File.ReadAllText(RepoConfig.PathOf("regions/toronto.json")),
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
        json[member] = JsonNode.Parse(value);
        var path = tmp.File("regions/bad.json", json.ToJsonString());

        Assert.Contains(member, Assert.Throws<InvalidDataException>(() => Regions.LoadFile(path)).Message);
    }

    [Fact]
    public void Salary_bounds_must_be_in_order()
    {
        using var tmp = new TempDir();
        var json = JsonNode.Parse(File.ReadAllText(RepoConfig.PathOf("regions/toronto.json")),
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
        json["Salary"]!["YearlyMin"] = 600000;
        var path = tmp.File("regions/bad.json", json.ToJsonString());

        Assert.Contains("YearlyMin", Assert.Throws<InvalidDataException>(() => Regions.LoadFile(path)).Message);
    }

    // A region file someone adds to config/regions/ is checked too.
    public static TheoryData<string> RegionFiles() =>
        [.. Directory.GetFiles(RepoConfig.PathOf("regions"), "*.json").Select(f => Path.GetFileName(f)!)];

    [Theory]
    [MemberData(nameof(RegionFiles))]
    public void Every_region_file_loads(string file)
    {
        var region = Regions.LoadFile(RepoConfig.PathOf($"regions/{file}"));
        Assert.Equal(Path.GetFileNameWithoutExtension(file), region.Name);
    }

    // Writes a changed copy of toronto.json as regions/<name>.json and returns the loader's error.
    private static string ErrorFor(Action<JsonNode> change, string name = "toronto")
    {
        using var tmp = new TempDir();
        var json = JsonNode.Parse(File.ReadAllText(RepoConfig.PathOf("regions/toronto.json")),
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
        change(json);
        var path = tmp.File($"regions/{name}.json", json.ToJsonString());
        return Assert.Throws<InvalidDataException>(() => Regions.LoadFile(path)).Message;
    }

    // A missing section would silently switch a rule off (no in-country words: nothing is ever remote in the country).
    [Theory]
    [InlineData("InCountry")]
    [InlineData("CitizenshipPatterns")]
    [InlineData("Boards")]
    public void A_missing_section_is_a_config_error(string member) =>
        Assert.Contains(member, ErrorFor(j => j.AsObject().Remove(member)));

    [Fact]
    public void A_missing_money_prefix_is_a_config_error() =>
        Assert.Contains("MoneyPrefix", ErrorFor(j => j["Salary"]!.AsObject().Remove("MoneyPrefix")));

    // An empty pattern matches everywhere: every location would look foreign, every range would look like home pay.
    [Theory]
    [InlineData("Foreign", "Words")]
    [InlineData("Salary", "Signs")]
    [InlineData("InCountry", "Codes")]
    public void A_blank_list_entry_is_a_config_error(string section, string list) =>
        Assert.Contains($"{section}.{list}", ErrorFor(j => j[section]![list]!.AsArray().Add("")));

    [Fact]
    public void An_adzuna_section_needs_its_country_and_place()
    {
        var error = ErrorFor(j => j["Adzuna"] = JsonNode.Parse("""{ "DistanceKm": 0 }"""));

        Assert.Contains("Adzuna.Country", error);
        Assert.Contains("Adzuna.Where", error);
        Assert.DoesNotContain("Adzuna.DistanceKm", error);
    }

    // Writes a changed copy of toronto.json and loads it.
    private static Region LoadChanged(Action<JsonNode> change)
    {
        using var tmp = new TempDir();
        var json = JsonNode.Parse(File.ReadAllText(RepoConfig.PathOf("regions/toronto.json")),
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
        change(json);
        return Regions.LoadFile(tmp.File("regions/toronto.json", json.ToJsonString()));
    }

    [Fact]
    public void Toronto_and_Abu_Dhabi_have_no_province_wide_fields()
    {
        foreach (var region in new[] { RepoConfig.Toronto, RepoConfig.AbuDhabi })
        {
            Assert.Null(region.Local.Anywhere);
            Assert.Null(region.Local.Exclude);
            Assert.Null(region.Local.Areas);
        }
        Assert.Equal("", RepoConfig.Toronto.JobBank!.Province);
    }

    [Fact]
    public void Ontario_file_loads_and_groups_every_gta_place()
    {
        var ontario = RepoConfig.Ontario;
        Assert.Equal(("ontario", "Ontario", "Remote (Canada)", "Ontario"),
            (ontario.Name, ontario.LocalLabel, ontario.RemoteLabel, ontario.JobBank!.Province));
        foreach (var place in RepoConfig.Toronto.Local.Places.Values.Distinct())
            Assert.Equal("Toronto area (GTA)", ontario.Local.Areas![place]);
    }

    [Fact]
    public void Job_bank_needs_exactly_one_of_economic_region_or_province()
    {
        const string message = "JobBank: set exactly one of EconomicRegion or Province.";
        Assert.Contains(message, ErrorFor(j => j["JobBank"]!["Province"] = "Ontario"));
        Assert.Contains(message, ErrorFor(j => j["JobBank"]!.AsObject().Remove("EconomicRegion")));

        var region = LoadChanged(j =>
        {
            j["JobBank"]!.AsObject().Remove("EconomicRegion");
            j["JobBank"]!["Province"] = "Ontario";
        });
        Assert.Equal("Ontario", region.JobBank!.Province);
    }

    [Fact]
    public void Adzuna_distance_may_be_left_out_but_not_negative()
    {
        Assert.Equal(0, LoadChanged(j => j["Adzuna"]!.AsObject().Remove("DistanceKm")).Adzuna!.DistanceKm);
        Assert.Contains("Adzuna.DistanceKm must be at least 1 when set.", ErrorFor(j => j["Adzuna"]!["DistanceKm"] = -5));
    }

    [Fact]
    public void Province_wide_lists_reject_empty_entries()
    {
        Assert.Contains("Local.Anywhere.Words has an empty entry.",
            ErrorFor(j => j["Local"]!["Anywhere"] = JsonNode.Parse("""{ "Words": [""], "Codes": ["ON"] }""")));
        Assert.Contains("Local.Exclude: '(' is not a valid pattern.",
            ErrorFor(j => j["Local"]!["Exclude"] = JsonNode.Parse("""["("]""")));
        Assert.Contains("Local.Areas has an empty place or area.",
            ErrorFor(j => j["Local"]!["Areas"] = JsonNode.Parse("""{ "Ottawa": "" }""")));
    }

    [Fact]
    public void A_context_name_must_be_one_of_the_places() =>
        Assert.Contains("Atlantis", ErrorFor(j => j["Local"]!["NeedsCountryContext"]!.AsArray().Add("Atlantis")));

    // The archiver tags collections with the file name and the processor filters by Name: they must agree.
    [Fact]
    public void Name_must_match_the_file_name() =>
        Assert.Contains("vancouver", ErrorFor(_ => { }, name: "vancouver"));

    // Browsers only know IANA names: a Windows-only id would blank the dashboard.
    [Fact]
    public void A_windows_only_time_zone_id_is_a_config_error() =>
        Assert.Contains("TimeZone", ErrorFor(j => j["TimeZone"] = "Eastern Standard Time"));
}
