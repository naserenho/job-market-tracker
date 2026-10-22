using Tracker.Interpreting;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Interpreting;

public class SkillCatalogTests
{
    private static readonly SkillCatalog Catalog = SkillCatalog.Load(RepoConfig.PathOf("skills.json"));

    private static IReadOnlySet<string> Match(string text) => Catalog.Match(text);

    [Fact]
    public void Committed_catalog_has_unique_names_and_a_useful_size()
    {
        Assert.InRange(Catalog.Skills.Count, 150, 220);
        Assert.Equal(Catalog.Skills.Count, Catalog.Skills.Select(s => s.Name).Distinct().Count());
    }

    [Fact]
    public void Dotnet_tokens_match()
    {
        var found = Match("Experience with C#, .NET 8 and ASP.NET Core, plus Entity Framework.");
        Assert.Superset(new HashSet<string> { "C#", ".NET", "ASP.NET Core", "Entity Framework" }, (ISet<string>)found.ToHashSet());
    }

    [Theory]
    [InlineData("React, Redux and TypeScript", "React")]
    [InlineData("RESTful APIs", "REST APIs")]
    [InlineData("Java, Go, Python", "Go")]
    [InlineData("Golang services", "Go")]
    [InlineData("Python, R, SQL", "R")]
    [InlineData("CI/CD pipelines", "CI/CD")]
    [InlineData("AKS clusters", "Kubernetes")]
    [InlineData("AKS clusters", "Azure")]
    [InlineData("Terraform modules", "Infrastructure as Code")]
    [InlineData("Strong JavaScript skills", "JavaScript")]
    [InlineData("Build accessible UIs that meet WCAG 2.1", "Accessibility")]
    [InlineData("Apply SOLID principles", "Design Patterns")]
    [InlineData("Helm charts on Kubernetes", "Helm")]
    [InlineData("Bash and PowerShell scripts", "Bash")]
    [InlineData("Ruby on Rails APIs", "Ruby on Rails")]
    [InlineData("Swift and SwiftUI", "Swift")]
    [InlineData("Build MCP servers for our agents", "AI Agents")]
    [InlineData("Vue.js and React", "Vue.js")]
    [InlineData("Bootstrap and Tailwind", "Bootstrap")]
    [InlineData("Software for government agencies", "Public Sector")]
    [InlineData("We build software for insurance companies", "Insurance")]
    [InlineData("EHR integrations for clinics", "Healthcare")]
    public void Real_mentions_match(string text, string skill) =>
        Assert.Contains(skill, Match(text));

    [Theory]
    [InlineData("We react quickly to incidents", "React")]
    [InlineData("the rest of the team", "REST APIs")]
    [InlineData("Let's go to the office", "Go")]
    [InlineData("Our R&D group", "R")]
    [InlineData("NoSQL stores", "SQL")]
    [InlineData("Push to GitHub", "Git")]
    [InlineData("Strong JavaScript skills", "Java")]
    [InlineData("OpenAI models", "AI (general)")]
    [InlineData("HTML and XML", "Machine Learning")]
    [InlineData("React Native apps", "React")]
    [InlineData("Comprehensive health and dental insurance", "Insurance")]
    [InlineData("You have a solid understanding of APIs", "Design Patterns")]
    [InlineData("You will be at the helm of our platform", "Helm")]
    [InlineData("Join our summer bash", "Bash")]
    [InlineData("Put guard rails around releases", "Ruby on Rails")]
    [InlineData("SWIFT messaging for payments", "Swift")]
    [InlineData("Microsoft Certified Professional (MCP)", "AI Agents")]
    [InlineData("Build bots in Copilot Studio", "AI Coding Assistants")]
    [InlineData("Un point de vue unique", "Vue.js")]
    [InlineData("Help us bootstrap the new team", "Bootstrap")]
    [InlineData("Director of Clinical Operations", "Healthcare")]
    [InlineData("Bring government-issued ID", "Public Sector")]
    [InlineData("Our AODA compliance statement", "Accessibility")]
    [InlineData("Health care spending account and life insurance", "Healthcare")]
    public void Ambiguous_words_do_not_create_demand(string text, string skill) =>
        Assert.DoesNotContain(skill, Match(text));

    [Fact]
    public void Load_rejects_duplicate_names()
    {
        using var tmp = new TempDir();
        var path = tmp.File("skills.json", """{"skills":[{"name":"A","category":"x","aliases":["a"]},{"name":"A","category":"x","aliases":["b"]}]}""");
        Assert.Contains("A", Assert.Throws<InvalidDataException>(() => SkillCatalog.Load(path)).Message);
    }

    [Fact]
    public void Load_names_the_skill_with_an_invalid_regex()
    {
        using var tmp = new TempDir();
        var path = tmp.File("skills.json", """{"skills":[{"name":"Broken","category":"x","aliases":["re:(unclosed"]}]}""");
        Assert.Contains("Broken", Assert.Throws<InvalidDataException>(() => SkillCatalog.Load(path)).Message);
    }
}
