using RagChatDemo.Shared.Contracts;

namespace RagChatDemo.ChatApi.Hubs;

/// <summary>Strongly-typed server-to-client methods for <see cref="ChatHub"/>, per contracts/signalr-hubs.md.</summary>
public interface IChatClient
{
    Task ResponseToken(Guid messageId, string token);

    Task ResponseComplete(Guid messageId, SourceCitationDto[] citedSources);

    Task ResponseError(Guid messageId, string message);
}
