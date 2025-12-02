namespace Api;

public record ScheduleRequest
{
    public required string Id { get; set; }
    public required string OrchestrationName { get; set; }
    public string? Input { get; set; }
    public required TimeSpan Interval { get; set; }
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
}