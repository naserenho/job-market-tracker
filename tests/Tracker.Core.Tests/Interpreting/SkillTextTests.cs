using Tracker.Interpreting;

namespace Tracker.Core.Tests.Interpreting;

public class SkillTextTests
{
    [Fact]
    public void Keeps_the_title_and_drops_hiring_notices()
    {
        var text = SkillText.ForMatching("Senior .NET Developer",
            "We use AI tools to screen applications. Build services in C#. We will accommodate accessibility needs.");

        Assert.StartsWith("Senior .NET Developer\n", text);
        Assert.Contains("Build services in C#.", text);
        Assert.DoesNotContain("screen applications", text);
        Assert.DoesNotContain("accommodate", text);
    }

    [Fact]
    public void Real_ai_work_is_kept() =>
        Assert.Contains("Build AI features", SkillText.ForMatching("Developer", "Build AI features for our dashboard."));
}
