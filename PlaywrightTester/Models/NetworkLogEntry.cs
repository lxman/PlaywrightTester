namespace PlaywrightTester.Models;

public class NetworkLogEntry
{
    public string Type { get; set; } = "";
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public int Status { get; set; }
    public DateTime Timestamp { get; set; }
}