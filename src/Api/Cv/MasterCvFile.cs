using System.Globalization;
using System.Text.Json;
using Tracker.Interpreting;

namespace Api.Cv;

// Reads config/cv.json on every call (it is small, and edits then apply without a restart). Every problem is reported
// with its path, the way config/profile.json reports them, and a file with problems gives no CV at all.
public static class MasterCvFile
{
    public const string Missing = "config/cv.json is missing. Copy config/cv.example.json to config/cv.json and fill in your CV.";
    public const int DefaultMaxBullets = 5;

    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static CvLoad Load(string path, SkillCatalog catalog)
    {
        string content;
        try
        {
            if (!File.Exists(path)) return new(null, [Missing]);
            content = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(null, [$"config/cv.json can't be read: {ex.Message}"]);
        }
        try
        {
            using var doc = JsonDocument.Parse(content, Options);
            return new Reader(catalog).Read(doc.RootElement);
        }
        catch (JsonException ex)
        {
            return new(null, [$"config/cv.json is not valid JSON near line {(ex.LineNumber ?? 0) + 1}."]);
        }
        catch (InvalidOperationException)
        {
            // Valid JSON syntax that isn't text: half of an emoji written as an escape, such as "\uD83D" alone.
            return new(null, ["config/cv.json has a \\u escape that is only half of a character (such as \\uD83D alone). Retype that character."]);
        }
    }

    private static string MonthText(DateOnly month) => month.ToString("MMM yyyy", CultureInfo.InvariantCulture);

    private sealed class Reader(SkillCatalog catalog)
    {
        private readonly List<string> _problems = [];
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
        private readonly HashSet<string> _known = catalog.Skills.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        public CvLoad Read(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object) return new(null, ["config/cv.json must hold one JSON object ({ ... })."]);
            var person = ReadPerson(root);
            var headline = Text(root, "headline", "headline", required: true);
            var summaries = Items(root, "summaries", "summaries", ReadSummary);
            if (summaries.Count == 0) Problem("summaries: add at least one summary.");
            var groups = Items(root, "skillGroups", "skillGroups", ReadGroup);
            var roles = Items(root, "roles", "roles", (e, p) => ReadSection(e, p, CvSectionKind.Role));
            if (roles.Count == 0) Problem("roles: add at least one role.");
            var projects = Items(root, "projects", "projects", (e, p) => ReadSection(e, p, CvSectionKind.Project));
            var education = Items(root, "education", "education", ReadEducation);
            var certifications = Items(root, "certifications", "certifications", ReadCertification);
            return _problems.Count > 0
                ? new(null, _problems)
                : new(new MasterCv(person, headline, summaries, groups, [.. roles, .. projects], education, certifications), []);
        }

        private CvPerson ReadPerson(JsonElement root)
        {
            if (!root.TryGetProperty("person", out var person) || person.ValueKind != JsonValueKind.Object)
            {
                Problem("person is required: your name, location and contact details.");
                return new CvPerson("", "", "", "", "", []);
            }
            return new CvPerson(Text(person, "name", "person.name", required: true), Text(person, "location", "person.location"),
                Text(person, "status", "person.status"), Text(person, "phone", "person.phone"),
                Text(person, "email", "person.email", required: true), Items(person, "links", "person.links", ReadLink));
        }

