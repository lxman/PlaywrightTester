namespace PlaywrightTester.Models;

public class TestCase
{
    public string? Title { get; set; }
    
    public List<TestStepData>? TestSteps { get; set; }
}