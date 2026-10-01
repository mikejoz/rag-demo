using System.Text.Json.Serialization;

namespace RagChatDemo.IngestionWorker.Confluence;

/// <summary>Wire shapes matching the Confluence REST v2 pages contract (real or stub).</summary>
internal record ConfluenceWirePage(
    string Id,
    string Title,
    ConfluenceWireBody Body,
    ConfluenceWireVersion Version,
    [property: JsonPropertyName("_links")] ConfluenceWireLinks Links);

internal record ConfluenceWireBody(ConfluenceWireStorage Storage);

internal record ConfluenceWireStorage(string Value);

internal record ConfluenceWireVersion(int Number, DateTimeOffset CreatedAt);

internal record ConfluenceWireLinks(string Webui);

internal record ConfluenceWireListResponse(
    IReadOnlyList<ConfluenceWirePage> Results,
    [property: JsonPropertyName("_links")] ConfluenceWireListLinks? ListLinks);

internal record ConfluenceWireListLinks(string? Next);
