using System.Text.RegularExpressions;

namespace Tracker.Interpreting;

// The text a posting's skills are matched in: its title and description, minus hiring notices (Ontario's AI-in-hiring
// disclosure, accommodation statements) that say nothing about the job's skills. The market data and CV tailoring both
// use it, so they always agree on a posting's skills.
public static partial class SkillText
{
    public static string ForMatching(string title, string description) =>
        $"{title}\n{AccommodationNotice().Replace(HiringAiDisclosure().Replace(description, " "), " ")}";

    // A sentence that pairs AI (or automated tools) with screening, hiring or applications, in either order.
    [GeneratedRegex(@"[^.\n]*(?:\b(?:AI|artificial intelligence|automated (?:tools?|systems?))\b[^.\n]{0,120}\b(?:screen\w*|hiring|recruit\w*|applicants?|job applications?|your application|applications (?:are|may|will)|resumes?|selection process|interview\w*|(?:review|evaluat|assess)\w* (?:candidates|applicants|applications))\b|\b(?:screen\w*|hiring|recruit\w*|applicants?|job applications?|your application|applications (?:are|may|will)|resumes?|selection process|interview\w*|(?:review|evaluat|assess)\w* (?:candidates|applicants|applications))\b[^.\n]{0,120}\b(?:AI|artificial intelligence|automated (?:tools?|systems?))\b)[^.\n]*[.\n]?", RegexOptions.IgnoreCase)]
    private static partial Regex HiringAiDisclosure();

    // "We will accommodate accessibility needs…", "Accessibility Statement…", AODA and disability notices.
    [GeneratedRegex(@"[^.\n]*\b(?:accommodat\w*|accessibility (?:needs|statement|requests?|plan|policy)|AODA|disabilit\w*)\b[^.\n]*[.\n]?", RegexOptions.IgnoreCase)]
    private static partial Regex AccommodationNotice();
}
