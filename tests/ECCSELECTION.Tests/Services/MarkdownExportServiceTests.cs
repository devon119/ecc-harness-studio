using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Services;
using ECCSELECTION.Web.ViewModels;

namespace ECCSELECTION.Tests.Services;

public class MarkdownExportServiceTests
{
    [Fact]
    public void ExportToMarkdown_IncludesHeaderRoleGroupsTableAndEscapedPipes()
    {
        var exporter = new MarkdownExportService();
        var result = new WizardResultViewModel
        {
            Request = new WizardRequestViewModel
            {
                ProjectType = "Web",
                ArchStyle = "MVC",
                TechStacks = new List<string> { "dotnet", "sqlite" },
                SdlcRoles = new List<string> { "SA", "PG_BE" },
                Mode = "Compact"
            },
            Groups = new List<RoleRecommendationGroup>
            {
                new()
                {
                    Role = "SA",
                    RoleDisplayName = "SA (系統分析)",
                    Items = new List<RecommendedComponentItem>
                    {
                        new()
                        {
                            ComponentId = 1,
                            Role = "SA",
                            RoleDisplayName = "SA (系統分析)",
                            ComponentType = ComponentType.Agent,
                            Name = "architect",
                            RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/agents/architect.md",
                            LocalSuggestedPath = ".claude/agents/architect.md",
                            Score = 85,
                            MatchReasons = new List<string> { "符合角色定位：SA (系統分析) (+40)" },
                            UsageGuide = "系統設計 | 架構規劃時使用"
                        }
                    }
                }
            }
        };

        var markdown = exporter.ExportToMarkdown(result);

        Assert.Contains("# ECC Harness 推薦元件清單", markdown);
        Assert.Contains("dotnet, sqlite", markdown);
        Assert.Contains("## SA (系統分析)", markdown);
        Assert.Contains("| 角色 | 類型 | 名稱 | 分數 | 推薦理由（為何被選中） | 使用方式與觸發時機 | GitHub Raw 下載與本地建議路徑 |", markdown);
        Assert.Contains("[Raw 下載](https://raw.githubusercontent.com/affaan-m/ECC/main/agents/architect.md)", markdown);
        Assert.Contains("`.claude/agents/architect.md`", markdown);
        Assert.Contains("系統設計 \\| 架構規劃時使用", markdown);
    }
}
