using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using RagChatDemo.Shared.Contracts;

namespace RagChatDemo.ChatApi.Tests;

public class ActivityHubTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task PostedEvents_AreDeliveredInOrder_ToConnectedClient()
    {
        // LongPolling is required for SignalR broadcast delivery to work reliably under
        // WebApplicationFactory's in-memory TestServer (WebSocket upgrades aren't supported there).
        await using var hubConnection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, "/hubs/activity"),
                HttpTransportType.LongPolling,
                options => options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler())
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        var received = new List<ActivityEventDto>();
        var allReceived = new TaskCompletionSource();
        hubConnection.On<ActivityEventDto>("ActivityEvent", e =>
        {
            received.Add(e);
            if (received.Count >= 3)
            {
                allReceived.TrySetResult();
            }
        });

        await hubConnection.StartAsync();

        using var httpClient = factory.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            var activityEvent = new ActivityEventDto
            {
                Id = Guid.NewGuid(),
                Category = ActivityCategory.Ingestion,
                Source = "IngestionWorker",
                Operation = $"step-{i}",
                Status = ActivityStatus.Succeeded,
                Timestamp = DateTimeOffset.UtcNow,
            };

            var response = await httpClient.PostAsJsonAsync("/internal/activity-events", activityEvent, JsonOptions);
            response.EnsureSuccessStatusCode();
        }

        await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(3, received.Count);
        Assert.Equal(["step-0", "step-1", "step-2"], received.Select(e => e.Operation));
        Assert.True(received[0].Sequence < received[1].Sequence);
        Assert.True(received[1].Sequence < received[2].Sequence);
    }
}
