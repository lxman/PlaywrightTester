using Microsoft.Playwright;

namespace PlaywrightTester;

public class WebKitService
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;

    public async Task<IBrowser> LaunchBrowserAsync(bool headless = true, int timeout = 30000)
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Webkit.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless,
            Timeout = timeout
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
