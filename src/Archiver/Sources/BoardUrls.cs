using System.Text.Json;
using Archiver.Configuration;

namespace Archiver.Sources;

public static class BoardUrls
{
    public const int SmartRecruitersPageSize = 100;

    // countryCode: the region's, for boards that can be asked for one country only (SmartRecruiters).
    public static string For(BoardConfig board, string countryCode = "")
    {
        var slug = Uri.EscapeDataString(board.Slug);
        return board.Ats switch
        {
            "greenhouse" => $"https://boards-api.greenhouse.io/v1/boards/{slug}/jobs?content=true",
            "lever" => $"https://api.lever.co/v0/postings/{slug}?mode=json",
            "ashby" => $"https://api.ashbyhq.com/posting-api/job-board/{slug}?includeCompensation=true",
            // Workable's public careers-page feed; www.workable.com/api/accounts/<slug> redirects here.
            "workable" => $"https://apply.workable.com/api/v1/widget/accounts/{slug}?details=true",
            "smartrecruiters" => $"https://api.smartrecruiters.com/v1/companies/{slug}/postings?"
                + (countryCode.Length > 0 ? $"country={Uri.EscapeDataString(countryCode.ToLowerInvariant())}&" : "")
                + $"limit={SmartRecruitersPageSize}",
            _ => throw new ArgumentException($"Unknown ATS '{board.Ats}'.", nameof(board)),
        };
    }

    // One SmartRecruiters posting with its description (the list has none).
    public static string SmartRecruitersDetail(BoardConfig board, string id) =>
        $"https://api.smartrecruiters.com/v1/companies/{Uri.EscapeDataString(board.Slug)}/postings/{Uri.EscapeDataString(id)}";

    // Number of postings in a board response, or null when the body is not the shape that ATS returns.
    public static int? CountJobs(string ats, byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (ats == "lever")
                return root.ValueKind == JsonValueKind.Array ? root.GetArrayLength() : null;
            var list = ats == "smartrecruiters" ? "content" : "jobs";
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(list, out var jobs)
                && jobs.ValueKind == JsonValueKind.Array
                ? jobs.GetArrayLength()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
