namespace Argus.Infrastructure.ExternalServices;

public sealed class IntelligenceOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8000";
    public int TimeoutSeconds { get; set; } = 30;
    public bool WorkerEnabled { get; set; } = true;
    public bool AutoProcessImports { get; set; } = true;
    public int MaxAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
    public int MaxTextCharacters { get; set; } = 250000;
    public int MaxCandidates { get; set; } = 5000;
    public double MatchThreshold { get; set; } = 0.85;
}
