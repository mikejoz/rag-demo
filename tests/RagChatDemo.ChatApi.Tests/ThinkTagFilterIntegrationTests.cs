using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RagChatDemo.ChatApi.Activity;
using RagChatDemo.ChatApi.Mcp;
using RagChatDemo.Shared.Contracts;
using RagChatDemo.Shared.Ollama;

namespace RagChatDemo.ChatApi.Tests;

/// <summary>
/// Integration test: verify that <think>...</think> tags emitted by the fake Ollama client are stripped
/// before reaching the SignalR client.
/// </summary>
public class ThinkTagFilterIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ThinkTagFilterIntegrationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMcpToolClient>();
                services.AddSingleton<IMcpToolClient, FakeMcpToolClient>();
                services.RemoveAll<IOllamaClient>();
                services.AddSingleton<IOllamaClient, FakeOllamaClient>();
            }));
    }

    [Fact]
    public async Task SendMessage_WithThinkTagsInFakeResponse_ClientReceivesOnlyVisibleText()
    {
        await using var hubConnection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, "/hubs/chat"),
                options => options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler())
            .Build();

        var tokens = new List<string>();
        SourceCitationDto[]? citedSources = null;
        var completed = new TaskCompletionSource();

        hubConnection.On<Guid, string>("ResponseToken", (_, token) => tokens.Add(token));
        hubConnection.On<Guid, SourceCitationDto[]>("ResponseComplete", (_, citations) =>
        {
            citedSources = citations;
            completed.TrySetResult();
        });
        hubConnection.On<Guid, string>("ResponseError", (_, message) =>
            completed.TrySetException(new InvalidOperationException(message)));

        await hubConnection.StartAsync();
        await hubConnection.SendAsync("SendMessage", "How do I reset my VPN password?");
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var fullText = string.Join("", tokens);

        // The FakeOllamaClient emits <think>...</think> tags — they should be stripped.
        Assert.DoesNotContain("<think", fullText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("</think", fullText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("chain of thought", fullText, StringComparison.OrdinalIgnoreCase);

        // The visible answer should still be present
        Assert.Contains("Open", fullText);
        Assert.Contains("self-service", fullText);
    }
}
