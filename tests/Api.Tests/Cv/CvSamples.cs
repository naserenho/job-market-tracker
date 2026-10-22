using Api.Cv;
using Tracker.Interpreting;

namespace Api.Tests.Cv;

// Small hand-made master CVs, so tailoring and layout tests don't depend on the skill catalog's aliases.
internal static class CvSamples
{
    public static IReadOnlySet<string> Set(params string[] items) => items.ToHashSet(StringComparer.Ordinal);

    public static CvBullet B(string id, string text, string[]? skills = null, bool always = false) => new(id, text, Set(skills ?? []), always);

    public static CvSection Role(string id, string title, string company, int max, params CvBullet[] bullets) =>
        new(id, CvSectionKind.Role, title, company, "Toronto, ON", "Oct 2023 – Present", max, bullets);

    public static CvSection Project(string id, string title, params CvBullet[] bullets) =>
        new(id, CvSectionKind.Project, title, "", "", "", 5, bullets);

    public static readonly CvPerson Person = new("Alex Example", "Toronto, ON", "Canadian Permanent Resident", "+1 416 555 0100",
        "alex@example.com", [new CvLink("GitHub", "https://github.com/alex-example")]);

    // Acme (max 3): a1 always [.NET], a2 [Azure DevOps, CI/CD], a3 [SQL Server], a4 [Docker].
    // Globex (max 2): g1 [REST APIs], g2 [React].
    public static MasterCv SampleCv(IReadOnlyList<CvSection>? sections = null, IReadOnlyList<CvSkillGroup>? groups = null,
        IReadOnlyList<CvEducation>? education = null, IReadOnlyList<CvCertification>? certifications = null, CvPerson? person = null) => new(
        person ?? Person,
        "Senior .NET Developer",
        [
            new CvSummary("dotnet", new HashSet<RoleFamily> { RoleFamily.Dotnet, RoleFamily.Fullstack }, "Builds .NET systems end to end."),
            new CvSummary("lead", new HashSet<RoleFamily> { RoleFamily.LeadManager }, "Leads small teams and still ships."),
        ],
        groups ??
        [
            new CvSkillGroup("Backend", [new CvSkillItem("C# / .NET", Set("C#", ".NET")), new CvSkillItem("SQL Server", Set("SQL Server"))]),
            new CvSkillGroup("Cloud", [new CvSkillItem("Docker", Set("Docker")), new CvSkillItem("Azure DevOps", Set("Azure DevOps"))]),
        ],
        sections ??
        [
            Role("acme", "Senior Developer", "Acme Corp", 3,
                B("a1", "Led the team.", [".NET"], always: true), B("a2", "Built CI/CD.", ["Azure DevOps", "CI/CD"]),
                B("a3", "Tuned SQL.", ["SQL Server"]), B("a4", "Ran Docker.", ["Docker"])),
            Role("globex", "Developer", "Globex", 2, B("g1", "Wrote APIs.", ["REST APIs"]), B("g2", "Built a React UI.", ["React"])),
        ],
        education ?? [new CvEducation("BSc Computer Engineering", "Example University", "2012-2016")],
        certifications ?? [new CvCertification("AZ-204", "2026", Set("Azure"))]);
}
