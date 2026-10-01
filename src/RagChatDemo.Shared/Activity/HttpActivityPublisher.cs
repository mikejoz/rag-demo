using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using RagChatDemo.Shared.Contracts;

namespace RagChatDemo.Shared.Activity;

/// <summary>
/// Posts activity events to the ChatApi's internal broadcast endpoint. Used by services
/// (IngestionWorker, McpServer) that don't hold their own SignalR hub.
/// </summary>
public class HttpActivityPublisher(HttpClient httpClient) : IActivityPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task PublishAsync(ActivityEventDto activityEvent, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/internal/activity-events", activityEvent, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
