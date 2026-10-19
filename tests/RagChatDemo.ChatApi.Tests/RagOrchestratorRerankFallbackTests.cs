using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RagChatDemo.ChatApi.Chat;
using RagChatDemo.ChatApi.Hubs;
using RagChatDemo.ChatApi.Mcp;
using RagChatDemo.Shared.Activity;
using RagChatDemo.Shared.Contracts;

namespace RagChatDemo.ChatApi.Tests;

/// <summary>Returns a search_knowledge_base result whose rerank step fell back, without a live MCP server.</summary>
internal sealed class FakeRerankFallbackMcpToolClient : IMcpToolClient
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
            ],
            new RerankOutcomeDto(
                "Tei", 20, 5, 10, true, "timeout",
                [],
                [])),
        };

        return Task.FromResult(new CallToolResult { StructuredContent = JsonSerializer.SerializeToElement(result, JsonOptions) });
    }
}

/// <summary>Captures every published activity event for assertion.</summary>
internal sealed class CapturingActivityPublisher : IActivityPublisher
{
    public List<ActivityEventDto> Published { get; } = [];

    public Task PublishAsync(ActivityEventDto activityEvent, CancellationToken cancellationToken = default)
    {
        Published.Add(activityEvent);
        return Task.CompletedTask;
    }
}

/// <summary>No-op chat client used to drive RagOrchestrator without a live SignalR connection.</summary>
internal sealed class NoOpChatClient : IChatClient
{
    public Task ResponseToken(Guid messageId, string token) => Task.CompletedTask;

    public Task ResponseComplete(Guid messageId, SourceCitationDto[] citedSources) => Task.CompletedTask;

    public Task ResponseError(Guid messageId, string message) => Task.CompletedTask;
}

public class RagOrchestratorRerankFallbackTests
{
    [Fact]
    public async Task HandleMessageAsync_WhenRerankFellBack_EmitsRerankFallbackActivityEvent()
    {
        var activityPublisher = new CapturingActivityPublisher();
        var orchestrator = new RagOrchestrator(
            new FakeRerankFallbackMcpToolClient(), new FakeOllamaClient(), activityPublisher);

        await orchestrator.HandleMessageAsync(
            "How do I reset my VPN password?", Guid.NewGuid(), new NoOpChatClient(), CancellationToken.None);

        var fallbackEvent = Assert.Single(activityPublisher.Published, e => e.Operation == "rerank_fallback");
        Assert.Equal(ActivityCategory.Retrieval, fallbackEvent.Category);
        Assert.Equal(ActivityStatus.Failed, fallbackEvent.Status);
        Assert.Contains("Re-ranker unavailable", fallbackEvent.Detail);
    }
}
