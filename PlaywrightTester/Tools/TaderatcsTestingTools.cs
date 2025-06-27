using System.ComponentModel;
using System.Text.Json;
using Microsoft.Playwright;
using ModelContextProtocol.Server;
using PlaywrightTester.Services;

namespace PlaywrightTester.Tools;

[McpServerToolType]
public class TaderatcsTestingTools(ToolService toolService)
{
    [McpServerTool]
    [Description("Execute comprehensive TADERATCS enrollment form test suite")]
    public async Task<string> ExecuteComprehensiveEnrollmentTest(
        [Description("Base URL of enrollment form")] string baseUrl = "http://localhost:4200",
        [Description("Browser session ID")] string sessionId = "default")
    {
        try
        {
            var page = toolService.GetPage(sessionId);
            if (page == null) return $"Session {sessionId} not found.";

            var testResults = new List<object>();
            
            // Test 1: Form Load and Initial State
            await page.GotoAsync(baseUrl);
            var personalTabVisible = await page.Locator("[data-testid='tab-info']").IsVisibleAsync();
            testResults.Add(new { test = "Form Load", passed = personalTabVisible, message = "Enrollment form loaded successfully" });
            
            // Test 2: Personal Information Form
            await FillPersonalInformation(page, testResults);
            
            // Test 3: Citizenship Information  
            await TestCitizenshipInformation(page, testResults);
            
            // Test 4: Address Information
            await TestAddressInformation(page, testResults);
            
            // Test 5: Contact Information
            await TestContactInformation(page, testResults);
            
            // Test 6: Form Validation
            await TestFormValidation(page, testResults);
            
            // Test 7: Progress Tracking
            await TestProgressTracking(page, testResults);
            
            var passedTests = testResults.Count(r => (bool)((dynamic)r).passed);
            var totalTests = testResults.Count;
            
            return $"TADERATCS Comprehensive Test Results: {passedTests}/{totalTests} passed\n" +
                   $"{JsonSerializer.Serialize(testResults, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch (Exception ex)
        {
            return $"Comprehensive test failed: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Test specific TADERATCS business rules")]
    public async Task<string> TestBusinessRules(
        [Description("Business rule type: ssn_validation, subprogram_rules, cross_tab_validation")] string ruleType,
        [Description("Browser session ID")] string sessionId = "default")
    {
        try
        {
            var page = toolService.GetPage(sessionId);
            if (page == null) return $"Session {sessionId} not found.";

            return ruleType.ToLower() switch
            {
                "ssn_validation" => await TestSsnValidation(page),
                "subprogram_rules" => await TestSubProgramRules(page),
                "cross_tab_validation" => await TestCrossTabValidation(page),
                _ => $"Unknown business rule type: {ruleType}"
            };
        }
        catch (Exception ex)
        {
            return $"Business rules test failed: {ex.Message}";
        }
    }

    private static async Task FillPersonalInformation(IPage page, List<object> results)
    {
        try
        {
            // Fill personal information
            await page.Locator("[data-testid='personal-first-name']").FillAsync("John");
            await page.Locator("[data-testid='personal-last-name']").FillAsync("Smith"); 
            await page.Locator("[data-testid='personal-date-of-birth']").FillAsync("1990-01-01");
            await page.Locator("[data-testid='personal-ssn']").FillAsync("123-45-6789");
            
            // Check if sex dropdown exists and select
            var sexDropdown = page.Locator("[data-testid='personal-sex']");
            if (await sexDropdown.IsVisibleAsync())
            {
                await sexDropdown.SelectOptionAsync("M");
            }
            
            results.Add(new { test = "Personal Information", passed = true, message = "Personal information filled successfully" });
        }
        catch (Exception ex)
        {
            results.Add(new { test = "Personal Information", passed = false, message = ex.Message });
        }
    }

    private static async Task TestCitizenshipInformation(IPage page, List<object> results)
    {
        try
        {
            // Navigate to citizenship tab if it exists
            var citizenshipTab = page.Locator("[data-testid='citizenship-us-citizen-yes']");
            if (await citizenshipTab.IsVisibleAsync())
            {
                await citizenshipTab.ClickAsync();
                results.Add(new { test = "Citizenship Information", passed = true, message = "Citizenship information completed" });
            }
            else
            {
                results.Add(new { test = "Citizenship Information", passed = false, message = "Citizenship controls not found" });
            }
        }
        catch (Exception ex)
        {
            results.Add(new { test = "Citizenship Information", passed = false, message = ex.Message });
        }
    }

    private static async Task TestAddressInformation(IPage page, List<object> results)
    {
        try
        {
            // Test address fields
            var streetField = page.Locator("[data-testid='address-street']");
            if (await streetField.IsVisibleAsync())
            {
                await streetField.FillAsync("123 Main Street");
                await page.Locator("[data-testid='address-city']").FillAsync("Anytown");
                await page.Locator("[data-testid='address-state']").SelectOptionAsync("VA");
                await page.Locator("[data-testid='address-postal-code']").FillAsync("12345");
                
                results.Add(new { test = "Address Information", passed = true, message = "Address information completed" });
            }
            else
            {
                results.Add(new { test = "Address Information", passed = false, message = "Address fields not found" });
            }
        }
        catch (Exception ex)
        {
            results.Add(new { test = "Address Information", passed = false, message = ex.Message });
        }
    }

    private static async Task TestContactInformation(IPage page, List<object> results)
    {
        try
        {
            // Test contact fields
            var phoneField = page.Locator("[data-testid='contact-phone']");
            if (await phoneField.IsVisibleAsync())
            {
                await phoneField.FillAsync("(555) 123-4567");
                await page.Locator("[data-testid='contact-email']").FillAsync("john.smith@example.com");
                
                results.Add(new { test = "Contact Information", passed = true, message = "Contact information completed" });
            }
            else
            {
                results.Add(new { test = "Contact Information", passed = false, message = "Contact fields not found" });
            }
        }
        catch (Exception ex)
        {
            results.Add(new { test = "Contact Information", passed = false, message = ex.Message });
        }
    }

    private static async Task TestFormValidation(IPage page, List<object> results)
    {
        try
        {
            // Clear a required field and check validation
            await page.Locator("[data-testid='personal-first-name']").ClearAsync();
            await page.Locator("[data-testid='personal-last-name']").ClickAsync(); // Trigger validation
            
            // Check for validation error
            var validationError = await page.Locator(".validation-error, .error-message, .mat-error").IsVisibleAsync();
            
            results.Add(new { test = "Form Validation", passed = validationError, message = validationError ? "Validation working" : "No validation feedback found" });
        }
        catch (Exception ex)
        {
            results.Add(new { test = "Form Validation", passed = false, message = ex.Message });
        }
    }

    private static async Task TestProgressTracking(IPage page, List<object> results)
    {
        try
        {
            // Check for progress indicators
            var progressElements = await page.Locator("[data-testid*='progress'], .progress-indicator, .completion-indicator").CountAsync();
            
            results.Add(new { test = "Progress Tracking", passed = progressElements > 0, message = $"Found {progressElements} progress indicators" });
        }
        catch (Exception ex)
        {
            results.Add(new { test = "Progress Tracking", passed = false, message = ex.Message });
        }
    }

    private static async Task<string> TestSsnValidation(IPage page)
    {
        var testCases = new[]
        {
            new { ssn = "000-00-0000", expectedValid = false },
            new { ssn = "123-45-6789", expectedValid = true },
            new { ssn = "111-11-1111", expectedValid = false },
            new { ssn = "invalid", expectedValid = false }
        };

        var results = new List<object>();
        
        foreach (var testCase in testCases)
        {
            try
            {
                await page.Locator("[data-testid='personal-ssn']").FillAsync(testCase.ssn);
                await page.Locator("[data-testid='personal-first-name']").ClickAsync(); // Trigger validation
                
                var hasError = await page.Locator(".validation-error, .error-message, .mat-error").IsVisibleAsync();
                var actualValid = !hasError;
                
                results.Add(new 
                { 
                    ssn = testCase.ssn, 
                    expected = testCase.expectedValid, 
                    actual = actualValid, 
                    passed = testCase.expectedValid == actualValid 
                });
            }
            catch (Exception ex)
            {
                results.Add(new { ssn = testCase.ssn, error = ex.Message, passed = false });
            }
        }

        return $"SSN Validation Test Results:\n{JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })}";
    }

    private static async Task<string> TestSubProgramRules(IPage page)
    {
        try
        {
            // Test SubProgram selection and requirements
            var subProgramDropdown = page.Locator("[data-testid='subprogram-select']");
            if (!await subProgramDropdown.IsVisibleAsync())
            {
                return "SubProgram dropdown not found - may be in different tab";
            }

            var results = new List<object>();
            var subPrograms = new[] { "FP", "NFP", "FPO" };

            foreach (var program in subPrograms)
            {
                try
                {
                    await subProgramDropdown.SelectOptionAsync(program);
                    await page.WaitForTimeoutAsync(500); // Wait for UI updates
                    
                    // Check conditional field visibility
                    var paymentRequired = await page.Locator("[data-testid*='payment']").IsVisibleAsync();
                    var fingerprintsRequired = await page.Locator("[data-testid*='fingerprint']").IsVisibleAsync();
                    
                    results.Add(new 
                    { 
                        subProgram = program, 
                        paymentVisible = paymentRequired, 
                        fingerprintsVisible = fingerprintsRequired 
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new { subProgram = program, error = ex.Message });
                }
            }

            return $"SubProgram Rules Test Results:\n{JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch (Exception ex)
        {
            return $"SubProgram rules test failed: {ex.Message}";
        }
    }

    private static async Task<string> TestCrossTabValidation(IPage page)
    {
        try
        {
            var results = new List<object>();

            // Test cross-tab field dependencies
            // Example: If not US citizen, ARN should be required
            var citizenshipTab = page.Locator("[data-testid='citizenship-us-citizen-no']");
            if (await citizenshipTab.IsVisibleAsync())
            {
                await citizenshipTab.ClickAsync();
                
                // Check if ARN field becomes required
                var arnField = page.Locator("[data-testid='citizenship-arn']");
                var arnRequired = await arnField.GetAttributeAsync("required") != null;
                
                results.Add(new { test = "ARN Required for Non-US Citizens", passed = arnRequired });
            }

            // Test address history requirements
            var addressCount = await page.Locator("[data-testid*='address']").CountAsync();
            results.Add(new { test = "Address Fields Present", passed = addressCount > 0 });

            return $"Cross-Tab Validation Results:\n{JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })}";
        }
        catch (Exception ex)
        {
            return $"Cross-tab validation test failed: {ex.Message}";
        }
    }
}
