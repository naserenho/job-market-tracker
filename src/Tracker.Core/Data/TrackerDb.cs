using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tracker.Data;

public sealed class TrackerDb(DbContextOptions<TrackerDb> options) : DbContext(options)
{
    public DbSet<RunEntity> Runs => Set<RunEntity>();
    public DbSet<PostingEntity> Postings => Set<PostingEntity>();
    public DbSet<SightingEntity> PostingSightings => Set<SightingEntity>();
    public DbSet<SkillEntity> Skills => Set<SkillEntity>();
    public DbSet<PostingSkillEntity> PostingSkills => Set<PostingSkillEntity>();
    public DbSet<AdzunaAggregateEntity> AdzunaAggregates => Set<AdzunaAggregateEntity>();
    public DbSet<JobBankMonthEntity> JobBankMonths => Set<JobBankMonthEntity>();
    public DbSet<JobBankRegionEntity> JobBankRegions => Set<JobBankRegionEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<RunEntity>().HasKey(r => r.RunId);

        b.Entity<PostingEntity>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => new { p.InScope, p.DuplicateOf });
            e.HasMany(p => p.Sightings).WithOne().HasForeignKey(s => s.PostingId);
            e.HasMany(p => p.Skills).WithOne().HasForeignKey(s => s.PostingId);
        });

        b.Entity<SightingEntity>(e =>
        {
            e.HasKey(s => new { s.PostingId, s.RunId });
            e.HasOne(s => s.Run).WithMany().HasForeignKey(s => s.RunId);
        });

        b.Entity<SkillEntity>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).ValueGeneratedNever();
            e.HasIndex(s => s.Name).IsUnique();
        });

        b.Entity<PostingSkillEntity>(e =>
        {
            e.HasKey(s => new { s.PostingId, s.SkillId });
            e.HasOne(s => s.Skill).WithMany().HasForeignKey(s => s.SkillId);
        });

        b.Entity<AdzunaAggregateEntity>(e =>
        {
            e.HasKey(a => new { a.RunId, a.Term, a.Kind });
            e.Property(a => a.Payload).HasColumnType("jsonb");
        });

        b.Entity<JobBankMonthEntity>(e =>
        {
            e.ToTable("jobbank_monthly");
            e.HasKey(j => new { j.Month, j.NocCode });
        });

        b.Entity<JobBankRegionEntity>(e =>
        {
            e.ToTable("jobbank_regions");
            e.HasKey(j => new { j.Month, j.EconomicRegion });
        });
    }
}

// Used by `dotnet ef` (no connection needed to add migrations) and by tests.
public sealed class TrackerDbFactory : IDesignTimeDbContextFactory<TrackerDb>
{
    public TrackerDb CreateDbContext(string[] args) => new(Options("Host=localhost;Port=5434;Database=tracker;Username=tracker"));

    public static DbContextOptions<TrackerDb> Options(string connectionString) =>
        new DbContextOptionsBuilder<TrackerDb>().UseNpgsql(connectionString).UseSnakeCaseNamingConvention().Options;
}
