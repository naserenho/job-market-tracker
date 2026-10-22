using Tracker.Analysis;
using Tracker.Interpreting;

namespace Api.Cv;

// Proposes a CV for one posting by choosing and ordering the owner's own bullets (spec "Proposal and pre-selection").
// It never writes text: everything it returns comes from config/cv.json.
public static class CvTailor
{
    public const int ThinSkillCount = 3;

    // The posting's skills, found the same way as the market data's, heaviest first.
    public static IReadOnlyList<PostingSkill> PostingSkills(SkillCatalog catalog, Profile profile, string title, string text)
    {
        var inTitle = catalog.Match(title);
        return [.. catalog.Match(SkillText.ForMatching(title, text))
            .Select(s => new PostingSkill(s, inTitle.Contains(s) ? 2 : 1, profile.LevelOf(s)))
            .OrderByDescending(s => s.Weight).ThenBy(s => s.Skill, StringComparer.Ordinal)];
    }

    // Too little to tailor from: fewer than three skills, or only a search-result snippet of the posting.
    public static bool IsThin(int skillCount, bool snippet) => snippet || skillCount < ThinSkillCount;

    public static int Score(CvBullet bullet, IReadOnlyDictionary<string, int> weights) =>
        bullet.Skills.Sum(s => weights.GetValueOrDefault(s));

    // `always` bullets first (even past the maximum), then the best scorers, then the rest in file order, up to
    // MaxBullets; the chosen ones print best score first, ties in file order.
    public static IReadOnlyList<string> Suggest(CvSection section, IReadOnlyDictionary<string, int> weights)
    {
        var ranked = section.Bullets.Select((bullet, index) => (Bullet: bullet, Index: index, Score: Score(bullet, weights))).ToList();
        var chosen = ranked.Where(x => x.Bullet.Always).ToList();
        var candidates = ranked.Where(x => !x.Bullet.Always && x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.Index)
            .Concat(ranked.Where(x => !x.Bullet.Always && x.Score == 0));
        foreach (var x in candidates)
        {
            if (chosen.Count >= section.MaxBullets) break;
            chosen.Add(x);
        }
        return [.. chosen.OrderByDescending(x => x.Score).ThenBy(x => x.Index).Select(x => x.Bullet.Id)];
    }

    public static CvSummary SuggestSummary(MasterCv cv, string title)
    {
        var family = TitleRules.Family(title);
        return cv.Summaries.FirstOrDefault(s => s.Families.Contains(family)) ?? cv.Summaries[0];
    }

    public static CvChoices Suggestion(MasterCv cv, IReadOnlyDictionary<string, int> weights, string title) =>
        new(cv.Headline, SuggestSummary(cv, title).Text, [.. cv.Sections.SelectMany(s => Suggest(s, weights))]);

    public static SavedChoices Saved(MasterCv cv, CvChoices choices, DateOnly generatedOn)
    {
        var known = cv.Bullets.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        return new(generatedOn, choices.Headline, choices.Summary,
            [.. choices.BulletIds.Where(known.Contains)], [.. choices.BulletIds.Where(id => !known.Contains(id))]);
    }

    public static CvProposal Propose(MasterCv cv, SkillCatalog catalog, Profile profile, TailorInput entry, (CvChoices Choices, DateOnly On)? saved)
    {
        var skills = PostingSkills(catalog, profile, entry.Title, entry.Text);
        var weights = skills.ToDictionary(s => s.Skill, s => s.Weight, StringComparer.Ordinal);
        var wanted = weights.Keys.ToHashSet(StringComparer.Ordinal);
        return new CvProposal(entry.EntryId, entry.Title, entry.Company, entry.TextSource, IsThin(skills.Count, entry.Snippet), entry.Version,
            skills, cv.Headline,
            [.. cv.Summaries.Select(s => new ProposalSummary(s.Id, [.. s.Families.Select(TitleRules.FamilyKey).Order(StringComparer.Ordinal)], s.Text))],
            SuggestSummary(cv, entry.Title).Id,
            [.. cv.SkillGroups.Select(g => new ProposalSkillGroup(g.Name,
                [.. CvLayout.OrderItems(g, wanted).Select(i => new ProposalSkillItem(i.Text, Sorted(i.Skills), i.Skills.Overlaps(wanted)))]))],
            [.. cv.Sections.Select(s => new ProposalSection(s.Id, s.Kind, s.Title, s.Company, s.Location, s.Dates, s.MaxBullets,
                [.. s.Bullets.Select(b => new ProposalBullet(b.Id, b.Text, Sorted(b.Skills), Score(b, weights), b.Always))]))],
            [.. cv.Certifications.Select(c => new ProposalCertification(c.Name, c.Year, Sorted(c.Skills)))],
            Suggestion(cv, weights, entry.Title),
            saved is { } previous ? Saved(cv, previous.Choices, previous.On) : null,
            CvLayout.FixedWords(cv));
    }

    private static IReadOnlyList<string> Sorted(IReadOnlySet<string> skills) => [.. skills.Order(StringComparer.Ordinal)];
}
