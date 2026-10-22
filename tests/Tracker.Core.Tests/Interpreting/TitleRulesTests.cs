using Tracker.Interpreting;

namespace Tracker.Core.Tests.Interpreting;

public class TitleRulesTests
{
    [Theory]
    [InlineData(".NET Developer", RoleFamily.Dotnet)]
    [InlineData("Senior C# Software Engineer", RoleFamily.Dotnet)]
    [InlineData("ASP.NET Full Stack Developer", RoleFamily.Dotnet)]
    [InlineData("Lead .NET Engineer", RoleFamily.Dotnet)]
    [InlineData("Full Stack Developer (React/Node)", RoleFamily.Fullstack)]
    [InlineData("Senior Fullstack Engineer", RoleFamily.Fullstack)]
    [InlineData("Software Engineer, Payments", RoleFamily.SoftwareEngineer)]
    [InlineData("Senior Backend Engineer", RoleFamily.SoftwareEngineer)]
    [InlineData("Staff Software Developer", RoleFamily.SoftwareEngineer)]
    [InlineData("Web Developer", RoleFamily.SoftwareEngineer)]
    [InlineData("Engineering Manager, Platform", RoleFamily.LeadManager)]
    [InlineData("Software Engineering Manager", RoleFamily.LeadManager)]
    [InlineData("Tech Lead", RoleFamily.LeadManager)]
    [InlineData("Director of Engineering", RoleFamily.LeadManager)]
    [InlineData("Business Developer", RoleFamily.Other)]
    [InlineData("Senior Data Engineer", RoleFamily.Other)]
    [InlineData("Solutions Engineer", RoleFamily.Other)]
    [InlineData("Product Manager", RoleFamily.Other)]
    [InlineData("Site Reliability Engineer", RoleFamily.Other)]
    [InlineData("Sales Team Lead - Burlington", RoleFamily.Other)]
    [InlineData("Press Tech Lead", RoleFamily.Other)]
    [InlineData("Customer Support Team Lead", RoleFamily.Other)]
    public void Family_first_match_wins(string title, RoleFamily expected) =>
        Assert.Equal(expected, TitleRules.Family(title));

    [Theory]
    [InlineData("Senior .NET Developer", Seniority.Senior)]
    [InlineData("Sr. Software Engineer", Seniority.Senior)]
    [InlineData("Software Engineer III", Seniority.Senior)]
    [InlineData("Software Engineer II", Seniority.Intermediate)]
    [InlineData("Intermediate Full Stack Developer", Seniority.Intermediate)]
    [InlineData("Junior Developer", Seniority.Junior)]
    [InlineData("Software Engineer Intern", Seniority.Junior)]
    [InlineData("Software Engineer I", Seniority.Junior)]
    [InlineData("Staff Engineer", Seniority.StaffPrincipal)]
    [InlineData("Principal Software Architect", Seniority.StaffPrincipal)]
    [InlineData("Lead Software Engineer", Seniority.Lead)]
    [InlineData("Senior Engineering Manager", Seniority.Manager)]
    [InlineData("Software Engineer", Seniority.Unspecified)]
    public void Seniority_from_title(string title, Seniority expected) =>
        Assert.Equal(expected, TitleRules.Seniority(title));

    [Fact]
    public void Family_keys_round_trip()
    {
        foreach (var family in Enum.GetValues<RoleFamily>())
            Assert.Equal(family, TitleRules.ParseFamily(TitleRules.FamilyKey(family)));
        Assert.Null(TitleRules.ParseFamily("astronaut"));
    }

    [Theory]
    [InlineData("Senior Software Engineer, Security", RoleFamily.SoftwareEngineer)]
    [InlineData("Staff Software Engineer, Machine Learning Platform", RoleFamily.SoftwareEngineer)]
    [InlineData("Canada- Jr Software Engineer (SRE)", RoleFamily.SoftwareEngineer)]
    [InlineData("Senior Software Engineer II, Ads Data Solutions Engineering", RoleFamily.SoftwareEngineer)]
    [InlineData("Sr. Software Dev Engineer, Aurora Control Plane, Aurora Security", RoleFamily.SoftwareEngineer)]
    [InlineData("Software Engineers (5)", RoleFamily.SoftwareEngineer)]
    [InlineData("Senior Software Engineer in Test", RoleFamily.Other)]
    [InlineData("Software Engineer, Testing", RoleFamily.Other)]
    [InlineData("Product Security Engineer", RoleFamily.Other)]
    [InlineData("Senior Developer (Windows), Product Security", RoleFamily.Other)]
    [InlineData("Software Developer and IT Support", RoleFamily.Other)]
    [InlineData("Senior Data Engineer", RoleFamily.Other)]
    public void A_specialty_does_not_hide_an_explicit_software_title(string title, RoleFamily expected) =>
        Assert.Equal(expected, TitleRules.Family(title));
}
