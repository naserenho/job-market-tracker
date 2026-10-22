using Api.Cv;
using static Api.Tests.Cv.CvSamples;

namespace Api.Tests.Cv;

public class CvLayoutTests
{
    private static readonly IReadOnlySet<string> None = Set();

    [Fact]
    public void Paragraphs_follow_the_page_order()
    {
        var choices = new CvChoices("Senior .NET Developer", "Builds .NET systems.", ["a2", "a1", "g2"]);

        var paragraphs = CvLayout.Paragraphs(SampleCv(), choices, None);

        Assert.Equal(new (CvStyle, string)[]
        {
            (CvStyle.Name, "Alex Example"),
            (CvStyle.Contact, "Toronto, ON · Canadian Permanent Resident · +1 416 555 0100 · alex@example.com · https://github.com/alex-example"),
            (CvStyle.Headline, "Senior .NET Developer"),
            (CvStyle.Heading, "Summary"),
            (CvStyle.Paragraph, "Builds .NET systems."),
            (CvStyle.Heading, "Skills"),
            (CvStyle.SkillLine, "Backend: C# / .NET, SQL Server"),
            (CvStyle.SkillLine, "Cloud: Docker, Azure DevOps"),
            (CvStyle.Heading, "Experience"),
            (CvStyle.EntryTitle, "Senior Developer — Acme Corp"),
            (CvStyle.EntryMeta, "Toronto, ON | Oct 2023 – Present"),
            (CvStyle.Bullet, "Built CI/CD."),
            (CvStyle.Bullet, "Led the team."),
            (CvStyle.EntryTitle, "Developer — Globex"),
            (CvStyle.EntryMeta, "Toronto, ON | Oct 2023 – Present"),
            (CvStyle.Bullet, "Built a React UI."),
            (CvStyle.Heading, "Education"),
            (CvStyle.Paragraph, "BSc Computer Engineering — Example University (2012-2016)"),
            (CvStyle.Heading, "Certifications"),
            (CvStyle.Paragraph, "AZ-204 (2026)"),
        }, paragraphs.Select(p => (p.Style, p.Text)));
    }

    [Fact]
    public void Empty_parts_are_left_out_but_every_role_keeps_its_title()
    {
        var cv = SampleCv(groups: [], education: [], certifications: [], person: Person with { Status = "", Phone = "", Links = [] });

        var texts = CvLayout.Paragraphs(cv, new CvChoices("Developer", "", []), None).Select(p => p.Text);

        Assert.Equal(new[]
        {
            "Alex Example", "Toronto, ON · alex@example.com", "Developer",
            "Experience", "Senior Developer — Acme Corp", "Toronto, ON | Oct 2023 – Present", "Developer — Globex", "Toronto, ON | Oct 2023 – Present",
        }, texts);
    }

    [Fact]
    public void Projects_show_under_their_own_heading_without_empty_details()
    {
        var cv = SampleCv(sections: [Role("acme", "Dev", "Acme", 3, B("a1", "Did it.")), Project("tracker", "Job Market Tracker", B("t1", "Built a dashboard."))],
            groups: [], education: [], certifications: []);

        var texts = CvLayout.Paragraphs(cv, new CvChoices("Dev", "", ["t1"]), None).Select(p => p.Text);

        Assert.Equal(new[] { "Projects", "Job Market Tracker", "Built a dashboard." }, texts.TakeLast(3));
    }

    [Fact]
    public void Skill_items_that_match_the_posting_come_first()
    {
        var line = CvLayout.Paragraphs(SampleCv(), new CvChoices("Dev", "", []), Set("Azure DevOps")).Single(p => p.Text.StartsWith("Cloud"));

        Assert.Equal("Cloud: Azure DevOps, Docker", line.Text);
        Assert.True(line.Runs[0].Bold);
    }

    [Fact]
    public void Links_are_runs_that_show_their_address()
    {
        var contact = CvLayout.Paragraphs(SampleCv(), new CvChoices("Dev", "", []), None)[1];

        var link = Assert.Single(contact.Runs, r => r.Url is not null);
        Assert.Equal(("https://github.com/alex-example", "https://github.com/alex-example"), (link.Text, link.Url));
    }

    [Fact]
    public void Fixed_words_count_everything_but_the_headline_summary_and_bullets()
    {
        var cv = SampleCv();
        var all = CvLayout.Words(CvLayout.Paragraphs(cv, new CvChoices("Senior Developer", "Two words", ["a1"]), None));

        // headline 2, "Summary" heading 1, summary 2, "Led the team." 3
        Assert.Equal(all - 2 - 1 - 2 - 3, CvLayout.FixedWords(cv));
    }
}
