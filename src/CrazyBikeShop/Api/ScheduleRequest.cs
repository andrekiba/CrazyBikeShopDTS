namespace Api;

public record ScheduleRequest
{
    public required string Id { get; set; }
    public required string OrchestrationName { get; set; }
    public object? Input { get; set; }
}