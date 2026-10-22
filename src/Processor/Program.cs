using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Processor;
using Tracker.Configuration;

var root = RepoRoot.Find(Directory.GetCurrentDirectory()) ?? RepoRoot.Find(AppContext.BaseDirectory);
var configuration = new ConfigurationBuilder().AddUserSecrets<Program>(optional: true).AddEnvironmentVariables().Build();
using var loggers = LoggerFactory.Create(b => b.AddConsole().AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning));
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
return await ProcessorApp.RunAsync(args, root, configuration.GetConnectionString("Tracker"), loggers, Console.Error, cts.Token);
