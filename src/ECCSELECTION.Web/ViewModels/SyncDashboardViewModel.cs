using ECCSELECTION.Web.Models;

namespace ECCSELECTION.Web.ViewModels;

public class SyncDashboardViewModel
{
    public int TotalComponents { get; set; }
    public int ActiveComponents { get; set; }
    public int AgentCount { get; set; }
    public int SkillCount { get; set; }
    public int RuleCount { get; set; }
    public int CommandCount { get; set; }

    public SyncLog? LatestSync { get; set; }
    public List<SyncLog> RecentLogs { get; set; } = new();

    public bool HasGitHubToken { get; set; }
    public bool LlmEnabled { get; set; }
    public string GitHubRepoFullName { get; set; } = "affaan-m/ECC";
}
