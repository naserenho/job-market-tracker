using System.Text.RegularExpressions;

namespace Api.Applications;

public sealed class TrackerInputException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public sealed class TrackerNotFoundException(string message) : Exception(message);

public sealed class TrackerConflictException(string message, Guid? existingId = null) : Exception(message)
{
    public Guid? ExistingId { get; } = existingId;
}

// tracker.json exists but can't be used: the tracker stays closed so the copy is never overwritten.
public sealed class TrackerBackupException(string message, Exception inner) : Exception(message, inner);

// Input rules shared by every tracker endpoint: trimmed text with length limits, one-line names, web links, safe files.
public static partial class Rules
{
    public const int MaxText = 200, MaxLongText = 20_000, MaxPostingText = 100_000;
    public const long MaxFileBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".doc"] = "application/msword",
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
    };

    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["postingText"] = "Job text",
        ["postingId"] = "Posting",
        ["url"] = "Link",
        ["linkedIn"] = "LinkedIn",
    };

    public static string Required(string? value, string field, int max = MaxText)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) throw new TrackerInputException(field, $"{Label(field)} is required.");
        return Limit(text, field, max);
    }

    public static string Optional(string? value, string field, int max = MaxText) => Limit((value ?? "").Trim(), field, max);

    public static string NoNul(string value, string field) => Limit(value, field, int.MaxValue);

    // Titles and names go into one-line places (inbox headers, table cells): collapse line breaks and runs of spaces.
    public static string SingleLine(string value) => Spaces().Replace(value, " ").Trim();

    public static string CompanyKey(string name) => SingleLine(name).ToLowerInvariant();

    public static string OptionalUrl(string? value, string field)
    {
        var text = Optional(value, field, 2000);
        if (text.Length == 0) return text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? text
            : throw new TrackerInputException(field, $"{Label(field)} must be a full web address starting with https://.");
    }

    public static string OptionalEmail(string? value, string field)
    {
        var text = Optional(value, field);
        return text.Length == 0 || EmailShape().IsMatch(text)
            ? text
            : throw new TrackerInputException(field, "Enter an email address like name@company.com.");
    }

    public static T ParseEnum<T>(string? value, string field) where T : struct, Enum
    {
        var text = (value ?? "").Trim();
        // Enum.TryParse reads "Shortlisted,Applied" as a bitwise OR of both names, so a comma is refused first.
        return !text.Contains(',') && Enum.TryParse<T>(text, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(text, out _)
            ? parsed
            : throw new TrackerInputException(field, $"Unknown {Label(field).ToLowerInvariant()} '{text}'. Use {string.Join(", ", Enum.GetNames<T>())}.");
    }

    // Keeps only the last path segment (Windows or Unix separators) and letters, digits, '-', '_' and '.'.
    public static string SafeFileName(string name)
    {
        var cleaned = Unsafe().Replace(Path.GetFileName(name.Replace('\\', '/')), "-").Trim('-', '.');
        if (cleaned.Length == 0) return "file";
        return cleaned.Length <= 100 ? cleaned : cleaned[^100..].TrimStart('-', '.');
    }

    public static string ContentType(string fileName) =>
        ContentTypes.TryGetValue(Path.GetExtension(fileName), out var type) ? type : "application/octet-stream";

    public static void CheckUpload(string fileName, long length)
    {
        if (!ContentTypes.ContainsKey(Path.GetExtension(fileName)))
            throw new TrackerInputException("file", "Upload a PDF, DOCX, DOC, TXT or MD file.");
        if (length == 0) throw new TrackerInputException("file", "The file is empty.");
        if (length > MaxFileBytes) throw new TrackerInputException("file", "Files can be up to 10 MB.");
    }

    // "salaryAsked" → "Salary asked", with a few names the form spells differently.
    public static string Label(string field) =>
        Labels.TryGetValue(field, out var label)
            ? label
            : char.ToUpperInvariant(field[0]) + Capitals().Replace(field[1..], m => " " + m.Value.ToLowerInvariant());

    // Postgres text can't hold the NUL character: every text field refuses it by name instead of failing the save.
    private static string Limit(string text, string field, int max)
    {
        if (text.Contains('\0')) throw new TrackerInputException(field, $"{Label(field)} contains a NUL character, which can't be saved.");
        return text.Length <= max ? text : throw new TrackerInputException(field, $"{Label(field)} is longer than {max} characters.");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex("[A-Z]")]
    private static partial Regex Capitals();

    [GeneratedRegex(@"[^A-Za-z0-9._-]+")]
    private static partial Regex Unsafe();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailShape();
}
