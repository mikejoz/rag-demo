namespace RagChatDemo.Shared.Contracts;

public enum ChatRole
{
    User,
    Assistant,
}

/// <summary>A single turn in a conversation (in-memory per connection only, not persisted).</summary>
public record ChatMessageDto
{
    public required Guid Id { get; init; }
    public required ChatRole Role { get; init; }
    public required string Text { get; init; }
    public Guid[] CitedChunkIds { get; init; } = [];
    public required DateTimeOffset CreatedAt { get; init; }
}
