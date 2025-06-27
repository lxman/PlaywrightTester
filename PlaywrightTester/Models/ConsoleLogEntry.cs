namespace PlaywrightTester.Models;

public class ConsoleLogEntry
{
    public string Type { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime Timestamp { get; set; }
}