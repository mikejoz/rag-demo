using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using RagChatDemo.ChatApi.Hubs;
using RagChatDemo.Shared.Activity;
using RagChatDemo.Shared.Contracts;

namespace RagChatDemo.ChatApi.Activity;

/// <summary>Broadcasts activity events directly to the Activity Hub (ChatApi hosts the hub itself).</summary>
public class DirectActivityPublisher(
    IHubContext<ActivityHub> hubContext,
    ActivityEventBuffer buffer,
    ILogger<DirectActivityPublisher> logger) : IActivityPublisher
{
    public async Task PublishAsync(ActivityEventDto activityEvent, CancellationToken cancellationToken = default)
    {
        // Always buffer first so a refresh/reconnect can still show the event.
        var sequenced = buffer.Add(activityEvent);

        try
        {
            // Do not use the caller's cancellation token: chat ConnectionAborted must not
            // cancel broadcasts to activity-hub clients (or fail the publish path mid-response).
            await hubContext.Clients.All.SendAsync("ActivityEvent", sequenced, CancellationToken.None);
            logger.LogDebug(
                "Activity published {Category}/{Operation} status={Status} seq={Sequence}",
                sequenced.Category, sequenced.Operation, sequenced.Status, sequenced.Sequence);
        }
        catch (Exception ex)
        {
            // Buffer already has the event; log and continue so chat is not failed by fan-out issues.
            logger.LogWarning(
                ex,
                "Activity broadcast failed for {Category}/{Operation} seq={Sequence} (event kept in buffer)",
                sequenced.Category, sequenced.Operation, sequenced.Sequence);
        }
    }
}
