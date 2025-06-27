using Microsoft.Playwright;
using PlaywrightTester.Models;

namespace PlaywrightTester.Services;

public class ChromeService
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private readonly List<ConsoleLogEntry> _consoleLogs = [];
    private readonly List<NetworkLogEntry> _networkLogs = [];

    public async Task<IBrowser> LaunchBrowserAsync(bool headless = true, int timeout = 30000)
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless,
            Timeout = timeout,
            Args =
            [
                "--disable-web-security", 
                "--disable-features=VizDisplayCompositor",
                "--allow-running-insecure-content"
            ]
        });
        return _browser;
    }

    public async Task<IBrowserContext> CreateContextAsync(Dictionary<string, object>? options = null)
    {
        if (_browser == null) throw new InvalidOperationException("Browser not launched");
        
        var contextOptions = new BrowserNewContextOptions();
        
        if (options != null)
        {
            if (options.TryGetValue("viewport", out var option))
            {
                if (option is Dictionary<string, object> viewport)
                {
                    contextOptions.ViewportSize = new ViewportSize
                    {
                        Width = Convert.ToInt32(viewport.GetValueOrDefault("width", 1920)),
                        Height = Convert.ToInt32(viewport.GetValueOrDefault("height", 1080))
                    };
                }
            }
            
            if (options.TryGetValue("recordVideo", out var value) && (bool)value)
            {
                contextOptions.RecordVideoDir = "test-videos/";
            }
        }
        
        _context = await _browser.NewContextAsync(contextOptions);
        return _context;
    }

    public async Task<IPage> CreatePageAsync()
    {
        if (_context == null) throw new InvalidOperationException("Context not created");
        _page = await _context.NewPageAsync();
        return _page;
    }

    public async Task SetupDebugging(IPage page)
    {
        // Monitor console messages and store them
        page.Console += (_, e) =>
        {
            var logEntry = new ConsoleLogEntry
            {
                Type = e.Type.ToString().ToLower(),
                Text = e.Text,
                Timestamp = DateTime.UtcNow
            };
            _consoleLogs.Add(logEntry);
            Console.WriteLine($"[CONSOLE {e.Type.ToString().ToUpper()}] {e.Text}");
        };

        // Monitor page errors and store them
        page.PageError += (_, e) =>
        {
            var logEntry = new ConsoleLogEntry
            {
                Type = "error",
                Text = $"PAGE ERROR: {e}",
                Timestamp = DateTime.UtcNow
            };
            _consoleLogs.Add(logEntry);
            Console.WriteLine($"[PAGE ERROR] {e}");
        };

        // Monitor network requests and store them
        page.Request += (_, e) =>
        {
            var networkEntry = new NetworkLogEntry
            {
                Type = "request",
                Method = e.Method,
                Url = e.Url,
                Timestamp = DateTime.UtcNow
            };
            _networkLogs.Add(networkEntry);
            Console.WriteLine($"[REQUEST] {e.Method} {e.Url}");
        };

        page.Response += (_, e) =>
        {
            var networkEntry = new NetworkLogEntry
            {
                Type = "response",
                Method = e.Request.Method,
                Url = e.Url,
                Status = e.Status,
                Timestamp = DateTime.UtcNow
            };
            _networkLogs.Add(networkEntry);
            Console.WriteLine($"[RESPONSE] {e.Status} {e.Url}");
        };

        // Setup enhanced debugging script
        await page.AddInitScriptAsync(@"
            (() => {
                // Console monitoring
                if (!window.consoleLogs) {
                    window.consoleLogs = [];
                    const originalLog = console.log;
                    const originalError = console.error;
                    const originalWarn = console.warn;
                    
                    console.log = function(...args) {
                        window.consoleLogs.push({type: 'log', message: args.join(' '), timestamp: new Date().toISOString()});
                        return originalLog.apply(console, args);
                    };
                    
                    console.error = function(...args) {
                        window.consoleLogs.push({type: 'error', message: args.join(' '), timestamp: new Date().toISOString()});
                        return originalError.apply(console, args);
                    };
                    
                    console.warn = function(...args) {
                        window.consoleLogs.push({type: 'warning', message: args.join(' '), timestamp: new Date().toISOString()});
                        return originalWarn.apply(console, args);
                    };
                }
                
                // Network monitoring (for fetch/xhr)
                if (!window.networkLogs) {
                    window.networkLogs = [];
                    const originalFetch = window.fetch;
                    window.fetch = function(...args) {
                        const url = typeof args[0] === 'string' ? args[0] : args[0].url;
                        window.networkLogs.push({
                            type: 'fetch',
                            url: url,
                            timestamp: new Date().toISOString()
                        });
                        return originalFetch.apply(this, args);
                    };
                }
            })();
        ");
    }

    public List<ConsoleLogEntry> GetConsoleLogs() => _consoleLogs.ToList();
    public List<NetworkLogEntry> GetNetworkLogs() => _networkLogs.ToList();
    
    public void ClearLogs()
    {
        _consoleLogs.Clear();
        _networkLogs.Clear();
    }

    public async Task CleanupAsync()
    {
        if (_page != null) await _page.CloseAsync();
        if (_context != null) await _context.CloseAsync();
        if (_browser != null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }

    public IPage? GetCurrentPage() => _page;
    public IBrowserContext? GetCurrentContext() => _context;
    public IBrowser? GetCurrentBrowser() => _browser;
}