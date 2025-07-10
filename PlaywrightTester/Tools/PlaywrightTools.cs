using System.ComponentModel;
using System.Text.Json;
using Microsoft.Playwright;
using ModelContextProtocol.Server;
using PlaywrightTester.Models;
using PlaywrightTester.Services;

namespace PlaywrightTester.Tools;

[McpServerToolType]
public class PlaywrightTools(ToolService toolService, PlaywrightSessionManager sessionManager)
{
    [McpServerTool]
    [Description("Launch a browser and create a new session. Returns session ID.")]
    public async Task<string> LaunchBrowser(
        [Description("Browser type: chrome, firefox, webkit")] string browserType = "chrome",
        [Description("Run in headless mode")] bool headless = true,
        [Description("Session ID for this browser instance")] string sessionId = "default")
    {
        var result = await sessionManager.CreateSessionAsync(sessionId, browserType, headless);
        
        // Also store in ToolService for backward compatibility
        var session = sessionManager.GetSession(sessionId);
        if (session != null)
        {
            if (session.Browser != null) toolService.StoreBrowser(sessionId, session.Browser);
            if (session.Context != null) toolService.StoreBrowserContext(sessionId, session.Context);
            if (session.Page != null) toolService.StorePage(sessionId, session.Page);
        }
        
        return result;
    }

    
    [McpServerTool]
    [Description("Navigate to a URL")]
    public async Task<string> NavigateToUrl(
        [Description("URL to navigate to")] string url,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available. Launch browser first.";

            await session.Page.GotoAsync(url);
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
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var finalSelector = DetermineSelector(selector);
            await session.Page.Locator(finalSelector).FillAsync(value);
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
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var finalSelector = DetermineSelector(selector);
            await session.Page.Locator(finalSelector).ClickAsync();
            return $"Successfully clicked element {selector}";
        }
        catch (Exception ex)
        {
            return $"Failed to click element: {ex.Message}";
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
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var result = await session.Page.EvaluateAsync<object>(jsCode);
            return result?.ToString() ?? "null";
        }
        catch (Exception ex)
        {
            return $"JavaScript execution failed: {ex.Message}";
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
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var finalSelector = DetermineSelector(selector);
            await session.Page.Locator(finalSelector).SelectOptionAsync(value);
            return $"Selected option '{value}' in dropdown {selector}";
        }
        catch (Exception ex)
        {
            return $"Failed to select option: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Get console logs from a browser session")]
    public async Task<string> GetConsoleLogs(
        [Description("Session ID")] string sessionId = "default",
        [Description("Filter by log type (log, error, warning, info, debug)")] string? logType = null,
        [Description("Maximum number of logs to return")] int maxLogs = 100)
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session == null)
                return $"Session {sessionId} not found. Active sessions: [{string.Join(", ", sessionManager.GetActiveSessionIds())}]";

            var logs = session.ConsoleLogs;
            var filteredLogs = logs.AsEnumerable();

            if (!string.IsNullOrEmpty(logType))
            {
                filteredLogs = filteredLogs.Where(log => log.Type.Equals(logType, StringComparison.OrdinalIgnoreCase));
            }

            var result = filteredLogs
                .OrderByDescending(log => log.Timestamp)
                .Take(maxLogs)
                .Select(log => new
                {
                    Type = log.Type,
                    Text = log.Text,
                    Timestamp = log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    Location = string.IsNullOrEmpty(log.Url) ? "" : $"{log.Url}:{log.LineNumber}:{log.ColumnNumber}",
                    IsError = log.IsError,
                    IsWarning = log.IsWarning
                })
                .ToList();

            var response = new
            {
                SessionId = sessionId,
                SessionActive = session.IsActive,
                LogsFound = result.Count,
                TotalLogsInSession = logs.Count,
                Logs = result
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to get console logs: {ex.Message}\nStack trace: {ex.StackTrace}";
        }
    }

    [McpServerTool]
    [Description("Get network activity from a browser session")]
    public async Task<string> GetNetworkActivity(
        [Description("Session ID")] string sessionId = "default",
        [Description("Filter by URL pattern (optional)")] string? urlFilter = null)
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session == null)
                return $"Session {sessionId} not found. Active sessions: [{string.Join(", ", sessionManager.GetActiveSessionIds())}]";

            var networkLogs = session.NetworkLogs;
            var filteredLogs = networkLogs.AsEnumerable();

            if (!string.IsNullOrEmpty(urlFilter))
            {
                filteredLogs = filteredLogs.Where(log => log.Url.Contains(urlFilter, StringComparison.OrdinalIgnoreCase));
            }

            var result = filteredLogs
                .OrderByDescending(log => log.Timestamp)
                .Take(100)
                .Select(log => new
                {
                    Type = log.Type,
                    Method = log.Method,
                    Url = log.Url,
                    Status = log.Status,
                    StatusText = log.StatusText,
                    Timestamp = log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    Headers = log.Headers,
                    RequestBody = log.RequestBody,
                    ResponseBody = log.ResponseBody,
                    ResourceType = log.ResourceType,
                    Duration = log.Duration,
                    IsApiCall = log.IsApiCall,
                    IsAuthRelated = log.IsAuthRelated
                })
                .ToList();

            var response = new
            {
                SessionId = sessionId,
                SessionActive = session.IsActive,
                NetworkLogsFound = result.Count,
                TotalNetworkLogsInSession = networkLogs.Count,
                NetworkActivity = result
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to get network activity: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Get session debug summary with counts and recent activity")]
    public async Task<string> GetSessionDebugSummary(
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session == null)
                return $"Session {sessionId} not found. Active sessions: [{string.Join(", ", sessionManager.GetActiveSessionIds())}]";

            var recentConsole = session.ConsoleLogs.OrderByDescending(log => log.Timestamp).Take(5).ToList();
            var recentNetwork = session.NetworkLogs.OrderByDescending(log => log.Timestamp).Take(5).ToList();
            
            var errorCount = session.ConsoleLogs.Count(log => log.IsError);
            var warningCount = session.ConsoleLogs.Count(log => log.IsWarning);
            var apiCallCount = session.NetworkLogs.Count(log => log.IsApiCall);
            var authCallCount = session.NetworkLogs.Count(log => log.IsAuthRelated);

            var summary = new
            {
                SessionId = sessionId,
                IsActive = session.IsActive,
                CreatedAt = session.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                ConsoleLogs = new
                {
                    Total = session.ConsoleLogs.Count,
                    Errors = errorCount,
                    Warnings = warningCount,
                    Recent = recentConsole.Select(log => new { log.Type, log.Text, Timestamp = log.Timestamp.ToString("HH:mm:ss.fff") }).ToList()
                },
                NetworkLogs = new
                {
                    Total = session.NetworkLogs.Count,
                    ApiCalls = apiCallCount,
                    AuthRelated = authCallCount,
                    Recent = recentNetwork.Select(log => new { log.Type, log.Method, log.Url, log.Status, Timestamp = log.Timestamp.ToString("HH:mm:ss.fff") }).ToList()
                },
                ActiveSessions = sessionManager.GetActiveSessionIds().ToList()
            };

            return JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to get session debug summary: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Clear console and network logs for a browser session")]
    public async Task<string> ClearSessionLogs(
        [Description("Session ID")] string sessionId = "default",
        [Description("Clear console logs")] bool clearConsole = true,
        [Description("Clear network logs")] bool clearNetwork = true)
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session == null)
                return $"Session {sessionId} not found.";

            var clearedItems = new List<string>();

            if (clearConsole)
            {
                var consoleCount = session.ConsoleLogs.Count;
                session.ConsoleLogs.Clear();
                clearedItems.Add($"{consoleCount} console logs");
            }

            if (clearNetwork)
            {
                var networkCount = session.NetworkLogs.Count;
                session.NetworkLogs.Clear();
                clearedItems.Add($"{networkCount} network logs");
            }

            return $"Successfully cleared {string.Join(" and ", clearedItems)} for session {sessionId}.";
        }
        catch (Exception ex)
        {
            return $"Failed to clear session logs: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Close browser session and cleanup resources")]
    public async Task<string> CloseBrowser(
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var success = await sessionManager.CloseSessionAsync(sessionId);
            if (success)
            {
                return $"Browser session {sessionId} closed successfully";
            }
            else
            {
                return $"Session {sessionId} not found or already closed";
            }
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
