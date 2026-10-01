using RagChatDemo.Shared.Contracts;

namespace RagChatDemo.ChatApi.Activity;

/// <summary>Thread-safe ring buffer of the last 50 activity events, replayed to newly connected hub clients.</summary>
public class ActivityEventBuffer
{
    private const int Capacity = 50;
    private readonly object gate = new();
    private readonly Queue<ActivityEventDto> events = new();
    private long nextSequence = 1;

    /// <summary>Assigns the next monotonic sequence number and stores the event, returning the sequenced copy.</summary>
    public ActivityEventDto Add(ActivityEventDto activityEvent)
    {
        lock (gate)
        {
            var sequenced = activityEvent with { Sequence = nextSequence++ };
            events.Enqueue(sequenced);
            while (events.Count > Capacity)
            {
                events.Dequeue();
            }

            return sequenced;
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
