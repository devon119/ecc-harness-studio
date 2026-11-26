using ECCSELECTION.Web.Data;
using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Options;
using ECCSELECTION.Web.Services.Interfaces;
using ECCSELECTION.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECCSELECTION.Web.Services;

public class EccCatalogService : IEccCatalogService
{
    private readonly AppDbContext _dbContext;
    private readonly GitHubSyncOptions _gitHubOptions;
    private readonly LlmEnrichmentOptions _llmOptions;

    public EccCatalogService(
        AppDbContext dbContext,
        IOptions<GitHubSyncOptions> gitHubOptions,
        IOptions<LlmEnrichmentOptions> llmOptions)
    {
        _dbContext = dbContext;
        _gitHubOptions = gitHubOptions.Value;
        _llmOptions = llmOptions.Value;
    }

    public async Task<EccCatalogFilterViewModel> QueryAsync(
        EccCatalogFilterViewModel filter,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.EccComponents.AsNoTracking().AsQueryable();

        if (!filter.IncludeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        if (filter.ComponentType.HasValue)
        {
            query = query.Where(c => c.ComponentType == filter.ComponentType.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword.Trim().ToLowerInvariant();
            query = query.Where(c =>
                c.Name.ToLower().Contains(kw) ||
                c.Path.ToLower().Contains(kw) ||
                c.DescriptionEn.ToLower().Contains(kw) ||
                c.SummaryZh.ToLower().Contains(kw));
        }

        var list = await query
            .OrderBy(c => c.ComponentType)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        // 精確 Token 比對（避免 java 誤匹配 javascript）
        if (!string.IsNullOrWhiteSpace(filter.SdlcRole))
        {
            var roleToken = filter.SdlcRole.Trim();
            list = list
                .Where(c => c.GetSdlcRoleTokens().Contains(roleToken, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(filter.TechStack))
        {
            var stackToken = filter.TechStack.Trim();
            list = list
                .Where(c => c.GetTechStackTokens().Contains(stackToken, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        filter.Items = list;
        filter.TotalCount = await _dbContext.EccComponents.CountAsync(cancellationToken);
        filter.ActiveCount = await _dbContext.EccComponents.CountAsync(c => c.IsActive, cancellationToken);

        return filter;
    }

    public async Task<EccComponent?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.EccComponents
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<bool> UpdateManualAsync(
        EccComponentEditViewModel model,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.EccComponents
            .FirstOrDefaultAsync(c => c.Id == model.Id, cancellationToken);

        if (entity is null)
        {
            return false;
        }

        entity.SummaryZh = (model.SummaryZh ?? string.Empty).Trim();
        entity.SdlcRoles = EccComponent.NormalizeTokens(
            (model.SdlcRoles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries),
            upperCase: true);
        entity.TechStacks = EccComponent.NormalizeTokens(
            (model.TechStacks ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries),
            upperCase: false);
        entity.ArchStyles = EccComponent.NormalizeTokens(
            (model.ArchStyles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries),
            upperCase: false);
        entity.UsageGuide = (model.UsageGuide ?? string.Empty).Trim();
        entity.IsManuallyEdited = true;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<SyncDashboardViewModel> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
    {
        var activeQuery = _dbContext.EccComponents.AsNoTracking().Where(c => c.IsActive);

        var total = await _dbContext.EccComponents.CountAsync(cancellationToken);
        var active = await activeQuery.CountAsync(cancellationToken);
        var agents = await activeQuery.CountAsync(c => c.ComponentType == ComponentType.Agent, cancellationToken);
        var skills = await activeQuery.CountAsync(c => c.ComponentType == ComponentType.Skill, cancellationToken);
        var rules = await activeQuery.CountAsync(c => c.ComponentType == ComponentType.Rule, cancellationToken);
        var commands = await activeQuery.CountAsync(c => c.ComponentType == ComponentType.Command, cancellationToken);

        var recentLogs = await _dbContext.SyncLogs
            .AsNoTracking()
            .OrderByDescending(l => l.StartedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        return new SyncDashboardViewModel
        {
            TotalComponents = total,
            ActiveComponents = active,
            AgentCount = agents,
            SkillCount = skills,
            RuleCount = rules,
            CommandCount = commands,
            LatestSync = recentLogs.FirstOrDefault(),
            RecentLogs = recentLogs,
            HasGitHubToken = !string.IsNullOrWhiteSpace(_gitHubOptions.PersonalAccessToken),
            LlmEnabled = _llmOptions.Enabled && !string.IsNullOrWhiteSpace(_llmOptions.ApiKey),
            GitHubRepoFullName = $"{_gitHubOptions.Owner}/{_gitHubOptions.Repo}"
        };
    }
}
