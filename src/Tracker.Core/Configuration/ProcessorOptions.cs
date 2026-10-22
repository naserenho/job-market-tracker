using System.Text.Json;

namespace Tracker.Configuration;

// config/processor.json: settings that are the same in every region. Regional ones live in config/regions/ (ADR 0015).
public sealed class ProcessorOptions
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    public int WindowDays { get; set; } = 30;

    public static ProcessorOptions Load(string path)
    {
        var options = JsonSerializer.Deserialize<ProcessorOptions>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException($"{path} is empty.");
        if (options.WindowDays < 1) throw new InvalidDataException($"{path}: WindowDays must be at least 1.");
        return options;
    }
}

// The Job Bank section of a region file (Canada only).
public sealed class JobBankOptions
{
    public string DatasetId { get; set; } = "";
    public int BackfillMonths { get; set; } = 12;
    // Exactly one of the two: a single economic region ("Toronto Region"), or a whole province ("Ontario").
    public string EconomicRegion { get; set; } = "";
    public string Province { get; set; } = "";
    public Dictionary<string, string> NocCodes { get; set; } = [];
}
