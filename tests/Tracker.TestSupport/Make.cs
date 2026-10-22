using Tracker.Interpreting;
using Tracker.Reading;

namespace Tracker.TestSupport;

public static class Make
{
    public static Posting Posting(string id, string[] runs, string[] skills, string? city = "Toronto", bool remote = false,
        RoleFamily family = RoleFamily.Dotnet, Seniority seniority = Seniority.Senior, SalaryRange? salary = null,
        bool fullText = true, string company = "Acme", string title = "Senior .NET Developer", bool flagged = false, string? duplicateOf = null) =>
        new(id, id.Split(':')[0], id.Split(':')[1], company, title, $"https://x.test/{id}", city ?? "Remote (Canada)",
            new Scope(city, city is not null, remote), family, seniority, salary, salary is null ? "none" : "structured",
            $"Description of {id}", fullText, flagged, skills.ToHashSet(), runs, duplicateOf);
}
