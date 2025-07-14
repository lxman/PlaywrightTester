using Microsoft.Playwright;
using PlaywrightTester.Models;

namespace PlaywrightTester.Services;

public class PlaywrightSessionManager
{
    private readonly Dictionary<string, SessionContext> _sessions = new();
    private IPlaywright? _playwright;

    public class SessionContext : IDisposable
    {
        public string SessionId { get; set; } = "";
        public IBrowser? Browser { get; set; }
        public IBrowserContext? Context { get; set; }
        public IPage? Page { get; set; }
        public List<ConsoleLogEntry> ConsoleLogs { get; set; } = [];
        public List<NetworkLogEntry> NetworkLogs { get; set; } = [];
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive => Browser != null && Context != null && Page != null;

        public void Dispose()
        {
            try
            {
                Page?.CloseAsync().Wait();
                Context?.CloseAsync().Wait();
                Browser?.CloseAsync().Wait();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    public async Task<string> CreateSessionAsync(string sessionId, string browserType = "chrome", bool headless = true)
    {
        try
        {
            // Initialize Playwright if needed
            _playwright ??= await Playwright.CreateAsync();

            // Clean up existing session if it exists
            if (_sessions.ContainsKey(sessionId))
            {
                await CloseSessionAsync(sessionId);
            }

            // Create new session
            var session = new SessionContext
            {
                SessionId = sessionId,
                CreatedAt = DateTime.UtcNow
            };

            // Launch browser based on type (simplified options)
            session.Browser = browserType.ToLower() switch
            {
                "chrome" or "chromium" => await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = headless
                    // Removed Args to prevent potential JSON depth issues
                }),
                "firefox" => await _playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = headless
                }),
                "webkit" => await _playwright.Webkit.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = headless
                }),
                _ => throw new ArgumentException($"Unsupported browser type: {browserType}")
            };

            // Create browser context
            session.Context = await session.Browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 }
            });

            // Create page
            session.Page = await session.Context.NewPageAsync();

            // Set up event listeners at CONTEXT level (this is key!)
            await SetupEventListenersAsync(session);

            // Store session
            _sessions[sessionId] = session;

            // Add initial debug entries to verify session tracking works
            session.ConsoleLogs.Add(new ConsoleLogEntry
            {
                Type = "debug",
                Text = $"Session {sessionId} created successfully at {DateTime.UtcNow:HH:mm:ss.fff}",
                Timestamp = DateTime.UtcNow
            });

            session.NetworkLogs.Add(new NetworkLogEntry
            {
                Type = "debug",
                Method = "SESSION",
                Url = $"debug://session-{sessionId}-created",
                Status = 200,
                StatusText = "Session Created",
                Timestamp = DateTime.UtcNow
            });

            return $"Session {sessionId} created successfully with {browserType} browser. " +
                   $"Session is active: {session.IsActive}. " +
                   $"Total active sessions: {_sessions.Count}";
        }
        catch (Exception ex)
        {
            return $"Failed to create session {sessionId}: {ex.Message}\nStack trace: {ex.StackTrace}";
        }
    }

    private async Task SetupEventListenersAsync(SessionContext session)
    {
        if (session.Context == null) return;

        // CRITICAL: Attach event listeners to CONTEXT, not page
        // This ensures they persist and work correctly

        // Console event listener at context level
        session.Context.Console += (_, e) =>
        {
            var logEntry = new ConsoleLogEntry
            {
                Type = e.Type.ToString().ToLower(),
                Text = e.Text,
                Timestamp = DateTime.UtcNow,
                Url = e.Location ?? "",
                LineNumber = 0,
                ColumnNumber = 0,
                Args = e.Args.Select(arg => arg?.ToString() ?? "").ToArray()
            };
            session.ConsoleLogs.Add(logEntry);
            
            // Debug output
            Console.WriteLine($"[SESSION-{session.SessionId}] Console {e.Type}: {e.Text}");
        };

        // Request event listener at context level
        session.Context.Request += async (_, e) =>
        {
            var networkEntry = new NetworkLogEntry
            {
                Type = "request",
                Method = e.Method,
                Url = e.Url,
                Timestamp = DateTime.UtcNow,
                Headers = e.Headers.Take(10).ToDictionary(kvp => kvp.Key.ToLower(), kvp => kvp.Value?.Length > 200 ? kvp.Value.Substring(0, 200) + "..." : kvp.Value ?? ""),
                RequestBody = "", // e.PostData ?? "", // DISABLED to prevent JSON depth issues
                ResourceType = e.ResourceType
            };
            session.NetworkLogs.Add(networkEntry);
            
            // Debug output
            Console.WriteLine($"[SESSION-{session.SessionId}] Request: {e.Method} {e.Url}");
        };

        // Response event listener at context level
        session.Context.Response += async (_, e) =>
        {
            var startTime = DateTime.UtcNow;
            var responseBody = "";
            var responseHeaders = new Dictionary<string, string>();

            try
            {
                // Capture response headers (limited to prevent size issues)
                foreach (var header in e.Headers.Take(10))
                {
                    if (responseHeaders.Count < 10) // Limit total headers
                    {
                        responseHeaders[header.Key.ToLower()] = header.Value?.Length > 200 ? 
                            header.Value.Substring(0, 200) + "..." : header.Value ?? "";
                    }
                }

                // Don't capture response body to prevent JSON depth issues
                if (e.Url.Contains("/api/") ||
                    responseHeaders.GetValueOrDefault("content-type", "").Contains("application/json") ||
                    responseHeaders.GetValueOrDefault("content-type", "").Contains("text/"))
                {
                    try
                    {
                        // responseBody = await e.TextAsync(); // DISABLED to prevent JSON depth issues
                        responseBody = $"[ResponseBody disabled - Content-Type: {responseHeaders.GetValueOrDefault("content-type", "unknown")}]";
                    }
                    catch
                    {
                        responseBody = "[Could not read response body]";
                    }
                }
            }
            catch (Exception ex)
            {
                responseBody = $"[Error reading response: {ex.Message}]";
            }

            var networkEntry = new NetworkLogEntry
            {
                Type = "response",
                Method = e.Request.Method,
                Url = e.Url,
                Status = e.Status,
                StatusText = e.StatusText,
                Timestamp = DateTime.UtcNow,
                Headers = responseHeaders,
                ResponseBody = responseBody,
                ResourceType = e.Request.ResourceType,
                Duration = (DateTime.UtcNow - startTime).TotalMilliseconds
            };
            session.NetworkLogs.Add(networkEntry);
            
            // Debug output  
            Console.WriteLine($"[SESSION-{session.SessionId}] Response: {e.Status} {e.Url}");
        };

        // Page error event listener
        if (session.Page != null)
        {
            session.Page.PageError += (_, e) =>
            {
                var logEntry = new ConsoleLogEntry
                {
                    Type = "error",
                    Text = $"PAGE ERROR: {e}",
                    Timestamp = DateTime.UtcNow
                };
                session.ConsoleLogs.Add(logEntry);
                
                Console.WriteLine($"[SESSION-{session.SessionId}] Page Error: {e}");
            };
        }

        // Add initial script to generate test console messages
        await session.Context.AddInitScriptAsync($@"
            (() => {{
                const sessionId = '{session.SessionId}';
                console.log('[INIT] Session ' + sessionId + ' debugging ready');
                
                // Override console methods to ensure we capture everything
                const originalLog = console.log;
                const originalError = console.error;
                const originalWarn = console.warn;
                
                console.log = function(...args) {{
                    originalLog.apply(console, ['[SESSION-{session.SessionId}]', ...args]);
                    return originalLog.apply(console, args);
                }};
            }})();
        ");
    }

    public SessionContext? GetSession(string sessionId)
    {
        return _sessions.GetValueOrDefault(sessionId);
    }

    public async Task<bool> CloseSessionAsync(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return false;

        try
        {
            session.Dispose();
            _sessions.Remove(sessionId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task CloseAllSessionsAsync()
    {
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
        _sessions.Clear();

        if (_playwright != null)
        {
            _playwright.Dispose();
            _playwright = null;
        }
    }

    public IEnumerable<string> GetActiveSessionIds()
    {
        return _sessions.Keys.ToList();
    }

    public int GetActiveSessionCount()
    {
        return _sessions.Count;
    }
}
