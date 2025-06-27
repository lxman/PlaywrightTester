using System.ComponentModel;
using System.Text.Json;
using Microsoft.Playwright;
using ModelContextProtocol.Server;

namespace PlaywrightTester;

[McpServerToolType]
public class AdvancedTestingTools(ToolService toolService, ChromeService chromeService)
{
    [McpServerTool]
    [Description("Simulate network conditions (slow, fast, offline)")]
    public async Task<string> SimulateNetworkConditions(
        [Description("Network type: slow, fast, offline, mobile3g, mobile4g")] string networkType,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var page = toolService.GetPage(sessionId);
            if (page == null) return $"Session {sessionId} not found.";

            var cdp = await page.Context.NewCDPSessionAsync(page);
            
            var networkConditions = networkType.ToLower() switch
            {
                "slow" => new { offline = false, downloadThroughput = 50 * 1024, uploadThroughput = 20 * 1024, latency = 500 },
                "mobile3g" => new { offline = false, downloadThroughput = 100 * 1024, uploadThroughput = 50 * 1024, latency = 300 },
                "mobile4g" => new { offline = false, downloadThroughput = 1 * 1024 * 1024, uploadThroughput = 500 * 1024, latency = 150 },
                "fast" => new { offline = false, downloadThroughput = 10 * 1024 * 1024, uploadThroughput = 5 * 1024 * 1024, latency = 10 },
                "offline" => new { offline = true, downloadThroughput = 0, uploadThroughput = 0, latency = 0 },
                _ => throw new ArgumentException($"Unknown network type: {networkType}")
            };

            await cdp.SendAsync("Network.emulateNetworkConditions", new Dictionary<string, object>
            {
                ["offline"] = networkConditions.offline,
                ["downloadThroughput"] = networkConditions.downloadThroughput,
                ["uploadThroughput"] = networkConditions.uploadThroughput,
                ["latency"] = networkConditions.latency
            });

            return $"Network conditions set to {networkType} - Download: {networkConditions.downloadThroughput / 1024}KB/s, Upload: {networkConditions.uploadThroughput / 1024}KB/s, Latency: {networkConditions.latency}ms";
        }
        catch (Exception ex)
        {
            return $"Failed to simulate network conditions: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Get performance metrics (Core Web Vitals, load times)")]
    public async Task<string> GetPerformanceMetrics(
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var page = toolService.GetPage(sessionId);
            if (page == null) return $"Session {sessionId} not found.";

            var metrics = await page.EvaluateAsync<object>(@"
                () => {
                    return new Promise((resolve) => {
                        // Get Core Web Vitals
                        const observer = new PerformanceObserver((list) => {
                            const entries = list.getEntries();
                            const vitals = {};
                            
                            entries.forEach((entry) => {
                                if (entry.entryType === 'largest-contentful-paint') {
                                    vitals.LCP = Math.round(entry.startTime);
                                }
                                if (entry.entryType === 'first-input') {
                                    vitals.FID = Math.round(entry.processingStart - entry.startTime);
                                }
                                if (entry.entryType === 'layout-shift' && !entry.hadRecentInput) {
                                    vitals.CLS = (vitals.CLS || 0) + entry.value;
                                }
                            });
                            
                            // Get navigation timing
                            const navigation = performance.getEntriesByType('navigation')[0];
                            const timing = {
                                domContentLoaded: Math.round(navigation.domContentLoadedEventStart),
                                loadComplete: Math.round(navigation.loadEventEnd),
                                firstPaint: Math.round(performance.getEntriesByType('paint')[0]?.startTime || 0),
                                memoryUsage: (performance as any).memory ? {
                                    used: Math.round((performance as any).memory.usedJSHeapSize / 1024 / 1024),
                                    total: Math.round((performance as any).memory.totalJSHeapSize / 1024 / 1024)
                                } : null
                            };
                            
                            resolve({ vitals, timing });
                        });
                        
                        observer.observe({ entryTypes: ['largest-contentful-paint', 'first-input', 'layout-shift'] });
                        
                        // Fallback timeout
                        setTimeout(() => {
                            const navigation = performance.getEntriesByType('navigation')[0];
                            resolve({
                                vitals: { LCP: 'pending', FID: 'pending', CLS: 'pending' },
                                timing: {
                                    domContentLoaded: Math.round(navigation.domContentLoadedEventStart),
                                    loadComplete: Math.round(navigation.loadEventEnd)
                                }
                            });
                        }, 2000);
                    });
                }
            ");

            return $"Performance metrics:\n{JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch (Exception ex)
        {
            return $"Failed to get performance metrics: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Upload files for testing (documents tab)")]
    public async Task<string> UploadFiles(
        [Description("File upload selector")] string selector,
        [Description("File paths or test file types (pdf, jpg, png, invalid)")] string[] filePaths,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var page = toolService.GetPage(sessionId);
            if (page == null) return $"Session {sessionId} not found.";

            var files = new List<FilePayload>();
            
            foreach (var filePath in filePaths)
            {
                // Generate test files if needed
                switch (filePath.ToLower())
                {
                    case "pdf":
                        files.Add(new FilePayload
                        {
                            Name = "test-document.pdf",
                            MimeType = "application/pdf",
                            Buffer = GenerateTestPdf()
                        });
                        break;
                    case "jpg":
                        files.Add(new FilePayload
                        {
                            Name = "test-image.jpg",
                            MimeType = "image/jpeg",
                            Buffer = GenerateTestImage()
                        });
                        break;
                    case "invalid":
                        files.Add(new FilePayload
                        {
                            Name = "malware.exe",
                            MimeType = "application/octet-stream",
                            Buffer = System.Text.Encoding.UTF8.GetBytes("This is a test file")
                        });
                        break;
                    default:
                        if (File.Exists(filePath))
                        {
                            files.Add(new FilePayload
                            {
                                Name = Path.GetFileName(filePath),
                                MimeType = GetMimeType(filePath),
                                Buffer = await File.ReadAllBytesAsync(filePath)
                            });
                        }
                        break;
                }
            }

            var fullSelector = selector.StartsWith('[') ? selector : $"[data-testid='{selector}']";
            await page.SetInputFilesAsync(fullSelector, files.ToArray());

            return $"Uploaded {files.Count} files: {string.Join(", ", files.Select(f => f.Name))}";
        }
        catch (Exception ex)
        {
            return $"File upload failed: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Simulate mobile device (iPhone, Android, iPad)")]
    public async Task<string> EmulateDevice(
        [Description("Device type: iphone12, iphone13, ipad, galaxy_s21, pixel5")] string deviceType,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var page = toolService.GetPage(sessionId);
            if (page == null) return $"Session {sessionId} not found.";

            var deviceConfig = deviceType.ToLower() switch
            {
                "iphone12" => new { width = 390, height = 844, userAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 14_0 like Mac OS X) AppleWebKit/605.1.15", isMobile = true },
                "iphone13" => new { width = 390, height = 844, userAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 15_0 like Mac OS X) AppleWebKit/605.1.15", isMobile = true },
                "ipad" => new { width = 820, height = 1180, userAgent = "Mozilla/5.0 (iPad; CPU OS 14_0 like Mac OS X) AppleWebKit/605.1.15", isMobile = true },
                "galaxy_s21" => new { width = 384, height = 854, userAgent = "Mozilla/5.0 (Linux; Android 11; SM-G991B) AppleWebKit/537.36", isMobile = true },
                "pixel5" => new { width = 393, height = 851, userAgent = "Mozilla/5.0 (Linux; Android 11; Pixel 5) AppleWebKit/537.36", isMobile = true },
                _ => throw new ArgumentException($"Unknown device type: {deviceType}")
            };

            await page.SetViewportSizeAsync(deviceConfig.width, deviceConfig.height);
            await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string> 
            { 
                ["User-Agent"] = deviceConfig.userAgent 
            });

            return $"Emulating {deviceType} - {deviceConfig.width}x{deviceConfig.height}";
        }
        catch (Exception ex)
        {
            return $"Device emulation failed: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Test accessibility features (keyboard navigation, screen reader)")]
    public async Task<string> TestAccessibility(
        [Description("Test type: keyboard_navigation, aria_labels, color_contrast, focus_order")] string testType,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var page = toolService.GetPage(sessionId);
            if (page == null) return $"Session {sessionId} not found.";

            return testType.ToLower() switch
            {
                "keyboard_navigation" => await TestKeyboardNavigation(page),
                "aria_labels" => await TestAriaLabels(page),
                "color_contrast" => await TestColorContrast(page),
                "focus_order" => await TestFocusOrder(page),
                _ => $"Unknown accessibility test type: {testType}"
            };
        }
        catch (Exception ex)
        {
            return $"Accessibility test failed: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Inject security test payloads (XSS, SQL injection)")]
    public async Task<string> InjectSecurityPayloads(
        [Description("Payload type: xss, sql_injection, script_injection, html_injection")] string payloadType,
        [Description("Target field selector")] string selector,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var page = toolService.GetPage(sessionId);
            if (page == null) return $"Session {sessionId} not found.";

            var payload = payloadType.ToLower() switch
            {
                "xss" => "<script>alert('XSS Test')</script>",
                "sql_injection" => "'; DROP TABLE users; --",
                "script_injection" => "javascript:alert('Script Injection')",
                "html_injection" => "<img src=x onerror=alert('HTML Injection')>",
                _ => throw new ArgumentException($"Unknown payload type: {payloadType}")
            };

            var fullSelector = selector.StartsWith('[') ? selector : $"[data-testid='{selector}']";
            
            // Clear field first
            await page.Locator(fullSelector).ClearAsync();
            await page.Locator(fullSelector).FillAsync(payload);
            
            // Check if payload was sanitized
            var fieldValue = await page.Locator(fullSelector).InputValueAsync();
            var isSanitized = fieldValue != payload;
            
            return $"Security payload '{payloadType}' injected into {selector}.\nOriginal: {payload}\nActual: {fieldValue}\nSanitized: {isSanitized}";
        }
        catch (Exception ex)
        {
            return $"Security injection failed: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Mock API responses for testing")]
    public async Task<string> MockApiResponse(
        [Description("URL pattern to intercept")] string urlPattern,
        [Description("Mock response status code")] int statusCode,
        [Description("Mock response body (JSON)")] string responseBody,
        [Description("Session ID")] string sessionId = "default")
    {
        try
        {
            var page = toolService.GetPage(sessionId);
            if (page == null) return $"Session {sessionId} not found.";

            await page.RouteAsync(urlPattern, async route =>
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = statusCode,
                    Body = responseBody,
                    Headers = new Dictionary<string, string>
                    {
                        ["Content-Type"] = "application/json",
                        ["Access-Control-Allow-Origin"] = "*"
                    }
                });
            });

            return $"API mock setup for {urlPattern} - Status: {statusCode}";
        }
        catch (Exception ex)
        {
            return $"API mocking failed: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Generate test data for forms")]
    public static async Task<string> GenerateTestData(
        [Description("Data type: person, address, company, ssn, email, phone")] string dataType,
        [Description("Count of records to generate")] int count = 1)
    {
        try
        {
            var testData = new List<object>();
            var random = new Random();

            for (var i = 0; i < count; i++)
            {
                var data = dataType.ToLower() switch
                {
                    "person" => GeneratePersonData(random),
                    "address" => GenerateAddressData(random),
                    "company" => GenerateCompanyData(random),
                    "ssn" => GenerateSsnData(random),
                    "email" => GenerateEmailData(random),
                    "phone" => GeneratePhoneData(random),
                    _ => throw new ArgumentException($"Unknown data type: {dataType}")
                };
                testData.Add(data);
            }

            return $"Generated {count} {dataType} records:\n{JsonSerializer.Serialize(testData, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch (Exception ex)
        {
            return $"Test data generation failed: {ex.Message}";
        }
    }

    private static object GeneratePersonData(Random random)
    {
        var firstNames = new[] { "John", "Jane", "Michael", "Sarah", "David", "Lisa", "Robert", "Emily" };
        var lastNames = new[] { "Smith", "Johnson", "Brown", "Davis", "Miller", "Wilson", "Moore", "Taylor" };
        var suffixes = new[] { "", "Jr", "Sr", "III", "IV" };

        return new
        {
            firstName = firstNames[random.Next(firstNames.Length)],
            middleName = firstNames[random.Next(firstNames.Length)],
            lastName = lastNames[random.Next(lastNames.Length)],
            suffix = suffixes[random.Next(suffixes.Length)],
            dateOfBirth = DateTime.Now.AddYears(-random.Next(18, 80)).ToString("yyyy-MM-dd"),
            sex = random.Next(2) == 0 ? "M" : "F",
            height = random.Next(60, 84).ToString(),
            weight = random.Next(120, 250).ToString()
        };
    }

    private static object GenerateAddressData(Random random)
    {
        var streets = new[] { "Main St", "Oak Ave", "Pine Rd", "Elm Dr", "Cedar Ln" };
        var cities = new[] { "New York", "Los Angeles", "Chicago", "Houston", "Phoenix" };
        var states = new[] { "NY", "CA", "IL", "TX", "AZ" };

        return new
        {
            street = $"{random.Next(100, 9999)} {streets[random.Next(streets.Length)]}",
            city = cities[random.Next(cities.Length)],
            state = states[random.Next(states.Length)],
            postalCode = random.Next(10000, 99999).ToString(),
            country = "US"
        };
    }

    private static object GenerateCompanyData(Random random)
    {
        var companies = new[] { "ABC Corp", "XYZ Industries", "Tech Solutions", "Global Systems", "Advanced Technologies" };
        var positions = new[] { "Software Engineer", "Manager", "Analyst", "Director", "Consultant" };

        return new
        {
            name = companies[random.Next(companies.Length)],
            position = positions[random.Next(positions.Length)],
            startDate = DateTime.Now.AddYears(-random.Next(1, 10)).ToString("yyyy-MM-dd"),
            endDate = random.Next(2) == 0 ? DateTime.Now.AddYears(-random.Next(0, 5)).ToString("yyyy-MM-dd") : ""
        };
    }

    private static object GenerateSsnData(Random random)
    {
        // Generate valid SSN format (not real SSNs)
        var area = random.Next(100, 665);
        var group = random.Next(1, 99);
        var serial = random.Next(1, 9999);
        
        return new
        {
            ssn = $"{area:D3}-{group:D2}-{serial:D4}",
            formatted = true,
            testOnly = true
        };
    }

    private static object GenerateEmailData(Random random)
    {
        var domains = new[] { "example.com", "test.org", "demo.net", "sample.edu" };
        var names = new[] { "john", "jane", "test", "demo", "user" };
        
        return new
        {
            email = $"{names[random.Next(names.Length)]}{random.Next(100, 999)}@{domains[random.Next(domains.Length)]}",
            type = "Personal"
        };
    }

    private static object GeneratePhoneData(Random random)
    {
        var areaCodes = new[] { "555", "212", "310", "312", "713" };
        
        return new
        {
            phone = $"({areaCodes[random.Next(areaCodes.Length)]}) {random.Next(100, 999)}-{random.Next(1000, 9999)}",
            type = "Mobile"
        };
    }

    private static byte[] GenerateTestPdf()
    {
        // Simple PDF header - for testing only
        var pdfContent = "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]>>endobj\nxref\n0 4\n0000000000 65535 f\n0000000010 00000 n\n0000000079 00000 n\n0000000000 00000 n\ntrailer<</Size 4/Root 1 0 R>>\n%%EOF";
        return System.Text.Encoding.UTF8.GetBytes(pdfContent);
    }

    private static byte[] GenerateTestImage()
    {
        // Minimal JPEG header - for testing only
        var jpegHeader = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        return jpegHeader;
    }

    private static string GetMimeType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }

    private static async Task<string> TestKeyboardNavigation(IPage page)
    {
        var result = await page.EvaluateAsync<object>(@"
            () => {
                const focusableElements = document.querySelectorAll(
                    'button, [href], input, select, textarea, [tabindex]:not([tabindex=""-1])'
                );
                
                const results = [];
                focusableElements.forEach((element, index) => {
                    const rect = element.getBoundingClientRect();
                    results.push({
                        tagName: element.tagName,
                        type: element.type || 'N/A',
                        id: element.id || 'N/A',
                        testId: element.getAttribute('data-testid') || 'N/A',
                        tabIndex: element.tabIndex,
                        visible: rect.width > 0 && rect.height > 0,
                        ariaLabel: element.getAttribute('aria-label') || 'N/A'
                    });
                });
                
                return {
                    totalFocusableElements: focusableElements.length,
                    elements: results
                };
            }
        ");

        return $"Keyboard navigation analysis:\n{JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })}";
    }

    private static async Task<string> TestAriaLabels(IPage page)
    {
        var result = await page.EvaluateAsync<object>(@"
            () => {
                const elementsNeedingLabels = document.querySelectorAll('input, button, select, textarea');
                const issues = [];
                
                elementsNeedingLabels.forEach(element => {
                    const hasAriaLabel = element.hasAttribute('aria-label');
                    const hasAriaLabelledBy = element.hasAttribute('aria-labelledby');
                    const hasLabel = element.closest('label') || document.querySelector(`label[for='${element.id}']`);
                    
                    if (!hasAriaLabel && !hasAriaLabelledBy && !hasLabel) {
                        issues.push({
                            tagName: element.tagName,
                            type: element.type || 'N/A',
                            id: element.id || 'N/A',
                            testId: element.getAttribute('data-testid') || 'N/A',
                            issue: 'Missing accessible label'
                        });
                    }
                });
                
                return {
                    totalElements: elementsNeedingLabels.length,
                    issuesFound: issues.length,
                    issues: issues
                };
            }
        ");

        return $"ARIA labels analysis:\n{JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })}";
    }

    private static async Task<string> TestColorContrast(IPage page)
    {
        var result = await page.EvaluateAsync<object>(@"
            () => {
                const textElements = document.querySelectorAll('p, span, div, label, button, a, h1, h2, h3, h4, h5, h6');
                const contrastIssues = [];
                
                textElements.forEach(element => {
                    const styles = window.getComputedStyle(element);
                    const backgroundColor = styles.backgroundColor;
                    const color = styles.color;
                    const fontSize = parseFloat(styles.fontSize);
                    
                    if (color && backgroundColor && color !== backgroundColor) {
                        contrastIssues.push({
                            tagName: element.tagName,
                            id: element.id || 'N/A',
                            testId: element.getAttribute('data-testid') || 'N/A',
                            textColor: color,
                            backgroundColor: backgroundColor,
                            fontSize: fontSize,
                            text: element.textContent?.substring(0, 50) + '...' || 'N/A'
                        });
                    }
                });
                
                return {
                    elementsChecked: textElements.length,
                    potentialIssues: contrastIssues.length,
                    elements: contrastIssues.slice(0, 10) // Limit output
                };
            }
        ");

        return $"Color contrast analysis:\n{JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })}";
    }

    private static async Task<string> TestFocusOrder(IPage page)
    {
        var result = await page.EvaluateAsync<object>(@"
            () => {
                const focusableElements = Array.from(document.querySelectorAll(
                    'button, [href], input, select, textarea, [tabindex]:not([tabindex=""-1])'
                )).filter(el => {
                    const rect = el.getBoundingClientRect();
                    return rect.width > 0 && rect.height > 0;
                });
                
                const focusOrder = focusableElements.map((element, index) => {
                    const rect = element.getBoundingClientRect();
                    return {
                        order: index + 1,
                        tagName: element.tagName,
                        type: element.type || 'N/A',
                        id: element.id || 'N/A',
                        testId: element.getAttribute('data-testid') || 'N/A',
                        tabIndex: element.tabIndex,
                        position: {
                            top: Math.round(rect.top),
                            left: Math.round(rect.left)
                        }
                    };
                });
                
                return {
                    totalElements: focusableElements.length,
                    focusOrder: focusOrder
                };
            }
        ");

        return $"Focus order analysis:\n{JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })}";
    }
}
