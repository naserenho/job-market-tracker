using Microsoft.EntityFrameworkCore;
using Tracker.Data;

namespace Tracker.Core.Tests.Data;

public class TrackerTablesTests
{
    [Fact]
    public void All_lists_every_mapped_table_once()
    {
        using var db = new TrackerDb(TrackerDbFactory.Options("Host=unused"));
        var mapped = db.Model.GetEntityTypes().Select(e => e.GetTableName()!).Order(StringComparer.Ordinal).ToList();
        var listed = TrackerTables.All.Split(", ").Order(StringComparer.Ordinal).ToList();
        Assert.Equal(mapped, listed);
    }
}
