using System.ComponentModel;
using System.Text.Json;
using Microsoft.Playwright;
using ModelContextProtocol.Server;
using PlaywrightTester.Services;
using System.Collections.Concurrent;

namespace PlaywrightTester.Tools;

[McpServerToolType]
public class NetworkTestingTools(PlaywrightSessionManager sessionManager)
{
    // Track active downloads per session
    private static readonly ConcurrentDictionary<string, List<DownloadInfo>> _activeDownloads = new();
    private static readonly ConcurrentDictionary<string, List<MockRule>> _mockRules = new();
    private static readonly ConcurrentDictionary<string, List<InterceptRule>> _interceptRules = new();

    public class DownloadInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FileName { get; set; } = "";
        public string LocalPath { get; set; } = "";
        public long Size { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? CompletedTime { get; set; }
        public string Status { get; set; } = "pending"; // pending, completed, failed
        public string? Error { get; set; }
        public string TriggerSelector { get; set; } = "";
    }

    public class MockRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UrlPattern { get; set; } = "";
        public string Method { get; set; } = "GET";
        public string ResponseBody { get; set; } = "";
        public int StatusCode { get; set; } = 200;
        public Dictionary<string, string> Headers { get; set; } = new();
        public TimeSpan? Delay { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int TimesUsed { get; set; } = 0;
    }

    public class InterceptRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UrlPattern { get; set; } = "";
        public string Method { get; set; } = "*"; // * for all methods
        public string Action { get; set; } = "block"; // block, modify, log
        public string? ModifiedBody { get; set; }
        public Dictionary<string, string> ModifiedHeaders { get; set; } = new();
        public int? ModifiedStatusCode { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int TimesTriggered { get; set; } = 0;
    }

    [McpServerTool]
    [Description("Mock API responses for testing with advanced features")]
    public async Task<string> MockApiResponse(
        [Description("URL pattern to match (supports wildcards like */api/users* or exact URLs)")] string urlPattern,
        [Description("Response body (JSON, XML, or plain text)")] string responseBody,
        [Description("HTTP status code")] int statusCode = 200,
        [Description("HTTP method to mock (GET, POST, PUT, DELETE, PATCH, OPTIONS, HEAD, or * for all)")] string method = "GET",
        [Description("Additional response headers as JSON object")] string? headers = null,
        [Description("Response delay in milliseconds")] int delayMs = 0,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            // Parse headers if provided
            var responseHeaders = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(headers))
            {
                try
                {
                    var headerDict = JsonSerializer.Deserialize<Dictionary<string, string>>(headers);
                    if (headerDict != null)
                        responseHeaders = headerDict;
                }
                catch
                {
                    return "Invalid headers JSON format";
                }
            }

            // Add default content-type if not specified
            if (!responseHeaders.ContainsKey("content-type"))
            {
                if (responseBody.TrimStart().StartsWith("{") || responseBody.TrimStart().StartsWith("["))
                    responseHeaders["content-type"] = "application/json";
                else if (responseBody.TrimStart().StartsWith("<"))
                    responseHeaders["content-type"] = "application/xml";
                else
                    responseHeaders["content-type"] = "text/plain";
            }

            var mockRule = new MockRule
            {
                UrlPattern = urlPattern,
                Method = method.ToUpper(),
                ResponseBody = responseBody,
                StatusCode = statusCode,
                Headers = responseHeaders,
                Delay = delayMs > 0 ? TimeSpan.FromMilliseconds(delayMs) : null
            };

            // Store the mock rule
            _mockRules.AddOrUpdate(sessionId, 
                [mockRule], 
                (key, existing) => { existing.Add(mockRule); return existing; });

