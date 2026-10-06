using Microsoft.AspNetCore.SignalR;
using RagChatDemo.ChatApi.Activity;

namespace RagChatDemo.ChatApi.Hubs;

/// <summary>Broadcasts background activity events; replays the last 50 buffered events on connect.</summary>
public class ActivityHub(ActivityEventBuffer buffer) : Hub
{
    public override async Task OnConnectedAsync()
    {
        foreach (var activityEvent in buffer.GetRecent())
        {
            await Clients.Caller.SendAsync("ActivityEvent", activityEvent);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Client can pull the current buffer (used after reconnect or to resync the activity log).</summary>
    public IReadOnlyList<RagChatDemo.Shared.Contracts.ActivityEventDto> GetRecent() => buffer.GetRecent();
}
