using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using RagChatDemo.IngestionWorker.Confluence;
using RagChatDemo.IngestionWorker.Jobs;
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

builder.Services.Configure<ConfluenceOptions>(builder.Configuration.GetSection(ConfluenceOptions.SectionName));
builder.Services.AddHttpClient<ConfluenceStubClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IConfiguration>().GetSection(ConfluenceOptions.SectionName).Get<ConfluenceOptions>();
    client.BaseAddress = new Uri(options?.BaseUrl ?? throw new InvalidOperationException("Confluence:BaseUrl is not configured."));
});
builder.Services.AddHttpClient<ConfluenceCloudClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IConfiguration>().GetSection(ConfluenceOptions.SectionName).Get<ConfluenceOptions>();
    client.BaseAddress = new Uri(options?.BaseUrl ?? throw new InvalidOperationException("Confluence:BaseUrl is not configured."));
});
builder.Services.AddScoped<IConfluenceClient>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ConfluenceOptions>>().Value;
    return options.UseStub
        ? sp.GetRequiredService<ConfluenceStubClient>()
        : sp.GetRequiredService<ConfluenceCloudClient>();
});

builder.Services.AddScoped<IngestionJob>();

builder.Services.AddHangfire(config => config
    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Postgres"))));
builder.Services.AddHangfireServer();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.UseHangfireDashboard("/hangfire");

RecurringJob.AddOrUpdate<IngestionJob>(
    "knowledge-base-ingestion", job => job.RunAsync(CancellationToken.None), Cron.Hourly);

app.Run();
