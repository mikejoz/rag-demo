using System.Text.Json.Serialization;

namespace RagChatDemo.ConfluenceStub;

public record ConfluenceStorageBody(string Value, string Representation = "storage");

public record ConfluencePageBody(ConfluenceStorageBody Storage);

public record ConfluencePageVersion(int Number, DateTimeOffset CreatedAt);

public record ConfluencePageLinks([property: JsonPropertyName("webui")] string Webui);

public record ConfluencePageResponse(
    string Id,
    string Title,
    string Status,
    ConfluencePageBody Body,
    ConfluencePageVersion Version,
    [property: JsonPropertyName("_links")] ConfluencePageLinks Links);

public record ConfluencePageListResponse(
    IReadOnlyList<ConfluencePageResponse> Results,
    [property: JsonPropertyName("_links")] ConfluenceListLinks ListLinks);

public record ConfluenceListLinks(string? Next = null);

public record UpdatePageRequest(string? Title, string BodyHtml);
