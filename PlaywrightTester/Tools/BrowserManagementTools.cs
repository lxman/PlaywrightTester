using System.ComponentModel;
using System.Text.Json;
using Microsoft.Playwright;
using ModelContextProtocol.Server;
using PlaywrightTester.Services;

namespace PlaywrightTester.Tools;

[McpServerToolType]
public class BrowserManagementTools(PlaywrightSessionManager sessionManager)
{
    [McpServerTool]
    [Description("Open new tab in current browser session")]
    public async Task<string> OpenNewTab(
        [Description("Optional URL to navigate to in new tab")] string? url = null,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Context == null)
                return $"Session {sessionId} not found or browser context not available.";

            // Create new page in existing context
            var newPage = await session.Context.NewPageAsync();
            
            // Navigate to URL if provided
            if (!string.IsNullOrEmpty(url))
            {
                await newPage.GotoAsync(url);
            }

            // Get all pages in context
            var allPages = session.Context.Pages;
            var newPageIndex = allPages.Count - 1;

            // Update session to track the new page
            if (newPageIndex >= 0)
            {
                // Store reference to new page in session for potential switching
                var result = new
                {
                    success = true,
                    message = "New tab opened successfully",
                    tabInfo = new
                    {
                        index = newPageIndex,
                        url = url ?? "about:blank",
                        title = string.IsNullOrEmpty(url) ? "New Tab" : await newPage.TitleAsync(),
                        totalTabs = allPages.Count
                    },
                    sessionInfo = new
                    {
                        sessionId = sessionId,
                        currentTabIndex = Array.IndexOf(allPages.ToArray(), session.Page),
                        totalTabs = allPages.Count
                    },
                    recommendation = "Use SwitchToTab to switch between tabs, or continue using current tab"
                };

                return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            }

            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Failed to create new tab",
                capability = "OpenNewTab"
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message,
                capability = "OpenNewTab"
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    [McpServerTool]
    [Description("Switch to a specific tab by index")]
    public async Task<string> SwitchToTab(
        [Description("Tab index (0-based)")] int index,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Context == null)
                return $"Session {sessionId} not found or browser context not available.";

            var allPages = session.Context.Pages;
            
            if (index < 0 || index >= allPages.Count)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Tab index {index} is out of range. Available tabs: 0 to {allPages.Count - 1}",
                    availableTabs = await GetTabList(allPages),
                    capability = "SwitchToTab"
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            var targetPage = allPages[index];
            
            // Bring the target page to front
            await targetPage.BringToFrontAsync();
            
            // Update the session's current page reference
            session.Page = targetPage;

            // Get current page info
            var currentTitle = await targetPage.TitleAsync();
            var currentUrl = targetPage.Url;

