using ECCSELECTION.Web.Options;
using ECCSELECTION.Web.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECCSELECTION.Web.Services;

public class LlmEnrichmentService : ILlmEnrichmentService
{
    private readonly HttpClient _httpClient;
    private readonly LlmEnrichmentOptions _options;
    private readonly ILogger<LlmEnrichmentService> _logger;

    public LlmEnrichmentService(
        HttpClient httpClient,
        IOptions<LlmEnrichmentOptions> options,
        ILogger<LlmEnrichmentService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> EnrichSummaryZhAsync(
        ParsedEccComponent parsed,
        string rawContent,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return null;
        }

        try
        {
            var snippet = rawContent.Length > 1500 ? rawContent[..1500] : rawContent;
            const string systemPrompt =
                "你是資深系統架構師。請以繁體中文用 1-2 句簡潔說明此 ECC 開發規範元件的核心用途與適用時機，勿輸出多餘前言。";
            var userPrompt =
                $"元件類型: {parsed.ComponentType}\n名稱: {parsed.Name}\n英文描述: {parsed.DescriptionEn}\n內容片段:\n{snippet}";

            var content = await LlmClientHelper.SendPromptAsync(
                _httpClient,
                _options,
                systemPrompt,
                userPrompt,
                temperature: 0.2,
                maxTokens: 200,
                responseMimeType: null,
                TimeSpan.FromSeconds(15),
                _logger,
                cancellationToken);

            var enriched = content is null ? null : LlmClientHelper.StripCodeFence(content).Trim();
            if (string.IsNullOrWhiteSpace(enriched))
            {
                _logger.LogWarning("LLM enrichment returned empty or invalid payload for {Path}. Falling back to deterministic summary.", parsed.Path);
                return null;
            }

            return enriched;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "LLM enrichment timed out after 15s for {Path}. Falling back to deterministic summary.", parsed.Path);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "LLM enrichment request error for {Path}. Falling back to deterministic summary.", parsed.Path);
            return null;
        }
    }
}
