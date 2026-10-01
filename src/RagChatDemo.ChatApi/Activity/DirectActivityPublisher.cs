using Microsoft.AspNetCore.SignalR;
using RagChatDemo.ChatApi.Hubs;
using RagChatDemo.Shared.Activity;
using RagChatDemo.Shared.Contracts;

namespace RagChatDemo.ChatApi.Activity;

/// <summary>Broadcasts activity events directly to the Activity Hub (ChatApi hosts the hub itself).</summary>
public class DirectActivityPublisher(IHubContext<ActivityHub> hubContext, ActivityEventBuffer buffer) : IActivityPublisher
{
    public async Task PublishAsync(ActivityEventDto activityEvent, CancellationToken cancellationToken = default)
    {
        var sequenced = buffer.Add(activityEvent);
        await hubContext.Clients.All.SendAsync("ActivityEvent", sequenced, cancellationToken);
    }
}
