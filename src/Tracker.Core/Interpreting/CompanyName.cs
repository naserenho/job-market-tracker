namespace Tracker.Interpreting;

// One company, however a source spells it: "Acme", "ACME Inc." and "The Acme Corporation" share a key.
public static class CompanyName
{
    public static string Key(string name)
    {
        var key = Deduplicator.Normalize(name);
        return key.Length > 0 ? key : name.Trim().ToLowerInvariant();
    }

    // The spelling to show for a group: the most frequent, then ordinal order.
    public static string Display(IEnumerable<string> spellings) =>
        spellings.Select(s => s.Trim()).GroupBy(s => s, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
}
