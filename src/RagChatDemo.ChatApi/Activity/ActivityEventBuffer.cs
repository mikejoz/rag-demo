using RagChatDemo.Shared.Contracts;

namespace RagChatDemo.ChatApi.Activity;

/// <summary>Thread-safe ring buffer of the last 50 activity events, replayed to newly connected hub clients.</summary>
public class ActivityEventBuffer
{
    private const int Capacity = 50;
    private readonly object gate = new();
    private readonly Queue<ActivityEventDto> events = new();

    public void Add(ActivityEventDto activityEvent)
    {
        lock (gate)
        {
            events.Enqueue(activityEvent);
            while (events.Count > Capacity)
            {
                events.Dequeue();
            }
        }
    }

    public IReadOnlyList<ActivityEventDto> GetRecent()
    {
        lock (gate)
        {
            return [.. events];
        }
    }
}
