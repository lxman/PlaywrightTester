using Microsoft.Playwright;
using System.Text.Json;

namespace PlaywrightTester;

public class ToolService
{
    private readonly Dictionary<string, IBrowserContext> _browserContexts = new();
    private readonly Dictionary<string, IPage> _pages = new();
    private readonly Dictionary<string, IBrowser> _browsers = new();

    // Browser Context Management
    public IBrowserContext? GetBrowserContext(string contextId)
    {
        return _browserContexts.TryGetValue(contextId, out var context) ? context : null;
    }

    public void StoreBrowserContext(string contextId, IBrowserContext context)
    {
        _browserContexts[contextId] = context;
    }

    public IPage? GetPage(string pageId)
    {
        return _pages.TryGetValue(pageId, out var page) ? page : null;
    }

    public void StorePage(string pageId, IPage page)
    {
        _pages[pageId] = page;
    }

    public IBrowser? GetBrowser(string browserId)
    {
        return _browsers.TryGetValue(browserId, out var browser) ? browser : null;
    }

    public void StoreBrowser(string browserId, IBrowser browser)
    {
        _browsers[browserId] = browser;
    }

    // Test Execution Helpers
    public static async Task<object> ExecuteTestStep(IPage page, object testStep)
    {
        var stepJson = JsonSerializer.Serialize(testStep);
        var step = JsonSerializer.Deserialize<TestStep>(stepJson);
        
        if (step == null) return new { success = false, error = "Invalid test step" };

        try
        {
            return step.Action?.ToUpper() switch
            {
                "NAVIGATE" => await NavigateToUrl(page, step.Target ?? ""),
                "FILL_FIELD" => await FillField(page, step.Target ?? "", step.Value ?? ""),
                "CLICK_ELEMENT" => await ClickElement(page, step.Target ?? ""),
                "SELECT_OPTION" => await SelectOption(page, step.Target ?? "", step.Value ?? ""),
                "VALIDATE_ELEMENT" => await ValidateElement(page, step.Target ?? "", step.Validation ?? ""),
                _ => new { success = false, error = $"Unknown action: {step.Action}" }
            };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    private static async Task<object> NavigateToUrl(IPage page, string url)
    {
        await page.GotoAsync(url);
        return new { success = true, message = $"Navigated to {url}" };
    }

    private static async Task<object> FillField(IPage page, string selector, string value)
    {
        // Support data-testid selectors
        var fullSelector = selector.StartsWith('[') ? selector : $"[data-testid='{selector}']";
        await page.Locator(fullSelector).FillAsync(value);
        return new { success = true, message = $"Field {selector} filled with value {value}" };
    }

    private static async Task<object> ClickElement(IPage page, string selector)
    {
        var fullSelector = selector.StartsWith('[') ? selector : $"[data-testid='{selector}']";
        await page.Locator(fullSelector).ClickAsync();
        return new { success = true, message = $"Clicked element {selector}" };
    }

    private static async Task<object> SelectOption(IPage page, string selector, string value)
    {
        var fullSelector = selector.StartsWith('[') ? selector : $"[data-testid='{selector}']";
        await page.Locator(fullSelector).SelectOptionAsync(value);
        return new { success = true, message = $"Selected option {value} in {selector}" };
    }

    private static async Task<object> ValidateElement(IPage page, string selector, string validation)
    {
        var fullSelector = selector.StartsWith('[') ? selector : $"[data-testid='{selector}']";
        var element = page.Locator(fullSelector);
        var isVisible = await element.IsVisibleAsync();
        return new { success = isVisible, message = $"Element {selector} validation: {validation}" };
    }

    // Cleanup
    public async Task CleanupResources()
    {
        foreach (var page in _pages.Values)
        {
            try { await page.CloseAsync(); } catch { }
        }
        
        foreach (var context in _browserContexts.Values)
        {
            try { await context.CloseAsync(); } catch { }
        }
        
        foreach (var browser in _browsers.Values)
        {
            try { await browser.CloseAsync(); } catch { }
        }
        
        _pages.Clear();
        _browserContexts.Clear();
        _browsers.Clear();
    }
}

public class TestStep
{
    public string? Action { get; set; }
    public string? Target { get; set; }
    public string? Value { get; set; }
    public string? Validation { get; set; }
    public string? Message { get; set; }
}