            // Set up the route
            await session.Page.RouteAsync(urlPattern, async route =>
            {
                var request = route.Request;
                
                // Check if method matches
                if (mockRule.Method != "*" && request.Method.ToUpper() != mockRule.Method)
                {
                    await route.ContinueAsync();
                    return;
                }

                // Check if rule is still active
                if (!mockRule.IsActive)
                {
                    await route.ContinueAsync();
                    return;
                }

                // Apply delay if specified
                if (mockRule.Delay.HasValue)
                {
                    await Task.Delay(mockRule.Delay.Value);
                }

                // Increment usage counter
                mockRule.TimesUsed++;

                // Fulfill the request with mock response
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = mockRule.StatusCode,
                    Headers = mockRule.Headers,
                    Body = mockRule.ResponseBody
                });
            });

            var result = new
            {
                success = true,
                mockRuleId = mockRule.Id,
                urlPattern = urlPattern,
                method = method,
                statusCode = statusCode,
                hasDelay = delayMs > 0,
                delayMs = delayMs,
                headers = responseHeaders,
                bodyLength = responseBody.Length,
                sessionId = sessionId,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to mock API response: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Intercept and modify network requests")]
    public async Task<string> InterceptRequests(
        [Description("URL pattern to intercept (supports wildcards)")] string urlPattern,
        [Description("Action to take: 'block', 'modify', 'log', or 'delay'")] string action = "block",
        [Description("HTTP method to intercept (* for all, or GET, POST, etc.)")] string method = "*",
        [Description("Modified response body (for 'modify' action)")] string? modifiedBody = null,
        [Description("Modified response headers as JSON (for 'modify' action)")] string? modifiedHeaders = null,
        [Description("Modified status code (for 'modify' action)")] int? modifiedStatusCode = null,
        [Description("Delay in milliseconds (for 'delay' action)")] int delayMs = 1000,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            // Parse modified headers if provided
            var headerDict = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(modifiedHeaders))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(modifiedHeaders);
                    if (parsed != null)
                        headerDict = parsed;
                }
                catch
                {
                    return "Invalid modified headers JSON format";
                }
            }

            var interceptRule = new InterceptRule
            {
                UrlPattern = urlPattern,
                Method = method.ToUpper(),
                Action = action.ToLower(),
                ModifiedBody = modifiedBody,
                ModifiedHeaders = headerDict,
                ModifiedStatusCode = modifiedStatusCode
            };

            // Store the intercept rule
            _interceptRules.AddOrUpdate(sessionId,
                [interceptRule],
                (key, existing) => { existing.Add(interceptRule); return existing; });

            // Set up the route
            await session.Page.RouteAsync(urlPattern, async route =>
            {
                var request = route.Request;
                
                // Check if method matches
                if (interceptRule.Method != "*" && request.Method.ToUpper() != interceptRule.Method)
                {
                    await route.ContinueAsync();
                    return;
                }

                // Check if rule is still active
                if (!interceptRule.IsActive)
                {
                    await route.ContinueAsync();
                    return;
                }

                // Increment trigger counter
                interceptRule.TimesTriggered++;

                switch (interceptRule.Action)
                {
                    case "block":
                        await route.AbortAsync();
                        break;
                        
                    case "modify":
                        // Get original response first
                        var response = await route.FetchAsync();
                        var originalBody = await response.TextAsync();
                        
                        await route.FulfillAsync(new RouteFulfillOptions
                        {
                            Status = interceptRule.ModifiedStatusCode ?? response.Status,
                            Headers = interceptRule.ModifiedHeaders.Count > 0 ? interceptRule.ModifiedHeaders : response.Headers,
                            Body = interceptRule.ModifiedBody ?? originalBody
                        });
                        break;
                        
                    case "delay":
                        await Task.Delay(delayMs);
                        await route.ContinueAsync();
                        break;
                        
                    case "log":
                        // Log the request details but continue normally
                        var requestInfo = new
                        {
                            url = request.Url,
                            method = request.Method,
                            headers = request.Headers,
                            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                        };
                        
                        // Store in session logs or console
                        session.ConsoleLogs.Add(new Models.ConsoleLogEntry
                        {
                            Type = "network-intercept",
                            Text = $"Intercepted request: {JsonSerializer.Serialize(requestInfo)}",
                            Timestamp = DateTime.UtcNow
                        });
                        
                        await route.ContinueAsync();
                        break;
                        
                    default:
                        await route.ContinueAsync();
                        break;
                }
            });

            var result = new
            {
                success = true,
                interceptRuleId = interceptRule.Id,
                urlPattern = urlPattern,
                method = method,
                action = action,
                hasModifications = !string.IsNullOrEmpty(modifiedBody) || modifiedStatusCode.HasValue || headerDict.Count > 0,
                sessionId = sessionId,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to intercept requests: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Handle file downloads with support for multiple concurrent downloads")]
    public async Task<string> WaitForDownload(
        [Description("Selector of element that triggers download")] string triggerSelector,
        [Description("Timeout in seconds to wait for download")] int timeoutSeconds = 30,
        [Description("Expected filename pattern (optional, for verification)")] string? expectedFileName = null,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var finalSelector = DetermineSelector(triggerSelector);
            var element = session.Page.Locator(finalSelector);
            
            // Check if element exists
            var count = await element.CountAsync();
            if (count == 0)
            {
                return JsonSerializer.Serialize(new { 
                    success = false, 
                    error = "Trigger element not found", 
                    selector = finalSelector 
                });
            }

            var downloadInfo = new DownloadInfo
            {
                TriggerSelector = finalSelector,
                StartTime = DateTime.UtcNow
            };

            // Set up download directory
            var downloadDir = Path.Combine(Directory.GetCurrentDirectory(), "downloads", sessionId);
            Directory.CreateDirectory(downloadDir);

            // Set up download handling
            var downloadTcs = new TaskCompletionSource<IDownload>();
            
            session.Page.Download += (_, download) =>
            {
                downloadTcs.TrySetResult(download);
            };

            try
            {
                // Trigger the download
                await element.ClickAsync();
                
                // Wait for download to start
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
                var completedTask = await Task.WhenAny(downloadTcs.Task, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    downloadInfo.Status = "failed";
                    downloadInfo.Error = "Download timeout - no download started";
                    
                    return JsonSerializer.Serialize(new { 
                        success = false, 
                        error = "Download timeout", 
                        downloadInfo = downloadInfo 
                    });
                }

                var download = await downloadTcs.Task;
                
                // Update download info
                downloadInfo.FileName = download.SuggestedFilename;
                downloadInfo.LocalPath = Path.Combine(downloadDir, downloadInfo.FileName);
                
                // Verify expected filename if provided
                if (!string.IsNullOrEmpty(expectedFileName) && !downloadInfo.FileName.Contains(expectedFileName))
                {
                    downloadInfo.Status = "warning";
                    downloadInfo.Error = $"Filename mismatch. Expected: {expectedFileName}, Got: {downloadInfo.FileName}";
                }

                // Save the download
                await download.SaveAsAsync(downloadInfo.LocalPath);
                
                // Get file info
                var fileInfo = new FileInfo(downloadInfo.LocalPath);
                downloadInfo.Size = fileInfo.Length;
                downloadInfo.CompletedTime = DateTime.UtcNow;
                downloadInfo.Status = downloadInfo.Status == "warning" ? "warning" : "completed";

                // Track the download
                _activeDownloads.AddOrUpdate(sessionId,
                    [downloadInfo],
                    (key, existing) => { existing.Add(downloadInfo); return existing; });

                var result = new
                {
                    success = true,
                    downloadId = downloadInfo.Id,
                    fileName = downloadInfo.FileName,
                    localPath = downloadInfo.LocalPath,
                    size = downloadInfo.Size,
                    downloadTime = (downloadInfo.CompletedTime - downloadInfo.StartTime)?.TotalSeconds ?? 0,
                    status = downloadInfo.Status,
                    error = downloadInfo.Error,
                    triggerSelector = finalSelector,
                    sessionId = sessionId,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                };

                return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                downloadInfo.Status = "failed";
                downloadInfo.Error = ex.Message;
                
                return $"Download failed: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            return $"Failed to handle download: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Clean up downloaded files and remove tracking")]
    public async Task<string> CleanupDownloads(
        [Description("Specific download ID to clean up (optional, if not provided cleans all for session)")] string? downloadId = null,
        [Description("Delete files from disk")] bool deleteFiles = true,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            if (!_activeDownloads.ContainsKey(sessionId))
            {
                return JsonSerializer.Serialize(new { 
                    success = true, 
                    message = "No downloads found for session",
                    cleanedCount = 0
                });
            }

            var downloads = _activeDownloads[sessionId];
            var toRemove = new List<DownloadInfo>();
            var cleanedFiles = new List<object>();
            var errors = new List<string>();

            if (!string.IsNullOrEmpty(downloadId))
            {
                // Clean specific download
                var download = downloads.FirstOrDefault(d => d.Id == downloadId);
                if (download != null)
                {
                    toRemove.Add(download);
                }
                else
                {
                    return JsonSerializer.Serialize(new { 
                        success = false, 
                        error = "Download ID not found" 
                    });
                }
            }
            else
            {
                // Clean all downloads for session
                toRemove.AddRange(downloads);
            }

            foreach (var download in toRemove)
            {
                try
                {
                    var fileDeleted = false;
                    if (deleteFiles && File.Exists(download.LocalPath))
                    {
                        File.Delete(download.LocalPath);
                        fileDeleted = true;
                    }

                    cleanedFiles.Add(new
                    {
                        downloadId = download.Id,
                        fileName = download.FileName,
                        localPath = download.LocalPath,
                        size = download.Size,
                        fileDeleted = fileDeleted,
                        status = download.Status
                    });

                    downloads.Remove(download);
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to clean {download.FileName}: {ex.Message}");
                }
            }

            // Clean up empty session entry
            if (downloads.Count == 0)
            {
                _activeDownloads.TryRemove(sessionId, out _);
            }

            var result = new
            {
                success = true,
                cleanedCount = cleanedFiles.Count,
                cleanedFiles = cleanedFiles,
                errors = errors,
                remainingDownloads = downloads.Count,
                sessionId = sessionId,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to cleanup downloads: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("List all active downloads for a session")]
    public async Task<string> ListActiveDownloads(
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var downloads = _activeDownloads.GetValueOrDefault(sessionId, []);
            
            var result = new
            {
                success = true,
                sessionId = sessionId,
                downloadCount = downloads.Count,
                downloads = downloads.Select(d => new
                {
                    id = d.Id,
                    fileName = d.FileName,
                    localPath = d.LocalPath,
                    size = d.Size,
                    status = d.Status,
                    startTime = d.StartTime.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    completedTime = d.CompletedTime?.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    duration = d.CompletedTime.HasValue ? 
                              (d.CompletedTime.Value - d.StartTime).TotalSeconds : (double?)null,
                    triggerSelector = d.TriggerSelector,
                    error = d.Error
                }).ToList(),
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to list downloads: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("List and manage active mock rules")]
    public async Task<string> ListMockRules(
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var mockRules = _mockRules.GetValueOrDefault(sessionId, []);
            
            var result = new
            {
                success = true,
                sessionId = sessionId,
                ruleCount = mockRules.Count,
                mockRules = mockRules.Select(r => new
                {
                    id = r.Id,
                    urlPattern = r.UrlPattern,
                    method = r.Method,
                    statusCode = r.StatusCode,
                    responseBodyLength = r.ResponseBody.Length,
                    hasDelay = r.Delay.HasValue,
                    delayMs = r.Delay?.TotalMilliseconds,
                    isActive = r.IsActive,
                    timesUsed = r.TimesUsed,
                    createdAt = r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss.fff")
                }).ToList(),
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to list mock rules: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("List and manage active intercept rules")]
    public async Task<string> ListInterceptRules(
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var interceptRules = _interceptRules.GetValueOrDefault(sessionId, []);
            
            var result = new
            {
                success = true,
                sessionId = sessionId,
                ruleCount = interceptRules.Count,
                interceptRules = interceptRules.Select(r => new
                {
                    id = r.Id,
                    urlPattern = r.UrlPattern,
                    method = r.Method,
                    action = r.Action,
                    hasModifications = !string.IsNullOrEmpty(r.ModifiedBody) || 
                                     r.ModifiedStatusCode.HasValue || 
                                     r.ModifiedHeaders.Count > 0,
                    isActive = r.IsActive,
                    timesTriggered = r.TimesTriggered,
                    createdAt = r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss.fff")
                }).ToList(),
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to list intercept rules: {ex.Message}";
        }
    }

    // Helper method for smart selector determination
    private static string DetermineSelector(string selector)
    {
        if (selector.Contains('[') || selector.Contains('.') || selector.Contains('#') || 
            selector.Contains('>') || selector.Contains(' ') || selector.Contains(':'))
        {
            return selector;
        }
        
        if (!string.IsNullOrEmpty(selector) && !selector.Contains('='))
        {
            return $"[data-testid='{selector}']";
        }
        
        return selector;
    }
}