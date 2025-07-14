using System.ComponentModel;
using System.Text.Json;
using Microsoft.Playwright;
using ModelContextProtocol.Server;
using PlaywrightTester.Services;

namespace PlaywrightTester.Tools;

[McpServerToolType]
public class VisualTestingTools(PlaywrightSessionManager sessionManager)
{
    [McpServerTool]
    [Description("Capture full page or element screenshot")]
    public async Task<string> CaptureScreenshot(
        [Description("Optional element selector to capture specific element")] string? selector = null,
        [Description("Capture full page including scrollable areas")] bool fullPage = false,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"screenshot_{timestamp}_{sessionId}.png";
            var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "screenshots", filename);
            
            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            byte[] screenshotBytes;
            
            if (!string.IsNullOrEmpty(selector))
            {
                // Element screenshot
                var finalSelector = DetermineSelector(selector);
                var element = session.Page.Locator(finalSelector);
                screenshotBytes = await element.ScreenshotAsync();
            }
            else
            {
                // Full page or viewport screenshot
                screenshotBytes = await session.Page.ScreenshotAsync(new PageScreenshotOptions
                {
                    FullPage = fullPage,
                    Path = outputPath
                });
            }

            // Save the screenshot
            await File.WriteAllBytesAsync(outputPath, screenshotBytes);
            
