namespace Tracker.Data;

public static class TrackerTables
{
    // Every table the rebuild replaces, in the order its TRUNCATE locks them. A reader that locks the same tables
    // must use this exact order, or a reader and a rebuild can deadlock.
    public const string All = "posting_skills, posting_sightings, postings, skills, runs, adzuna_aggregates, jobbank_monthly, jobbank_regions";
}
