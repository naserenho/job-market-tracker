using Archiver.Manifest;

namespace Archiver.Tests.Manifest;

public class UrlRedactorTests
{
    [Fact]
    public void Redact_removes_credential_parameters_only()
    {
        var url = "https://api.adzuna.com/v1/api/jobs/ca/search/1?app_id=ID1&app_key=KEY1&what=tech%20lead&where=Toronto";
        Assert.Equal("https://api.adzuna.com/v1/api/jobs/ca/search/1?what=tech%20lead&where=Toronto", UrlRedactor.Redact(url));
    }

    [Fact]
    public void Redact_drops_the_question_mark_when_only_credentials_were_present() =>
        Assert.Equal("https://x.test/histogram", UrlRedactor.Redact("https://x.test/histogram?app_id=a&app_key=b"));

    [Fact]
    public void Redact_leaves_urls_without_query_alone() =>
        Assert.Equal("https://boards-api.greenhouse.io/v1/boards/faire/jobs", UrlRedactor.Redact("https://boards-api.greenhouse.io/v1/boards/faire/jobs"));

    [Fact]
    public void ScrubText_masks_credentials_inside_free_text()
    {
        var scrubbed = UrlRedactor.ScrubText("Request to https://a.test/?app_id=ID1&app_key=KEY1 failed");
        Assert.DoesNotContain("ID1", scrubbed);
        Assert.DoesNotContain("KEY1", scrubbed);
        Assert.Contains("app_key=***", scrubbed);
    }
}
