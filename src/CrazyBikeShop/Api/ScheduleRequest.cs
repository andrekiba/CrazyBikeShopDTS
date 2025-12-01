namespace Api;

public record ScheduleRequest
{
    public required string Id { get; set; }
    /// <summary>
    /// Gets or sets the name of the orchestration to be scheduled.
    /// </summary>
    public required string OrchestrationName { get; set; }
    /// <summary>
    /// Gets or sets the input data for the orchestration.
    /// </summary>
    public string? Input { get; set; }
    /// <summary>
    /// Gets or sets the time interval between schedule executions.
    /// </summary>
    public required TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>
    /// Gets or sets the time when the schedule should start.
    /// </summary>
    public DateTimeOffset? StartAt { get; set; }
    /// <summary>
    /// Gets or sets the time when the schedule should end.
    /// </summary>
    public DateTimeOffset? EndAt { get; set; }
}