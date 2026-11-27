using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ECCSELECTION.Web.Options;
using Microsoft.Extensions.Logging;

namespace ECCSELECTION.Web.Services;

/// <summary>
/// 共用 LLM HTTP 呼叫與回應解析輔助類別，支援 OpenAI 相容端點與原生 Google Gemini API。
/// </summary>
public static class LlmClientHelper
{
    private const string DefaultGeminiBaseEndpoint = "https://generativelanguage.googleapis.com/v1beta";
    private const string DefaultGeminiModel = "gemini-2.5-flash";
    private const string DefaultOpenAiModel = "gpt-4o-mini";

    public static string StripCodeFence(string text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewline >= 0 && lastFence > firstNewline)
        {
            return trimmed[(firstNewline + 1)..lastFence].Trim();
        }

        return trimmed;
    }

    public static bool IsNativeGeminiEndpoint(string? endpoint)
    {
        var trimmed = (endpoint ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed) ||
               (trimmed.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.Contains("/openai", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<string?> SendPromptAsync(
        HttpClient httpClient,
        LlmEnrichmentOptions options,
        string? systemPrompt,
        string userPrompt,
        double temperature,
        int? maxTokens,
        string? responseMimeType,
        TimeSpan timeout,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return null;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var endpoint = (options.Endpoint ?? string.Empty).Trim();
        if (IsNativeGeminiEndpoint(endpoint))
        {
            return await SendNativeGeminiAsync(
                httpClient,
                options,
                endpoint,
                systemPrompt,
                userPrompt,
                temperature,
                maxTokens,
                responseMimeType,
                logger,
                timeoutCts.Token);
        }

        return await SendOpenAiCompatibleAsync(
            httpClient,
            options,
            endpoint,
            systemPrompt,
            userPrompt,
            temperature,
            maxTokens,
            logger,
            timeoutCts.Token);
    }

    private static async Task<string?> SendNativeGeminiAsync(
        HttpClient httpClient,
        LlmEnrichmentOptions options,
        string endpoint,
        string? systemPrompt,
        string userPrompt,
        double temperature,
        int? maxTokens,
        string? responseMimeType,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var model = string.IsNullOrWhiteSpace(options.Model) ? DefaultGeminiModel : options.Model.Trim();
        var targetUrl = BuildNativeGeminiUrl(endpoint, model, logger);
        if (targetUrl is null)
        {
            return null;
        }

        var generationConfig = new Dictionary<string, object>
        {
            ["temperature"] = temperature
        };
        if (maxTokens.HasValue)
        {
            generationConfig["maxOutputTokens"] = maxTokens.Value;
        }

        if (!string.IsNullOrWhiteSpace(responseMimeType))
        {
            generationConfig["responseMimeType"] = responseMimeType;
        }

        var payload = new Dictionary<string, object>
        {
            ["contents"] = new[]
            {
                new
                {
                    parts = new[] { new { text = userPrompt } }
                }
            },
            ["generationConfig"] = generationConfig
        };

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            payload["systemInstruction"] = new
            {
                parts = new[] { new { text = systemPrompt } }
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, targetUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-goog-api-key", options.ApiKey.Trim());

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Native Gemini API returned {StatusCode}.", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
            candidates.GetArrayLength() > 0 &&
            candidates[0].TryGetProperty("content", out var content) &&
            content.TryGetProperty("parts", out var parts) &&
            parts.GetArrayLength() > 0 &&
            parts[0].TryGetProperty("text", out var textElem))
        {
            return textElem.GetString();
        }

        return null;
    }

    private static async Task<string?> SendOpenAiCompatibleAsync(
        HttpClient httpClient,
        LlmEnrichmentOptions options,
        string endpoint,
        string? systemPrompt,
        string userPrompt,
        double temperature,
        int? maxTokens,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var openAiUrl = endpoint.TrimEnd('/');
        if (!openAiUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            openAiUrl += "/chat/completions";
        }

        if (!TryCreateHttpUri(openAiUrl, logger, out var validatedUri))
        {
            return null;
        }

        var model = string.IsNullOrWhiteSpace(options.Model) ? DefaultOpenAiModel : options.Model.Trim();
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new { role = "system", content = systemPrompt });
        }

        messages.Add(new { role = "user", content = userPrompt });

        var payload = new Dictionary<string, object>
        {
            ["model"] = model,
            ["temperature"] = temperature,
            ["messages"] = messages
        };
        if (maxTokens.HasValue)
        {
            payload["max_tokens"] = maxTokens.Value;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, validatedUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey.Trim());

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("OpenAI-compatible LLM API returned {StatusCode}.", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var msg) &&
            msg.TryGetProperty("content", out var contentEl))
        {
            return contentEl.GetString();
        }

        return null;
    }

    private static Uri? BuildNativeGeminiUrl(string endpoint, string model, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new Uri($"{DefaultGeminiBaseEndpoint}/models/{Uri.EscapeDataString(model)}:generateContent");
        }

        if (!TryCreateHttpUri(endpoint, logger, out var parsedUri) || parsedUri is null)
        {
            return null;
        }

        var trimmed = endpoint.TrimEnd('/');
        if (trimmed.Contains(":generateContent", StringComparison.OrdinalIgnoreCase))
        {
            return parsedUri;
        }

        var escapedModel = Uri.EscapeDataString(model);
        var url = parsedUri.AbsolutePath == "/"
            ? $"{trimmed}/v1beta/models/{escapedModel}:generateContent"
            : trimmed.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
                ? $"{trimmed}/{escapedModel}:generateContent"
                : $"{trimmed}/models/{escapedModel}:generateContent";

        return TryCreateHttpUri(url, logger, out var finalUri) ? finalUri : null;
    }

    private static bool TryCreateHttpUri(string rawUri, ILogger logger, out Uri? uri)
    {
        if (Uri.TryCreate(rawUri, UriKind.Absolute, out uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        logger.LogWarning("Invalid LLM endpoint URI '{Endpoint}'. Falling back to deterministic behavior.", rawUri);
        uri = null;
        return false;
    }
}
