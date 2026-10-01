using Microsoft.EntityFrameworkCore;
using RagChatDemo.McpServer.Tools;
using RagChatDemo.Shared.Activity;
using RagChatDemo.Shared.Data;
using RagChatDemo.Shared.Ollama;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddHttpClient<IActivityPublisher, HttpActivityPublisher>((sp, client) =>
{
    var baseUrl = sp.GetRequiredService<IConfiguration>()["ChatApi:BaseUrl"];
    client.BaseAddress = new Uri(baseUrl ?? throw new InvalidOperationException("ChatApi:BaseUrl is not configured."));
});

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<SearchKnowledgeBaseTool>()
    .WithTools<CreateSupportTicketTool>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<RagChatDemoDbContext>().Database.Migrate();
}

app.MapGet("/", () => "Hello World!");

app.MapMcp();

app.Run();
