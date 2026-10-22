using System.Text.Json;
using Tracker.Configuration;

namespace Tracker.Reading;

// Region: the manifest's "region"; collections from before regions existed are Toronto ones.
public sealed record ArchiveRun(string RunId, DateTimeOffset StartedAt, string FullPath, string Region = Regions.Legacy);

public sealed record AdzunaAggregate(string RunId, string Term, string Kind, string PayloadJson);

// Reads the phase 0 layout: data/raw/<run>/{manifest.json, adzuna/search, adzuna/histogram, adzuna/top_companies, boards}.
// Every rebuild re-reads the whole archive, so a file that can't be parsed (an error page saved as JSON, a truncated
// write) is skipped with a warning instead of failing every future run.
public static class Archive
{
    private static readonly string[] AggregateKinds = ["histogram", "top_companies"];

    // Completed real runs only (they have a readable manifest); "_dryrun" and other "_" folders are never read.
    public static IReadOnlyList<ArchiveRun> Runs(string dataRoot)
    {
        var raw = Path.Combine(dataRoot, "raw");
        if (!Directory.Exists(raw)) return [];
        var runs = new List<ArchiveRun>();
        foreach (var dir in Directory.EnumerateDirectories(raw))
        {
            var id = Path.GetFileName(dir);
            var manifest = Path.Combine(dir, "manifest.json");
            if (id.StartsWith('_') || !File.Exists(manifest)) continue;
            try
            {
                using var doc = JsonFiles.Load(manifest);
                runs.Add(new ArchiveRun(id, doc.RootElement.GetProperty("startedAt").GetDateTimeOffset(), dir,
                    doc.RootElement.Str("region") is { Length: > 0 } region ? region : Regions.Legacy));
            }
            catch (Exception ex) when (IsUnreadable(ex))
            {
                // An unreadable manifest means the run never finished writing; it is left out like a run without one.
            }
        }
        return [.. runs.OrderBy(r => r.StartedAt).ThenBy(r => r.RunId, StringComparer.Ordinal)];
    }

    public static IEnumerable<RawPosting> ReadPostings(ArchiveRun run, Region region, ICollection<string> warnings)
    {
        var ctx = new SourceContext(run.RunId, region);
        foreach (var file in Files(run, "adzuna", "search"))
            foreach (var p in Safely(run, file, warnings, root => SourceReaders.Adzuna(root, ctx)))
                yield return p;

        foreach (var file in Files(run, "boards"))
        {
            var name = JsonFiles.BaseName(file);
            var dash = name.IndexOf('-');
            if (dash <= 0) continue;
            var (ats, slug) = (name[..dash], name[(dash + 1)..]);
            Func<JsonElement, IEnumerable<RawPosting>>? read = ats switch
            {
                "greenhouse" => root => SourceReaders.Greenhouse(root, slug, ctx),
                "lever" => root => SourceReaders.Lever(root, slug, ctx),
                "ashby" => root => SourceReaders.Ashby(root, slug, ctx),
                "workable" => root => SourceReaders.Workable(root, slug, ctx),
                "smartrecruiters" => root => SourceReaders.SmartRecruiters(root, slug, id => Detail(Path.Combine(Path.GetDirectoryName(file)!, name, $"{id}.json")), ctx),
                _ => null,
            };
            if (read is null) continue;
            foreach (var p in Safely(run, file, warnings, read)) yield return p;
        }
    }

    // A posting's detail file beside its board's list (SmartRecruiters); null when it wasn't saved or can't be read.
    private static JsonElement? Detail(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonFiles.Load(path);
            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            return null;
        }
    }

    public static IEnumerable<AdzunaAggregate> ReadAggregates(ArchiveRun run, ICollection<string> warnings)
    {
        foreach (var kind in AggregateKinds)
            foreach (var file in Files(run, "adzuna", kind))
            {
                var name = JsonFiles.BaseName(file);
                var term = name.EndsWith("-gta", StringComparison.Ordinal) ? name[..^4] : name;
                foreach (var a in Safely<AdzunaAggregate>(run, file, warnings, root => [new AdzunaAggregate(run.RunId, term, kind, root.GetRawText())]))
                    yield return a;
            }
    }

    // Sources in a run's manifest that did not fully succeed, as report warnings (the archiver records them per source).
    public static IReadOnlyList<string> CollectionProblems(ArchiveRun run)
    {
        try
        {
            using var doc = JsonFiles.Load(Path.Combine(run.FullPath, "manifest.json"));
            var problems = new List<string>();
            if (doc.RootElement.Obj("sources") is { } sources)
                foreach (var source in sources.EnumerateObject())
                {
                    var outcome = source.Value.Str("outcome");
                    if (outcome is null or "succeeded" or "notApplicable") continue; // notApplicable: the region doesn't use it
                    var ok = source.Value.Num("ok") ?? 0;
                    var failed = source.Value.Num("failed") ?? 0;
                    var detail = failed > 0 ? $" ({failed} of {ok + failed} requests failed)" : "";
                    var note = source.Value.Str("note")?.Trim().TrimEnd('.');
                    problems.Add($"Latest collection ({run.RunId}): {source.Name} {outcome}{detail}{(string.IsNullOrEmpty(note) ? "" : $". {note}")}.");
                }
            if (doc.RootElement.Bool("cancelled") == true)
                problems.Add($"Latest collection ({run.RunId}) was cancelled before it finished.");
            return problems;
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            return [$"Latest collection ({run.RunId}): its manifest could not be read."];
        }
    }

    private static IReadOnlyList<T> Safely<T>(ArchiveRun run, string file, ICollection<string> warnings, Func<JsonElement, IEnumerable<T>> read)
    {
        try
        {
            using var doc = JsonFiles.Load(file);
            return [.. read(doc.RootElement)];
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            warnings.Add($"Skipped unreadable file {Path.GetFileName(file)} in run {run.RunId}: {ex.Message}");
            return [];
        }
    }

    private static bool IsUnreadable(Exception ex) =>
        ex is JsonException or IOException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException;

    private static IEnumerable<string> Files(ArchiveRun run, params string[] parts)
    {
        var dir = Path.Combine([run.FullPath, .. parts]);
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir).Where(JsonFiles.IsJson).Order(StringComparer.Ordinal)
            : Enumerable.Empty<string>();
    }
}
