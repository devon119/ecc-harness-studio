using System.Net;
using System.Text;
using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Options;
using ECCSELECTION.Web.Services;
using ECCSELECTION.Web.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECCSELECTION.Tests.Services;

public sealed class LlmEnrichmentServiceTests
{
    [Fact]
    public async Task EnrichSummaryZhAsync_WithoutApiKey_ReturnsNullWithoutHttpCall()
    {
        int callCount = 0;
        var handler = new StubHttpHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var service = new LlmEnrichmentService(
            new HttpClient(handler),
            Options.Create(new LlmEnrichmentOptions { Enabled = true, ApiKey = string.Empty }),
            NullLogger<LlmEnrichmentService>.Instance);

        string? result = await service.EnrichSummaryZhAsync(CreateParsed(), "raw content");

        Assert.Null(result);
        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task EnrichSummaryZhAsync_NativeGeminiEndpoint_SendsGoogApiKeyAndStripsCodeFence()
    {
        string? capturedKeyHeader = null;
        string? capturedUrl = null;
        var handler = new StubHttpHandler(req =>
        {
            capturedUrl = req.RequestUri!.ToString();
            if (req.Headers.TryGetValues("x-goog-api-key", out var values))
            {
                capturedKeyHeader = values.FirstOrDefault();
            }

            const string payload = """
                {
                  "candidates": [
                    {
                      "content": {
                        "parts": [
                          { "text": "```markdown\n這是一個架構師元件，用於設計系統架構。\n```" }
                        ]
                      }
                    }
                  ]
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        });

        var service = new LlmEnrichmentService(
            new HttpClient(handler),
            Options.Create(new LlmEnrichmentOptions
            {
                Enabled = true,
                ApiKey = "gemini-test-key",
                Endpoint = "https://generativelanguage.googleapis.com/v1beta",
                Model = "gemini-2.5-flash"
            }),
            NullLogger<LlmEnrichmentService>.Instance);

        string? result = await service.EnrichSummaryZhAsync(CreateParsed(), "raw content");

        Assert.NotNull(capturedUrl);
        Assert.Contains("generativelanguage.googleapis.com", capturedUrl);
        Assert.Equal("gemini-test-key", capturedKeyHeader);
        Assert.Equal("這是一個架構師元件，用於設計系統架構。", result);
    }

    [Theory]
    [InlineData("http://:::invalid-endpoint")]
    [InlineData("not-a-valid-uri")]
    public async Task EnrichSummaryZhAsync_InvalidEndpoint_GracefullyReturnsNull(string invalidEndpoint)
    {
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("Should not be called."));
        var service = new LlmEnrichmentService(
            new HttpClient(handler),
            Options.Create(new LlmEnrichmentOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                Endpoint = invalidEndpoint
            }),
            NullLogger<LlmEnrichmentService>.Instance);

        string? result = await service.EnrichSummaryZhAsync(CreateParsed(), "raw content");

        Assert.Null(result);
    }

    [Theory]
    [InlineData("```json\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("```\nhello\n```", "hello")]
    [InlineData("  plain text  ", "plain text")]
    public void StripCodeFence_RemovesMarkdownFences(string input, string expected)
    {
        Assert.Equal(expected, LlmClientHelper.StripCodeFence(input));
    }

    private static ParsedEccComponent CreateParsed() => new(
        ComponentType.Agent,
        "architect",
        "agents/architect.md",
        "System architect agent",
        "架構師",
        "SA",
        "dotnet",
        "mvc",
        "Use for architecture");

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
