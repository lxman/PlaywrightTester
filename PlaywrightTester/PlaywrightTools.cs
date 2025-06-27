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
    private readonly Dictionary<string, SessionData> _sessionData = new();

    public class SessionData
    {
        public List<ConsoleLogEntry> ConsoleLogs { get; set; } = [];
        public List<NetworkLogEntry> NetworkLogs { get; set; } = [];
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

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

            // Clear any existing session data for this sessionId
            if (_sessionData.ContainsKey(sessionId))
            {
                _sessionData[sessionId].ConsoleLogs.Clear();
                _sessionData[sessionId].NetworkLogs.Clear();
            }
            else
            {
                _sessionData[sessionId] = new SessionData();
            }

            switch (browserType.ToLower())
            {
                case "chrome":
                    browser = await chromeService.LaunchBrowserAsync(headless);
                    context = await chromeService.CreateContextAsync();
                    page = await chromeService.CreatePageAsync();
                    await SetupSessionSpecificDebugging(page, sessionId);
                    break;
                case "firefox":
                    browser = await firefoxService.LaunchBrowserAsync(headless);
                    context = await firefoxService.CreateContextAsync();
                    page = await firefoxService.CreatePageAsync();
                    await SetupSessionSpecificDebugging(page, sessionId);
                    break;
                case "webkit":
                    browser = await webKitService.LaunchBrowserAsync(headless);
                    context = await webKitService.CreateContextAsync();
                    page = await webKitService.CreatePageAsync();
                    await SetupSessionSpecificDebugging(page, sessionId);
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
            
            // DEBUGGING: Verify session data was stored
            var sessionDataExists = _sessionData.ContainsKey(sessionId);
            var activeSessionExists = _activeSessions.ContainsKey(sessionId);
            
            return $"Browser {browserType} launched successfully. Session ID: {sessionId}\n" +
                   $"DEBUG: Session data created: {sessionDataExists}, Active session stored: {activeSessionExists}\n" +
                   $"DEBUG: Total sessions: {_sessionData.Count}, Active: {_activeSessions.Count}";
        }
        catch (Exception ex)
        {
            return $"Failed to launch browser: {ex.Message}";
        }
    }

    private async Task SetupSessionSpecificDebugging(IPage page, string sessionId)
    {
        // Ensure session data exists
        if (!_sessionData.ContainsKey(sessionId))
            _sessionData[sessionId] = new SessionData();

        var sessionData = _sessionData[sessionId];

        // DEBUGGING: Add a test log entry to verify session data works
        sessionData.ConsoleLogs.Add(new ConsoleLogEntry
        {
            Type = "debug",
            Text = $"Session {sessionId} debugging setup completed",
            Timestamp = DateTime.UtcNow
        });

        // Monitor console messages and store them in session-specific logs
        page.Console += (_, e) =>
        {
            var logEntry = new ConsoleLogEntry
            {
                Type = e.Type.ToString().ToLower(),
                Text = e.Text,
                Timestamp = DateTime.UtcNow
            };
            sessionData.ConsoleLogs.Add(logEntry);
            // DEBUGGING: Console log when we capture console messages
            Console.WriteLine($"DEBUG: Console message captured for session {sessionId}: {e.Text}");
        };

        // Monitor network requests and store them in session-specific logs
        page.Request += (_, e) =>
        {
            var networkEntry = new NetworkLogEntry
            {
                Type = "request",
                Method = e.Method,
                Url = e.Url,
                Timestamp = DateTime.UtcNow
            };
            sessionData.NetworkLogs.Add(networkEntry);
            // DEBUGGING: Console log when we capture network requests
            Console.WriteLine($"DEBUG: Network request captured for session {sessionId}: {e.Method} {e.Url}");
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
            sessionData.NetworkLogs.Add(networkEntry);
            // DEBUGGING: Console log when we capture network responses
            Console.WriteLine($"DEBUG: Network response captured for session {sessionId}: {e.Status} {e.Url}");
        };

        // DEBUGGING: Add network activity tracking script
        await page.AddInitScriptAsync($@"
            (() => {{
                const sessionId = '{sessionId}';
                console.log('DEBUG: Init script loaded for session ' + sessionId);
                
                // Log when page loads
                if (document.readyState === 'loading') {{
                    document.addEventListener('DOMContentLoaded', () => {{
                        console.log('DEBUG: DOMContentLoaded for session ' + sessionId);
                    }});
                }} else {{
                    console.log('DEBUG: Document already loaded for session ' + sessionId);
                }}
            }})();
        ");
    }
    
    [McpServerTool]
    [Description("Navigate to a URL")]
    public async Task<string> NavigateToUrl(
        [Description("URL to navigate to")] string url,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.GetValueOrDefault(sessionId));
                      
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
    [Description("Fill a form field using CSS selector or data-testid")]
    public async Task<string> FillField(
        [Description("Field selector (CSS selector or data-testid value)")] string selector,
        [Description("Value to fill")] string value,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.GetValueOrDefault(sessionId));
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            // FIXED: Use smart selector determination instead of automatic data-testid wrapping
            var finalSelector = DetermineSelector(selector);
            
            await page.Locator(finalSelector).FillAsync(value);
            return $"Field {selector} filled in with value {value}";
        }
        catch (Exception ex)
        {
            return $"Failed to fill field: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Click an element using CSS selector or data-testid")]
    public async Task<string> ClickElement(
        [Description("Element selector (CSS selector or data-testid value)")] string selector,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.GetValueOrDefault(sessionId));
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            var finalSelector = DetermineSelector(selector);
            
            await page.Locator(finalSelector).ClickAsync();
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
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.GetValueOrDefault(sessionId));
                      
            if (page == null)
                return $"Session {sessionId} not found.";

            var finalSelector = DetermineSelector(selector);
            
            await page.Locator(finalSelector).SelectOptionAsync(value);
            return $"Field {selector} filled in with value {value}";
        }
        catch (Exception ex)
        {
            return $"Failed to select option: {ex.Message}";
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
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.GetValueOrDefault(sessionId));
                      
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
    [Description("Get network activity from the current session")]
    public async Task<string> GetNetworkActivity(
        [Description("Session ID")] string sessionId = "default",
        [Description("Filter by URL pattern (optional)")] string? urlFilter = null)
    {
        try
        {
            // DEBUGGING: Show all available sessions
            var availableSessions = string.Join(", ", _sessionData.Keys);
            var activeSessionsInfo = string.Join(", ", _activeSessions.Keys);
            
            // FIXED: Use session-specific network logs
            if (!_sessionData.TryGetValue(sessionId, out var sessionData))
                return $"Session {sessionId} not found.\n" +
                       $"Available session data: [{availableSessions}]\n" +
                       $"Active sessions: [{activeSessionsInfo}]\n" +
                       $"Total session data count: {_sessionData.Count}";

            var networkLogs = sessionData.NetworkLogs;
            var consoleLogs = sessionData.ConsoleLogs;
            
            // DEBUGGING: Show console logs for troubleshooting
            var consoleInfo = consoleLogs.Count > 0 
                ? $"Console logs ({consoleLogs.Count}): {string.Join(", ", consoleLogs.Take(3).Select(c => c.Text))}"
                : "No console logs";
            
            var filteredLogs = string.IsNullOrEmpty(urlFilter) 
                ? networkLogs 
                : networkLogs.Where(log => log.Url.Contains(urlFilter, StringComparison.OrdinalIgnoreCase));

            if (!filteredLogs.Any())
                return string.IsNullOrEmpty(urlFilter) 
                    ? $"No network activity captured in session {sessionId} (Total logs: {networkLogs.Count})\n" +
                      $"DEBUG: {consoleInfo}\n" +
                      $"Session created: {sessionData.CreatedAt}"
                    : $"No network activity matching '{urlFilter}' found in session {sessionId} (Total logs: {networkLogs.Count})\n" +
                      $"DEBUG: {consoleInfo}";

            var networkSummary = filteredLogs.Select(log => new 
            {
                Type = log.Type,
                Method = log.Method,
                Url = log.Url,
                Status = log.Status > 0 ? log.Status.ToString() : "pending",
                Timestamp = log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")
            });

            return $"Network activity for session {sessionId}:\n{JsonSerializer.Serialize(networkSummary, new JsonSerializerOptions { WriteIndented = true })}\n" +
                   $"DEBUG: {consoleInfo}";
        }
        catch (Exception ex)
        {
            return $"Failed to get network activity: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Close browser session and cleanup resources")]
    public async Task<string> CloseBrowser(
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            // Clean up session data
            _sessionData.Remove(sessionId);
            
            var page = toolService.GetPage(sessionId) ?? 
                      (_activeSessions.GetValueOrDefault(sessionId));
            
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

    // Helper method for smart selector determination
    private static string DetermineSelector(string selector)
    {
        // FIXED: Smart selector determination instead of automatic wrapping
        
        // If it's already a CSS selector (contains CSS syntax), use as-is
        if (selector.Contains('[') || selector.Contains('.') || selector.Contains('#') || 
            selector.Contains('>') || selector.Contains(' ') || selector.Contains(':'))
        {
            return selector;
        }
        
        // If it looks like a simple data-testid value, wrap it
        // This preserves the data-testid functionality while avoiding the wrapping bug
        if (!string.IsNullOrEmpty(selector) && !selector.Contains('='))
        {
            return $"[data-testid='{selector}']";
        }
        
        // Default: use as-is
        return selector;
    }
}
