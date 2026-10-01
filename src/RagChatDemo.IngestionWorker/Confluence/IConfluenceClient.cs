namespace RagChatDemo.IngestionWorker.Confluence;

public class ConfluenceOptions
{
    public const string SectionName = "Confluence";

    /// <summary>When true, use <see cref="ConfluenceStubClient"/>; otherwise <see cref="ConfluenceCloudClient"/>.</summary>
    public bool UseStub { get; set; } = true;

    public required string BaseUrl { get; set; }

    /// <summary>Confluence Cloud account email, used for Basic auth with <see cref="ApiToken"/> (real Confluence only).</summary>
    public string? Email { get; set; }

    /// <summary>Confluence Cloud API token (real Confluence only); supplied via user-secrets/Kubernetes Secret.</summary>
    public string? ApiToken { get; set; }
}

/// <summary>A knowledge-base page fetched from Confluence (real or stub), ready for chunking.</summary>
public record ConfluencePage(string ExternalId, string Title, string HtmlContent, string WebUrl, DateTimeOffset LastModifiedAt);

public interface IConfluenceClient
{
    Task<IReadOnlyList<ConfluencePage>> GetPagesAsync(CancellationToken cancellationToken = default);
}
