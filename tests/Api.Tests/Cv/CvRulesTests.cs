using Api.Applications;
using Api.Cv;
using static Api.Tests.Cv.CvSamples;

namespace Api.Tests.Cv;

public class CvRulesTests
{
    [Fact]
    public void A_good_request_becomes_choices()
    {
        var choices = CvRules.Check(new GenerateRequest("  Senior\nDeveloper ", " Hi. ", ["a2", "a1"]), SampleCv());

        Assert.Equal(("Senior Developer", "Hi."), (choices.Headline, choices.Summary));
        Assert.Equal(new[] { "a2", "a1" }, choices.BulletIds);
    }

    [Theory]
    [InlineData("chosen twice", "a1", "a1")]
    [InlineData("isn't in config/cv.json", "a1", "zzz")]
    public void Bad_bullet_lists_are_refused(string message, params string[] ids)
    {
        var error = Assert.Throws<TrackerInputException>(() => CvRules.Check(new GenerateRequest("Dev", "", ids), SampleCv()));

        Assert.Equal("bulletIds", error.Field);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void More_than_300_bullets_are_refused() =>
        Assert.Equal("bulletIds", Assert.Throws<TrackerInputException>(() =>
            CvRules.Check(new GenerateRequest("Dev", "", [.. Enumerable.Range(0, 301).Select(i => $"b{i}")]), SampleCv())).Field);

    [Fact]
    public void Choices_round_trip_and_unreadable_json_is_no_choices()
    {
        var choices = new CvChoices("Dev", "Hi.", ["a1"]);

        Assert.Equal(choices.BulletIds, CvChoicesJson.Read(CvChoicesJson.Write(choices))!.BulletIds);
        Assert.Null(CvChoicesJson.Read("{ not json"));
        Assert.Null(CvChoicesJson.Read("{}"));
    }
}
