using ECCSELECTION.Web.Models.Enums;

namespace ECCSELECTION.Web.ViewModels;

public class WizardResultViewModel
{
    public WizardRequestViewModel Request { get; set; } = new();
    public List<HarnessSuiteViewModel> Suites { get; set; } = new();
    public List<RoleRecommendationGroup> Groups { get; set; } = new();
    public List<RecommendedComponentItem> AdditionalCandidates { get; set; } = new();
    public string CurationSource { get; set; } = "Deterministic";
    public string HarnessPlaybookMarkdown { get; set; } = string.Empty;
    public string ChromeGeminiPrompt { get; set; } = string.Empty;
    public bool IsGeminiApiConfigured { get; set; }
    public int TotalRecommendedCount => Groups.Sum(g => g.Items.Count);
    public int TotalUniqueComponentCount => Suites
        .SelectMany(s => s.AllItems)
        .Select(i => i.ComponentId)
        .Distinct()
        .Count();
}

public class HarnessSuiteViewModel
{
    public string SuiteId { get; set; } = string.Empty;
    public RecommendedComponentItem LeadAgent { get; set; } = new();
    public List<string> CoveredRoles { get; set; } = new();
    public List<RecommendedComponentItem> PairedSkills { get; set; } = new();
    public List<RecommendedComponentItem> PairedRules { get; set; } = new();
    public List<RecommendedComponentItem> PairedCommands { get; set; } = new();

    public IEnumerable<RecommendedComponentItem> AllItems
    {
        get
        {
            yield return LeadAgent;
            foreach (var skill in PairedSkills) yield return skill;
            foreach (var rule in PairedRules) yield return rule;
            foreach (var command in PairedCommands) yield return command;
        }
    }
}

public class RoleRecommendationGroup
{
    public string Role { get; set; } = string.Empty;
    public string RoleDisplayName { get; set; } = string.Empty;
    public List<RecommendedComponentItem> Items { get; set; } = new();
}

public class RecommendedComponentItem
{
    public int ComponentId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string RoleDisplayName { get; set; } = string.Empty;
    public ComponentType ComponentType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string RawUrl { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
    public string LocalSuggestedPath { get; set; } = string.Empty;
    public int Score { get; set; }
    public List<string> MatchReasons { get; set; } = new();
    public string UsageGuide { get; set; } = string.Empty;
    public string SummaryZh { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;
    public string TechStacks { get; set; } = string.Empty;
    public string ArchStyles { get; set; } = string.Empty;
}
