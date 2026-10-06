using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Options;

namespace RagChatDemo.IngestionWorker.Confluence;

/// <summary>Talks to a real Confluence Cloud space via API token auth (research.md D1).</summary>
public class ConfluenceCloudClient(HttpClient httpClient, IOptions<ConfluenceOptions> options) : IConfluenceClient
{
    private const int MaxPagesOfResults = 20;

    public async Task<IReadOnlyList<ConfluencePage>> GetPagesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuthentication();

        var pages = new List<ConfluencePage>();
        string? next = "/wiki/api/v2/pages?body-format=storage&limit=100";

        for (var i = 0; next is not null && i < MaxPagesOfResults; i++)
        {
            var response = await httpClient.GetFromJsonAsync<ConfluenceWireListResponse>(next, cancellationToken);
            if (response is null)
            {
                break;
            }

            pages.AddRange(response.Results.Select(p => new ConfluencePage(
                p.Id,
                p.Title,
                p.Body.Storage.Value,
                ConfluenceWebUrl.Resolve(httpClient.BaseAddress, p.Links.Webui),
                p.Version.CreatedAt)));

            next = response.ListLinks?.Next;
        }

        return pages;
    }

    public async Task<ConfluencePage?> GetPageByIdAsync(string externalId, CancellationToken cancellationToken = default)
    {
        ApplyAuthentication();

        var response = await httpClient.GetFromJsonAsync<ConfluenceWirePage>(
            $"/wiki/api/v2/pages/{Uri.EscapeDataString(externalId)}?body-format=storage", cancellationToken);

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

    private void ApplyAuthentication()
    {
        if (httpClient.DefaultRequestHeaders.Authorization is not null)
        {
            return;
        }

        var email = options.Value.Email;
        var apiToken = options.Value.ApiToken;
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(apiToken))
        {
            throw new InvalidOperationException(
                "Confluence:Email and Confluence:ApiToken must be configured when Confluence:UseStub is false.");
        }

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{email}:{apiToken}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }
}
