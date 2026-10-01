using RagChatDemo.McpServer.Tools;

namespace RagChatDemo.McpServer.Tests;

public class CreateSupportTicketToolTests
{
    [Fact]
    public async Task CreateSupportTicketAsync_ReturnsCreatedTicket()
    {
        var tool = new CreateSupportTicketTool();

        var result = await tool.CreateSupportTicketAsync("VPN won't connect", "User reports VPN client fails to connect since this morning.");

        Assert.StartsWith("TICKET-", result.TicketId);
        Assert.Equal("created", result.Status);
    }
}
