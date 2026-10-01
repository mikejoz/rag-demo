using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RagChatDemo.ChatApi.Activity;
using RagChatDemo.ChatApi.Hubs;
using RagChatDemo.Shared.Activity;
using RagChatDemo.Shared.Contracts;
using RagChatDemo.Shared.Data;
using RagChatDemo.Shared.Ollama;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddDbContext<RagChatDemoDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Postgres"),
        npgsql => npgsql.UseVector()));

builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));
builder.Services.AddHttpClient<OllamaClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IConfiguration>().GetSection(OllamaOptions.SectionName).Get<OllamaOptions>();
    client.BaseAddress = new Uri(options?.BaseUrl ?? throw new InvalidOperationException("Ollama:BaseUrl is not configured."));
});

builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<ActivityEventBuffer>();
builder.Services.AddSingleton<IActivityPublisher, DirectActivityPublisher>();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapHub<ActivityHub>("/hubs/activity");

app.MapPost("/internal/activity-events", async (ActivityEventDto activityEvent, IActivityPublisher publisher) =>
{
    await publisher.PublishAsync(activityEvent);
    return Results.Accepted();
});

app.Run();
