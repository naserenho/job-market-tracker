using Tracker.Interpreting;
using Tracker.Reading;
using Tracker.TestSupport;

namespace Tracker.Core.Tests.Interpreting;

public class InterpreterTests
{
    private static readonly Interpreter Interpreter = new(
        RepoConfig.Toronto,
        SkillCatalog.Load(RepoConfig.PathOf("skills.json")));

    private static RawPosting Raw(string title = "Senior .NET Developer", string location = "Toronto, Ontario, Canada", string description = "C# and Azure.", SalaryRange? salary = null, string? country = null) =>
        new("greenhouse", "7", "r2", "Acme", title, "https://x.test/7", location, null, country, description, true, salary);

    [Fact]
    public void Interprets_scope_title_and_skills()
    {
        var p = Interpreter.Interpret(Raw(), ["r1", "r2"]);

        Assert.Equal("greenhouse:7", p.Id);
        Assert.True(p.Scope.IsLocal);
        Assert.Equal(RoleFamily.Dotnet, p.Family);
        Assert.Equal(Seniority.Senior, p.Seniority);
        Assert.Superset(new HashSet<string> { ".NET", "C#", "Azure" }, (ISet<string>)p.Skills.ToHashSet());
        Assert.Equal("r1", p.FirstSeenRun);
        Assert.Equal("r2", p.LastSeenRun);
    }

    [Fact]
    public void Structured_salary_wins_over_text()
    {
        var p = Interpreter.Interpret(Raw(description: "Range $90,000 - $100,000 in Toronto.", salary: new SalaryRange(120000, 140000, SalaryPeriod.Year)), ["r1"]);
        Assert.Equal("structured", p.SalaryOrigin);
        Assert.Equal(120000, p.Salary!.Min);
    }

    [Fact]
    public void Text_salary_is_parsed_when_no_structured_salary()
    {
        var p = Interpreter.Interpret(Raw(description: "Salary in Toronto: $110,000 - $130,000."), ["r1"]);
        Assert.Equal("parsed", p.SalaryOrigin);
        Assert.Equal(110000, p.Salary!.Min);
    }

    [Fact]
    public void Implausible_structured_salary_is_dropped()
    {
        var p = Interpreter.Interpret(Raw(salary: new SalaryRange(10000, 10000, SalaryPeriod.Year)), ["r1"]);
        Assert.Null(p.Salary);
        Assert.Equal("none", p.SalaryOrigin);
    }

    [Theory]
    [InlineData("We use AI tools to screen applications. Build services in C#.")]
    [InlineData("Applications may be reviewed with the help of artificial intelligence. Build services in C#.")]
    public void Hiring_ai_disclosure_does_not_count_as_ai_demand(string description)
    {
        var skills = Interpreter.Interpret(Raw(description: description), ["r1"]).Skills;
        Assert.Contains("C#", skills);
        Assert.DoesNotContain("AI (general)", skills);
    }

    [Theory]
    [InlineData("We will accommodate their individual accessibility needs throughout the process. Build UI in React.")]
    [InlineData("Accessibility Statement: accommodations are available on request. Build UI in React.")]
    public void Accommodation_notices_do_not_count_as_accessibility_demand(string description)
    {
        var skills = Interpreter.Interpret(Raw(description: description), ["r1"]).Skills;
        Assert.Contains("React", skills);
        Assert.DoesNotContain("Accessibility", skills);
    }

    [Fact]
    public void Real_ai_work_still_counts() =>
        Assert.Contains("AI (general)", Interpreter.Interpret(Raw(description: "Build AI features for our candidates' dashboard team."), ["r1"]).Skills);

    [Theory]
    [InlineData("Must be a Canadian citizen.", true)]
    [InlineData("Ability to obtain Secret clearance.", true)]
    [InlineData("Enhanced Reliability security screening required.", true)]
    [InlineData("Open to permanent residents and citizens alike.", false)]
    public void Citizenship_or_clearance_flag(string description, bool flagged) =>
        Assert.Equal(flagged, Interpreter.Interpret(Raw(description: description), ["r1"]).CitizenshipOrClearance);

    private static readonly Interpreter AbuDhabi = new(RepoConfig.AbuDhabi, SkillCatalog.Load(RepoConfig.PathOf("skills.json")));

    [Theory]
    [InlineData("This role is open to UAE Nationals only.", true)]
    [InlineData("Part of our Emiratisation programme.", true)]
    [InlineData("Canadian citizenship required.", false)] // another region's phrase
    [InlineData("We welcome applicants of every nationality.", false)]
    public void Abu_Dhabi_flags_nationals_only_roles(string description, bool flagged) =>
        Assert.Equal(flagged, AbuDhabi.Interpret(Raw(location: "Abu Dhabi, UAE", description: description), ["r1"]).CitizenshipOrClearance);

    [Fact]
    public void Abu_Dhabi_reads_a_monthly_salary_in_the_text()
    {
        var p = AbuDhabi.Interpret(Raw(location: "Abu Dhabi, UAE", description: "Salary: AED 30,000 - 40,000 per month."), ["r1"]);

        Assert.Equal((new SalaryRange(30_000, 40_000, SalaryPeriod.Month), "parsed"), (p.Salary, p.SalaryOrigin));
    }
}
