using RagChatDemo.Shared.Activity;
using RagChatDemo.Shared.Contracts;

namespace RagChatDemo.IngestionWorker.Tests;

internal sealed class NoOpActivityPublisher : IActivityPublisher
{
    public Task PublishAsync(ActivityEventDto activityEvent, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
