using System.Globalization;
using System.Net;
using System.Text;
using Tracker.Analysis;
using Tracker.Configuration;
using Tracker.Interpreting;
using Tracker.Reading;

namespace Tracker.Reporting;

// The report for one region: its title, labels, number and date format, currency and sources come from the region file.
public sealed class ReportRenderer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly Region _region;
    private readonly CultureInfo _culture;

    private ReportRenderer(Region region)
    {
        _region = region;
        _culture = region.CultureInfo;
    }

    private const string FontLink =
        "<link rel=\"stylesheet\" href=\"https://fonts.googleapis.com/css2?family=Archivo:wdth,wght@87.5,700;87.5,800&amp;family=IBM+Plex+Mono:wght@400;500&amp;family=Source+Sans+3:wght@400;600&amp;display=swap\">";

    // A claude.ai page fragment: the viewer supplies doctype/html/head/body, so the title and styles come first.
    public static string RenderFragment(ReportModel m, Region region) => new ReportRenderer(region).Fragment(m);

    private string Fragment(ReportModel m)
    {
        var h = new StringBuilder();
        h.Append("<title>").Append(E(_region.Title)).Append("</title>\n").Append(FontLink).Append('\n');
        h.Append("<style>").Append(Css).Append("</style>\n");
        h.Append("<main class=\"report\">\n");
        Masthead(h, m);
        SnapshotRow(h, m.Snapshot);
        if (m.Areas is not null) AreaSection(h, m);
        SkillBars(h, m);
        GapsAndStrengths(h, m);
        SalaryTable(h, m);
        MatchList(h, m);
        CompanyList(h, m);
        if (m.Trends is not null) TrendSection(h, m);
        if (_region.JobBank is not null) JobBankTable(h, m);
        Method(h, m);
        h.Append("</main>\n");
        return h.ToString();
    }

    // The same page as a standalone file for opening on this PC.
    public static string RenderDocument(string fragment) =>
        "<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1, viewport-fit=cover\">\n</head>\n<body>\n"
        + fragment + "</body>\n</html>\n";

    private void Masthead(StringBuilder h, ReportModel m)
    {
        h.Append("<header class=\"masthead\"><p class=\"eyebrow\">").Append(E(_region.LocalLabel)).Append(" · ").Append(E(_region.RemoteLabel)).Append(" · updated ")
            .Append(E(m.LatestRunAt.ToString("MMMM d, yyyy", _culture))).Append("</p>")
            .Append("<h1>").Append(E(_region.Title)).Append("</h1>")
            .Append("<p class=\"lede\">").Append(Num(m.Snapshot.InWindow)).Append(" postings in your target roles over the last ")
            .Append(m.WindowDays).Append(" days, ").Append(Num(m.Snapshot.ActiveNow)).Append(" open right now.");
        if (m.Gaps.Count > 0) h.Append(" Your biggest gap: <strong>").Append(E(m.Gaps[0].Skill)).Append("</strong>.");
        h.Append("</p></header>\n");
    }

    private void SnapshotRow(StringBuilder h, Snapshot s)
    {
        h.Append("<section class=\"stats\" aria-label=\"Snapshot\">");
        Stat(h, "Open now", Num(s.ActiveNow), "seen in the latest run");
        Stat(h, "New since last run", Num(s.NewSinceLastRun), s.Runs > 1 ? "first seen this run" : "needs a second run");
        Stat(h, "State a salary", Pct(s.InWindow == 0 ? 0 : (double)s.WithSalary / s.InWindow), $"{Num(s.WithSalary)} of {Num(s.InWindow)} postings");
        Stat(h, "History", s.HistoryDays == 1 ? "1 day" : $"{s.HistoryDays} days", $"{s.Runs} runs collected");
        h.Append("</section>\n");
    }

    private void Stat(StringBuilder h, string label, string value, string note) =>
        h.Append("<div class=\"stat\"><p class=\"stat-label\">").Append(E(label)).Append("</p><p class=\"stat-value\">")
            .Append(E(value)).Append("</p><p class=\"stat-note\">").Append(E(note)).Append("</p></div>");

    private void SkillBars(StringBuilder h, ReportModel m)
    {
        Open(h, "skills", "Skills in demand",
            $"Share of the {Num(m.Snapshot.FullText)} full-text postings in your target roles over the last {m.WindowDays} days that mention each skill."
            + (_region.Adzuna is null ? "" : $" Adzuna snippet postings ({Num(m.Snapshot.Snippets)}) are too short to count here."));
        if (m.TopSkills.Count == 0)
        {
            Empty(h, "No full-text postings in the window yet.");
        }
        else
        {
            var max = m.TopSkills.Max(s => s.FullTextShare);
            h.Append("<ol class=\"bars\">");
            foreach (var s in m.TopSkills)
            {
                var width = max <= 0 ? 0 : s.FullTextShare / max * 100;
                h.Append("<li class=\"bar-row\"><span class=\"bar-name\">").Append(E(s.Skill)).Append("</span>")
                    .Append("<span class=\"bar-track\" aria-hidden=\"true\"><span class=\"bar-fill\" style=\"width:").Append(width.ToString("0.#", Inv)).Append("%\"></span></span>")
                    .Append("<span class=\"bar-value\">").Append(E(Pct(s.FullTextShare))).Append("</span>")
                    .Append(Chip(s.Level)).Append("</li>");
            }
            h.Append("</ol>");
        }
        Close(h);
    }

    private void GapsAndStrengths(StringBuilder h, ReportModel m)
    {
        h.Append("<section id=\"gaps\" class=\"pair\" aria-label=\"Gaps and strengths\">");
        Ranked(h, "Your gaps", "Demand × how far you are from the skill. Close these first.", m.Gaps);
        Ranked(h, "Strengths employers want", "Demand × your level. Lead your CV with these.", m.Strengths);
        h.Append("</section>\n");
    }

    private void Ranked(StringBuilder h, string title, string note, IReadOnlyList<SkillScore> items)
    {
        h.Append("<div class=\"col\"><h2>").Append(E(title)).Append("</h2><p class=\"note\">").Append(E(note)).Append("</p>");
        if (items.Count == 0)
        {
            Empty(h, "Nothing to rank yet.");
        }
        else
        {
            h.Append("<ol class=\"ranked\">");
            foreach (var s in items)
                h.Append("<li><span class=\"rank-line\"><span class=\"rank-name\">").Append(E(s.Skill)).Append("</span>")
                    .Append("<span class=\"rank-meta\">in ").Append(E(Pct(s.Demand))).Append(" of postings</span>")
                    .Append(Chip(s.Level)).Append("</span></li>");
            h.Append("</ol>");
        }
        h.Append("</div>");
    }

    // Only for a region that groups its places into areas (Ontario): how the cities compare.
    private void AreaSection(StringBuilder h, ReportModel m)
    {
        var a = m.Areas!;
        Open(h, "areas", "Where the jobs are",
            $"Your target roles in the last {m.WindowDays} days, by area. A posting listing several cities counts once, under the first. "
            + "Adzuna's province-wide search leans towards Toronto, so company boards and Job Bank carry the smaller cities.");
        h.Append("<div class=\"scroll\"><table><thead><tr><th scope=\"col\">Area</th><th scope=\"col\" class=\"num\">Postings</th>")
            .Append("<th scope=\"col\" class=\"num\">Share</th><th scope=\"col\" class=\"num\">With salary</th><th scope=\"col\" class=\"num\">Median salary</th></tr></thead><tbody>");
        foreach (var c in a.Areas)
            h.Append("<tr><td>").Append(E(c.Area)).Append("</td><td class=\"num\">").Append(Num(c.Postings)).Append("</td><td class=\"num\">")
                .Append(Pct(c.Share)).Append("</td><td class=\"num\">").Append(Num(c.WithSalary)).Append("</td><td class=\"num\">")
                .Append(c.MedianYearly is { } median ? Money(median) : "—").Append("</td></tr>");
        h.Append("</tbody></table></div>");
        if (a.JobBank.Count > 0)
        {
            h.Append("<p class=\"note\">Job Bank tech postings by economic region, all months on file.</p>")
                .Append("<div class=\"scroll\"><table><thead><tr><th scope=\"col\">Economic region</th><th scope=\"col\" class=\"num\">Postings</th>")
                .Append("<th scope=\"col\" class=\"num\">Vacancies</th></tr></thead><tbody>");
            foreach (var j in a.JobBank)
                h.Append("<tr><td>").Append(E(j.EconomicRegion)).Append("</td><td class=\"num\">").Append(Num(j.Postings))
                    .Append("</td><td class=\"num\">").Append(Num(j.Vacancies)).Append("</td></tr>");
            h.Append("</tbody></table></div>");
        }
        Close(h);
    }

    private void SalaryTable(StringBuilder h, ReportModel m)
    {
        Open(h, "salary", $"Salary ({_region.Salary.Currency}, yearly)",
            "Midpoint of each posted range, by role and seniority. Hourly rates count as 2,080 hours a year"
            + (_region.Salary.MonthlyIsSalary ? "; monthly pay counts as 12 months a year." : "."));
        if (m.Salaries.Count == 0)
        {
            Empty(h, "No postings with a salary in the window yet.");
        }
        else
        {
            h.Append("<div class=\"scroll\"><table><thead><tr><th scope=\"col\">Role</th><th scope=\"col\">Seniority</th>")
                .Append("<th scope=\"col\" class=\"num\">Postings</th><th scope=\"col\" class=\"num\">Median</th><th scope=\"col\" class=\"num\">Middle 50%</th></tr></thead><tbody>");
            foreach (var c in m.Salaries)
            {
                h.Append("<tr><td>").Append(E(FamilyLabel(c.Family))).Append("</td><td>").Append(E(SeniorityLabel(c.Seniority)))
                    .Append("</td><td class=\"num\">").Append(Num(c.Count)).Append("</td>");
                if (c.Median is { } median && c.P25 is { } p25 && c.P75 is { } p75)
                    h.Append("<td class=\"num\">").Append(Money(median)).Append("</td><td class=\"num\">").Append(Money(p25)).Append('–').Append(Money(p75)).Append("</td>");
                else
                    h.Append("<td class=\"num muted\" colspan=\"2\">not enough data</td>");
                h.Append("</tr>");
            }
            h.Append("</tbody></table></div>");
        }
        Close(h);
    }

    private void MatchList(StringBuilder h, ReportModel m)
    {
        Open(h, "matches", "Best-matching open postings",
            "Open in the latest run, ranked by the share of their skills you already have at working level or better.");
        if (m.Matches.Count == 0)
        {
            Empty(h, "No open postings with enough recognised skills yet.");
        }
        else
        {
            h.Append("<ol class=\"matches\">");
            foreach (var p in m.Matches)
            {
                h.Append("<li class=\"match\"><div class=\"match-main\">");
                if (SafeUrl(p.Url) is { } url)
                    h.Append("<a class=\"match-title\" href=\"").Append(E(url)).Append("\" target=\"_blank\" rel=\"noopener noreferrer\">").Append(E(p.Title)).Append("</a>");
                else
                    h.Append("<span class=\"match-title\">").Append(E(p.Title)).Append("</span>");
                h.Append("<p class=\"match-meta\">").Append(E(p.Company)).Append(" · ").Append(E(p.Location));
                if (p.Salary is { } salary) h.Append(" · ").Append(E(SalaryText(salary)));
                h.Append("</p>");
                if (p.Missing.Count > 0) h.Append("<p class=\"match-missing\">Missing: ").Append(E(string.Join(", ", p.Missing))).Append("</p>");
                h.Append("</div><div class=\"match-side\"><span class=\"pill\">").Append(E(Pct(p.Score))).Append(" match</span>");
                if (p.CitizenshipOrClearance) h.Append("<span class=\"flag\">Citizenship or clearance</span>");
                h.Append("</div></li>");
            }
            h.Append("</ol>");
        }
        Close(h);
    }

    private void CompanyList(StringBuilder h, ReportModel m)
    {
        Open(h, "companies", "Who's hiring", $"Distinct postings in your target roles over the last {m.WindowDays} days.");
        if (m.Companies.Count == 0)
        {
            Empty(h, "No companies yet.");
        }
        else
        {
            h.Append("<ol class=\"companies\">");
            foreach (var c in m.Companies)
                h.Append("<li><span>").Append(E(c.Company)).Append("</span><span class=\"num\">").Append(Num(c.Postings)).Append("</span></li>");
            h.Append("</ol>");
        }
        Close(h);
    }

    private void TrendSection(StringBuilder h, ReportModel m)
    {
        var t = m.Trends!;
        Open(h, "trends", "Trends",
            $"Postings in your target roles seen each week, and the skills whose share moved most between the previous {m.WindowDays} days and the latest {m.WindowDays}.");
        WeeklyChart(h, t.Weekly);
        if (t.Moves.Count > 0)
        {
            h.Append("<div class=\"scroll\"><table><thead><tr><th scope=\"col\">Skill</th><th scope=\"col\" class=\"num\">Previous</th>")
                .Append("<th scope=\"col\" class=\"num\">Latest</th><th scope=\"col\" class=\"num\">Change</th></tr></thead><tbody>");
            foreach (var move in t.Moves)
            {
                var points = (move.Current - move.Previous) * 100;
                var css = points > 0 ? "up" : points < 0 ? "down" : "muted";
                h.Append("<tr><td>").Append(E(move.Skill)).Append("</td><td class=\"num\">").Append(E(Pct(move.Previous)))
                    .Append("</td><td class=\"num\">").Append(E(Pct(move.Current))).Append("</td><td class=\"num ").Append(css).Append("\">")
                    .Append(points > 0 ? "+" : "").Append(points.ToString("0", Inv)).Append(" pts</td></tr>");
            }
            h.Append("</tbody></table></div>");
        }
        Close(h);
    }

    private void WeeklyChart(StringBuilder h, IReadOnlyList<WeekCount> weeks)
    {
        const double W = 640, H = 180, L = 48, R = 20, T = 16, B = 34;
        var max = Math.Max(1, weeks.Max(w => w.Active));
        double X(int i) => weeks.Count == 1 ? L + (W - L - R) / 2 : L + i * (W - L - R) / (weeks.Count - 1);
        double Y(int v) => T + (H - T - B) * (1 - (double)v / max);
        var line = string.Join(' ', weeks.Select((w, i) => $"{F(X(i))},{F(Y(w.Active))}"));
        var area = $"{F(X(0))},{F(Y(0))} {line} {F(X(weeks.Count - 1))},{F(Y(0))}";

        h.Append("<figure class=\"chart\"><svg viewBox=\"0 0 640 180\" role=\"img\" aria-label=\"Open postings per week, from ")
            .Append(weeks[0].Active).Append(" to ").Append(weeks[^1].Active).Append("\">")
            .Append($"<line class=\"grid\" x1=\"{F(L)}\" x2=\"{F(W - R)}\" y1=\"{F(Y(0))}\" y2=\"{F(Y(0))}\"/>")
            .Append($"<line class=\"grid\" x1=\"{F(L)}\" x2=\"{F(W - R)}\" y1=\"{F(Y(max))}\" y2=\"{F(Y(max))}\"/>")
            .Append($"<text class=\"axis\" x=\"{F(L - 8)}\" y=\"{F(Y(0) + 4)}\" text-anchor=\"end\">0</text>")
            .Append($"<text class=\"axis\" x=\"{F(L - 8)}\" y=\"{F(Y(max) + 4)}\" text-anchor=\"end\">{max}</text>")
            .Append($"<polygon class=\"area\" points=\"{area}\"/>")
            .Append($"<polyline class=\"line\" fill=\"none\" points=\"{line}\"/>")
            .Append($"<circle class=\"dot\" r=\"4.5\" cx=\"{F(X(weeks.Count - 1))}\" cy=\"{F(Y(weeks[^1].Active))}\"/>")
            .Append($"<text class=\"axis\" x=\"{F(L)}\" y=\"{F(H - 10)}\">{E(WeekLabel(weeks[0].WeekStart))}</text>")
            .Append($"<text class=\"axis\" x=\"{F(W - R)}\" y=\"{F(H - 10)}\" text-anchor=\"end\">{E(WeekLabel(weeks[^1].WeekStart))}</text>")
            .Append("</svg><figcaption>Open postings per week in your target roles</figcaption></figure>");
    }

    private void JobBankTable(StringBuilder h, ReportModel m)
    {
        var jobBank = _region.JobBank!;
        var where = string.IsNullOrWhiteSpace(jobBank.Province) ? $"the {jobBank.EconomicRegion}" : jobBank.Province;
        Open(h, "jobbank", "Job Bank context",
            $"Government of Canada Job Bank postings for tech occupations (NOC 2021) in {where}. Job Bank carries few tech roles, so read this as background.");
        if (m.JobBank.Count == 0)
        {
            Empty(h, "No Job Bank data yet.");
        }
        else
        {
            var month = m.JobBank.Max(j => j.Month);
            h.Append("<p class=\"note\">").Append(E(month.ToString("MMMM yyyy", _culture))).Append("</p>")
                .Append("<div class=\"scroll\"><table><thead><tr><th scope=\"col\">Occupation</th><th scope=\"col\" class=\"num\">Postings</th>")
                .Append("<th scope=\"col\" class=\"num\">Vacancies</th><th scope=\"col\" class=\"num\">Median wage range</th></tr></thead><tbody>");
            foreach (var j in m.JobBank.Where(j => j.Month == month).OrderByDescending(j => j.Postings))
            {
                h.Append("<tr><td>").Append(E(j.NocName)).Append(" <span class=\"muted\">").Append(E(j.NocCode)).Append("</span></td><td class=\"num\">")
                    .Append(Num(j.Postings)).Append("</td><td class=\"num\">").Append(Num(j.Vacancies)).Append("</td><td class=\"num\">")
                    .Append(j.WageMinMedian is { } lo && j.WageMaxMedian is { } hi ? $"{Money(lo)}–{Money(hi)}" : "—").Append("</td></tr>");
            }
            h.Append("</tbody></table></div>");
        }
        Close(h);
    }

    private void Method(StringBuilder h, ReportModel m)
    {
        var sources = string.Join(" · ", m.Snapshot.BySource.Select(kv => $"{kv.Key} {Num(kv.Value)}"));
        h.Append("<footer class=\"method\"><h2>How this is made</h2><ul>")
            .Append("<li>Sources: ").Append(E(SourcesText())).Append(".</li>")
            .Append("<li>Counted: postings located in ").Append(E(_region.LocalLabel)).Append(" or listed as ").Append(E(_region.RemoteLabel))
            .Append(", in your target roles. The same job found on several sites counts once. By source: ")
            .Append(E(sources)).Append(".</li>")
            .Append("<li>Skills come from a curated list of names and aliases matched in the posting text. Salary is taken from structured fields first, then from ")
            .Append(E(_region.Salary.Currency)).Append(" ranges stated in the text.</li>")
            .Append("<li>").Append(Num(m.Snapshot.CitizenshipFlagged)).Append(" postings say only citizens or nationals may apply, or ask for a security clearance.</li>")
            .Append("<li>Run ").Append(E(m.LatestRunId)).Append(", generated ").Append(E(m.GeneratedAt.ToString("MMMM d, yyyy, HH:mm", _culture))).Append(".</li></ul>");
        if (m.Warnings.Count > 0)
        {
            h.Append("<h3>Data warnings</h3><ul>");
            foreach (var w in m.Warnings) h.Append("<li>").Append(E(w)).Append("</li>");
            h.Append("</ul>");
        }
        if (_region.Adzuna is not null)
            h.Append("<p>Jobs data partly by <a href=\"https://www.adzuna.").Append(E(AdzunaDomain(_region.Adzuna.Country))).Append("\" target=\"_blank\" rel=\"noopener noreferrer\">Adzuna</a>.</p>");
        h.Append("</footer>\n");
    }

    private static readonly (string Ats, string Name)[] BoardTypes =
        [("greenhouse", "Greenhouse"), ("lever", "Lever"), ("ashby", "Ashby"), ("workable", "Workable"), ("smartrecruiters", "SmartRecruiters")];

    private string SourcesText()
    {
        var parts = new List<string>();
        if (_region.Adzuna is not null) parts.Add("the Adzuna job search API");
        var types = BoardTypes.Where(t => _region.Boards.Any(b => b.Ats == t.Ats)).Select(t => t.Name).ToList();
        var boards = types.Count < 2 ? string.Join("", types) : string.Join(", ", types[..^1]) + " and " + types[^1];
        parts.Add($"public {boards} job boards of tech employers hiring in {_region.LocalLabel}");
        if (_region.JobBank is not null) parts.Add("Job Bank open data");
        parts.Add("postings saved to the inbox");
        return string.Join(", ", parts[..^1]) + ", and " + parts[^1];
    }

    // Adzuna's site for the searched country (attribution link).
    private static string AdzunaDomain(string country) => country.ToLowerInvariant() switch
    {
        "gb" => "co.uk",
        "us" => "com",
        "au" => "com.au",
        "nz" => "co.nz",
        "za" => "co.za",
        "in" => "in",
        "sg" => "sg",
        var c => c,
    };

    private void Open(StringBuilder h, string id, string title, string note) =>
        h.Append("<section id=\"").Append(id).Append("\" aria-labelledby=\"").Append(id).Append("-h\"><div class=\"section-head\"><h2 id=\"")
            .Append(id).Append("-h\">").Append(E(title)).Append("</h2><p class=\"note\">").Append(E(note)).Append("</p></div>");

    private void Close(StringBuilder h) => h.Append("</section>\n");

    private void Empty(StringBuilder h, string text) => h.Append("<p class=\"empty\">").Append(E(text)).Append("</p>");

    private string Chip(SkillLevel level) =>
        $"<span class=\"chip lvl-{level.ToString().ToLowerInvariant()}\">{LevelLabel(level)}</span>";

    private string LevelLabel(SkillLevel level) => level switch
    {
        SkillLevel.Strong => "Strong",
        SkillLevel.Working => "Working",
        SkillLevel.Learning => "Learning",
        _ => "Gap",
    };

    private string FamilyLabel(RoleFamily family) => family switch
    {
        RoleFamily.Dotnet => ".NET",
        RoleFamily.Fullstack => "Full stack",
        RoleFamily.SoftwareEngineer => "Software engineer",
        RoleFamily.LeadManager => "Lead / manager",
        _ => "Other",
    };

    private string SeniorityLabel(Seniority seniority) => seniority switch
    {
        Seniority.Junior => "Junior",
        Seniority.Intermediate => "Intermediate",
        Seniority.Senior => "Senior",
        Seniority.StaffPrincipal => "Staff / principal",
        Seniority.Lead => "Lead",
        Seniority.Manager => "Manager",
        _ => "Not stated",
    };

    private string SalaryText(SalaryRange s) =>
        s.Period == SalaryPeriod.Hour
            ? $"{_region.Salary.MoneyPrefix}{s.Min.ToString("0", Inv)}–{_region.Salary.MoneyPrefix}{s.Max.ToString("0", Inv)}/h"
            : $"{Money(s.YearlyMin)}–{Money(s.YearlyMax)}";

    private string? SafeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? url : null;

    private string E(string? text) => WebUtility.HtmlEncode(text ?? "");
    private string Num(int n) => n.ToString("N0", _culture);
    private string Money(decimal amount) => _region.Salary.MoneyPrefix + (amount / 1000m).ToString("0", Inv) + "k";
    private string Pct(double share) => share <= 0 ? "0%" : share < 0.005 ? "<1%" : (share * 100).ToString("0", Inv) + "%";
    private string F(double value) => value.ToString("0.#", Inv);
    private string WeekLabel(DateOnly day) => day.ToString("MMM d", _culture);

    private const string Css = """
        :root{--bg:#F2F4F7;--surface:#FFFFFF;--ink:#17212B;--muted:#586676;--rule:#D5DCE4;--accent:#A8132B;--accent-soft:#F5DADF;--bar:#2B5B8C;--bar-track:#E1E8F0;--good:#1C7445;--good-soft:#DAF0E3;--info:#2B5B8C;--info-soft:#DFE8F3;--warn:#99570A;--warn-soft:#F6E7D0;--display:"Archivo","Arial Narrow",system-ui,sans-serif;--body:"Source Sans 3","Segoe UI",system-ui,-apple-system,sans-serif;--mono:"IBM Plex Mono",ui-monospace,"Cascadia Mono",Consolas,monospace}
        @media (prefers-color-scheme:dark){:root:not([data-theme="light"]){color-scheme:dark;--bg:#0E141B;--surface:#151E28;--ink:#E2E8EE;--muted:#94A2B1;--rule:#27333F;--accent:#FF7385;--accent-soft:#3E1D26;--bar:#72A9DD;--bar-track:#1D2B3A;--good:#5DCB90;--good-soft:#16311F;--info:#72A9DD;--info-soft:#172838;--warn:#F0AB5A;--warn-soft:#342413}}
        :root[data-theme="dark"]{color-scheme:dark;--bg:#0E141B;--surface:#151E28;--ink:#E2E8EE;--muted:#94A2B1;--rule:#27333F;--accent:#FF7385;--accent-soft:#3E1D26;--bar:#72A9DD;--bar-track:#1D2B3A;--good:#5DCB90;--good-soft:#16311F;--info:#72A9DD;--info-soft:#172838;--warn:#F0AB5A;--warn-soft:#342413}
        *,*::before,*::after{box-sizing:border-box}
        body{margin:0;background:var(--bg);color:var(--ink);font-family:var(--body);font-size:16px;line-height:1.55}
        .report{max-width:980px;margin:0 auto;padding-inline:max(16px,4vw);padding-block:28px 56px;display:grid;gap:44px}
        h1,h2,h3{font-family:var(--display);font-stretch:87.5%;text-wrap:balance;margin:0;line-height:1.1}
        h1{font-size:clamp(2.2rem,6vw,3.4rem);font-weight:800;letter-spacing:-.01em}
        h2{font-size:1.45rem;font-weight:700}
        h3{font-size:1.05rem;font-weight:700}
        p{margin:0}
        a{color:var(--bar);text-underline-offset:2px}
        a:focus-visible{outline:2px solid var(--accent);outline-offset:2px;border-radius:2px}
        .eyebrow,.stat-label,.axis,.rank-meta,.bar-value,.num,.chip,.pill,.flag,thead th{font-family:var(--mono);font-variant-numeric:tabular-nums}
        .eyebrow{font-size:.78rem;letter-spacing:.06em;text-transform:uppercase;color:var(--muted)}
        .masthead{display:grid;gap:10px;padding-bottom:22px;border-bottom:3px solid var(--accent)}
        .lede{font-size:1.15rem;max-width:62ch}
        .stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:12px}
        .stat{background:var(--surface);border:1px solid var(--rule);border-radius:10px;padding:14px 16px;display:grid;gap:2px}
        .stat-label{font-size:.72rem;letter-spacing:.06em;text-transform:uppercase;color:var(--muted)}
        .stat-value{font-family:var(--display);font-stretch:87.5%;font-size:2rem;font-weight:800;font-variant-numeric:tabular-nums}
        .stat-note{font-size:.85rem;color:var(--muted)}
        section{display:grid;gap:14px}
        .section-head{display:grid;gap:6px}
        .note{color:var(--muted);font-size:.95rem;max-width:68ch}
        .empty{color:var(--muted);font-style:italic}
        .bars{list-style:none;margin:0;padding:0;display:grid;gap:6px}
        .bar-row{display:grid;grid-template-columns:minmax(7.5rem,12rem) minmax(0,1fr) 3.2rem 5.6rem;grid-template-areas:"name track value chip";align-items:center;gap:10px}
        .bar-name{grid-area:name;overflow-wrap:anywhere}
        .bar-track{grid-area:track;height:12px;background:var(--bar-track);border-radius:3px;overflow:hidden}
        .bar-fill{display:block;height:100%;background:var(--bar);border-radius:3px}
        .bar-value{grid-area:value;text-align:right;font-size:.85rem}
        .bar-row .chip{grid-area:chip}
        .chip{display:inline-block;font-size:.72rem;padding:2px 8px;border-radius:999px;justify-self:start;white-space:nowrap}
        .lvl-strong{background:var(--good-soft);color:var(--good)}
        .lvl-working{background:var(--info-soft);color:var(--info)}
        .lvl-learning{background:var(--warn-soft);color:var(--warn)}
        .lvl-none{background:var(--accent-soft);color:var(--accent)}
        .pair{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:28px}
        .col{display:grid;gap:10px;align-content:start}
        .ranked{margin:0;padding-left:1.6rem;display:grid;gap:8px}
        .ranked li::marker{font-family:var(--mono);color:var(--muted);font-size:.85rem}
        .rank-line{display:flex;flex-wrap:wrap;align-items:baseline;gap:6px 10px}
        .rank-name{font-weight:600}
        .rank-meta{font-size:.8rem;color:var(--muted)}
        .scroll{overflow-x:auto;border:1px solid var(--rule);border-radius:10px;background:var(--surface)}
        table{border-collapse:collapse;width:100%;min-width:520px;font-size:.95rem}
        th,td{padding:9px 12px;text-align:left;border-bottom:1px solid var(--rule)}
        thead th{font-size:.72rem;letter-spacing:.06em;text-transform:uppercase;color:var(--muted);font-weight:500}
        tbody tr:last-child td{border-bottom:0}
        .num{text-align:right;white-space:nowrap}
        .muted{color:var(--muted)}
        .up{color:var(--good)}
        .down{color:var(--accent)}
        .matches{list-style:none;margin:0;padding:0;display:grid;gap:10px}
        .match{display:flex;flex-wrap:wrap;justify-content:space-between;gap:8px 16px;padding:12px 14px;background:var(--surface);border:1px solid var(--rule);border-radius:10px}
        .match-main{display:grid;gap:3px;min-width:0;flex:1 1 280px}
        .match-title{font-weight:600;overflow-wrap:anywhere}
        .match-meta,.match-missing{font-size:.9rem;color:var(--muted)}
        .match-side{display:flex;flex-wrap:wrap;align-items:flex-start;gap:6px}
        .pill{font-size:.8rem;padding:3px 10px;border-radius:999px;background:var(--info-soft);color:var(--info);white-space:nowrap}
        .flag{font-size:.75rem;padding:3px 10px;border-radius:999px;background:var(--warn-soft);color:var(--warn);white-space:nowrap}
        .companies{list-style:none;margin:0;padding:0;display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:0 24px}
        .companies li{display:flex;justify-content:space-between;gap:12px;padding:7px 0;border-bottom:1px solid var(--rule)}
        .chart{margin:0;background:var(--surface);border:1px solid var(--rule);border-radius:10px;padding:12px}
        .chart svg{display:block;width:100%;height:auto;max-width:100%}
        .chart figcaption{font-size:.85rem;color:var(--muted);margin-top:6px}
        .grid{stroke:var(--rule);stroke-width:1;fill:none}
        .line{stroke:var(--bar);stroke-width:2.5;stroke-linejoin:round;stroke-linecap:round}
        .area{fill:var(--bar);fill-opacity:.12;stroke:none}
        .dot{fill:var(--bar);stroke:var(--surface);stroke-width:2}
        .axis{fill:var(--muted);font-size:12px}
        .method{display:grid;gap:10px;padding-top:20px;border-top:1px solid var(--rule);color:var(--muted);font-size:.92rem}
        .method ul{margin:0;padding-left:1.2rem;display:grid;gap:4px}
        .method h2{font-size:1.1rem;color:var(--ink)}
        @media (max-width:560px){.bar-row{grid-template-columns:minmax(0,1fr) auto 3rem;grid-template-areas:"name chip value" "track track track";row-gap:4px}}
        """;
}
