using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RagChatDemo.ConfluenceStub;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks()
    .AddCheck<SelfHealthCheck>("self", tags: new[] { "live" });

builder.Services.AddSingleton<ConfluenceStubStore>();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = tag => tag.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = tag => tag.Tags.Contains("ready") });

app.MapGet("/wiki/api/v2/pages", (ConfluenceStubStore store) =>
{
    var results = store.GetAll().Select(ToResponse).ToArray();
    return Results.Ok(new ConfluencePageListResponse(results, new ConfluenceListLinks()));
});

app.MapGet("/wiki/api/v2/pages/{id}", (string id, ConfluenceStubStore store) =>
{
    var page = store.GetById(id);
    return page is null ? Results.NotFound() : Results.Ok(ToResponse(page));
});

// Not part of the real Confluence contract; lets a demo operator edit an article and show
// re-ingestion pick up the change (quickstart.md SC-003).
app.MapPut("/wiki/api/v2/pages/{id}", (string id, UpdatePageRequest request, ConfluenceStubStore store) =>
{
    var page = store.UpdateContent(id, request.Title, request.BodyHtml);
    return page is null ? Results.NotFound() : Results.Ok(ToResponse(page));
});

app.Run();

static ConfluencePageResponse ToResponse(StubPage page) => new(
    page.Id,
    page.Title,
    "current",
    new ConfluencePageBody(new ConfluenceStorageBody(page.BodyHtml)),
    new ConfluencePageVersion(page.VersionNumber, page.LastModifiedAt),
    new ConfluencePageLinks($"/wiki/spaces/IT/pages/{page.Id}"));

public sealed class SelfHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(HealthCheckResult.Healthy());
}

