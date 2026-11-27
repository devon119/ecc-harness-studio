using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ECCSELECTION.Web.Data;
using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Options;
using ECCSELECTION.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECCSELECTION.Web.Services;

public class GitHubSyncService : IGitHubSyncService
{
    private readonly AppDbContext _dbContext;
    private readonly HttpClient _httpClient;
    private readonly IEccParserService _parserService;
    private readonly ILlmEnrichmentService _llmService;
    private readonly GitHubSyncOptions _options;
    private readonly ILogger<GitHubSyncService> _logger;

    private static readonly (Regex Pattern, ComponentType Type)[] ComponentPathRules =
    {
        (new Regex(@"^agents/[^/]+\.md$", RegexOptions.Compiled | RegexOptions.IgnoreCase), ComponentType.Agent),
        (new Regex(@"^skills/[^/]+/SKILL\.md$", RegexOptions.Compiled | RegexOptions.IgnoreCase), ComponentType.Skill),
        (new Regex(@"^rules/[^/]+/[^/]+\.md$", RegexOptions.Compiled | RegexOptions.IgnoreCase), ComponentType.Rule),
        (new Regex(@"^commands/[^/]+\.md$", RegexOptions.Compiled | RegexOptions.IgnoreCase), ComponentType.Command)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GitHubSyncService(
        AppDbContext dbContext,
        HttpClient httpClient,
        IEccParserService parserService,
        ILlmEnrichmentService llmService,
        IOptions<GitHubSyncOptions> options,
        ILogger<GitHubSyncService> logger)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
        _parserService = parserService;
        _llmService = llmService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SyncLog> SyncAsync(CancellationToken cancellationToken = default)
    {
        var syncLog = new SyncLog
        {
            StartedAt = DateTime.UtcNow,
            Status = SyncStatus.Running
        };
        _dbContext.SyncLogs.Add(syncLog);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var errors = new List<string>();

        try
        {
            var treeItems = await FetchMatchedTreeItemsAsync(cancellationToken);
            syncLog.TotalFound = treeItems.Count;

            var existingByPath = await _dbContext.EccComponents
                .ToDictionaryAsync(c => c.Path, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.UtcNow;

            foreach (var (item, componentType) in treeItems)
            {
                seenPaths.Add(item.Path);

                if (existingByPath.TryGetValue(item.Path, out var existing))
                {
                    if (string.Equals(existing.Sha, item.Sha, StringComparison.Ordinal))
                    {
                        existing.LastSyncedAt = now;
                        existing.IsActive = true;
                        continue;
                    }

                    var rawContent = await TryFetchRawContentAsync(item.Path, errors, cancellationToken);
                    if (rawContent is null)
                    {
                        continue;
                    }

                    var parsed = _parserService.Parse(componentType, item.Path, rawContent);
                    existing.ComponentType = componentType;
                    existing.Name = parsed.Name;
                    existing.Sha = item.Sha;
                    existing.RawUrl = BuildRawUrl(item.Path);
                    existing.HtmlUrl = BuildHtmlUrl(item.Path);
                    existing.DescriptionEn = parsed.DescriptionEn;
                    existing.LastSyncedAt = now;
                    existing.IsActive = true;

                    if (!existing.IsManuallyEdited)
                    {
                        var llmSummary = await _llmService.EnrichSummaryZhAsync(parsed, rawContent, cancellationToken);
                        existing.SummaryZh = llmSummary ?? parsed.SummaryZh;
                        existing.SdlcRoles = parsed.SdlcRoles;
                        existing.TechStacks = parsed.TechStacks;
                        existing.ArchStyles = parsed.ArchStyles;
                        existing.UsageGuide = parsed.UsageGuide;
                    }

                    syncLog.UpdatedCount++;
                }
                else
                {
                    var rawContent = await TryFetchRawContentAsync(item.Path, errors, cancellationToken);
                    if (rawContent is null)
                    {
                        continue;
                    }

                    var parsed = _parserService.Parse(componentType, item.Path, rawContent);
                    var llmSummary = await _llmService.EnrichSummaryZhAsync(parsed, rawContent, cancellationToken);

                    var entity = new EccComponent
                    {
                        ComponentType = componentType,
                        Name = parsed.Name,
                        Path = item.Path,
                        Sha = item.Sha,
                        RawUrl = BuildRawUrl(item.Path),
                        HtmlUrl = BuildHtmlUrl(item.Path),
                        DescriptionEn = parsed.DescriptionEn,
                        SummaryZh = llmSummary ?? parsed.SummaryZh,
                        SdlcRoles = parsed.SdlcRoles,
                        TechStacks = parsed.TechStacks,
                        ArchStyles = parsed.ArchStyles,
                        UsageGuide = parsed.UsageGuide,
                        LastSyncedAt = now,
                        IsActive = true,
                        IsManuallyEdited = false
                    };

                    _dbContext.EccComponents.Add(entity);
                    existingByPath[item.Path] = entity;
                    syncLog.AddedCount++;
                }
            }

            foreach (var existing in existingByPath.Values)
            {
                if (existing.IsActive &&
                    !existing.Path.StartsWith("builtin/", StringComparison.OrdinalIgnoreCase) &&
                    !seenPaths.Contains(existing.Path))
                {
                    existing.IsActive = false;
                    existing.LastSyncedAt = now;
                    syncLog.RemovedCount++;
                }
            }

            syncLog.CompletedAt = DateTime.UtcNow;
            syncLog.Status = errors.Count > 0 ? SyncStatus.PartialSuccess : SyncStatus.Success;
            if (errors.Count > 0)
            {
                syncLog.ErrorMessage = string.Join(" | ", errors.Take(10));
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return syncLog;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GitHub sync failed for {Owner}/{Repo}@{Branch}.", _options.Owner, _options.Repo, _options.Branch);
            _dbContext.ChangeTracker.Clear();
            syncLog.CompletedAt = DateTime.UtcNow;
            syncLog.Status = SyncStatus.Failed;
            syncLog.ErrorMessage = ex.Message;

            if (syncLog.Id > 0)
            {
                _dbContext.SyncLogs.Update(syncLog);
            }
            else
            {
                _dbContext.SyncLogs.Add(syncLog);
            }

            await _dbContext.SaveChangesAsync(CancellationToken.None);
            return syncLog;
        }
    }

    private async Task<List<(GitTreeItem Item, ComponentType Type)>> FetchMatchedTreeItemsAsync(CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{_options.Owner}/{_options.Repo}/git/trees/{_options.Branch}?recursive=1";
        using var request = CreateGitHubRequest(url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"GitHub Trees API returned {(int)response.StatusCode} ({response.ReasonPhrase}): {body}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var treeResponse = JsonSerializer.Deserialize<GitTreeResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("GitHub Trees API returned empty or invalid JSON.");

        var matched = new List<(GitTreeItem, ComponentType)>();
        foreach (var item in treeResponse.Tree)
        {
            if (!string.Equals(item.Type, "blob", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(item.Path) ||
                string.IsNullOrWhiteSpace(item.Sha))
            {
                continue;
            }

            var componentType = MatchComponentType(item.Path);
            if (componentType.HasValue)
            {
                matched.Add((item, componentType.Value));
            }
        }

        return matched;
    }

    private async Task<string?> TryFetchRawContentAsync(string path, List<string> errors, CancellationToken cancellationToken)
    {
        var rawUrl = BuildRawUrl(path);
        try
        {
            using var request = CreateGitHubRequest(rawUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var msg = $"Raw download failed ({(int)response.StatusCode}) for {path}";
                _logger.LogWarning(msg);
                errors.Add(msg);
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            var msg = $"Raw download timed out for {path}: {ex.Message}";
            _logger.LogWarning(ex, "Raw download timed out for {Path}", path);
            errors.Add(msg);
            return null;
        }
        catch (HttpRequestException ex)
        {
            var msg = $"Raw download error for {path}: {ex.Message}";
            _logger.LogWarning(ex, "Raw download error for {Path}", path);
            errors.Add(msg);
            return null;
        }
    }

    private HttpRequestMessage CreateGitHubRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("ECCSELECTION-Sync/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        if (!string.IsNullOrWhiteSpace(_options.PersonalAccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.PersonalAccessToken);
        }

        return request;
    }

    private static ComponentType? MatchComponentType(string path)
    {
        foreach (var (pattern, type) in ComponentPathRules)
        {
            if (pattern.IsMatch(path))
            {
                return type;
            }
        }

        return null;
    }

    private string BuildRawUrl(string path) =>
        $"https://raw.githubusercontent.com/{_options.Owner}/{_options.Repo}/{_options.Branch}/{path}";

    private string BuildHtmlUrl(string path) =>
        $"https://github.com/{_options.Owner}/{_options.Repo}/blob/{_options.Branch}/{path}";

    private sealed class GitTreeResponse
    {
        [JsonPropertyName("tree")]
        public List<GitTreeItem> Tree { get; set; } = new();
    }

    private sealed class GitTreeItem
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("sha")]
        public string Sha { get; set; } = string.Empty;
    }
}
