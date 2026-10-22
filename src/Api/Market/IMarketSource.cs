namespace Api.Market;

public sealed record MarketLoad(string Version, MarketSnapshot Data);

public interface IMarketSource
{
    // Null when the database still holds knownVersion; otherwise the current version and its data, read from one snapshot.
    // Never null when knownVersion is null.
    Task<MarketLoad?> LoadIfChangedAsync(string? knownVersion, CancellationToken ct);
}

// The market tables come from an older version (a column was renamed since): the Processor's next run migrates them.
public sealed class MarketSchemaOutdatedException(Exception inner) : Exception(Text, inner)
{
    public const string Text =
        "The market data was built by an older version of this tool. Run the Processor once to update it: scripts/run-pipeline.cmd, " +
        "or scripts/register-task.ps1 then .artifacts/processor/Processor.exe.";
}

// Postgres can't be reached: not running, wrong port, starting up, or refusing the login.
public sealed class DatabaseUnavailableException(Exception inner) : Exception(Text, inner)
{
    public const string Text = "The tracker database isn't running. Start Docker Desktop (and `docker compose up -d postgres`).";
}
