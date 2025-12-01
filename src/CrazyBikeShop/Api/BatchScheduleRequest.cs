namespace Api;

public record BatchScheduleRequest
{
    public required int TotalOrchestrations { get; set;  }
    public required int IntervalSeconds { get; set; }
}