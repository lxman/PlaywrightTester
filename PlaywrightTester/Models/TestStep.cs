namespace PlaywrightTester.Models;

public class TestStep
{
    public string? Action { get; set; }
    public string? Target { get; set; }
    public string? Value { get; set; }
    public string? Validation { get; set; }
    public string? Message { get; set; }
}