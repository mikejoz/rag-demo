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
                ConfluenceWebUrl.Resolve(httpClient.BaseAddress, p.Links.Webui),
                p.Version.CreatedAt))
            .ToArray()
            ?? [];
    }

    public async Task<ConfluencePage?> GetPageByIdAsync(string externalId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<ConfluenceWirePage>(
            $"/wiki/api/v2/pages/{Uri.EscapeDataString(externalId)}", cancellationToken);

        if (response is null)
        {
            return null;
        }

        return new ConfluencePage(
            response.Id,
            response.Title,
            response.Body.Storage.Value,
            ConfluenceWebUrl.Resolve(httpClient.BaseAddress, response.Links.Webui),
            response.Version.CreatedAt);
    }
}
