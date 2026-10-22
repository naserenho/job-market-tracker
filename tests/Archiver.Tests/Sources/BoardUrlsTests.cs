using System.Text;
using Archiver.Configuration;
using Archiver.Sources;

namespace Archiver.Tests.Sources;

public class BoardUrlsTests
{
    [Theory]
    [InlineData("greenhouse", "faire", "https://boards-api.greenhouse.io/v1/boards/faire/jobs?content=true")]
    [InlineData("lever", "wattpad", "https://api.lever.co/v0/postings/wattpad?mode=json")]
    [InlineData("ashby", "cohere", "https://api.ashbyhq.com/posting-api/job-board/cohere?includeCompensation=true")]
    [InlineData("workable", "march-networks", "https://apply.workable.com/api/v1/widget/accounts/march-networks?details=true")]
    public void For_builds_the_public_board_url(string ats, string slug, string expected) =>
        Assert.Equal(expected, BoardUrls.For(new BoardConfig { Ats = ats, Slug = slug }));

    [Fact]
    public void Smartrecruiters_lists_only_the_region_country() =>
        Assert.Equal("https://api.smartrecruiters.com/v1/companies/ADGAGroupConsultantsInc1/postings?country=ca&limit=100",
            BoardUrls.For(new BoardConfig { Ats = "smartrecruiters", Slug = "ADGAGroupConsultantsInc1" }, "CA"));

    [Fact]
    public void For_rejects_unknown_ats() =>
        Assert.Throws<ArgumentException>(() => BoardUrls.For(new BoardConfig { Ats = "workday", Slug = "rbc" }));

    [Theory]
    [InlineData("greenhouse", "{\"jobs\":[{},{},{}]}", 3)]
    [InlineData("ashby", "{\"jobs\":[]}", 0)]
    [InlineData("lever", "[{},{}]", 2)]
    [InlineData("lever", "{\"ok\":false}", null)]
    [InlineData("workable", "{\"name\":\"Fidus\",\"description\":null,\"jobs\":[{},{}]}", 2)]
    [InlineData("smartrecruiters", "{\"offset\":0,\"limit\":100,\"totalFound\":3,\"content\":[{},{},{}]}", 3)]
    [InlineData("smartrecruiters", "{\"jobs\":[]}", null)]
    [InlineData("greenhouse", "<html>", null)]
    public void CountJobs_understands_each_ats_shape(string ats, string body, int? expected) =>
        Assert.Equal(expected, BoardUrls.CountJobs(ats, Encoding.UTF8.GetBytes(body)));
}
