using Api.Cv;
using Api.Tests.TestSupport;
using Tracker.Analysis;
using static Api.Tests.Cv.CvSamples;

namespace Api.Tests.Cv;

public class CvTailorTests
{
    private static Dictionary<string, int> W(params string[] skills) => skills.ToDictionary(s => s, _ => 1, StringComparer.Ordinal);

    [Fact]
    public void Posting_skills_weigh_double_when_the_title_names_them()
    {
        // profile.example.json: C# and .NET strong, React learning.
        var skills = CvTailor.PostingSkills(Data.Settings.Catalog, Data.Settings.Profile, "Senior .NET Developer",
            "C# and React. We will accommodate accessibility needs.");

        Assert.Equal(new[]
        {
            new PostingSkill(".NET", 2, SkillLevel.Strong), new PostingSkill("C#", 1, SkillLevel.Strong), new PostingSkill("React", 1, SkillLevel.Learning),
        }, skills);
    }

    [Fact]
    public void Always_bullets_stay_and_the_best_scorers_fill_up_to_max()
    {
        var acme = SampleCv().Sections[0]; // max 3: a1 always, a2 [Azure DevOps, CI/CD], a3 [SQL Server], a4 [Docker]
        Assert.Equal(new[] { "a2", "a4", "a1" }, CvTailor.Suggest(acme, W("Azure DevOps", "CI/CD", "Docker")));
    }

    [Fact]
    public void Leftover_slots_are_filled_in_file_order() =>
        Assert.Equal(new[] { "a4", "a1", "a2" }, CvTailor.Suggest(SampleCv().Sections[0], W("Docker")));

    [Fact]
    public void Always_bullets_are_kept_even_beyond_the_maximum()
    {
        var section = Role("r", "Dev", "Co", 1, B("x", "One.", always: true), B("y", "Two.", always: true), B("z", "Three.", ["C#"]));
        Assert.Equal(new[] { "x", "y" }, CvTailor.Suggest(section, W("C#")));
    }

    [Fact]
    public void Ties_keep_the_file_order()
    {
        var section = Role("r", "Dev", "Co", 2, B("x", "One.", ["A"]), B("y", "Two.", ["B"]), B("z", "Three.", ["C"]));
        Assert.Equal(new[] { "x", "y" }, CvTailor.Suggest(section, W("A", "B", "C")));
    }

    [Fact]
    public void Title_skills_outweigh_others()
    {
        var section = Role("r", "Dev", "Co", 1, B("x", "One.", ["React"]), B("y", "Two.", ["C#"]));
        Assert.Equal(new[] { "y" }, CvTailor.Suggest(section, new Dictionary<string, int> { ["C#"] = 2, ["React"] = 1 }));
    }

    [Theory]
    [InlineData("Senior .NET Developer", "dotnet")]
    [InlineData("Engineering Manager", "lead")]
    [InlineData("Pastry Chef", "dotnet")] // no summary for "other": the first one
    public void The_summary_follows_the_title_s_role_family(string title, string expected) =>
        Assert.Equal(expected, CvTailor.SuggestSummary(SampleCv(), title).Id);

    [Fact]
    public void Saved_choices_drop_bullets_the_cv_no_longer_has()
    {
        var saved = CvTailor.Saved(SampleCv(), new CvChoices("Dev", "Hi.", ["a2", "gone-1", "g1"]), new DateOnly(2027, 3, 1));

        Assert.Equal(new[] { "a2", "g1" }, saved.BulletIds);
        Assert.Equal(new[] { "gone-1" }, saved.DroppedIds);
    }

    [Theory]
    [InlineData(2, false, true)]
    [InlineData(3, false, false)]
    [InlineData(8, true, true)]
    public void Thin_means_fewer_than_three_skills_or_a_snippet(int skills, bool snippet, bool thin) =>
        Assert.Equal(thin, CvTailor.IsThin(skills, snippet));

    [Fact]
    public void The_proposal_puts_it_together()
    {
        var input = new TailorInput(Guid.NewGuid(), "Senior .NET Developer", "Acme", "Azure DevOps and CI/CD.", "entry", Snippet: false, Version: 7);

        var proposal = CvTailor.Propose(SampleCv(), Data.Settings.Catalog, Data.Settings.Profile, input, saved: null);

        Assert.False(proposal.Thin);
        Assert.Equal(("dotnet", "Senior .NET Developer", "Builds .NET systems end to end."),
            (proposal.SuggestedSummaryId, proposal.Suggestion.Headline, proposal.Suggestion.Summary));
        Assert.Equal(new[] { "a1", "a2", "a3", "g1", "g2" }, proposal.Suggestion.BulletIds);
        Assert.Equal(new[] { "Azure DevOps", "Docker" }, proposal.SkillGroups[1].Items.Select(i => i.Text));
        Assert.Equal((true, false), (proposal.SkillGroups[1].Items[0].Matches, proposal.SkillGroups[1].Items[1].Matches));
        Assert.Equal(2, proposal.Sections[0].Bullets.Single(b => b.Id == "a2").Score);
        Assert.Equal(new[] { "dotnet", "fullstack" }, proposal.Summaries[0].Families);
        Assert.Equal(CvLayout.FixedWords(SampleCv()), proposal.FixedWords);
        Assert.Null(proposal.Saved);
    }

    [Fact]
    public void The_proposal_carries_saved_choices()
    {
        var input = new TailorInput(Guid.NewGuid(), "Developer", "Acme", "C#", "entry", Snippet: false, Version: 7);

        var proposal = CvTailor.Propose(SampleCv(), Data.Settings.Catalog, Data.Settings.Profile, input,
            (new CvChoices("Lead dev", "Hi.", ["g2", "nope"]), new DateOnly(2027, 3, 1)));

        Assert.Equal(("Lead dev", new DateOnly(2027, 3, 1)), (proposal.Saved!.Headline, proposal.Saved.GeneratedOn));
        Assert.Equal(new[] { "nope" }, proposal.Saved.DroppedIds);
    }
}
