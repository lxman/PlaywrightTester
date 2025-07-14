// Suppress console output for clean MCP communication
// TEMPORARILY DISABLED FOR DEBUGGING
// Console.SetOut(TextWriter.Null);
// Console.SetError(TextWriter.Null);

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlaywrightTester;
using PlaywrightTester.Services;
using PlaywrightTester.Tools;
using System.Text.Json;
using System.Text.Json.Serialization;

Console.SetOut(TextWriter.Null);
Console.SetError(TextWriter.Null);

var builder = Host.CreateApplicationBuilder(args);

// Configure JSON serialization options globally to handle deep object structures
builder.Services.Configure<JsonSerializerOptions>(options =>
{
    options.MaxDepth = 512; // Increased from default 64 to handle deep Angular component trees
    options.ReferenceHandler = ReferenceHandler.IgnoreCycles; // Handle circular references
    options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.WriteIndented = true;
});

builder.Services
    .AddSingleton<ToolService>()
    .AddSingleton<PlaywrightSessionManager>()  // New session manager
    .AddSingleton<PlaywrightTools>()
    .AddSingleton<AngularStyleTools>()
    .AddSingleton<VisualTestingTools>()
    .AddSingleton<AdvancedTestingTools>()
    .AddSingleton<DatabaseTestingTools>()
    .AddSingleton<TaderatcsTestingTools>()
    .AddSingleton<InteractionTestingTools>()
    .AddSingleton<NetworkTestingTools>()
    .AddSingleton<PerformanceTestingTools>()
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
