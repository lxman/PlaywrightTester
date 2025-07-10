namespace PlaywrightTester.Models;

public class ConsoleLogEntry
{
    public string Type { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Url { get; set; } = "";
    public int LineNumber { get; set; }
    public int ColumnNumber { get; set; }
    public string[] Args { get; set; } = Array.Empty<string>();
    public bool IsError => Type.Equals("error", StringComparison.OrdinalIgnoreCase);
    public bool IsWarning => Type.Equals("warning", StringComparison.OrdinalIgnoreCase) || Type.Equals("warn", StringComparison.OrdinalIgnoreCase);
}