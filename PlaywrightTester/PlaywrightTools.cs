using System.ComponentModel;
using System.Text.Json;
using Microsoft.Playwright;
using ModelContextProtocol.Server;
using PlaywrightTester.Models;

namespace PlaywrightTester;

[McpServerToolType]
public class PlaywrightTools(ToolService toolService, ChromeService chromeService, FirefoxService firefoxService, WebKitService webKitService)
{
    private readonly Dictionary<string, IPage> _activeSessions = new();

    [McpServerTool]
    [Description("Launch a browser and create a new session. Returns session ID.")]
    public async Task<string> LaunchBrowser(
        [Description("Browser type: chrome, firefox, webkit")] string browserType = "chrome",
        [Description("Run in headless mode")] bool headless = true,
        [Description("Session ID for this browser instance")] string sessionId = "default")
    {
        try
        {
            IBrowser browser;
            IBrowserContext context;
            IPage page;

            switch (browserType.ToLower())
            {
                case "chrome":
                    browser = await chromeService.LaunchBrowserAsync(headless);
                    context = await chromeService.CreateContextAsync();
                    page = await chromeService.CreatePageAsync();
                    await chromeService.SetupDebugging(page);
                    break;
                case "firefox":
                    browser = await firefoxService.LaunchBrowserAsync(headless);
                    context = await firefoxService.CreateContextAsync();
                    page = await firefoxService.CreatePageAsync();
                    break;
                case "webkit":
                    browser = await webKitService.LaunchBrowserAsync(headless);
                    context = await webKitService.CreateContextAsync();
                    page = await webKitService.CreatePageAsync();
                    break;
                default:
                    throw new ArgumentException($"Unsupported browser type: {browserType}");
            }

            // Store all objects in ToolService for proper session management
            toolService.StoreBrowser(sessionId, browser);
            toolService.StoreBrowserContext(sessionId, context);
            toolService.StorePage(sessionId, page);
            
            // Keep backward compatibility with local storage
            _activeSessions[sessionId] = page;
            
            return $"Browser {browserType} launched successfully. Session ID: {sessionId}";
        }
        catch (Exception ex)
        {
            return $"Failed to launch browser: {ex.Message}";
        }
    }
    
