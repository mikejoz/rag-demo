namespace RagChatDemo.ConfluenceStub;

/// <summary>A mutable in-memory "Confluence page" so the stub supports editing content at runtime (demo SC-003).</summary>
public class StubPage
{
    public required string Id { get; init; }
    public required string Title { get; set; }
    public required string BodyHtml { get; set; }
    public int VersionNumber { get; set; } = 1;
    public DateTimeOffset LastModifiedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Thread-safe in-memory store of seeded IT/support articles, mimicking a Confluence space.</summary>
public class ConfluenceStubStore
{
    private readonly object gate = new();
    private readonly List<StubPage> pages = SeedArticles.Create();

    public IReadOnlyList<StubPage> GetAll()
    {
        lock (gate)
        {
            return [.. pages];
        }
    }

    public StubPage? GetById(string id)
    {
        lock (gate)
        {
            return pages.FirstOrDefault(p => p.Id == id);
        }
    }

    public StubPage? UpdateContent(string id, string? title, string bodyHtml)
    {
        lock (gate)
        {
            var page = pages.FirstOrDefault(p => p.Id == id);
            if (page is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                page.Title = title;
            }

            page.BodyHtml = bodyHtml;
            page.VersionNumber++;
            page.LastModifiedAt = DateTimeOffset.UtcNow;
            return page;
        }
    }
}