            var result = new
            {
                success = true,
                filename = filename,
                path = outputPath,
                size = screenshotBytes.Length,
                timestamp = timestamp,
                type = string.IsNullOrEmpty(selector) ? "page" : "element",
                selector = selector,
                fullPage = fullPage,
                sessionId = sessionId
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to capture screenshot: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Capture screenshot of specific element")]
    public async Task<string> CaptureElementScreenshot(
        [Description("Element selector (CSS selector or data-testid)")] string selector,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var finalSelector = DetermineSelector(selector);
            var element = session.Page.Locator(finalSelector);
            
            // Check if element exists
            var count = await element.CountAsync();
            if (count == 0)
            {
                return JsonSerializer.Serialize(new { 
                    success = false, 
                    error = "Element not found", 
                    selector = finalSelector 
                });
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"element_{timestamp}_{sessionId}.png";
            var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "screenshots", filename);
            
            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            // Get element info before screenshot
            var elementInfo = await session.Page.EvaluateAsync<object>($@"
                (() => {{
                    const element = document.querySelector('{finalSelector.Replace("'", "\\'")}');
                    if (!element) return {{ error: 'Element not found' }};
                    
                    const rect = element.getBoundingClientRect();
                    const computedStyles = window.getComputedStyle(element);
                    
                    return {{
                        tagName: element.tagName.toLowerCase(),
                        className: element.className,
                        dimensions: {{
                            width: rect.width,
                            height: rect.height
                        }},
                        position: {{
                            top: rect.top,
                            left: rect.left
                        }},
                        visible: rect.width > 0 && rect.height > 0 && 
                                computedStyles.visibility === 'visible' && 
                                computedStyles.display !== 'none'
                    }};
                }})()
            ");

            // Take screenshot
            var screenshotBytes = await element.ScreenshotAsync();
            await File.WriteAllBytesAsync(outputPath, screenshotBytes);
            
            var result = new
            {
                success = true,
                filename = filename,
                path = outputPath,
                size = screenshotBytes.Length,
                timestamp = timestamp,
                selector = finalSelector,
                elementInfo = elementInfo,
                sessionId = sessionId
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to capture element screenshot: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Generate PDF of current page")]
    public async Task<string> GeneratePDF(
        [Description("Optional output path (default: auto-generated)")] string? path = null,
        [Description("Use landscape orientation")] bool landscape = false,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = path ?? $"page_{timestamp}_{sessionId}.pdf";
            var outputPath = Path.IsPathRooted(filename) ? filename : 
                           Path.Combine(Directory.GetCurrentDirectory(), "pdfs", filename);
            
            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            // Generate PDF
            var pdfBytes = await session.Page.PdfAsync(new PagePdfOptions
            {
                Path = outputPath,
                Format = "A4",
                Landscape = landscape,
                PrintBackground = true
            });

            // Get page info
            var pageInfo = await session.Page.EvaluateAsync<object>(@"
                (() => {
                    return {
                        title: document.title,
                        url: window.location.href,
                        viewport: {
                            width: window.innerWidth,
                            height: window.innerHeight
                        },
                        documentHeight: document.documentElement.scrollHeight,
                        lastModified: document.lastModified
                    };
                })()
            ");
            
            var result = new
            {
                success = true,
                filename = Path.GetFileName(outputPath),
                path = outputPath,
                size = pdfBytes.Length,
                timestamp = timestamp,
                landscape = landscape,
                pageInfo = pageInfo,
                sessionId = sessionId
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to generate PDF: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Simulate hover effects on element")]
    public async Task<string> HoverElement(
        [Description("Element selector (CSS selector or data-testid)")] string selector,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var finalSelector = DetermineSelector(selector);
            var element = session.Page.Locator(finalSelector);
            
            // Check if element exists
            var count = await element.CountAsync();
            if (count == 0)
            {
                return JsonSerializer.Serialize(new { 
                    success = false, 
                    error = "Element not found", 
                    selector = finalSelector 
                });
            }

            // Get styles before hover
            var beforeStyles = await session.Page.EvaluateAsync<object>($@"
                (() => {{
                    const element = document.querySelector('{finalSelector.Replace("'", "\\'")}');
                    if (!element) return {{ error: 'Element not found' }};
                    
                    const computedStyles = window.getComputedStyle(element);
                    return {{
                        backgroundColor: computedStyles.backgroundColor,
                        color: computedStyles.color,
                        borderColor: computedStyles.borderColor,
                        opacity: computedStyles.opacity,
                        transform: computedStyles.transform,
                        cursor: computedStyles.cursor,
                        textDecoration: computedStyles.textDecoration,
                        boxShadow: computedStyles.boxShadow
                    }};
                }})()
            ");

            // Perform hover
            await element.HoverAsync();
            
            // Wait a bit for hover effects to apply
            await Task.Delay(200);

            // Get styles after hover
            var afterStyles = await session.Page.EvaluateAsync<object>($@"
                (() => {{
                    const element = document.querySelector('{finalSelector.Replace("'", "\\'")}');
                    if (!element) return {{ error: 'Element not found' }};
                    
                    const computedStyles = window.getComputedStyle(element);
                    return {{
                        backgroundColor: computedStyles.backgroundColor,
                        color: computedStyles.color,
                        borderColor: computedStyles.borderColor,
                        opacity: computedStyles.opacity,
                        transform: computedStyles.transform,
                        cursor: computedStyles.cursor,
                        textDecoration: computedStyles.textDecoration,
                        boxShadow: computedStyles.boxShadow
                    }};
                }})()
            ");

            var result = new
            {
                success = true,
                selector = finalSelector,
                beforeHover = beforeStyles,
                afterHover = afterStyles,
                sessionId = sessionId,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to hover element: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Analyze hover effects and style changes")]
    public async Task<string> AnalyzeHoverEffects(
        [Description("Element selector (CSS selector or data-testid)")] string selector,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var session = sessionManager.GetSession(sessionId);
            if (session?.Page == null)
                return $"Session {sessionId} not found or page not available.";

            var finalSelector = DetermineSelector(selector);
            var element = session.Page.Locator(finalSelector);
            
            // Check if element exists
            var count = await element.CountAsync();
            if (count == 0)
            {
                return JsonSerializer.Serialize(new { 
                    success = false, 
                    error = "Element not found", 
                    selector = finalSelector 
                });
            }

            // Comprehensive hover analysis
            var hoverAnalysis = await session.Page.EvaluateAsync<object>($@"
                (() => {{
                    const element = document.querySelector('{finalSelector.Replace("'", "\\'")}');
                    if (!element) return {{ error: 'Element not found' }};
                    
                    // Get initial styles
                    const initialStyles = window.getComputedStyle(element);
                    const initialState = {{
                        backgroundColor: initialStyles.backgroundColor,
                        color: initialStyles.color,
                        borderColor: initialStyles.borderColor,
                        opacity: initialStyles.opacity,
                        transform: initialStyles.transform,
                        cursor: initialStyles.cursor,
                        textDecoration: initialStyles.textDecoration,
                        boxShadow: initialStyles.boxShadow,
                        fontSize: initialStyles.fontSize,
                        fontWeight: initialStyles.fontWeight,
                        borderRadius: initialStyles.borderRadius,
                        padding: initialStyles.padding,
                        margin: initialStyles.margin
                    }};
                    
                    // Trigger hover
                    element.dispatchEvent(new MouseEvent('mouseenter', {{ bubbles: true }}));
                    
                    // Wait for styles to change
                    return new Promise(resolve => {{
                        setTimeout(() => {{
                            const hoverStyles = window.getComputedStyle(element);
                            const hoverState = {{
                                backgroundColor: hoverStyles.backgroundColor,
                                color: hoverStyles.color,
                                borderColor: hoverStyles.borderColor,
                                opacity: hoverStyles.opacity,
                                transform: hoverStyles.transform,
                                cursor: hoverStyles.cursor,
                                textDecoration: hoverStyles.textDecoration,
                                boxShadow: hoverStyles.boxShadow,
                                fontSize: hoverStyles.fontSize,
                                fontWeight: hoverStyles.fontWeight,
                                borderRadius: hoverStyles.borderRadius,
                                padding: hoverStyles.padding,
                                margin: hoverStyles.margin
                            }};
                            
                            // Compare states
                            const changes = {{}};
                            const similarities = [];
                            
                            Object.keys(initialState).forEach(property => {{
                                if (initialState[property] !== hoverState[property]) {{
                                    changes[property] = {{
                                        before: initialState[property],
                                        after: hoverState[property]
                                    }};
                                }} else {{
                                    similarities.push(property);
                                }}
                            }});
                            
                            // Check for CSS hover rules
                            const hasHoverRules = !!Array.from(document.styleSheets)
                                .some(sheet => {{
                                    try {{
                                        return Array.from(sheet.cssRules || []).some(rule => 
                                            rule.selectorText && rule.selectorText.includes(':hover')
                                        );
                                    }} catch (e) {{
                                        return false;
                                    }}
                                }});
                            
                            // Reset hover state
                            element.dispatchEvent(new MouseEvent('mouseleave', {{ bubbles: true }}));
                            
                            resolve({{
                                selector: '{finalSelector.Replace("'", "\\'")}',
                                elementInfo: {{
                                    tagName: element.tagName.toLowerCase(),
                                    className: element.className,
                                    id: element.id || null,
                                    hasHoverRules: hasHoverRules
                                }},
                                initialState: initialState,
                                hoverState: hoverState,
                                changes: changes,
                                similarities: similarities,
                                analysis: {{
                                    hasHoverEffects: Object.keys(changes).length > 0,
                                    changedProperties: Object.keys(changes),
                                    unchangedProperties: similarities,
                                    significantChanges: Object.keys(changes).filter(prop => 
                                        ['backgroundColor', 'color', 'transform', 'boxShadow'].includes(prop)
                                    )
                                }}
                            }});
                        }}, 100);
                    }});
                }})()
            ");

            // Also perform actual hover for verification
            await element.HoverAsync();
            await Task.Delay(200);

            var result = new
            {
                success = true,
                analysis = hoverAnalysis,
                sessionId = sessionId,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Failed to analyze hover effects: {ex.Message}";
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
