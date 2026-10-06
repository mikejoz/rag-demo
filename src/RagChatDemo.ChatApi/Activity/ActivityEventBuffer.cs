using RagChatDemo.Shared.Contracts;

namespace RagChatDemo.ChatApi.Activity;

/// <summary>
/// Thread-safe ring buffer of the last 50 activity events, replayed to newly connected hub clients.
/// Prefer dropping older <see cref="ActivityCategory.Ingestion"/> events so chat-path categories
/// (McpTool / Generation / Retrieval) are not wiped by an ingestion run.
/// </summary>
public class ActivityEventBuffer
{
    private const int Capacity = 50;
    private readonly object gate = new();
    private readonly List<ActivityEventDto> events = new();
    private long nextSequence = 1;

    /// <summary>Assigns the next monotonic sequence number and stores the event, returning the sequenced copy.</summary>
    public ActivityEventDto Add(ActivityEventDto activityEvent)
    {
        lock (gate)
        {
            var sequenced = activityEvent with { Sequence = nextSequence++ };
            events.Add(sequenced);
            TrimToCapacity();
            return sequenced;
        }
    }

    public IReadOnlyList<ActivityEventDto> GetRecent()
    {
        lock (gate)
        {
            return events.OrderBy(e => e.Sequence).ToArray();
        }
    }

    private void TrimToCapacity()
    {
        while (events.Count > Capacity)
        {
            // Drop oldest ingestion event first when possible so chat activity survives.
            var ingestionIndex = events.FindIndex(e => e.Category == ActivityCategory.Ingestion);
            if (ingestionIndex >= 0)
            {
                events.RemoveAt(ingestionIndex);
            }
            else
            {
                events.RemoveAt(0);
            }
        }
    }
}