            var result = new
            {
                success = true,
                message = $"Switched to tab {index}",
                currentTab = new
                {
                    index = index,
                    title = currentTitle,
                    url = currentUrl
                },
                tabList = await GetTabList(allPages),
                sessionInfo = new
                {
                    sessionId = sessionId,
                    activeTabIndex = index,
                    totalTabs = allPages.Count
                }
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message,
                capability = "SwitchToTab"
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    [McpServerTool]
    [Description("Clear browser storage (localStorage, sessionStorage, or indexedDB)")]
    public async Task<string> ClearStorage(
        [Description("Storage type: 'localStorage', 'sessionStorage', 'indexedDB', 'cookies', 'all'")] string type = "all",
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var clearResults = new List<object>();
            var errors = new List<string>();

            // Normalize type parameter
            var normalizedType = type.ToLower().Trim();

            // Clear localStorage
            if (normalizedType == "localstorage" || normalizedType == "all")
            {
                try
                {
                    var localStorageCount = await session.Page.EvaluateAsync<int>(@"
                        () => {
                            const count = localStorage.length;
                            localStorage.clear();
                            return count;
                        }
                    ");

                    clearResults.Add(new
                    {
                        storageType = "localStorage",
                        status = "cleared",
                        itemsRemoved = localStorageCount
                    });
                }
                catch (Exception ex)
                {
                    errors.Add($"localStorage: {ex.Message}");
                }
            }

            // Clear sessionStorage
            if (normalizedType == "sessionstorage" || normalizedType == "all")
            {
                try
                {
                    var sessionStorageCount = await session.Page.EvaluateAsync<int>(@"
                        () => {
                            const count = sessionStorage.length;
                            sessionStorage.clear();
                            return count;
                        }
                    ");

                    clearResults.Add(new
                    {
                        storageType = "sessionStorage",
                        status = "cleared",
                        itemsRemoved = sessionStorageCount
                    });
                }
                catch (Exception ex)
                {
                    errors.Add($"sessionStorage: {ex.Message}");
                }
            }

            // Clear IndexedDB
            if (normalizedType == "indexeddb" || normalizedType == "all")
            {
                try
                {
                    var indexedDbResult = await session.Page.EvaluateAsync<object>(@"
                        async () => {
                            try {
                                const databases = await indexedDB.databases();
                                const deletedDatabases = [];
                                
                                for (const db of databases) {
                                    const deleteRequest = indexedDB.deleteDatabase(db.name);
                                    await new Promise((resolve, reject) => {
                                        deleteRequest.onsuccess = () => resolve();
                                        deleteRequest.onerror = () => reject(deleteRequest.error);
                                    });
                                    deletedDatabases.push(db.name);
                                }
                                
                                return {
                                    success: true,
                                    deletedDatabases: deletedDatabases,
                                    count: deletedDatabases.length
                                };
                            } catch (error) {
                                return {
                                    success: false,
                                    error: error.message
                                };
                            }
                        }
                    ");

                    if (indexedDbResult != null)
                    {
                        var dbResult = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(indexedDbResult));
                        if (dbResult.GetProperty("success").GetBoolean())
                        {
                            clearResults.Add(new
                            {
                                storageType = "indexedDB",
                                status = "cleared",
                                databasesRemoved = dbResult.GetProperty("count").GetInt32(),
                                databases = dbResult.GetProperty("deletedDatabases")
                            });
                        }
                        else
                        {
                            errors.Add($"indexedDB: {dbResult.GetProperty("error").GetString()}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"indexedDB: {ex.Message}");
                }
            }

            // Clear cookies
            if (normalizedType == "cookies" || normalizedType == "all")
            {
                try
                {
                    var cookies = await session.Context.CookiesAsync();
                    await session.Context.ClearCookiesAsync();

                    clearResults.Add(new
                    {
                        storageType = "cookies",
                        status = "cleared",
                        itemsRemoved = cookies.Count
                    });
                }
                catch (Exception ex)
                {
                    errors.Add($"cookies: {ex.Message}");
                }
            }

            // Clear cache (if supported)
            if (normalizedType == "cache" || normalizedType == "all")
            {
                try
                {
                    await session.Page.EvaluateAsync(@"
                        async () => {
                            if ('caches' in window) {
                                const cacheNames = await caches.keys();
                                await Promise.all(
                                    cacheNames.map(cacheName => caches.delete(cacheName))
                                );
                                return cacheNames.length;
                            }
                            return 0;
                        }
                    ");

                    clearResults.Add(new
                    {
                        storageType = "cache",
                        status = "cleared"
                    });
                }
                catch (Exception ex)
                {
                    errors.Add($"cache: {ex.Message}");
                }
            }

            // Validate storage type
            var validTypes = new[] { "localstorage", "sessionstorage", "indexeddb", "cookies", "cache", "all" };
            if (!validTypes.Contains(normalizedType))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Invalid storage type: {type}. Valid types: {string.Join(", ", validTypes)}",
                    capability = "ClearStorage"
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            var result = new
            {
                success = clearResults.Count > 0,
                message = $"Storage clearing completed for type: {type}",
                cleared = clearResults,
                errors = errors,
                summary = new
                {
                    storageTypesCleared = clearResults.Count,
                    totalErrors = errors.Count,
                    requestedType = type
                },
                recommendations = GenerateStorageRecommendations(clearResults, errors, normalizedType)
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message,
                capability = "ClearStorage"
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    private static async Task<object[]> GetTabList(IReadOnlyList<IPage> pages)
    {
        var tabList = new List<object>();

        for (int i = 0; i < pages.Count; i++)
        {
            try
            {
                var page = pages[i];
                var title = await page.TitleAsync();
                var url = page.Url;

                tabList.Add(new
                {
                    index = i,
                    title = string.IsNullOrEmpty(title) ? "Untitled" : title,
                    url = string.IsNullOrEmpty(url) ? "about:blank" : url,
                    isClosed = page.IsClosed
                });
            }
            catch
            {
                // If we can't get page info, add basic info
                tabList.Add(new
                {
                    index = i,
                    title = "Unknown",
                    url = "Unknown",
                    isClosed = true
                });
            }
        }

        return tabList.ToArray();
    }

    private static object[] GenerateStorageRecommendations(List<object> clearResults, List<string> errors, string type)
    {
        var recommendations = new List<object>();

        if (errors.Count > 0)
        {
            recommendations.Add(new
            {
                priority = "HIGH",
                category = "Storage Clearing Issues",
                action = $"Investigate {errors.Count} storage clearing error(s)",
                impact = "Some storage types may not have been properly cleared"
            });
        }

        if (clearResults.Count > 0)
        {
            recommendations.Add(new
            {
                priority = "INFO",
                category = "Storage Management",
                action = "Consider refreshing the page to see effects of storage clearing",
                impact = "Some applications may need to reload to reflect cleared storage"
            });
        }

        if (type == "all")
        {
            recommendations.Add(new
            {
                priority = "INFO",
                category = "Testing Best Practice",
                action = "Use specific storage types for targeted testing",
                impact = "Clearing specific storage types can help isolate testing scenarios"
            });
        }

        return recommendations.ToArray();
    }
}
