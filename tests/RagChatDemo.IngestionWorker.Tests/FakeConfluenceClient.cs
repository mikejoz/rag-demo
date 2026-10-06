using Pgvector;
using RagChatDemo.IngestionWorker.Confluence;

namespace RagChatDemo.IngestionWorker.Tests;

internal sealed class FakeConfluenceClient(params ConfluencePage[] pages) : IConfluenceClient
{
    private readonly Dictionary<string, ConfluencePage> _pagesById = new();

    public Task<IReadOnlyList<ConfluencePage>> GetPagesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ConfluencePage>>(pages);

    public Task<ConfluencePage?> GetPageByIdAsync(string externalId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_pagesById.GetValueOrDefault(externalId));
    }
}
