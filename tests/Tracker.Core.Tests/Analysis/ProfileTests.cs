using Tracker.Analysis;
using Tracker.Interpreting;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Analysis;

public class ProfileTests
{
    private static readonly SkillCatalog Catalog = new([new("C#", "language", ["C#"]), new("Azure", "cloud", ["Azure"])]);

    [Fact]
    public void Load_reads_families_and_levels()
    {
        using var tmp = new TempDir();
        var path = tmp.File("profile.json", """{"targetFamilies":["dotnet","lead_manager"],"skills":{"C#":"strong","Azure":"Learning"}}""");

        var profile = Profile.Load(path, Catalog);

        Assert.Equal(new HashSet<RoleFamily> { RoleFamily.Dotnet, RoleFamily.LeadManager }, profile.TargetFamilies);
        Assert.Equal(SkillLevel.Learning, profile.LevelOf("Azure"));
        Assert.Equal(SkillLevel.None, profile.LevelOf("Go"));
    }

    [Fact]
    public void Load_lists_every_problem()
    {
        using var tmp = new TempDir();
        var path = tmp.File("profile.json", """{"targetFamilies":["astronaut"],"skills":{"Cobol":"strong","C#":"expert"}}""");

        var message = Assert.Throws<InvalidDataException>(() => Profile.Load(path, Catalog)).Message;

        Assert.Contains("astronaut", message);
        Assert.Contains("Cobol", message);
        Assert.Contains("expert", message);
    }

    [Fact]
    public void Committed_example_profile_is_valid() =>
        Profile.Load(RepoConfig.PathOf("profile.example.json"), SkillCatalog.Load(RepoConfig.PathOf("skills.json")));

    [Theory]
    [InlineData(SkillLevel.Strong, 1.0)]
    [InlineData(SkillLevel.Working, 0.6)]
    [InlineData(SkillLevel.Learning, 0.3)]
    [InlineData(SkillLevel.None, 0.0)]
    public void Weights(SkillLevel level, double weight) => Assert.Equal(weight, Profile.Weight(level));
}
