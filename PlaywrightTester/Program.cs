// Suppress console output for clean MCP communication
// TEMPORARILY DISABLED FOR DEBUGGING
// Console.SetOut(TextWriter.Null);
// Console.SetError(TextWriter.Null);

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlaywrightTester;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddSingleton<ToolService>()
    .AddSingleton<ChromeService>()
    .AddSingleton<FirefoxService>()
    .AddSingleton<WebKitService>()
    .AddSingleton<PlaywrightTools>()
    .AddSingleton<AdvancedTestingTools>()
    .AddSingleton<DatabaseTestingTools>()
    .AddSingleton<TaderatcsTestingTools>()
    .AddLogging(logging =>
    {
        logging.ClearProviders();
        logging.SetMinimumLevel(LogLevel.Error);

        // Suppress noisy framework logs
        logging.AddFilter("Microsoft", LogLevel.None);
        logging.AddFilter("System", LogLevel.None);
        logging.AddFilter("Microsoft.Playwright", LogLevel.None);
    })
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();
await host.RunAsync();
