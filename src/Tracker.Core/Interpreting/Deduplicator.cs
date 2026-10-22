using System.Text.RegularExpressions;

namespace Tracker.Interpreting;

// The same job often appears on Adzuna and on the company's own board, or is reposted with a new id.
// Postings with the same normalized company + title + city that were open at the same time (seen in at least one
// common run) form a cluster. One is kept and takes the cluster's sightings, plus a salary if it had none;
// the rest point at it. The same title posted again months later is a new posting, not a duplicate.
public static partial class Deduplicator
{
    private static readonly HashSet<string> CompanyNoise = ["inc", "ltd", "llc", "corp", "corporation", "limited", "co", "company", "the"];

    public static IReadOnlyList<Posting> MarkDuplicates(IReadOnlyList<Posting> postings, IReadOnlyList<string> runOrder)
    {
        var order = runOrder.Select((run, i) => (run, i)).ToDictionary(x => x.run, x => x.i, StringComparer.Ordinal);
        var updated = postings.ToDictionary(p => p.Id, p => p, StringComparer.Ordinal);

        foreach (var group in postings.GroupBy(Key).Where(g => g.Key.Length > 0 && g.Count() > 1))
            foreach (var cluster in OpenAtTheSameTime([.. group]).Where(c => c.Count > 1))
            {
                var ranked = cluster
                    .OrderByDescending(p => p.IsFullText)
                    .ThenByDescending(p => order.GetValueOrDefault(p.LastSeenRun, -1))
                    .ThenBy(p => p.Id, StringComparer.Ordinal)
                    .ToList();
                var keep = ranked[0];
                var runs = cluster.SelectMany(p => p.SightingRuns).Distinct(StringComparer.Ordinal)
                    .OrderBy(r => order.GetValueOrDefault(r, int.MaxValue)).ThenBy(r => r, StringComparer.Ordinal).ToList();
                var salarySource = keep.Salary is not null ? keep : ranked.FirstOrDefault(p => p.Salary is not null);

                updated[keep.Id] = keep with
                {
                    SightingRuns = runs,
                    Salary = salarySource?.Salary,
                    SalaryOrigin = salarySource?.SalaryOrigin ?? keep.SalaryOrigin,
                };
                foreach (var p in cluster.Where(p => p.Id != keep.Id)) updated[p.Id] = p with { DuplicateOf = keep.Id };
            }

        return [.. postings.Select(p => updated[p.Id])];
    }

    public static string Normalize(string text)
    {
        var words = NonWord().Replace(text.ToLowerInvariant(), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !CompanyNoise.Contains(w));
        return string.Join(' ', words);
    }

    // Groups postings whose sightings overlap, directly or through another member.
    private static IEnumerable<List<Posting>> OpenAtTheSameTime(List<Posting> group)
    {
        var remaining = new List<Posting>(group);
        while (remaining.Count > 0)
        {
            var cluster = new List<Posting> { remaining[0] };
            var runs = new HashSet<string>(remaining[0].SightingRuns, StringComparer.Ordinal);
            remaining.RemoveAt(0);
            bool grew;
            do
            {
                grew = false;
                for (var i = remaining.Count - 1; i >= 0; i--)
                {
                    if (!remaining[i].SightingRuns.Any(runs.Contains)) continue;
                    runs.UnionWith(remaining[i].SightingRuns);
                    cluster.Add(remaining[i]);
                    remaining.RemoveAt(i);
                    grew = true;
                }
            } while (grew);
            yield return cluster;
        }
    }

    private static string Key(Posting p)
    {
        var company = Normalize(p.Company);
        var title = Normalize(p.Title);
        return company.Length == 0 || title.Length == 0 ? "" : $"{company}|{title}|{p.Scope.City?.ToLowerInvariant()}";
    }

    [GeneratedRegex(@"[^\p{L}\p{N}#+]+")]
    private static partial Regex NonWord();
}
