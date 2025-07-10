namespace PlaywrightTester.Models;

public class NetworkLogEntry
{
    public string Type { get; set; } = "";
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public int Status { get; set; }
    public string StatusText { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public string RequestBody { get; set; } = "";
    public string ResponseBody { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public double Duration { get; set; }
    public bool IsApiCall => Url.Contains("/api/");
    public bool IsAuthRelated => Url.Contains("auth") || Url.Contains("login") || Headers.ContainsKey("authorization");
}