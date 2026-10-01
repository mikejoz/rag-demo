using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace RagChatDemo.ChatApi.Mcp;

public class McpOptions
{
    public const string SectionName = "Mcp";

    public required string ServerUrl { get; set; }
}

/// <summary>Lazily-connected client for the RagChatDemo MCP server (search_knowledge_base, create_support_ticket).</summary>
public class McpToolClient(Microsoft.Extensions.Options.IOptions<McpOptions> options) : IMcpToolClient, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private McpClient? client;

    public async Task<IList<McpClientTool>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        var connected = await GetClientAsync(cancellationToken);
        return await connected.ListToolsAsync(cancellationToken: cancellationToken);
    }

    public async Task<CallToolResult> CallToolAsync(
        string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var connected = await GetClientAsync(cancellationToken);
        return await connected.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
    }

    private async Task<McpClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (client is not null)
        {
            return client;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            client ??= await McpClient.CreateAsync(
                new HttpClientTransport(new() { Endpoint = new Uri(options.Value.ServerUrl) }),
                cancellationToken: cancellationToken);
            return client;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (client is not null)
        {
            await client.DisposeAsync();
        }

        gate.Dispose();
    }
}
