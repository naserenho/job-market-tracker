using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Tracker.TestSupport;

namespace Api.Tests.TestSupport;

// The real API wired to a given database and archive folder, with the committed example profile and master CV.
// config/profile.json and config/cv.json are gitignored: tests never read the owner's own files.
// The region is Toronto, whichever one config/sources.json names; a test can still pick another with Tracker:Region.
public sealed class ApiFactory(string connectionString, string dataRoot, string? cvPath = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:tracker", connectionString);
        builder.UseSetting("Tracker:DataRoot", dataRoot);
        builder.UseSetting("Tracker:ProfilePath", RepoConfig.PathOf("profile.example.json"));
        builder.UseSetting("Tracker:CvPath", cvPath ?? RepoConfig.PathOf("cv.example.json"));
        builder.UseSetting("Tracker:Region", "toronto");
    }
}
