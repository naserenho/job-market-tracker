using Tracker.Interpreting;

namespace Tracker.Core.Tests.Interpreting;

public class DeduplicatorTests
{
    private static Posting P(string id, string company, string title, string? city, bool fullText, params string[] runs) =>
        new(id, id.Split(':')[0], id.Split(':')[1], company, title, "", "", new Scope(city, city is not null, false),
            RoleFamily.Fullstack, Seniority.Senior, null, "none", "", fullText, false, new HashSet<string>(), runs, null);

    [Fact]
    public void The_same_title_months_apart_is_a_new_posting_not_a_duplicate()
    {
        var result = Deduplicator.MarkDuplicates([
            P("greenhouse:1", "Acme", "Developer", "Toronto", true, "r1"),
            P("greenhouse:2", "Acme", "Developer", "Toronto", true, "r3"),
        ], RunOrder);
        Assert.All(result, p => Assert.Null(p.DuplicateOf));
    }

    [Fact]
    public void The_kept_posting_takes_a_duplicates_salary_and_all_sightings()
    {
        var salary = new Tracker.Reading.SalaryRange(100_000, 120_000, Tracker.Reading.SalaryPeriod.Year);
        var result = Deduplicator.MarkDuplicates([
            P("adzuna:A", "Acme", "Developer", "Toronto", false, "r2", "r3") with { Salary = salary, SalaryOrigin = "structured" },
            P("greenhouse:G", "Acme", "Developer", "Toronto", true, "r1", "r2"),
        ], RunOrder);

        var kept = result.Single(p => p.Id == "greenhouse:G");
        Assert.Null(kept.DuplicateOf);
        Assert.Equal(salary, kept.Salary);
        Assert.Equal("structured", kept.SalaryOrigin);
        Assert.Equal(new[] { "r1", "r2", "r3" }, kept.SightingRuns);
        Assert.Equal("greenhouse:G", result.Single(p => p.Id == "adzuna:A").DuplicateOf);
    }

    private static readonly string[] RunOrder = ["r1", "r2", "r3"];

    [Fact]
    public void Cross_source_copy_points_at_the_full_text_posting()
    {
        var result = Deduplicator.MarkDuplicates([
            P("adzuna:A3", "Acme", "Senior Full Stack Developer", "Toronto", false, "r3"),
            P("greenhouse:101", "Acme Inc.", "Senior Full-Stack Developer", "Toronto", true, "r1", "r2", "r3"),
        ], RunOrder);

        Assert.Null(result.Single(p => p.Id == "greenhouse:101").DuplicateOf);
        Assert.Equal("greenhouse:101", result.Single(p => p.Id == "adzuna:A3").DuplicateOf);
    }

    [Fact]
    public void Same_title_in_different_cities_is_not_a_duplicate()
    {
        var result = Deduplicator.MarkDuplicates([
            P("greenhouse:1", "Acme", "Developer", "Toronto", true, "r1"),
            P("greenhouse:2", "Acme", "Developer", "Mississauga", true, "r1"),
        ], RunOrder);
        Assert.All(result, p => Assert.Null(p.DuplicateOf));
    }

    [Fact]
    public void Among_full_text_copies_the_most_recently_seen_wins()
    {
        var result = Deduplicator.MarkDuplicates([
            P("greenhouse:1", "Acme", "Developer", "Toronto", true, "r1"),
            P("lever:9", "Acme", "Developer", "Toronto", true, "r1", "r3"),
        ], RunOrder);
        Assert.Equal("lever:9", result.Single(p => p.Id == "greenhouse:1").DuplicateOf);
    }

    [Theory]
    [InlineData("Acme Inc.", "acme")]
    [InlineData("The Example Company Ltd", "example")]
    [InlineData("Senior Full-Stack Developer", "senior full stack developer")]
    [InlineData("C# / .NET Developer", "c# net developer")]
    public void Normalize(string input, string expected) => Assert.Equal(expected, Deduplicator.Normalize(input));
}
