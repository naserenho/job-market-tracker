using Tracker.Analysis;

namespace Api.Cv;

// A skill the posting asks for: weight 2 when the title names it too. The owner's level comes from config/profile.json.
public sealed record PostingSkill(string Skill, int Weight, SkillLevel Level);

public sealed record ProposalBullet(string Id, string Text, IReadOnlyList<string> Skills, int Score, bool Always);

public sealed record ProposalSection(string Id, CvSectionKind Kind, string Title, string Company, string Location, string Dates,
    int MaxBullets, IReadOnlyList<ProposalBullet> Bullets);

public sealed record ProposalSkillItem(string Text, IReadOnlyList<string> Skills, bool Matches);

public sealed record ProposalSkillGroup(string Name, IReadOnlyList<ProposalSkillItem> Items);

public sealed record ProposalSummary(string Id, IReadOnlyList<string> Families, string Text);

public sealed record ProposalCertification(string Name, string Year, IReadOnlyList<string> Skills);

// The newest generated CV's choices, minus bullets cv.json no longer has (listed in DroppedIds).
public sealed record SavedChoices(DateOnly GeneratedOn, string Headline, string Summary, IReadOnlyList<string> BulletIds,
    IReadOnlyList<string> DroppedIds);

// The entry as tailoring needs it. Its text is already resolved: entry text first, then the market description.
public sealed record TailorInput(Guid EntryId, string Title, string Company, string Text, string TextSource, bool Snippet, uint Version);

// Everything the Tailor page shows. TextSource is "entry", "market" or "none"; Version is the entry's, for the job-text form.
public sealed record CvProposal(Guid EntryId, string Title, string Company, string TextSource, bool Thin, uint Version,
    IReadOnlyList<PostingSkill> PostingSkills, string Headline, IReadOnlyList<ProposalSummary> Summaries, string SuggestedSummaryId,
    IReadOnlyList<ProposalSkillGroup> SkillGroups, IReadOnlyList<ProposalSection> Sections, IReadOnlyList<ProposalCertification> Certifications,
    CvChoices Suggestion, SavedChoices? Saved, int FixedWords);
