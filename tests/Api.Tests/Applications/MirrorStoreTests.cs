using System.Text.Json;
using Api.Applications;
using Tracker.TestSupport;

namespace Api.Tests.Applications;

public class MirrorStoreTests
{
    private static readonly DateTimeOffset At = new(2027, 3, 1, 14, 0, 0, TimeSpan.Zero);

    internal static MirrorFile Sample()
    {
        var company = new Company { Id = Guid.NewGuid(), Name = "RBC", NameKey = "rbc", AnnualLeave = "20 days", CreatedAt = At, UpdatedAt = At };
        var entry = new JobApplication
        {
            Id = Guid.NewGuid(), CompanyId = company.Id, PostingId = "greenhouse:101", Title = "Senior .NET Developer",
            Status = ApplicationStatus.Interviewing, Origin = EntryOrigin.Market, WorkModel = WorkModel.Hybrid, OfficeDays = "3 days",
            CreatedAt = At, UpdatedAt = At,
        };
        return new MirrorFile(MirrorFile.CurrentFormat, At, [company], [entry],
            [new ApplicationEvent { Id = Guid.NewGuid(), ApplicationId = entry.Id, OnDate = new DateOnly(2027, 3, 1), ToStatus = ApplicationStatus.Applied, CreatedAt = At }],
            [new FollowUp { Id = Guid.NewGuid(), ApplicationId = entry.Id, Action = "Email Sam", DueOn = new DateOnly(2027, 3, 8), CreatedAt = At }],
            [new Contact { Id = Guid.NewGuid(), ApplicationId = entry.Id, Name = "Sam", Email = "sam@rbc.com", CreatedAt = At }],
            [new ApplicationDocument { Id = Guid.NewGuid(), ApplicationId = entry.Id, Kind = DocumentKind.Cv, Label = "CV v3", FileName = "CV.pdf",
                ContentType = "application/pdf", SizeBytes = 1234, SentOn = new DateOnly(2027, 3, 1), CreatedAt = At }]);
    }

    [Fact]
    public void Written_files_read_back_identically()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "tracker", "tracker.json");
        var file = Sample();

        MirrorStore.Write(path, file);
        var back = MirrorStore.Read(path)!;

        Assert.Equal(JsonSerializer.Serialize(file, MirrorStore.Json), JsonSerializer.Serialize(back, MirrorStore.Json));
        Assert.Contains("\"status\": \"Interviewing\"", File.ReadAllText(path));
        Assert.DoesNotContain("version", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_file_reads_as_nothing() => Assert.Null(MirrorStore.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "tracker.json")));

    [Fact]
    public void A_failed_write_leaves_the_previous_copy_intact()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "tracker.json");
        MirrorStore.Write(path, Sample());
        var before = File.ReadAllText(path);
        Directory.CreateDirectory(path + ".tmp"); // the temp file can't be created

        Assert.ThrowsAny<Exception>(() => MirrorStore.Write(path, Sample() with { SavedAt = At.AddDays(1) })); // IOException or, on Windows, access denied
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"format\": 99, \"savedAt\": \"2027-03-01T00:00:00Z\", \"companies\": [], \"applications\": [], \"events\": [], \"followUps\": [], \"contacts\": [], \"documents\": []}")]
    public void Unreadable_or_newer_files_are_refused_with_the_file_named(string content)
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "tracker.json");
        File.WriteAllText(path, content);

        var error = Assert.Throws<TrackerBackupException>(() => MirrorStore.Read(path));

        Assert.Contains("tracker.json", error.Message);
    }

    [Fact]
    public void A_file_the_process_may_not_open_counts_as_unreadable()
    {
        Assert.True(MirrorStore.IsUnreadable(new UnauthorizedAccessException()));
        Assert.True(MirrorStore.IsUnreadable(new IOException()));
        Assert.False(MirrorStore.IsUnreadable(new InvalidOperationException()));
    }
}
