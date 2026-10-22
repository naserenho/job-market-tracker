using Api;
using Tracker.TestSupport;

namespace Api.Tests;

public class MarketSettingsTests
{
    [Fact]
    public void Load_reads_config_and_the_given_profile()
    {
        var settings = MarketSettings.Load(RepoConfig.Root, RepoConfig.PathOf("profile.example.json"), "D:/somewhere/data");

        Assert.Equal(RepoConfig.Root, settings.RepoRoot);
        Assert.Equal("D:/somewhere/data", settings.DataRoot);
        Assert.InRange(settings.Catalog.Skills.Count, 150, 220);
        Assert.NotEmpty(settings.Profile.TargetFamilies);
        Assert.Equal(30, settings.Options.WindowDays);
    }

    [Fact]
    public void Missing_profile_explains_how_to_create_it()
    {
        using var tmp = new TempDir();
        var error = Assert.Throws<InvalidDataException>(() => MarketSettings.Load(RepoConfig.Root, Path.Combine(tmp.Path, "profile.json")));
        Assert.Contains("profile.example.json", error.Message);
    }
}
