using Archiver;
using Archiver.Configuration;
using Archiver.Manifest;
using Archiver.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

const string Usage = "Usage: Archiver [--dry-run | --verify-boards]";

var dryRun = args.Contains("--dry-run");
var verifyBoards = args.Contains("--verify-boards");
var unknown = args.Where(a => a is not ("--dry-run" or "--verify-boards")).ToList();
if (unknown.Count > 0 || (dryRun && verifyBoards))
{
    Console.Error.WriteLine(unknown.Count > 0 ? $"Unknown argument(s): {string.Join(' ', unknown)}" : "Pick one mode.");
    Console.Error.WriteLine(Usage);
    return ExitCodes.ConfigInvalid;
}

var root = RepoRoot.Find(Directory.GetCurrentDirectory()) ?? RepoRoot.Find(AppContext.BaseDirectory);
if (root is null)
{
    Console.Error.WriteLine("Could not find config/sources.json in this folder or any parent. Run from the repo root.");
    return ExitCodes.ConfigInvalid;
}

var regionFile = RegionConfig.Resolve(root, null, out var regionError);
if (regionFile is null)
{
    Console.Error.WriteLine($"Config error: {regionError}");
    return ExitCodes.ConfigInvalid;
}

IHost host;
try
{
    // Host args are not passed through: our flags are not configuration keys.
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = root });
    builder.Configuration
        .AddJsonFile(Path.Combine(root, "config", "sources.json"), optional: false, reloadOnChange: false)
        .AddJsonFile(regionFile, optional: false, reloadOnChange: false)
        .AddUserSecrets<ArchiveRunner>(optional: true)
        .AddEnvironmentVariables();
    builder.Logging
        .AddFilter("System.Net.Http.HttpClient", LogLevel.Warning)
        .AddFilter("Polly", LogLevel.Warning);

    builder.Services.Configure<SourcesOptions>(builder.Configuration);
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddArchiverHttpClients();
    builder.Services.AddSingleton<JobBoardSource>();
    builder.Services.AddSingleton<IArchiveSource, AdzunaSource>();
    builder.Services.AddSingleton<IArchiveSource>(sp => sp.GetRequiredService<JobBoardSource>());
    builder.Services.AddSingleton<IArchiveSource, JobBankSource>(); // last: slowest, largest downloads
    builder.Services.AddSingleton<ArchiveRunner>();
    host = builder.Build();

    var errors = SourcesValidator.Validate(host.Services.GetRequiredService<IOptions<SourcesOptions>>().Value);
    if (errors.Count > 0)
    {
        foreach (var error in errors) Console.Error.WriteLine($"Config error: {error}");
        return ExitCodes.ConfigInvalid;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Config error: {ErrorText.Chain(ex)}");
    return ExitCodes.ConfigInvalid;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// Disposing the host flushes the console logger before the process exits.
using (host)
{
    try
    {
        if (verifyBoards)
        {
            var checks = await host.Services.GetRequiredService<JobBoardSource>().VerifyAsync(cts.Token);
            foreach (var c in checks)
                Console.WriteLine($"{(c.Ok ? "OK  " : "FAIL")} {c.Board.Ats}/{c.Board.Slug}: {(c.Ok ? $"{c.Jobs} jobs" : c.Error)}");
            Console.WriteLine($"{checks.Count(c => c.Ok)}/{checks.Count} boards OK");
            return checks.All(c => c.Ok) ? ExitCodes.Ok : ExitCodes.AllFailed;
        }

        return await host.Services.GetRequiredService<ArchiveRunner>().RunAsync(Path.Combine(root, "data"), dryRun, cts.Token);
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
        Console.Error.WriteLine("Cancelled.");
        return ExitCodes.AllFailed;
    }
}
