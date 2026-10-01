using Pgvector;
using RagChatDemo.IngestionWorker.Confluence;

namespace RagChatDemo.IngestionWorker.Tests;

internal sealed class FakeConfluenceClient(params ConfluencePage[] pages) : IConfluenceClient
{
    public Task<IReadOnlyList<ConfluencePage>> GetPagesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ConfluencePage>>(pages);
}
