using System.Text.RegularExpressions;

namespace Tracker.Interpreting;

public enum RoleFamily { Dotnet, Fullstack, SoftwareEngineer, LeadManager, Other }

public enum Seniority { Unspecified, Junior, Intermediate, Senior, StaffPrincipal, Lead, Manager }

public static partial class TitleRules
{
    // First match wins: a ".NET Full Stack Developer" is .NET, a "Lead Software Engineer" is a software engineer (seniority lead).
    public static RoleFamily Family(string title)
    {
        if (Dotnet().IsMatch(title)) return RoleFamily.Dotnet;
        if (Fullstack().IsMatch(title)) return RoleFamily.Fullstack;
        if (SoftwareEngineer().IsMatch(title) && !NeverSoftwareEngineer().IsMatch(title)
            && (ExplicitSoftwareTitle().IsMatch(title) || !SpecialtyEngineer().IsMatch(title))) return RoleFamily.SoftwareEngineer;
        if (LeadManager().IsMatch(title) && !NotSoftwareLead().IsMatch(title)) return RoleFamily.LeadManager;
        return RoleFamily.Other;
    }

    public static Seniority Seniority(string title)
    {
        if (Manager().IsMatch(title)) return Interpreting.Seniority.Manager;
        if (Lead().IsMatch(title)) return Interpreting.Seniority.Lead;
        if (Staff().IsMatch(title)) return Interpreting.Seniority.StaffPrincipal;
        if (Senior().IsMatch(title)) return Interpreting.Seniority.Senior;
        if (Intermediate().IsMatch(title)) return Interpreting.Seniority.Intermediate;
        if (Junior().IsMatch(title)) return Interpreting.Seniority.Junior;
        return Interpreting.Seniority.Unspecified;
    }

    public static string FamilyKey(RoleFamily family) => family switch
    {
        RoleFamily.Dotnet => "dotnet",
        RoleFamily.Fullstack => "fullstack",
        RoleFamily.SoftwareEngineer => "software_engineer",
        RoleFamily.LeadManager => "lead_manager",
        _ => "other",
    };

    public static RoleFamily? ParseFamily(string key) =>
        Enum.GetValues<RoleFamily>().Cast<RoleFamily?>().FirstOrDefault(f => FamilyKey(f!.Value) == key);

    [GeneratedRegex(@"\.net\b|\bdotnet\b|\bc#", RegexOptions.IgnoreCase)]
    private static partial Regex Dotnet();

    [GeneratedRegex(@"\bfull[\s-]?stack\b", RegexOptions.IgnoreCase)]
    private static partial Regex Fullstack();

    [GeneratedRegex(@"\b(?:software|back[\s-]?end|front[\s-]?end|web|application|platform|mobile|ios|android|java|python|react|node(?:\.js)?|golang|ruby|php|typescript|javascript|angular|product)\b.{0,25}?\b(?:engineer|developer|programmer)s?\b|\b(?:developer|programmer)s?\b|^\s*(?:senior|sr\.?|staff|principal|lead)?\s*engineers?\b", RegexOptions.IgnoreCase)]
    private static partial Regex SoftwareEngineer();

    // "Software Engineer, Security" is a software engineer; "Security Engineer" is not.
    [GeneratedRegex(@"\bsoftware\s+(?:(?:development|dev)\s+)?(?:engineer|developer)s?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitSoftwareTitle();

    [GeneratedRegex(@"\bbusiness develop|\bsales\b|\bsupport\b|\bqa\b|\btest|\bhardware|\bmechanical|\belectrical|\bfield engineer", RegexOptions.IgnoreCase)]
    private static partial Regex NeverSoftwareEngineer();

    [GeneratedRegex(@"\bdata engineer|\bmachine learning|\bml engineer|\bsecurity\b|\bsite reliability|\bsre\b|\bdevops\b|\bsolutions? engineer|\bnetwork", RegexOptions.IgnoreCase)]
    private static partial Regex SpecialtyEngineer();

    [GeneratedRegex(@"\bengineering manager\b|\bmanager,?\s+(?:of\s+)?(?:software|engineering|development)\b|\bsoftware (?:development |engineering )?manager\b|\bdevelopment manager\b|\bdirector,?\s+(?:of\s+)?(?:software\s+)?engineering\b|\bhead of (?:engineering|development|software)\b|\bvp,?\s+(?:of\s+)?engineering\b|\btech(?:nical)? lead\b|\bteam lead\b|\b(?:development|dev) lead\b|\blead developer\b|\bchief technology\b|\bcto\b", RegexOptions.IgnoreCase)]
    private static partial Regex LeadManager();

    // "Sales Team Lead", "Press Tech Lead": lead titles outside software teams.
    [GeneratedRegex(@"\b(?:sales|customer|support|retail|store|warehouse|press|marketing|account|planning|shift|call cent)", RegexOptions.IgnoreCase)]
    private static partial Regex NotSoftwareLead();

    [GeneratedRegex(@"\b(?:manager|director|head of|vp|vice president|chief|cto)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Manager();

    [GeneratedRegex(@"\blead\b", RegexOptions.IgnoreCase)]
    private static partial Regex Lead();

    [GeneratedRegex(@"\b(?:staff|principal|distinguished|architect)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Staff();

    [GeneratedRegex(@"\b(?:senior|sr\.?|iii|iv)(?:\s|$|\b)", RegexOptions.IgnoreCase)]
    private static partial Regex Senior();

    [GeneratedRegex(@"\b(?:intermediate|mid[\s-]?level|ii)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Intermediate();

    [GeneratedRegex(@"\b(?:junior|jr\.?|intern|internship|co-?op|new grad|graduate|entry[\s-]level)\b|\s+i\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Junior();
}
