using ECCSELECTION.Web.Data;
using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Services.Interfaces;
using ECCSELECTION.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace ECCSELECTION.Web.Services;

public class RecommendationService : IRecommendationService
{
    private readonly AppDbContext _dbContext;

    private static readonly string[] GreenfieldKeywords =
    [
        "architect", "plan", "planner", "tdd", "gan-planner", "gan-generator",
        "database-reviewer", "api-design", "scaffold", "greenfield", "spec"
    ];

    private static readonly string[] BrownfieldKeywords =
    [
        "explore", "code-explorer", "spec-miner", "refactor", "cleaner",
        "silent-failure", "harness-optimizer", "review", "security", "audit",
        "e2e", "coverage", "legacy", "migration", "brownfield"
    ];

    private static readonly HashSet<string> GenericAgentSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "reviewer", "guide", "architect", "runner", "optimizer", "analyzer",
        "evaluator", "generator", "planner", "resolver", "explorer", "miner",
        "cleaner", "hunter", "updater", "specialist", "operator", "agent"
    };

    private static readonly Dictionary<string, string> RoleDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SA"] = "SA (系統分析)",
        ["SD"] = "SD (系統設計)",
        ["PG_FE"] = "PG-前端",
        ["PG_BE"] = "PG-後端",
        ["PG_DB"] = "PG-資料庫",
        ["PG_TEST"] = "PG-測試",
        ["QA"] = "QA (品保審查)"
    };

    public RecommendationService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WizardResultViewModel> RecommendAsync(
        WizardRequestViewModel request,
        CancellationToken cancellationToken = default)
    {
        var activeComponents = await _dbContext.EccComponents
            .AsNoTracking()
            .Where(c => c.IsActive)
            .ToListAsync(cancellationToken);

        return Recommend(request, activeComponents);
    }

    public WizardResultViewModel Recommend(
        WizardRequestViewModel request,
        IEnumerable<EccComponent> components)
    {
        var activeList = components.Where(c => c.IsActive).ToList();
        var selectedRoles = request.SdlcRoles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selectedStacks = request.TechStacks
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selectedArch = (request.ArchStyle ?? string.Empty).Trim().ToLowerInvariant();
        var selectedProjectType = (request.ProjectType ?? string.Empty).Trim();
        var selectedLifecycle = string.IsNullOrWhiteSpace(request.ProjectLifecycle)
            ? "Greenfield"
            : request.ProjectLifecycle.Trim();
        var isCompact = !string.Equals(request.Mode, "Comprehensive", StringComparison.OrdinalIgnoreCase);
        var minScore = isCompact ? 50 : 40;
        var maxItemsPerRole = isCompact ? 5 : 15;
        var requirementKeywords = (request.RequirementKeywords ?? [])
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new WizardResultViewModel
        {
            Request = request
        };

        foreach (var role in selectedRoles)
        {
            var roleDisplay = GetRoleDisplayName(role);
            var scoredItems = new List<RecommendedComponentItem>();

            foreach (var component in activeList)
            {
                var evaluated = EvaluateForRole(
                    component,
                    role,
                    roleDisplay,
                    selectedStacks,
                    selectedArch,
                    selectedProjectType,
                    selectedLifecycle,
                    requirementKeywords);
                if (evaluated is not null && evaluated.Score >= minScore)
                {
                    scoredItems.Add(evaluated);
                }
            }

            var ordered = scoredItems
                .OrderByDescending(i => i.Score)
                .ThenBy(i => GetTypePriority(i.ComponentType))
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Take(maxItemsPerRole)
                .ToList();

            result.Groups.Add(new RoleRecommendationGroup
            {
                Role = role,
                RoleDisplayName = roleDisplay,
                Items = ordered
            });
        }

        result.Suites = BuildAgentSuites(
            activeList,
            selectedRoles,
            selectedStacks,
            selectedArch,
            selectedProjectType,
            selectedLifecycle,
            requirementKeywords,
            isCompact,
            minScore);

        var suiteIds = result.Suites
            .SelectMany(s => s.AllItems)
            .Select(i => i.ComponentId)
            .ToHashSet();

        result.AdditionalCandidates = result.Groups
            .SelectMany(g => g.Items)
            .Where(i => !suiteIds.Contains(i.ComponentId))
            .GroupBy(i => i.ComponentId)
            .Select(g => g.OrderByDescending(x => x.Score).First())
            .OrderBy(i => GetTypePriority(i.ComponentType))
            .ThenByDescending(i => i.Score)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();

        return result;
    }

    private static RecommendedComponentItem? EvaluateForRole(
        EccComponent component,
        string targetRole,
        string roleDisplay,
        HashSet<string> selectedStacks,
        string selectedArch,
        string selectedProjectType,
        string selectedLifecycle,
        IReadOnlyList<string> requirementKeywords)
    {
        var componentRoles = component.GetSdlcRoleTokens();
        if (!componentRoles.Contains(targetRole, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var componentStacks = component.GetTechStackTokens();
        if (!IsTechStackCompatible(componentStacks, selectedStacks, out var matchedStacks, out var hasCommon))
        {
            return null;
        }

        var score = 40;
        var reasons = new List<string>
        {
            $"符合角色定位：{roleDisplay} (+40)"
        };

        if (matchedStacks.Count > 0)
        {
            var stackBonus = Math.Min(matchedStacks.Count * 25, 50);
            score += stackBonus;
            reasons.Add($"命中技術棧：{string.Join(", ", matchedStacks)} (+{stackBonus})");
        }

        if (hasCommon)
        {
            score += 15;
            reasons.Add("通用工程規範（適用跨技術棧） (+15)");
        }

        var componentArches = component.GetArchStyleTokens();
        if (!string.IsNullOrEmpty(selectedArch) && componentArches.Contains(selectedArch, StringComparer.OrdinalIgnoreCase))
        {
            score += 15;
            reasons.Add($"符合架構風格：{selectedArch.ToUpperInvariant()} (+15)");
        }
        else if (componentArches.Contains("general", StringComparer.OrdinalIgnoreCase))
        {
            score += 5;
            reasons.Add("通用架構規範 (+5)");
        }

        var projectBonus = ComputeProjectTypeBonus(
            selectedProjectType,
            targetRole,
            component,
            selectedStacks,
            componentStacks,
            componentArches,
            out var projectReason);

        if (projectBonus > 0 && projectReason is not null)
        {
            score += projectBonus;
            reasons.Add($"{projectReason} (+{projectBonus})");
        }

        var lifecycleBonus = ComputeLifecycleBonus(selectedLifecycle, component, out var lifecycleReason);
        if (lifecycleBonus > 0 && lifecycleReason is not null)
        {
            score += lifecycleBonus;
            reasons.Add($"{lifecycleReason} (+{lifecycleBonus})");
        }

        var keywordBonus = ComputeRequirementKeywordBonus(component, requirementKeywords, out var matchedKeywords);
        if (keywordBonus > 0)
        {
            score += keywordBonus;
            reasons.Add($"命中需求關鍵字：{string.Join(", ", matchedKeywords)} (+{keywordBonus})");
        }

        return ToRecommendedItem(component, targetRole, roleDisplay, score, reasons);
    }

    private static int ComputeRequirementKeywordBonus(
        EccComponent component,
        IReadOnlyList<string> requirementKeywords,
        out List<string> matchedKeywords)
    {
        matchedKeywords = new List<string>();
        if (requirementKeywords.Count == 0)
        {
            return 0;
        }

        var searchable = $"{component.Name} {component.DescriptionEn} {component.SummaryZh} {component.UsageGuide} {component.TechStacks}"
            .ToLowerInvariant();

        foreach (var kw in requirementKeywords)
        {
            if (searchable.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                matchedKeywords.Add(kw);
            }
        }

        return Math.Min(matchedKeywords.Count * 8, 32);
    }

    private static bool IsTechStackCompatible(
        IReadOnlyList<string> componentStacks,
        HashSet<string> selectedStacks,
        out List<string> matchedStacks,
        out bool hasCommon)
    {
        hasCommon = componentStacks.Contains("common", StringComparer.OrdinalIgnoreCase);
        var specificStacks = componentStacks
            .Where(s => !string.Equals(s, "common", StringComparison.OrdinalIgnoreCase))
            .ToList();

        matchedStacks = specificStacks
            .Where(s => selectedStacks.Contains(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 衝突排除：元件明確屬於特定語言/技術棧（不含 common），但使用者完全未選中該技術棧
        return hasCommon || specificStacks.Count == 0 || matchedStacks.Count > 0;
    }

    private static RecommendedComponentItem ToRecommendedItem(
        EccComponent component,
        string role,
        string roleDisplay,
        int score,
        List<string> reasons) => new()
        {
            ComponentId = component.Id,
            Role = role,
            RoleDisplayName = roleDisplay,
            ComponentType = component.ComponentType,
            Name = component.Name,
            Path = component.Path,
            RawUrl = component.RawUrl,
            HtmlUrl = component.HtmlUrl,
            LocalSuggestedPath = BuildLocalSuggestedPath(component.ComponentType, component.Name, component.Path),
            Score = score,
            MatchReasons = reasons,
            UsageGuide = string.IsNullOrWhiteSpace(component.UsageGuide) ? component.SummaryZh : component.UsageGuide,
            SummaryZh = component.SummaryZh,
            DescriptionEn = component.DescriptionEn,
            TechStacks = component.TechStacks,
            ArchStyles = component.ArchStyles
        };

    private static List<HarnessSuiteViewModel> BuildAgentSuites(
        List<EccComponent> activeList,
        List<string> selectedRoles,
        HashSet<string> selectedStacks,
        string selectedArch,
        string selectedProjectType,
        string selectedLifecycle,
        IReadOnlyList<string> requirementKeywords,
        bool isCompact,
        int minScore)
    {
        var maxSuites = isCompact ? 3 : 6;
        var topAgents = SelectTopAgents(
            activeList,
            selectedRoles,
            selectedStacks,
            selectedArch,
            selectedProjectType,
            selectedLifecycle,
            requirementKeywords,
            minScore,
            maxSuites);

        var nonAgents = activeList.Where(c => c.ComponentType != ComponentType.Agent).ToList();
        var suites = new List<HarnessSuiteViewModel>();

        foreach (var (agent, leadItem, coveredRoles) in topAgents)
        {
            var pairedCandidates = EvaluateSuitePairings(
                agent,
                leadItem,
                nonAgents,
                selectedStacks,
                selectedArch,
                selectedProjectType,
                selectedLifecycle,
                isCompact);

            suites.Add(BuildSuiteViewModel(agent.Id, leadItem, coveredRoles, pairedCandidates, isCompact));
        }

        return suites;
    }

    private static List<(EccComponent Agent, RecommendedComponentItem Item, HashSet<string> Roles)> SelectTopAgents(
        List<EccComponent> activeList,
        List<string> selectedRoles,
        HashSet<string> selectedStacks,
        string selectedArch,
        string selectedProjectType,
        string selectedLifecycle,
        IReadOnlyList<string> requirementKeywords,
        int minScore,
        int maxSuites)
    {
        var agents = activeList.Where(c => c.ComponentType == ComponentType.Agent).ToList();
        var bestByAgentId = new Dictionary<int, (EccComponent Agent, RecommendedComponentItem Item, HashSet<string> Roles)>();

        foreach (var role in selectedRoles)
        {
            var roleDisplay = GetRoleDisplayName(role);
            foreach (var agent in agents)
            {
                var evaluated = EvaluateForRole(
                    agent,
                    role,
                    roleDisplay,
                    selectedStacks,
                    selectedArch,
                    selectedProjectType,
                    selectedLifecycle,
                    requirementKeywords);
                if (evaluated is null || evaluated.Score < minScore)
                {
                    continue;
                }

                if (!bestByAgentId.TryGetValue(agent.Id, out var existing))
                {
                    bestByAgentId[agent.Id] = (agent, evaluated, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { roleDisplay });
                }
                else
                {
                    existing.Roles.Add(roleDisplay);
                    if (evaluated.Score > existing.Item.Score)
                    {
                        bestByAgentId[agent.Id] = (agent, evaluated, existing.Roles);
                    }
                }
            }
        }

        return bestByAgentId.Values
            .OrderByDescending(x => x.Item.Score)
            .ThenBy(x => x.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxSuites)
            .ToList();
    }

    private static List<RecommendedComponentItem> EvaluateSuitePairings(
        EccComponent agent,
        RecommendedComponentItem leadItem,
        List<EccComponent> nonAgents,
        HashSet<string> selectedStacks,
        string selectedArch,
        string selectedProjectType,
        string selectedLifecycle,
        bool isCompact)
    {
        var minSkillOrCommandScore = isCompact ? 40 : 30;
        const int minRuleScore = 30;
        var agentTokens = ExtractCoreTokens(agent.Name);
        var agentRoles = agent.GetSdlcRoleTokens();
        var pairedCandidates = new List<RecommendedComponentItem>();

        foreach (var candidate in nonAgents)
        {
            var candStacks = candidate.GetTechStackTokens();
            if (!IsTechStackCompatible(candStacks, selectedStacks, out var matchedStacks, out var hasCommon))
            {
                continue;
            }

            var pairScore = 0;
            var pairReasons = new List<string>();

            var candRoles = candidate.GetSdlcRoleTokens();
            var overlapRoles = agentRoles.Intersect(candRoles, StringComparer.OrdinalIgnoreCase).ToList();
            if (overlapRoles.Count > 0)
            {
                var roleBonus = Math.Min(overlapRoles.Count * 15, 35);
                pairScore += roleBonus;
                pairReasons.Add($"與 {agent.Name} 共同涵蓋角色（{string.Join(", ", overlapRoles)}）(+{roleBonus})");
            }

            if (matchedStacks.Count > 0)
            {
                var stackBonus = Math.Min(matchedStacks.Count * 20, 40);
                pairScore += stackBonus;
                pairReasons.Add($"符合專案技術棧：{string.Join(", ", matchedStacks)} (+{stackBonus})");
            }
            else if (hasCommon)
            {
                pairScore += 15;
                pairReasons.Add("通用跨技術棧規範 (+15)");
            }

            var isGameContext = string.Equals(selectedProjectType, "Game", StringComparison.OrdinalIgnoreCase) ||
                selectedStacks.Contains("luau") ||
                selectedStacks.Contains("godot") ||
                selectedStacks.Contains("cpp");

            if (isGameContext &&
                !string.IsNullOrEmpty(selectedArch) &&
                candidate.GetArchStyleTokens().Contains(selectedArch, StringComparer.OrdinalIgnoreCase))
            {
                pairScore += 15;
                pairReasons.Add($"符合專案架構：{selectedArch.ToUpperInvariant()} (+15)");
            }

            if (isGameContext &&
                selectedStacks.Contains("luau") &&
                (candStacks.Contains("luau", StringComparer.OrdinalIgnoreCase) ||
                 candidate.Name.Contains("roblox", StringComparison.OrdinalIgnoreCase) ||
                 candidate.Name.Contains("luau", StringComparison.OrdinalIgnoreCase)))
            {
                pairScore += 15;
                pairReasons.Add("符合 Roblox / Luau 遊戲開發套件 (+15)");
            }

            var matchedTokens = agentTokens
                .Where(t => candidate.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                            candidate.Path.Contains(t, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchedTokens.Count > 0)
            {
                pairScore += 35;
                pairReasons.Add($"與 {agent.Name} 領域高度互補：{string.Join(", ", matchedTokens)} (+35)");
            }
            else
            {
                var descTokens = agentTokens
                    .Where(t => candidate.DescriptionEn.Contains(t, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (descTokens.Count > 0)
                {
                    pairScore += 20;
                    pairReasons.Add($"功能描述與 {agent.Name} 互補：{string.Join(", ", descTokens)} (+20)");
                }
            }

            var lifecycleBonus = ComputeLifecycleBonus(selectedLifecycle, candidate, out var lifecycleReason);
            if (lifecycleBonus > 0 && lifecycleReason is not null)
            {
                pairScore += 15;
                pairReasons.Add($"{lifecycleReason} (+15)");
            }

            var requiredPairScore = candidate.ComponentType == ComponentType.Rule
                ? minRuleScore
                : minSkillOrCommandScore;

            if (pairScore >= requiredPairScore)
            {
                var roleLabel = overlapRoles.FirstOrDefault() ?? leadItem.Role;
                pairedCandidates.Add(ToRecommendedItem(
                    candidate,
                    roleLabel,
                    GetRoleDisplayName(roleLabel),
                    pairScore,
                    pairReasons));
            }
        }

        return pairedCandidates;
    }

    private static HarnessSuiteViewModel BuildSuiteViewModel(
        int agentId,
        RecommendedComponentItem leadItem,
        HashSet<string> coveredRoles,
        List<RecommendedComponentItem> pairedCandidates,
        bool isCompact)
    {
        var maxSkills = isCompact ? 2 : 3;
        var maxRules = isCompact ? 2 : 3;
        var maxCommands = isCompact ? 1 : 2;

        return new HarnessSuiteViewModel
        {
            SuiteId = $"suite-{agentId}",
            LeadAgent = leadItem,
            CoveredRoles = coveredRoles.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList(),
            PairedSkills = TakeTopPairedByType(pairedCandidates, ComponentType.Skill, maxSkills),
            PairedRules = TakeTopPairedByType(pairedCandidates, ComponentType.Rule, maxRules),
            PairedCommands = TakeTopPairedByType(pairedCandidates, ComponentType.Command, maxCommands)
        };
    }

    private static List<RecommendedComponentItem> TakeTopPairedByType(
        List<RecommendedComponentItem> pairedCandidates,
        ComponentType type,
        int limit) =>
        pairedCandidates
            .Where(c => c.ComponentType == type)
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();

    private static List<string> ExtractCoreTokens(string agentName)
    {
        return agentName
            .Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3 && !GenericAgentSuffixes.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ComputeLifecycleBonus(string lifecycle, EccComponent component, out string? reason)
    {
        reason = null;
        var text = $"{component.Name} {component.Path} {component.DescriptionEn}";

        if (string.Equals(lifecycle, "Brownfield", StringComparison.OrdinalIgnoreCase))
        {
            if (BrownfieldKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                reason = "適用舊專案導入與重構（Brownfield）";
                return 20;
            }
            return 0;
        }

        if (GreenfieldKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            reason = "適用新專案從零建置（Greenfield）";
            return 20;
        }

        return 0;
    }

    private static int ComputeProjectTypeBonus(
        string projectType,
        string targetRole,
        EccComponent component,
        HashSet<string> selectedStacks,
        IReadOnlyList<string> componentStacks,
        IReadOnlyList<string> componentArches,
        out string? reason)
    {
        reason = null;

        if (string.Equals(projectType, "Game", StringComparison.OrdinalIgnoreCase))
        {
            var text = $"{component.Name} {component.Path} {component.DescriptionEn}";
            if (selectedStacks.Contains("luau"))
            {
                if (componentStacks.Contains("luau", StringComparer.OrdinalIgnoreCase) ||
                    componentArches.Contains("ecs", StringComparer.OrdinalIgnoreCase) ||
                    text.Contains("roblox", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("luau", StringComparison.OrdinalIgnoreCase))
                {
                    reason = "符合 Roblox / Luau 遊戲開發特性";
                    return 20;
                }

                if (component.Name.StartsWith("gan-", StringComparison.OrdinalIgnoreCase))
                {
                    reason = "符合 Game 遊戲開發型態特性";
                    return 5;
                }

                if (componentStacks.Contains("godot", StringComparer.OrdinalIgnoreCase) ||
                    componentStacks.Contains("cpp", StringComparer.OrdinalIgnoreCase) ||
                    text.Contains("godot", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("gdscript", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("game", StringComparison.OrdinalIgnoreCase))
                {
                    reason = "符合 Game 遊戲開發型態特性";
                    return 10;
                }
            }
            else if (componentStacks.Contains("luau", StringComparer.OrdinalIgnoreCase) ||
                     componentStacks.Contains("godot", StringComparer.OrdinalIgnoreCase) ||
                     componentStacks.Contains("cpp", StringComparer.OrdinalIgnoreCase) ||
                     text.Contains("godot", StringComparison.OrdinalIgnoreCase) ||
                     text.Contains("gdscript", StringComparison.OrdinalIgnoreCase) ||
                     text.Contains("game", StringComparison.OrdinalIgnoreCase) ||
                     text.Contains("roblox", StringComparison.OrdinalIgnoreCase) ||
                     component.Name.StartsWith("gan-", StringComparison.OrdinalIgnoreCase))
            {
                reason = "符合 Game 遊戲開發型態特性";
                return 15;
            }
        }

        if (string.Equals(projectType, "Mobile", StringComparison.OrdinalIgnoreCase) &&
            (componentStacks.Contains("kotlin", StringComparer.OrdinalIgnoreCase) ||
             componentArches.Contains("mvvm", StringComparer.OrdinalIgnoreCase)))
        {
            reason = "符合 Mobile 專案型態特性";
            return 10;
        }

        if (string.Equals(projectType, "API", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(targetRole, "PG_BE", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(targetRole, "PG_DB", StringComparison.OrdinalIgnoreCase)))
        {
            reason = "符合 API 後端與資料庫核心角色";
            return 10;
        }

        if ((string.Equals(projectType, "Web", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(projectType, "Fullstack", StringComparison.OrdinalIgnoreCase)) &&
            (componentStacks.Contains("react", StringComparer.OrdinalIgnoreCase) ||
             componentStacks.Contains("vue", StringComparer.OrdinalIgnoreCase) ||
             componentArches.Contains("mvc", StringComparer.OrdinalIgnoreCase)))
        {
            reason = $"符合 {projectType} 專案型態特性";
            return 10;
        }

        return 0;
    }

    private static int GetTypePriority(ComponentType type) => type switch
    {
        ComponentType.Agent => 0,
        ComponentType.Rule => 1,
        ComponentType.Skill => 2,
        ComponentType.Command => 3,
        _ => 4
    };

    private static string BuildLocalSuggestedPath(ComponentType type, string name, string repoPath)
    {
        return type switch
        {
            ComponentType.Agent => $".claude/agents/{name}.md",
            ComponentType.Skill => $".claude/skills/{name}/SKILL.md",
            ComponentType.Command => $".claude/commands/{name}.md",
            ComponentType.Rule => $".claude/{repoPath.TrimStart('/')}",
            _ => $".claude/{repoPath.TrimStart('/')}"
        };
    }

    public static string GetRoleDisplayName(string role)
    {
        return RoleDisplayNames.TryGetValue(role, out var display) ? display : role;
    }
}
