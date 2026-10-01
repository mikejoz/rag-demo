using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RagChatDemo.ChatApi.Mcp;
using RagChatDemo.Shared.Contracts;

namespace RagChatDemo.ChatApi.Tests;

/// <summary>Returns fixed tool results without needing a live MCP server.</summary>
internal sealed class FakeMcpToolClient : IMcpToolClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<IList<McpClientTool>> ListToolsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<CallToolResult> CallToolAsync(
        string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        object result = toolName switch
        {
            "create_support_ticket" => new CreateSupportTicketResult("TICKET-1042", "created"),
            _ => new SearchKnowledgeBaseResult(
            [
                new KnowledgeBaseSearchHit(
                    Guid.NewGuid(), "VPN Password Reset", "https://example.com/vpn-reset", 0.95, "Open the self-service portal and click Reset."),
            ]),
        };

        return Task.FromResult(new CallToolResult { StructuredContent = JsonSerializer.SerializeToElement(result, JsonOptions) });
    }
}