        private CvLink ReadLink(JsonElement e, string path)
        {
            var url = Text(e, "url", $"{path}.url", required: true);
            if (url.Length > 0 && !(Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
                Problem($"{path}.url: use a full web address starting with https://.");
            return new CvLink(Text(e, "label", $"{path}.label", required: true), url);
        }

        private CvSummary ReadSummary(JsonElement e, string path)
        {
            var id = Id(e, path);
            var families = new HashSet<RoleFamily>();
            foreach (var (value, at) in Strings(e, "families", $"{path}.families"))
            {
                if (TitleRules.ParseFamily(value) is { } family) families.Add(family);
                else Problem($"{at}: unknown family '{value}' (use dotnet, fullstack, software_engineer, lead_manager or other).");
            }
            return new CvSummary(id, families, Text(e, "text", $"{path}.text", required: true));
        }

        private CvSkillGroup ReadGroup(JsonElement e, string path) =>
            new(Text(e, "name", $"{path}.name", required: true), Items(e, "items", $"{path}.items", ReadItem));

        private CvSkillItem ReadItem(JsonElement e, string path)
        {
            var text = Text(e, "text", $"{path}.text", required: true);
            return new CvSkillItem(text, Skills(e, path, text));
        }

        private CvSection ReadSection(JsonElement e, string path, CvSectionKind kind)
        {
            var role = kind == CvSectionKind.Role;
            var id = Id(e, path);
            var title = Text(e, "title", $"{path}.title", required: true);
            var company = Text(e, "company", $"{path}.company", required: role);
            var location = Text(e, "location", $"{path}.location");
            var start = Month(e, "start", $"{path}.start", required: role);
            var end = Month(e, "end", $"{path}.end", required: false);
            if (start is { } from && end is { } to && to < from) Problem($"{path}.end: {to:yyyy-MM} is before the start, {from:yyyy-MM}.");
            if (start is null && end is not null && !role) Problem($"{path}.start is required when end is set.");
            var dates = start is null ? "" : $"{MonthText(start.Value)} – {(end is null ? "Present" : MonthText(end.Value))}";
            var max = MaxBullets(e, path);
            var bullets = Items(e, "bullets", $"{path}.bullets", ReadBullet);
            if (role && bullets.Count == 0) Problem($"{path}.bullets: add at least one bullet.");
            return new CvSection(id, kind, title, company, location, dates, max, bullets);
        }

        private CvBullet ReadBullet(JsonElement e, string path)
        {
            var id = Id(e, path);
            var text = Text(e, "text", $"{path}.text", required: true);
            var always = false;
            if (e.TryGetProperty("always", out var flag) && flag.ValueKind != JsonValueKind.Null)
            {
                if (flag.ValueKind is JsonValueKind.True or JsonValueKind.False) always = flag.GetBoolean();
                else Problem($"{path}.always must be true or false.");
            }
            return new CvBullet(id, text, Skills(e, path, text), always);
        }

        private CvEducation ReadEducation(JsonElement e, string path) =>
            new(Text(e, "degree", $"{path}.degree", required: true), Text(e, "school", $"{path}.school", required: true),
                Text(e, "years", $"{path}.years"));

        private CvCertification ReadCertification(JsonElement e, string path)
        {
            var name = Text(e, "name", $"{path}.name", required: true);
            return new CvCertification(name, Text(e, "year", $"{path}.year"), Skills(e, path, name));
        }

        private void Problem(string text) => _problems.Add(text);

        private string Id(JsonElement e, string path)
        {
            var id = Text(e, "id", $"{path}.id", required: true);
            if (id.Length > 0 && !_ids.Add(id)) Problem($"{path}.id: \"{id}\" is used twice.");
            return id;
        }

        private string Text(JsonElement parent, string name, string path, bool required = false)
        {
            if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                if (required) Problem($"{path} is required.");
                return "";
            }
            if (value.ValueKind != JsonValueKind.String)
            {
                Problem($"{path} must be text in quotes.");
                return "";
            }
            var text = value.GetString()!.Trim();
            if (required && text.Length == 0) Problem($"{path} is required.");
            return text;
        }

        private List<T> Items<T>(JsonElement parent, string name, string path, Func<JsonElement, string, T> read)
        {
            if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return [];
            if (value.ValueKind != JsonValueKind.Array)
            {
                Problem($"{path} must be a list ([ ... ]).");
                return [];
            }
            var items = new List<T>();
            var i = 0;
            foreach (var item in value.EnumerateArray())
            {
                var at = $"{path}[{i++}]";
                if (item.ValueKind != JsonValueKind.Object) Problem($"{at} must be an object ({{ ... }}).");
                else items.Add(read(item, at));
            }
            return items;
        }

        private IEnumerable<(string Value, string Path)> Strings(JsonElement parent, string name, string path)
        {
            if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) yield break;
            if (value.ValueKind != JsonValueKind.Array)
            {
                Problem($"{path} must be a list ([ ... ]).");
                yield break;
            }
            var i = 0;
            foreach (var item in value.EnumerateArray())
            {
                var at = $"{path}[{i++}]";
                if (item.ValueKind == JsonValueKind.String) yield return (item.GetString()!.Trim(), at);
                else Problem($"{at} must be text in quotes.");
            }
        }

        private IReadOnlySet<string> Skills(JsonElement e, string path, string text)
        {
            var skills = new HashSet<string>(catalog.Match(text), StringComparer.Ordinal);
            foreach (var (value, at) in Strings(e, "skills", $"{path}.skills"))
            {
                if (_known.Contains(value)) skills.Add(value);
                else Problem($"{at}: '{value}' is not a skill in config/skills.json (names must match exactly).");
            }
            return skills;
        }

        private DateOnly? Month(JsonElement e, string name, string path, bool required)
        {
            var text = Text(e, name, path, required);
            if (text.Length == 0) return null;
            if (DateOnly.TryParseExact(text, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)) return month;
            Problem($"{path}: use year and month like 2023-10, not '{text}'.");
            return null;
        }

        private int MaxBullets(JsonElement e, string path)
        {
            if (!e.TryGetProperty("maxBullets", out var value) || value.ValueKind == JsonValueKind.Null) return DefaultMaxBullets;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var max) && max >= 1) return max;
            Problem($"{path}.maxBullets must be a whole number, 1 or more.");
            return DefaultMaxBullets;
        }
    }
}
