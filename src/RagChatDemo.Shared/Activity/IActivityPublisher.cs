using RagChatDemo.Shared.Contracts;

namespace RagChatDemo.Shared.Activity;

public interface IActivityPublisher
{
    Task PublishAsync(ActivityEventDto activityEvent, CancellationToken cancellationToken = default);
}
