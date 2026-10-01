using Microsoft.AspNetCore.SignalR;
using RagChatDemo.ChatApi.Chat;

namespace RagChatDemo.ChatApi.Hubs;

/// <summary>Chat hub per contracts/signalr-hubs.md: client sends a message, server streams the grounded response.</summary>
public class ChatHub(RagOrchestrator orchestrator) : Hub<IChatClient>
{
    public async Task SendMessage(string text)
    {
        var messageId = Guid.NewGuid();
        try
        {
            await orchestrator.HandleMessageAsync(text, messageId, Clients.Caller, Context.ConnectionAborted);
        }
        catch (Exception ex)
        {
            await Clients.Caller.ResponseError(messageId, ex.Message);
        }
    }
}
