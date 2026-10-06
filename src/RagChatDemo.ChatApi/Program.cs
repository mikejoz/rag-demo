using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RagChatDemo.ChatApi.Activity;
using RagChatDemo.ChatApi.Chat;
using RagChatDemo.ChatApi.Hubs;
using RagChatDemo.ChatApi.Mcp;
using RagChatDemo.Shared.Activity;
using RagChatDemo.Shared.Contracts;
using RagChatDemo.Shared.Data;
using RagChatDemo.Shared.Diagnostics;
using RagChatDemo.Shared.Ollama;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi();
}

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<RagChatDemoDbContext>("database", tags: ["ready"])
    .AddCheck<AlwaysHealthyHealthCheck>("self", tags: new[] { "live" });

builder.Services.AddDbContext<RagChatDemoDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Postgres"),
        npgsql => npgsql.UseVector()));

builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));
builder.Services.AddHttpClient<IOllamaClient, OllamaClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IConfiguration>().GetSection(OllamaOptions.SectionName).Get<OllamaOptions>();
    client.BaseAddress = new Uri(options?.BaseUrl ?? throw new InvalidOperationException("Ollama:BaseUrl is not configured."));
});

builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<ActivityEventBuffer>();
builder.Services.AddSingleton<IActivityPublisher, DirectActivityPublisher>();

builder.Services.Configure<McpOptions>(builder.Configuration.GetSection(McpOptions.SectionName));
builder.Services.AddSingleton<McpToolClient>();
builder.Services.AddSingleton<IMcpToolClient>(sp => sp.GetRequiredService<McpToolClient>());
builder.Services.AddScoped<RagOrchestrator>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<RagChatDemoDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    var swaggerHtml = @"<!DOCTYPE html>
<html lang=""en"">
<head><meta charset=""utf-8""/><meta name=""viewport"" content=""width=device-width,initial-scale=1""/>
<title>RagChatDemo Chat API - Swagger UI</title>
<link rel=""stylesheet"" href=""https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui.css""/>
<style>html{box-sizing:border-box;overflow:visible}body{margin:0;background:#fafafa}.swagger-ui .topbar{display:none}</style>
</head>
<body><div id=""swagger-ui""></div>
<script src=""https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui-standalone-preset.js""></script>
<script src=""https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui-bundle.js""></script>
<script>window.onload=()=>{const ui=SwaggerUIBundle({url:""/openapi/v1.json"",dom_id:""#swagger-ui"",presets:[SwaggerUIBundle.presets.apis,SwaggerUIStandalonePreset],layout:""BaseLayout"",persistAuthorization:true});window.ui=ui};</script>
</body></html>";
    app.MapGet("/swagger", () => Results.Content(swaggerHtml, "text/html"));
}

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = tag => tag.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = tag => tag.Tags.Contains("ready") });

app.MapHub<ActivityHub>("/hubs/activity");
app.MapHub<ChatHub>("/hubs/chat");

app.MapPost("/internal/activity-events", async (ActivityEventDto activityEvent, IActivityPublisher publisher) =>
{
    await publisher.PublishAsync(activityEvent);
    return Results.Accepted();
})
.WithSummary("Publish an activity event")
.WithDescription(
    "Internal endpoint used by the MCP server and ingestion worker to report activity. " +
    "The event is assigned a sequence number, buffered (last 50 events), and broadcast to all " +
    "clients connected to the /hubs/activity SignalR hub. Returns 202 Accepted.")
.Produces(StatusCodes.Status202Accepted);

app.Run();

/// <summary>Exposed for <c>WebApplicationFactory&lt;Program&gt;</c> in integration tests.</summary>
public partial class Program;
