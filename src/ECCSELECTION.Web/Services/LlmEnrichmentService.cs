using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
        if (!_options.Enabled ||
            string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            return null;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

            var endpoint = _options.Endpoint.TrimEnd('/');
            if (!endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                endpoint += "/chat/completions";
            }

            var model = string.IsNullOrWhiteSpace(_options.Model) ? "gpt-4o-mini" : _options.Model;
            var snippet = rawContent.Length > 1500 ? rawContent[..1500] : rawContent;

            var payload = new
            {
                model,
                temperature = 0.2,
                max_tokens = 200,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "你是資深系統架構師。請以繁體中文用 1-2 句簡潔說明此 ECC 開發規範元件的核心用途與適用時機，勿輸出多餘前言。"
                    },
                    new
                    {
                        role = "user",
                        content = $"元件類型: {parsed.ComponentType}\n名稱: {parsed.Name}\n英文描述: {parsed.DescriptionEn}\n內容片段:\n{snippet}"
                    }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("LLM enrichment failed with status {StatusCode} for {Path}. Falling back to deterministic summary.",
                    response.StatusCode, parsed.Path);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var contentElement))
            {
                var enriched = contentElement.GetString()?.Trim();
                return string.IsNullOrWhiteSpace(enriched) ? null : enriched;
            }

            _logger.LogWarning("LLM enrichment returned unexpected payload for {Path}. Falling back to deterministic summary.", parsed.Path);
            return null;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "LLM enrichment timed out after 15s for {Path}. Falling back to deterministic summary.", parsed.Path);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "LLM enrichment request error for {Path}. Falling back to deterministic summary.", parsed.Path);
            return null;
        }
    }
}
