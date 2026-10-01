namespace RagChatDemo.Shared.Contracts;

public enum ActivityCategory
{
    Ingestion,
    Retrieval,
    McpTool,
    Generation,
}

public enum ActivityStatus
{
    Started,
    Succeeded,
    Failed,
}

/// <summary>A single observable background step, broadcast over the activity SignalR hub.</summary>
public record ActivityEventDto
{
    public required Guid Id { get; init; }
    public required ActivityCategory Category { get; init; }
    public required string Source { get; init; }
    public required string Operation { get; init; }
    public string? Target { get; init; }
    public required ActivityStatus Status { get; init; }
    public string? Detail { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}
