using Tracker.Interpreting;

namespace Api.Cv;

public sealed record CvLink(string Label, string Url);

public sealed record CvPerson(string Name, string Location, string Status, string Phone, string Email, IReadOnlyList<CvLink> Links);

// A summary variant, suggested for postings whose title falls in one of its role families.
public sealed record CvSummary(string Id, IReadOnlySet<RoleFamily> Families, string Text);

// Skills: the tags written in cv.json plus every skill the catalog finds in the text.
public sealed record CvSkillItem(string Text, IReadOnlySet<string> Skills);

public sealed record CvSkillGroup(string Name, IReadOnlyList<CvSkillItem> Items);

public sealed record CvBullet(string Id, string Text, IReadOnlySet<string> Skills, bool Always);

public enum CvSectionKind { Role, Project }

// A role or a project. Dates are already formatted ("Oct 2023 – Present"; empty for an undated project).
public sealed record CvSection(string Id, CvSectionKind Kind, string Title, string Company, string Location, string Dates,
    int MaxBullets, IReadOnlyList<CvBullet> Bullets);

public sealed record CvEducation(string Degree, string School, string Years);

public sealed record CvCertification(string Name, string Year, IReadOnlySet<string> Skills);

// config/cv.json after validation: every true bullet the owner can put on a CV. Roles come first, then projects.
public sealed record MasterCv(CvPerson Person, string Headline, IReadOnlyList<CvSummary> Summaries, IReadOnlyList<CvSkillGroup> SkillGroups,
    IReadOnlyList<CvSection> Sections, IReadOnlyList<CvEducation> Education, IReadOnlyList<CvCertification> Certifications)
{
    public IEnumerable<CvBullet> Bullets => Sections.SelectMany(s => s.Bullets);
}

// Either a usable master CV or the problems that stop it loading.
public sealed record CvLoad(MasterCv? Cv, IReadOnlyList<string> Problems)
{
    public bool Ready => Cv is not null;
}
