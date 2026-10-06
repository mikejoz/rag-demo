using RagChatDemo.IngestionWorker.Confluence;

namespace RagChatDemo.IngestionWorker.Tests;

public class ConfluenceWebUrlTests
{
    [Fact]
    public void Resolve_prefixes_wiki_for_cloud_webui_paths()
    {
        var site = new Uri("https://michaeljozwik.atlassian.net");
        var url = ConfluenceWebUrl.Resolve(site, "/spaces/SD/pages/720897/Printer+Setup+and+Troubleshooting");

        Assert.Equal(
            "https://michaeljozwik.atlassian.net/wiki/spaces/SD/pages/720897/Printer+Setup+and+Troubleshooting",
            url);
    }

    [Fact]
    public void Resolve_does_not_double_prefix_when_webui_already_includes_wiki()
    {
        var site = new Uri("https://stub.local");
        var url = ConfluenceWebUrl.Resolve(site, "/wiki/spaces/IT/pages/1");

        Assert.Equal("https://stub.local/wiki/spaces/IT/pages/1", url);
    }

    [Fact]
    public void Resolve_keeps_absolute_urls()
    {
        var site = new Uri("https://michaeljozwik.atlassian.net");
        var absolute = "https://example.com/wiki/spaces/X/pages/1";
        Assert.Equal(absolute, ConfluenceWebUrl.Resolve(site, absolute));
    }
}
