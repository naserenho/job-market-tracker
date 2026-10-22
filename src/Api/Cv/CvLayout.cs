namespace Api.Cv;

// What the owner chose for one CV. It is saved with a generated file, so the Tailor page can start from it next time.
public sealed record CvChoices(string Headline, string Summary, IReadOnlyList<string> BulletIds);

public enum CvStyle { Name, Contact, Headline, Heading, EntryTitle, EntryMeta, Paragraph, SkillLine, Bullet }

// A piece of a paragraph: plain, bold, or a link that shows its address.
public sealed record CvRun(string Text, bool Bold = false, string? Url = null);

public sealed record CvParagraph(CvStyle Style, IReadOnlyList<CvRun> Runs)
{
    public string Text => string.Concat(Runs.Select(r => r.Text));

    public static CvParagraph Plain(CvStyle style, string text) => new(style, [new CvRun(text)]);
}

// The CV's content in page order, before any Word formatting. The .docx renderer, the page's word count and the tests
// all read this one list, so the text an applicant-tracking system sees is the text tested here.
public static class CvLayout
{
    public const string Separator = " · ";
    private static readonly IReadOnlySet<string> NoSkills = new HashSet<string>();

    public static IReadOnlyList<CvParagraph> Paragraphs(MasterCv cv, CvChoices choices, IReadOnlySet<string> postingSkills)
    {
        var list = new List<CvParagraph> { CvParagraph.Plain(CvStyle.Name, cv.Person.Name), Contact(cv.Person) };
        var headline = choices.Headline.Trim();
        if (headline.Length > 0) list.Add(CvParagraph.Plain(CvStyle.Headline, headline));
        var summary = choices.Summary.Trim();
        if (summary.Length > 0)
        {
            list.Add(CvParagraph.Plain(CvStyle.Heading, "Summary"));
            list.Add(CvParagraph.Plain(CvStyle.Paragraph, summary));
        }
        if (cv.SkillGroups.Count > 0)
        {
            list.Add(CvParagraph.Plain(CvStyle.Heading, "Skills"));
            list.AddRange(cv.SkillGroups.Select(g => new CvParagraph(CvStyle.SkillLine,
                [new CvRun($"{g.Name}: ", Bold: true), new CvRun(string.Join(", ", OrderItems(g, postingSkills).Select(i => i.Text)))])));
        }
        Entries(list, "Experience", [.. cv.Sections.Where(s => s.Kind == CvSectionKind.Role)], choices);
        Entries(list, "Projects", [.. cv.Sections.Where(s => s.Kind == CvSectionKind.Project)], choices);
        if (cv.Education.Count > 0)
        {
            list.Add(CvParagraph.Plain(CvStyle.Heading, "Education"));
            list.AddRange(cv.Education.Select(e => CvParagraph.Plain(CvStyle.Paragraph,
                $"{e.Degree} — {e.School}{(e.Years.Length > 0 ? $" ({e.Years})" : "")}")));
        }
        if (cv.Certifications.Count > 0)
        {
            list.Add(CvParagraph.Plain(CvStyle.Heading, "Certifications"));
            list.AddRange(cv.Certifications.Select(c => CvParagraph.Plain(CvStyle.Paragraph, c.Year.Length > 0 ? $"{c.Name} ({c.Year})" : c.Name)));
        }
        return list;
    }

    // Items that show a posting skill come first; each half keeps the file's order.
    public static IReadOnlyList<CvSkillItem> OrderItems(CvSkillGroup group, IReadOnlySet<string> postingSkills) =>
        [.. group.Items.Where(i => i.Skills.Overlaps(postingSkills)), .. group.Items.Where(i => !i.Skills.Overlaps(postingSkills))];

    public static int Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    public static int Words(IEnumerable<CvParagraph> paragraphs) => paragraphs.Sum(p => Words(p.Text));

    // Everything on the page except the headline, the summary and the bullets: the Tailor page adds those as they change.
    public static int FixedWords(MasterCv cv) => Words(Paragraphs(cv, new CvChoices("", "", []), NoSkills));

    private static CvParagraph Contact(CvPerson person)
    {
        var parts = new[] { person.Location, person.Status, person.Phone, person.Email }.Where(p => p.Length > 0).Select(p => new CvRun(p))
            .Concat(person.Links.Select(l => new CvRun(l.Url, Url: l.Url)));
        var runs = new List<CvRun>();
        foreach (var part in parts)
        {
            if (runs.Count > 0) runs.Add(new CvRun(Separator));
            runs.Add(part);
        }
        return new CvParagraph(CvStyle.Contact, runs);
    }

    // Every role or project keeps its title line, even with no bullets chosen, so the work history stays complete.
    private static void Entries(List<CvParagraph> list, string heading, IReadOnlyList<CvSection> sections, CvChoices choices)
    {
        if (sections.Count == 0) return;
        list.Add(CvParagraph.Plain(CvStyle.Heading, heading));
        foreach (var section in sections)
        {
            list.Add(CvParagraph.Plain(CvStyle.EntryTitle, section.Company.Length > 0 ? $"{section.Title} — {section.Company}" : section.Title));
            var meta = string.Join(" | ", new[] { section.Location, section.Dates }.Where(p => p.Length > 0));
            if (meta.Length > 0) list.Add(CvParagraph.Plain(CvStyle.EntryMeta, meta));
            var byId = section.Bullets.ToDictionary(b => b.Id, StringComparer.Ordinal);
            list.AddRange(choices.BulletIds.Where(byId.ContainsKey).Select(id => CvParagraph.Plain(CvStyle.Bullet, byId[id].Text)));
        }
    }
}
