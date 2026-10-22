using Api.Cv;
using Tracker.Interpreting;
using Tracker.TestSupport;

namespace Api.Tests.Cv;

public class MasterCvTests
{
    private static readonly SkillCatalog Catalog = SkillCatalog.Load(RepoConfig.PathOf("skills.json"));

    private static CvLoad LoadText(string json)
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "cv.json");
        File.WriteAllText(path, json);
        return MasterCvFile.Load(path, Catalog);
    }

    [Fact]
    public void The_example_file_loads()
    {
        var load = MasterCvFile.Load(RepoConfig.PathOf("cv.example.json"), Catalog);

        Assert.Empty(load.Problems);
        var cv = load.Cv!;
        Assert.Equal("Alex Example", cv.Person.Name);
        Assert.Equal(new[] { "acme", "globex", "tracker" }, cv.Sections.Select(s => s.Id));
        Assert.Equal((CvSectionKind.Role, "Oct 2023 – Present", 4), (cv.Sections[0].Kind, cv.Sections[0].Dates, cv.Sections[0].MaxBullets));
        Assert.Equal("Jun 2016 – Sep 2023", cv.Sections[1].Dates);
        Assert.Equal((CvSectionKind.Project, 5), (cv.Sections[2].Kind, cv.Sections[2].MaxBullets));
    }

    [Fact]
    public void Bullet_skills_are_its_tags_plus_skills_named_in_its_text()
    {
        var cv = MasterCvFile.Load(RepoConfig.PathOf("cv.example.json"), Catalog).Cv!;

        var lead = cv.Bullets.Single(b => b.Id == "acme-lead");
        Assert.True(lead.Always);
        Assert.Superset(new HashSet<string> { "Technical Leadership", ".NET", "Next.js" }, (ISet<string>)lead.Skills.ToHashSet());
        Assert.False(cv.Bullets.Single(b => b.Id == "acme-cicd").Always);
    }

    [Fact]
    public void Every_problem_is_reported_with_its_path()
    {
        var load = LoadText("""
            {
              "person": { "name": "Sam", "links": [ { "label": "Site", "url": "example.com" } ] },
              "headline": "Developer",
              "summaries": [ { "id": "s1", "families": ["dotnet", "chef"], "text": "Hi." } ],
              "roles": [
                { "id": "r1", "title": "Dev", "start": "2023-13", "maxBullets": 0,
                  "bullets": [ { "id": "x-1", "text": "One." }, { "id": "x-1", "text": "Two.", "skills": ["Cobol"], "always": "yes" } ] }
              ]
            }
            """);

        Assert.Null(load.Cv);
        Assert.Equal(new[]
        {
            "person.email is required.",
            "person.links[0].url: use a full web address starting with https://.",
            "summaries[0].families[1]: unknown family 'chef' (use dotnet, fullstack, software_engineer, lead_manager or other).",
            "roles[0].company is required.",
            "roles[0].start: use year and month like 2023-10, not '2023-13'.",
            "roles[0].maxBullets must be a whole number, 1 or more.",
            "roles[0].bullets[1].id: \"x-1\" is used twice.",
            "roles[0].bullets[1].always must be true or false.",
            "roles[0].bullets[1].skills[0]: 'Cobol' is not a skill in config/skills.json (names must match exactly).",
        }, load.Problems);
    }

    [Fact]
    public void A_missing_file_is_one_problem_naming_the_example() =>
        Assert.Equal(new[] { MasterCvFile.Missing },
            MasterCvFile.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "cv.json"), Catalog).Problems);

    [Fact]
    public void Broken_json_names_the_line() =>
        Assert.Equal(new[] { "config/cv.json is not valid JSON near line 3." },
            LoadText("{\n  \"headline\": \"Dev\",\n  \"person\": { oops }\n}").Problems);

    // Half of an emoji typed as a \u escape: valid JSON syntax, but not text .NET can read. A problem, never a 500.
    [Fact]
    public void A_lone_surrogate_escape_is_a_problem()
    {
        var load = LoadText("""{ "person": { "name": "Sam \uD83D", "email": "s@example.com" }, "headline": "Dev" }""");

        Assert.Null(load.Cv);
        Assert.Contains("\\u", Assert.Single(load.Problems));
    }

    [Fact]
    public void Leaving_out_optional_parts_uses_the_defaults()
    {
        var load = LoadText("""
            { "person": { "name": "Sam", "email": "sam@example.com" }, "headline": "Developer",
              "summaries": [ { "id": "s", "text": "Hi." } ],
              "roles": [ { "id": "r", "title": "Dev", "company": "Co", "start": "2020-01", "bullets": [ { "id": "b", "text": "Did C# work." } ] } ] }
            """);

        var cv = Assert.IsType<MasterCv>(load.Cv);
        var role = Assert.Single(cv.Sections);
        Assert.Equal((5, "Jan 2020 – Present", false), (role.MaxBullets, role.Dates, role.Bullets[0].Always));
        Assert.Contains("C#", role.Bullets[0].Skills);
        Assert.Empty(cv.SkillGroups);
        Assert.Empty(cv.Certifications);
    }

    [Fact]
    public void A_file_without_roles_or_summaries_says_what_to_add() =>
        Assert.Equal(new[] { "summaries: add at least one summary.", "roles: add at least one role." },
            LoadText("""{ "person": { "name": "Sam", "email": "s@example.com" }, "headline": "Dev" }""").Problems);

    [Fact]
    public void The_real_master_cv_is_gitignored() =>
        Assert.Contains("config/cv.json", File.ReadAllLines(Path.Combine(RepoConfig.Root, ".gitignore")));

    [Fact]
    public void An_end_before_its_start_and_a_project_end_without_a_start_are_problems()
    {
        var load = LoadText("""
            { "person": { "name": "Sam", "email": "sam@example.com" }, "headline": "Developer",
              "summaries": [ { "id": "s", "text": "Hi." } ],
              "roles": [ { "id": "r", "title": "Dev", "company": "Co", "start": "2023-10", "end": "2021-05", "bullets": [ { "id": "b", "text": "Did work." } ] } ],
              "projects": [ { "id": "p", "title": "Tool", "end": "2024-01", "bullets": [ { "id": "pb", "text": "Built it." } ] } ] }
            """);

        Assert.Equal(new[] { "roles[0].end: 2021-05 is before the start, 2023-10.", "projects[0].start is required when end is set." }, load.Problems);
    }
}
