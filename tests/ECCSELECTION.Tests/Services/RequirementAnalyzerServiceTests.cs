using System.Net;
using System.Text;
using ECCSELECTION.Web.Options;
using ECCSELECTION.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECCSELECTION.Tests.Services;

public class RequirementAnalyzerServiceTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }

    private readonly RequirementAnalyzerService _service = new();

    [Fact]
    public async Task AnalyzeAsync_WithMarkdownDocumentAndPurpose_ExtractsTechStacksKeywordsAndLifecycle()
    {
        var markdown = """
            # 既有系統重構需求書
            本專案為 legacy 舊系統重構，使用 Python FastAPI 與 PostgreSQL，
            需要嚴格的 security 稽核與 pytest 單元測試覆蓋率。
            """;
        var bytes = Encoding.UTF8.GetBytes(markdown);
        await using var stream = new MemoryStream(bytes);
        IFormFile file = new FormFile(stream, 0, bytes.Length, "requirementDocument", "prd.md");

        var result = await _service.AnalyzeAsync("建立高並發微服務 API", file);

        Assert.True(result.IsSuccess);
        Assert.Equal("prd.md", result.SourceFileName);
        Assert.Equal("API", result.SuggestedProjectType);
        Assert.Equal("Brownfield", result.SuggestedProjectLifecycle);
        Assert.Contains("python", result.SuggestedTechStacks);
        Assert.Contains("postgres", result.SuggestedTechStacks);
        Assert.Contains("security", result.ExtractedKeywords);
        Assert.Contains("refactor", result.ExtractedKeywords);
        Assert.Equal("Python 3.12 / FastAPI / pytest", result.SuggestedTargetEnvironment);
        Assert.Equal("pytest", result.SuggestedTestCommand);
        Assert.Equal("ruff check .", result.SuggestedFormatCommand);
    }

    [Fact]
    public async Task AnalyzeAsync_WithDisallowedExtension_ReturnsFailure()
    {
        var bytes = Encoding.UTF8.GetBytes("binary-placeholder");
        await using var stream = new MemoryStream(bytes);
        IFormFile file = new FormFile(stream, 0, bytes.Length, "requirementDocument", "malware.exe");

        var result = await _service.AnalyzeAsync(null, file);

        Assert.False(result.IsSuccess);
        Assert.Contains("不支援的需求文件格式", result.ErrorMessage);
    }

    [Fact]
    public async Task AnalyzeAsync_WithBinaryNullBytes_ReturnsFailure()
    {
        var bytes = new byte[] { 0x48, 0x65, 0x00, 0x6C, 0x6F };
        await using var stream = new MemoryStream(bytes);
        IFormFile file = new FormFile(stream, 0, bytes.Length, "requirementDocument", "spec.txt");

        var result = await _service.AnalyzeAsync(null, file);

        Assert.False(result.IsSuccess);
        Assert.Contains("二進位", result.ErrorMessage);
    }

    [Fact]
    public async Task AnalyzeAsync_WithLlmEnabled_MergesStructuredLlmInsights()
    {
        var llmJson = """
            {
              "choices": [
                {
                  "message": {
                    "content": "{\"projectType\":\"API\",\"projectLifecycle\":\"Greenfield\",\"archStyle\":\"Clean\",\"techStacks\":[\"dotnet\",\"postgres\"],\"sdlcRoles\":[\"SA\",\"PG_BE\",\"QA\"],\"keywords\":[\"idempotency\",\"rate-limit\"],\"suggestedProjectName\":\"PayCoreApi\",\"suggestedTargetEnvironment\":\".NET 10 / PostgreSQL 16\",\"suggestedBuildCommand\":\"dotnet build PayCore.sln -warnaserror\",\"suggestedFormatCommand\":\"dotnet format --verify-no-changes\",\"suggestedTestCommand\":\"dotnet test --no-build\",\"suggestedSecurityScanCommand\":\"npx --yes ecc-agentshield scan --path . --format text\",\"summaryNote\":\"建議採用 Clean Architecture 與幂等性設計。\"}"
                  }
                }
              ]
            }
            """;

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(llmJson, Encoding.UTF8, "application/json")
        });

        var service = new RequirementAnalyzerService(
            new HttpClient(handler),
            Options.Create(new LlmEnrichmentOptions
            {
                Enabled = true,
                Endpoint = "https://api.example.com/v1",
                ApiKey = "test-key",
                Model = "gpt-4o-mini"
            }),
            NullLogger<RequirementAnalyzerService>.Instance);

        var result = await service.AnalyzeAsync("建立高並發金流 API", null);

        Assert.True(result.IsSuccess);
        Assert.Equal("LLM + Deterministic", result.AnalysisSource);
        Assert.Equal("PayCoreApi", result.SuggestedProjectName);
        Assert.Equal(".NET 10 / PostgreSQL 16", result.SuggestedTargetEnvironment);
        Assert.Equal("dotnet build PayCore.sln -warnaserror", result.SuggestedBuildCommand);
        Assert.Equal("API", result.SuggestedProjectType);
        Assert.Contains("idempotency", result.ExtractedKeywords);
        Assert.Contains("rate-limit", result.ExtractedKeywords);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenLlmFails_FallsBackGracefullyToDeterministic()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var service = new RequirementAnalyzerService(
            new HttpClient(handler),
            Options.Create(new LlmEnrichmentOptions
            {
                Enabled = true,
                Endpoint = "https://api.example.com/v1",
                ApiKey = "test-key"
            }),
            NullLogger<RequirementAnalyzerService>.Instance);

        var result = await service.AnalyzeAsync("建立 .NET API 與 PostgreSQL 安全稽核服務", null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Deterministic (LLM Fallback)", result.AnalysisSource);
        Assert.Contains("dotnet", result.SuggestedTechStacks);
    }
}
