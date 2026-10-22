using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tracker.Data;

namespace Api.Applications;

// An entry's job text and where it came from: "entry" (saved in the tracker), "market" (the collected posting) or "none".
// Snippet: the market text is only a search-result snippet (an Adzuna posting), not the full posting.
public sealed record EntryText(string Text, string Source, bool Snippet = false);

internal static class EntryTexts
{
    public const string FromEntry = "entry", FromMarket = "market", None = "none";

    // Text saved on the entry wins, so the owner can replace a market snippet with the full posting; otherwise a market
    // entry reads its posting's description.
    public static async Task<EntryText> ResolveAsync(JobApplication entry, TrackerDb marketDb, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(entry.PostingText)) return new(entry.PostingText, FromEntry);
        if (entry.Origin == EntryOrigin.Market && entry.PostingId is { } postingId
            && await MarketTextAsync(marketDb, postingId, ct) is { Text.Length: > 0 } market)
            return market;
        return new("", None);
    }

    // Where the entry's posting stands after the latest rebuild. The snapshot only holds counted postings, so anything
    // else is looked up in the market tables. Pending: no rebuild has seen it yet.
    public static async Task<MarketState?> StateAsync(JobApplication entry, bool inSnapshot, TrackerDb marketDb, CancellationToken ct)
    {
        if (entry.PostingId is not { } postingId) return null;
        if (inSnapshot) return MarketState.Counted;
        try
        {
            var row = await marketDb.Postings.AsNoTracking().Where(p => p.Id == postingId)
                .Select(p => new { p.InScope, p.DuplicateOf }).FirstOrDefaultAsync(ct);
            return row is null ? MarketState.Pending
                : row.DuplicateOf is not null ? MarketState.Duplicate
                : row.InScope ? MarketState.Counted
                : MarketState.OutOfScope;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return MarketState.Pending;
        }
    }

    // Read from the market tables rather than the in-memory snapshot: the snapshot leaves out postings a later rebuild
    // marked as duplicates, but their text (and whether it is only a snippet) is still here.
    // The market tables only exist after the Processor's first rebuild (e.g. right after a restore): no text until then.
    private static async Task<EntryText?> MarketTextAsync(TrackerDb marketDb, string postingId, CancellationToken ct)
    {
        try
        {
            return await marketDb.Postings.AsNoTracking().Where(p => p.Id == postingId)
                .Select(p => new EntryText(p.Description, FromMarket, !p.IsFullText)).FirstOrDefaultAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return null;
        }
    }
}
