namespace RagChatDemo.Shared.Contracts;

public record SourceCitationDto
{
    public required string DocumentTitle { get; init; }
    public required string SourceUrl { get; init; }
}
