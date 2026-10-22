using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Applications;

// The owner's own records. Separate from the market tables (TrackerDb): its own migrations history table, and never
// in TrackerTables.All, so the Processor's TRUNCATE-and-reload can't touch it (ADR 0010).
public sealed class ApplicationsDb(DbContextOptions<ApplicationsDb> options) : DbContext(options)
{
    public const string HistoryTable = "__applications_migrations";

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<JobApplication> Applications => Set<JobApplication>();
    public DbSet<ApplicationEvent> Events => Set<ApplicationEvent>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ApplicationDocument> Documents => Set<ApplicationDocument>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Company>(e =>
        {
            e.ToTable("companies");
            e.HasIndex(c => c.NameKey).IsUnique();
            e.Property(c => c.Version).IsRowVersion().HasColumnName("xmin");
        });

        b.Entity<JobApplication>(e =>
        {
            e.ToTable("applications");
            e.HasOne(a => a.Company).WithMany().HasForeignKey(a => a.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => a.PostingId).IsUnique();
            e.Property(a => a.Status).HasConversion<string>();
            e.Property(a => a.Origin).HasConversion<string>();
            e.Property(a => a.WorkModel).HasConversion<string>();
            e.Property(a => a.Version).IsRowVersion().HasColumnName("xmin");
            e.HasMany(a => a.Events).WithOne().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(a => a.FollowUps).WithOne().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(a => a.Contacts).WithOne().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(a => a.Documents).WithOne().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ApplicationEvent>(e =>
        {
            e.ToTable("application_events");
            e.Property(x => x.FromStatus).HasConversion<string>();
            e.Property(x => x.ToStatus).HasConversion<string>();
        });
        b.Entity<FollowUp>(e => e.ToTable("follow_ups"));
        b.Entity<Contact>(e => e.ToTable("contacts"));
        b.Entity<ApplicationDocument>(e =>
        {
            e.ToTable("application_documents");
            e.Property(x => x.Kind).HasConversion<string>();
        });
    }

    public static DbContextOptions<ApplicationsDb> Options(string connectionString) =>
        new DbContextOptionsBuilder<ApplicationsDb>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(HistoryTable))
            .UseSnakeCaseNamingConvention()
            .Options;
}

// Used by `dotnet ef` only (no host start-up needed to build migrations).
public sealed class ApplicationsDbFactory : IDesignTimeDbContextFactory<ApplicationsDb>
{
    public ApplicationsDb CreateDbContext(string[] args) =>
        new(ApplicationsDb.Options("Host=localhost;Port=5434;Database=tracker;Username=tracker"));
}
