using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RagChatDemo.ChatApi.Activity;
using RagChatDemo.ChatApi.Mcp;
using RagChatDemo.Shared.Contracts;
using RagChatDemo.Shared.Ollama;

namespace RagChatDemo.ChatApi.Tests;

public class ChatHubTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ChatHubTests(WebApplicationFactory<Program> factory)
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
    public async Task SendMessage_WithSeededKnowledge_StreamsTokensAndCompletesWithCitation()
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

        Assert.NotEmpty(tokens);
        Assert.NotNull(citedSources);
        var citation = Assert.Single(citedSources!);
        Assert.Equal("VPN Password Reset", citation.DocumentTitle);
        Assert.Equal("https://example.com/vpn-reset", citation.SourceUrl);
    }

    [Fact]
    public async Task SendMessage_WithTicketIntent_InvokesCreateSupportTicketTool()
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
        await hubConnection.SendAsync("SendMessage", "Please log a support ticket for this issue.");
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotEmpty(tokens);
        Assert.Empty(citedSources!);

        var buffer = factory.Services.GetRequiredService<ActivityEventBuffer>();
        var mcpToolEvent = Assert.Single(
            buffer.GetRecent(), e => e.Category == ActivityCategory.McpTool && e.Status == ActivityStatus.Succeeded);
        Assert.Equal("create_support_ticket", mcpToolEvent.Operation);
    }
}
