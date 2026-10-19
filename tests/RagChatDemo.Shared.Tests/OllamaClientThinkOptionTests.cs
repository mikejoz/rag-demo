using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RagChatDemo.Shared.Ollama;

namespace RagChatDemo.Shared.Tests;

public sealed class OllamaClientThinkOptionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task StreamChatAsync_DefaultThink_False_InRequest()
    {
        string? capturedJson = null;
        var handler = new CapturingHttpMessageHandler(r =>
        {
            capturedJson = r.Content.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"message\":{\"content\":\"hello\",\"tool_calls\":null},\"done\":true}\n")))
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };
        var options = Options.Create(new OllamaOptions { BaseUrl = "http://fake/" });
        var client = new OllamaClient(http, options);

        await foreach (var _ in client.StreamChatAsync([]))
        {
            // consume
        }

        Assert.NotNull(capturedJson);
        Assert.Contains("\"think\":false", capturedJson);
    }

    [Fact]
    public async Task StreamChatAsync_ExplicitThinkTrue_PassedInRequest()
    {
        string? capturedJson = null;
        var handler = new CapturingHttpMessageHandler(r =>
        {
            capturedJson = r.Content.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"message\":{\"content\":\"hello\",\"tool_calls\":null},\"done\":true}\n")))
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };
        var options = Options.Create(new OllamaOptions { BaseUrl = "http://fake/", Think = false });
        var client = new OllamaClient(http, options);

        await foreach (var _ in client.StreamChatAsync([], think: true))
        {
            // consume
        }

        Assert.NotNull(capturedJson);
        Assert.Contains("\"think\":true", capturedJson);
    }

    [Fact]
    public async Task StreamChatAsync_OptionsThinkTrue_NoOverride_UseOptions()
    {
        string? capturedJson = null;
        var handler = new CapturingHttpMessageHandler(r =>
        {
            capturedJson = r.Content.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"message\":{\"content\":\"hello\",\"tool_calls\":null},\"done\":true}\n")))
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://fake/") };
        var options = Options.Create(new OllamaOptions { BaseUrl = "http://fake/", Think = true });
        var client = new OllamaClient(http, options);

        // Don't override — think param stays null, so it falls through to OllamaOptions.Think (true)
        await foreach (var _ in client.StreamChatAsync([]))
        {
            // consume
        }

        Assert.NotNull(capturedJson);
        Assert.Contains("\"think\":true", capturedJson);
    }

    private sealed class CapturingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
