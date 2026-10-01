using System.Net.Http.Json;

namespace RagChatDemo.IngestionWorker.Confluence;

/// <summary>Talks to <c>RagChatDemo.ConfluenceStub</c>, used when no real Confluence account is configured.</summary>
public class ConfluenceStubClient(HttpClient httpClient) : IConfluenceClient
{
    public async Task<IReadOnlyList<ConfluencePage>> GetPagesAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<ConfluenceWireListResponse>(
            "/wiki/api/v2/pages", cancellationToken);

        return response?.Results
            .Select(p => new ConfluencePage(
                p.Id,
                p.Title,
                p.Body.Storage.Value,
                new Uri(httpClient.BaseAddress!, p.Links.Webui).ToString(),
                p.Version.CreatedAt))
            .ToArray()
            ?? [];
    }
}
