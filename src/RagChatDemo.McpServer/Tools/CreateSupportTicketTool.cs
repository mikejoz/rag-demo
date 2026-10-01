using System.ComponentModel;
using System.Threading;
using ModelContextProtocol.Server;
using RagChatDemo.Shared.Contracts;

namespace RagChatDemo.McpServer.Tools;

[McpServerToolType]
public sealed class CreateSupportTicketTool
{
    private static int ticketCounter = 1000;

    [McpServerTool(Name = "create_support_ticket")]
    [Description("Creates a support ticket for the user's issue. Stubbed - no real ticketing system is involved.")]
    public Task<CreateSupportTicketResult> CreateSupportTicketAsync(
        [Description("Short summary of the issue.")] string summary,
        [Description("Full details of the issue.")] string details)
    {
        var ticketId = $"TICKET-{Interlocked.Increment(ref ticketCounter)}";
        return Task.FromResult(new CreateSupportTicketResult(ticketId, "created"));
    }
}
