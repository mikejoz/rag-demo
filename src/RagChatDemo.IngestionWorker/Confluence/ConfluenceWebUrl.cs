namespace RagChatDemo.IngestionWorker.Confluence;

/// <summary>
/// Confluence Cloud returns <c>webui</c> paths like <c>/spaces/KEY/pages/ID/Title</c> that are
/// relative to the wiki base (<c>https://tenant.atlassian.net/wiki</c>), not the site root.
/// Resolving against the site root alone yields broken "Page Unavailable" links.
/// </summary>
public static class ConfluenceWebUrl
{
    public static string Resolve(Uri? siteBaseAddress, string webui)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webui);

        if (Uri.TryCreate(webui, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        var path = webui.StartsWith('/') ? webui : "/" + webui;
        if (!path.StartsWith("/wiki/", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(path, "/wiki", StringComparison.OrdinalIgnoreCase))
        {
            path = "/wiki" + path;
        }

        var siteRoot = siteBaseAddress
            ?? throw new InvalidOperationException("HttpClient.BaseAddress must be configured for Confluence.");

        // Ensure we combine against the host root so an absolute path keeps /wiki.
        var root = new Uri(siteRoot.GetLeftPart(UriPartial.Authority) + "/");
        return new Uri(root, path.TrimStart('/')).ToString();
    }
}
