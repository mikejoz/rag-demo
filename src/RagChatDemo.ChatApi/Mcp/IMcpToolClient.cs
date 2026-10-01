using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace RagChatDemo.ChatApi.Mcp;

public interface IMcpToolClient
{
    Task<IList<McpClientTool>> ListToolsAsync(CancellationToken cancellationToken = default);

    Task<CallToolResult> CallToolAsync(
        string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default);
}