    [McpServerTool]
    [Description("Navigate to a URL")]
    public async Task<string> NavigateToUrl(
        [Description("URL to navigate to")] string url,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            // Try to get page from ToolService first, then fall back to local storage
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
                      
            if (page == null)
                return $"Session {sessionId} not found. Launch browser first.";

            await page.GotoAsync(url);
            return $"Successfully navigated to {url}";
        }
        catch (Exception ex)
        {
            return $"Navigation failed: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Fill a form field using data-testid or CSS selector")]
    public async Task<string> FillField(
        [Description("Field selector (data-testid or CSS selector)")] string selector,
        [Description("Value to fill")] string value,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            // Try to get page from ToolService first, then fall back to local storage
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            var fullSelector = selector.StartsWith('[') ? selector : $"[data-testid='{selector}']";
            await page.Locator(fullSelector).FillAsync(value);
            return $"Field {selector} filled in with value {value}";
        }
        catch (Exception ex)
        {
            return $"Failed to fill field: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Click an element using data-testid or CSS selector")]
    public async Task<string> ClickElement(
        [Description("Element selector (data-testid or CSS selector)")] string selector,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            // Try to get page from ToolService first, then fall back to local storage
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            var fullSelector = selector.StartsWith('[') ? selector : $"[data-testid='{selector}']";
            await page.Locator(fullSelector).ClickAsync();
            return $"Successfully clicked element {selector}";
        }
        catch (Exception ex)
        {
            return $"Failed to click element: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Select an option from a dropdown")]
    public async Task<string> SelectOption(
        [Description("Dropdown selector")] string selector,
        [Description("Option value to select")] string value,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            // Try to get page from ToolService first, then fall back to local storage
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            var fullSelector = selector.StartsWith('[') ? selector : $"[data-testid='{selector}']";
            await page.Locator(fullSelector).SelectOptionAsync(value);
            return $"Field {selector} filled in with value {value}";
        }
        catch (Exception ex)
        {
            return $"Failed to select option: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Clear localStorage and sessionStorage")]
    public async Task<string> ClearLocalStorage(
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            // Try to get page from ToolService first, then fall back to local storage
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            await page.EvaluateAsync("() => { localStorage.clear(); sessionStorage.clear(); }");
            return "LocalStorage and sessionStorage cleared successfully";
        }
        catch (Exception ex)
        {
            return $"Failed to clear localStorage: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Get localStorage contents")]
    public async Task<string> GetLocalStorageContents(
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            // Try to get page from ToolService first, then fall back to local storage
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            var result = await page.EvaluateAsync<string>(
                "() => JSON.stringify(Object.keys(localStorage).reduce((obj, key) => { obj[key] = localStorage.getItem(key); return obj; }, {}), null, 2)");
            
            return string.IsNullOrEmpty(result) || result == "{}" ? "LocalStorage is empty" : $"LocalStorage contents:\n{result}";
        }
        catch (Exception ex)
        {
            return $"Failed to get localStorage: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Validate element state (visible, enabled, contains text)")]
    public async Task<string> ValidateElement(
        [Description("Element selector")] string selector,
        [Description("Validation type: visible, enabled, disabled, text")] string validationType,
        [Description("Expected text content (for text validation)")] string? expectedText = null,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            // Try to get page from ToolService first, then fall back to local storage
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            var fullSelector = selector.StartsWith('[') ? selector : $"[data-testid='{selector}']";
            var element = page.Locator(fullSelector);

            return validationType.ToLower() switch
            {
                "visible" => await element.IsVisibleAsync() ? $"Element {selector} is visible" : $"Element {selector} is not visible",
                "enabled" => await element.IsEnabledAsync() ? $"Element {selector} is enabled" : $"Element {selector} is disabled",
                "disabled" => !await element.IsEnabledAsync() ? $"Element {selector} is disabled" : $"Element {selector} is enabled",
                "text" => await ValidateElementText(element, expectedText ?? "", selector),
                _ => $"Unknown validation type: {validationType}"
            };
        }
        catch (Exception ex)
        {
            return $"Element validation failed: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Take a screenshot of the current page")]
    public async Task<string> TakeScreenshot(
        [Description("Filename for screenshot")] string filename = "screenshot.png",
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            // Try to get page from ToolService first, then fall back to local storage
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            await page.ScreenshotAsync(new PageScreenshotOptions { Path = filename, FullPage = true });
            return $"Screenshot saved as {filename}";
        }
        catch (Exception ex)
        {
            return $"Failed to take screenshot: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Execute a complete test case from MongoDB test collection")]
    public async Task<string> ExecuteTestCase(
        [Description("Test case data as JSON string")] string testCaseJson,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            // Try to get page from ToolService first, then fall back to local storage
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            var testCase = JsonSerializer.Deserialize<TestCase>(testCaseJson);
            if (testCase?.TestSteps == null) return "Invalid test case format";

            var results = new List<string>();
            
            foreach (var step in testCase.TestSteps)
            {
                var result = await ToolService.ExecuteTestStep(page, step);
                results.Add($"Step {step.Step}: {JsonSerializer.Serialize(result)}");
            }

            return $"Test case '{testCase.Title}' executed:\n{string.Join("\n", results)}";
        }
        catch (Exception ex)
        {
            return $"Test execution failed: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Wait for a specific element to appear")]
    public async Task<string> WaitForElement(
        [Description("Element selector")] string selector,
        [Description("Timeout in milliseconds")] int timeoutMs = 30000,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            // Try to get page from ToolService first, then fall back to local storage
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            var fullSelector = selector.StartsWith('[') ? selector : $"[data-testid='{selector}']";
            await page.Locator(fullSelector).WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs });
            return $"Element {selector} appeared within {timeoutMs}ms";
        }
        catch (Exception ex)
        {
            return $"Element wait failed: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Execute custom JavaScript on the page")]
    public async Task<string> ExecuteJavaScript(
        [Description("JavaScript code to execute")] string jsCode,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            // Try to get page from ToolService first, then fall back to local storage
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            var result = await page.EvaluateAsync<object>(jsCode);
            return $"JavaScript executed. Result: {JsonSerializer.Serialize(result)}";
        }
        catch (Exception ex)
        {
            return $"JavaScript execution failed: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Close browser session and cleanup resources")]
    public async Task<string> CloseBrowser(
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            // Try to get page from both sources and close if it exists
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
            
            if (page != null)
            {
                await page.CloseAsync();
                _activeSessions.Remove(sessionId);
            }

            await chromeService.CleanupAsync();
            await firefoxService.CleanupAsync();
            await webKitService.CleanupAsync();
            await toolService.CleanupResources();

            return $"Browser session {sessionId} closed successfully";
        }
        catch (Exception ex)
        {
            return $"Failed to close browser: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Get console messages (errors, warnings, logs) from the current session")]
    public async Task<string> GetConsoleMessages(
        [Description("Session ID")] string sessionId = "default",
        [Description("Message type filter: all, error, warning, log")] string messageType = "all")
    {
        try
        {
            // Try to get page from ToolService first, then fall back to local storage
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            // Get console messages that have been captured
            var consoleMessages = await page.EvaluateAsync<object[]>(@"
                () => {
                    if (!window.consoleLogs) window.consoleLogs = [];
                    return window.consoleLogs;
                }
            ");

            var filteredMessages = messageType.ToLower() == "all" 
                ? consoleMessages 
                : consoleMessages.Where(msg => msg.ToString()?.Contains(messageType, StringComparison.OrdinalIgnoreCase) == true);

            return consoleMessages.Any() 
                ? $"Console messages:\n{JsonSerializer.Serialize(filteredMessages, new JsonSerializerOptions { WriteIndented = true })}"
                : "No console messages captured";
        }
        catch (Exception ex)
        {
            return $"Failed to get console messages: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Get network requests and responses from the current session")]
    public async Task<string> GetNetworkActivity(
        [Description("Session ID")] string sessionId = "default",
        [Description("Filter by URL pattern (optional)")] string? urlFilter = null)
    {
        try
        {
            // Try to get page from ToolService first, then fall back to local storage
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            // Get network activity that has been captured
            var networkActivity = await page.EvaluateAsync<object[]>(@"
                () => {
                    if (!window.networkLogs) window.networkLogs = [];
                    return window.networkLogs;
                }
            ");

            var filteredActivity = string.IsNullOrEmpty(urlFilter) 
                ? networkActivity 
                : networkActivity.Where(req => req.ToString()?.Contains(urlFilter, StringComparison.OrdinalIgnoreCase) == true);

            return networkActivity.Any() 
                ? $"Network activity:\n{JsonSerializer.Serialize(filteredActivity, new JsonSerializerOptions { WriteIndented = true })}"
                : "No network activity captured";
        }
        catch (Exception ex)
        {
            return $"Failed to get network activity: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Monitor localStorage changes in real-time")]
    public async Task<string> MonitorLocalStorageChanges(
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            // Try to get page from ToolService first, then fall back to local storage
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            await page.AddInitScriptAsync(@"
                (() => {
                    if (!window.localStorageMonitor) {
                        window.localStorageMonitor = [];
                        const originalSetItem = localStorage.setItem;
                        localStorage.setItem = function(key, value) {
                            window.localStorageMonitor.push({
                                action: 'setItem',
                                key: key,
                                value: value.substring(0, 200) + (value.length > 200 ? '...' : ''),
                                timestamp: new Date().toISOString()
                            });
                            return originalSetItem.call(this, key, value);
                        };
                        
                        const originalRemoveItem = localStorage.removeItem;
                        localStorage.removeItem = function(key) {
                            window.localStorageMonitor.push({
                                action: 'removeItem', 
                                key: key,
                                timestamp: new Date().toISOString()
                            });
                            return originalRemoveItem.call(this, key);
                        };
                    }
                })();
            ");

            return "LocalStorage monitoring activated. Use GetLocalStorageActivity to view changes.";
        }
        catch (Exception ex)
        {
            return $"Failed to setup localStorage monitoring: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Get localStorage activity log")]
    public async Task<string> GetLocalStorageActivity(
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            // Try to get page from ToolService first, then fall back to local storage
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            var activity = await page.EvaluateAsync<object[]>(@"
                () => {
                    return window.localStorageMonitor || [];
                }
            ");

            return activity.Any() 
                ? $"LocalStorage activity:\n{JsonSerializer.Serialize(activity, new JsonSerializerOptions { WriteIndented = true })}"
                : "No localStorage activity recorded";
        }
        catch (Exception ex)
        {
            return $"Failed to get localStorage activity: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Get captured console logs from ChromeService")]
    public async Task<string> GetCapturedConsoleLogs(
        [Description("Session ID")] string sessionId = "default",
        [Description("Log type filter: all, log, error, warning")] string logType = "all")
    {
        try
        {
            var consoleLogs = chromeService.GetConsoleLogs();
            
            var filteredLogs = logType.ToLower() == "all" 
                ? consoleLogs 
                : consoleLogs.Where(log => log.Type.Equals(logType, StringComparison.OrdinalIgnoreCase));

            if (!filteredLogs.Any())
                return $"No console logs of type '{logType}' found";

            var logSummary = filteredLogs.Select(log => new 
            {
                Type = log.Type,
                Message = log.Text,
                Timestamp = log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")
            });

            return $"Console logs ({logType}):\n{JsonSerializer.Serialize(logSummary, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch (Exception ex)
        {
            return $"Failed to get console logs: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Get captured network requests and responses")]
    public async Task<string> GetCapturedNetworkLogs(
        [Description("Session ID")] string sessionId = "default",
        [Description("URL filter (optional)")] string? urlFilter = null)
    {
        try
        {
            var networkLogs = chromeService.GetNetworkLogs();
            
            var filteredLogs = string.IsNullOrEmpty(urlFilter) 
                ? networkLogs 
                : networkLogs.Where(log => log.Url.Contains(urlFilter, StringComparison.OrdinalIgnoreCase));

            if (!filteredLogs.Any())
                return string.IsNullOrEmpty(urlFilter) 
                    ? "No network activity captured" 
                    : $"No network activity matching '{urlFilter}' found";

            var networkSummary = filteredLogs.Select(log => new 
            {
                Type = log.Type,
                Method = log.Method,
                Url = log.Url,
                Status = log.Status > 0 ? log.Status.ToString() : "pending",
                Timestamp = log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")
            });

            return $"Network activity:\n{JsonSerializer.Serialize(networkSummary, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch (Exception ex)
        {
            return $"Failed to get network logs: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Clear all captured logs (console and network)")]
    public async Task<string> ClearCapturedLogs(
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            chromeService.ClearLogs();
            
            // Also clear browser-side logs if session exists
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
            
            if (page != null)
            {
                await page.EvaluateAsync(@"
                    () => {
                        if (window.consoleLogs) window.consoleLogs = [];
                        if (window.networkLogs) window.networkLogs = [];
                        if (window.localStorageMonitor) window.localStorageMonitor = [];
                    }
                ");
            }

            return "All logs cleared successfully";
        }
        catch (Exception ex)
        {
            return $"Failed to clear logs: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Execute JavaScript with enhanced error capturing")]
    public async Task<string> ExecuteJavaScriptWithErrorCapture(
        [Description("JavaScript code to execute")] string jsCode,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            // Try to get page from ToolService first, then fall back to local storage
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.TryGetValue(sessionId, out var localPage) ? localPage : null);
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            // Wrap the code to capture any errors
            var wrappedCode = $@"
                (() => {{
                    try {{
                        const result = {jsCode};
                        return {{ success: true, result: result, error: null }};
                    }} catch (error) {{
                        return {{ success: false, result: null, error: error.message }};
                    }}
                }})()
            ";

            var result = await page.EvaluateAsync<object>(wrappedCode);
            var resultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            
            return $"JavaScript execution result:\n{resultJson}";
        }
        catch (Exception ex)
        {
            return $"JavaScript execution failed: {ex.Message}";
        }
    }

    private static async Task<string> ValidateElementText(ILocator element, string expectedText, string selector)
    {
        var actualText = await element.TextContentAsync() ?? "";
        return actualText.Contains(expectedText) 
            ? $"Element {selector} contains expected text: {expectedText}"
            : $"Element {selector} text '{actualText}' does not contain '{expectedText}'";
    }
}
