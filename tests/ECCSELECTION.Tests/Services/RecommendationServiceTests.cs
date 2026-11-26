using ECCSELECTION.Tests.Helpers;
using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Services;
using ECCSELECTION.Web.ViewModels;

namespace ECCSELECTION.Tests.Services;

public class RecommendationServiceTests
{
    [Fact]
    public void Recommend_DotNetMvcRequest_ExcludesPythonAndGoAndIncludesReasons()
    {
        using var dbScope = new TestDbContextScope();
        var service = new RecommendationService(dbScope.Context);

        var components = new List<EccComponent>
        {
            new()
            {
                Id = 1,
                ComponentType = ComponentType.Agent,
                Name = "dotnet-architect",
                Path = "agents/dotnet-architect.md",
                RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/agents/dotnet-architect.md",
                HtmlUrl = "https://github.com/affaan-m/ECC/blob/main/agents/dotnet-architect.md",
                SdlcRoles = "SA,SD,PG_BE",
                TechStacks = "dotnet",
                ArchStyles = "mvc,clean",
                SummaryZh = ".NET 架構代理",
                UsageGuide = "用於系統架構與 MVC API 設計",
                IsActive = true
            },
            new()
            {
                Id = 2,
                ComponentType = ComponentType.Rule,
                Name = "python-style",
                Path = "rules/python/coding-style.md",
                RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/rules/python/coding-style.md",
                HtmlUrl = "https://github.com/affaan-m/ECC/blob/main/rules/python/coding-style.md",
                SdlcRoles = "PG_BE",
                TechStacks = "python",
                ArchStyles = "general",
                SummaryZh = "Python 規範",
                UsageGuide = "Python 開發規範",
                IsActive = true
            },
            new()
            {
                Id = 3,
                ComponentType = ComponentType.Rule,
                Name = "common-security",
                Path = "rules/common/security.md",
                RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/rules/common/security.md",
                HtmlUrl = "https://github.com/affaan-m/ECC/blob/main/rules/common/security.md",
                SdlcRoles = "SA,PG_BE,PG_DB",
                TechStacks = "common",
                ArchStyles = "general",
                SummaryZh = "通用安全規範",
                UsageGuide = "跨語言安全性檢查",
                IsActive = true
            }
        };

        var request = new WizardRequestViewModel
        {
            ProjectType = "Web",
            ArchStyle = "MVC",
            TechStacks = new List<string> { "dotnet" },
            SdlcRoles = new List<string> { "SA", "PG_BE", "PG_DB" },
            Mode = "Compact"
        };

        var result = service.Recommend(request, components);

        var beGroup = result.Groups.Single(g => g.Role == "PG_BE");
        Assert.Contains(beGroup.Items, i => i.Name == "dotnet-architect");
        Assert.Contains(beGroup.Items, i => i.Name == "common-security");
        Assert.DoesNotContain(beGroup.Items, i => i.Name == "python-style");

        var archItem = beGroup.Items.Single(i => i.Name == "dotnet-architect");
        Assert.True(archItem.Score >= 80);
        Assert.NotEmpty(archItem.MatchReasons);
        Assert.Contains(archItem.MatchReasons, r => r.Contains("dotnet"));
        Assert.Contains(archItem.MatchReasons, r => r.Contains("MVC"));
    }

    [Fact]
    public void Recommend_CompactVsComprehensive_RespectsCountAndScoreThresholds()
    {
        using var dbScope = new TestDbContextScope();
        var service = new RecommendationService(dbScope.Context);

        var components = Enumerable.Range(1, 10)
            .Select(i => new EccComponent
            {
                Id = i,
                ComponentType = ComponentType.Rule,
                Name = $"common-rule-{i:D2}",
                Path = $"rules/common/rule-{i:D2}.md",
                RawUrl = $"https://raw.githubusercontent.com/affaan-m/ECC/main/rules/common/rule-{i:D2}.md",
                HtmlUrl = $"https://github.com/affaan-m/ECC/blob/main/rules/common/rule-{i:D2}.md",
                SdlcRoles = "PG_BE",
                TechStacks = "common",
                ArchStyles = "general",
                SummaryZh = $"Rule {i}",
                UsageGuide = $"Guide {i}",
                IsActive = true
            })
            .ToList();

        var compactReq = new WizardRequestViewModel
        {
            ProjectType = "API",
            ArchStyle = "Clean",
            TechStacks = new List<string> { "dotnet" },
            SdlcRoles = new List<string> { "PG_BE" },
            Mode = "Compact"
        };

        var comprehensiveReq = new WizardRequestViewModel
        {
            ProjectType = "API",
            ArchStyle = "Clean",
            TechStacks = new List<string> { "dotnet" },
            SdlcRoles = new List<string> { "PG_BE" },
            Mode = "Comprehensive"
        };

        var compactResult = service.Recommend(compactReq, components);
        var comprehensiveResult = service.Recommend(comprehensiveReq, components);

        Assert.Equal(5, compactResult.Groups.Single().Items.Count);
        Assert.Equal(10, comprehensiveResult.Groups.Single().Items.Count);
    }

    [Fact]
    public void Recommend_GreenfieldVsBrownfield_AndAgentSuites_PairsSkillsRulesCommands()
    {
        using var dbScope = new TestDbContextScope();
        var service = new RecommendationService(dbScope.Context);

        var components = new List<EccComponent>
        {
            new()
            {
                Id = 10,
                ComponentType = ComponentType.Agent,
                Name = "architect",
                Path = "agents/architect.md",
                RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/agents/architect.md",
                SdlcRoles = "SA,SD",
                TechStacks = "common",
                ArchStyles = "general",
                SummaryZh = "架構規劃師",
                IsActive = true
            },
            new()
            {
                Id = 11,
                ComponentType = ComponentType.Agent,
                Name = "code-explorer",
                Path = "agents/code-explorer.md",
                RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/agents/code-explorer.md",
                SdlcRoles = "SA,PG_BE",
                TechStacks = "common",
                ArchStyles = "general",
                SummaryZh = "既有程式碼探索者",
                IsActive = true
            },
            new()
            {
                Id = 12,
                ComponentType = ComponentType.Agent,
                Name = "tdd-guide",
                Path = "agents/tdd-guide.md",
                RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/agents/tdd-guide.md",
                SdlcRoles = "PG_BE,PG_TEST",
                TechStacks = "common",
                ArchStyles = "general",
                SummaryZh = "TDD 測試驅動引導",
                IsActive = true
            },
            new()
            {
                Id = 13,
                ComponentType = ComponentType.Skill,
                Name = "tdd-workflow",
                Path = "skills/tdd-workflow/SKILL.md",
                RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/skills/tdd-workflow/SKILL.md",
                SdlcRoles = "PG_TEST",
                TechStacks = "common",
                ArchStyles = "general",
                SummaryZh = "TDD 工作流",
                IsActive = true
            },
            new()
            {
                Id = 14,
                ComponentType = ComponentType.Command,
                Name = "tdd",
                Path = "commands/tdd.md",
                RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/commands/tdd.md",
                SdlcRoles = "PG_TEST",
                TechStacks = "common",
                ArchStyles = "general",
                SummaryZh = "/tdd 指令",
                IsActive = true
            },
            new()
            {
                Id = 15,
                ComponentType = ComponentType.Rule,
                Name = "common-testing",
                Path = "rules/common/testing.md",
                RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/rules/common/testing.md",
                SdlcRoles = "PG_TEST,QA",
                TechStacks = "common",
                ArchStyles = "general",
                SummaryZh = "通用測試規範",
                IsActive = true
            }
        };

        var greenfieldReq = new WizardRequestViewModel
        {
            ProjectType = "Game",
            ProjectLifecycle = "Greenfield",
            ArchStyle = "ECS",
            TechStacks = ["luau"],
            SdlcRoles = ["SA", "PG_BE", "PG_TEST"],
            Mode = "Compact"
        };

        var brownfieldReq = new WizardRequestViewModel
        {
            ProjectType = "Game",
            ProjectLifecycle = "Brownfield",
            ArchStyle = "ECS",
            TechStacks = ["luau"],
            SdlcRoles = ["SA", "PG_BE", "PG_TEST"],
            Mode = "Compact"
        };

        var greenfieldResult = service.Recommend(greenfieldReq, components);
        var brownfieldResult = service.Recommend(brownfieldReq, components);

        var gfArchitect = greenfieldResult.Suites.Single(s => s.LeadAgent.Name == "architect").LeadAgent;
        var bfExplorer = brownfieldResult.Suites.Single(s => s.LeadAgent.Name == "code-explorer").LeadAgent;
        Assert.Contains(gfArchitect.MatchReasons, r => r.Contains("Greenfield"));
        Assert.Contains(bfExplorer.MatchReasons, r => r.Contains("Brownfield"));

        var tddSuite = greenfieldResult.Suites.Single(s => s.LeadAgent.Name == "tdd-guide");
        Assert.Contains(tddSuite.PairedSkills, s => s.Name == "tdd-workflow");
        Assert.Contains(tddSuite.PairedCommands, c => c.Name == "tdd");
        Assert.Contains(tddSuite.PairedRules, r => r.Name == "common-testing");
    }

    [Fact]
    public void Recommend_WithRequirementKeywords_BoostsMatchingComponentsAndExplainsReason()
    {
        using var dbScope = new TestDbContextScope();
        var service = new RecommendationService(dbScope.Context);

        var components = new List<EccComponent>
        {
            new()
            {
                Id = 20,
                ComponentType = ComponentType.Agent,
                Name = "security-reviewer",
                Path = "agents/security-reviewer.md",
                RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/agents/security-reviewer.md",
                SdlcRoles = "QA",
                TechStacks = "common",
                ArchStyles = "general",
                DescriptionEn = "Security and payment audit specialist",
                SummaryZh = "安全與金流稽核專家",
                IsActive = true
            }
        };

        var request = new WizardRequestViewModel
        {
            ProjectType = "API",
            ProjectLifecycle = "Greenfield",
            ArchStyle = "Clean",
            TechStacks = ["dotnet"],
            SdlcRoles = ["QA"],
            Mode = "Compact",
            RequirementKeywords = ["security", "payment"]
        };

        var result = service.Recommend(request, components);
        var qaItem = Assert.Single(result.Groups.Single(g => g.Role == "QA").Items);
        Assert.Contains(qaItem.MatchReasons, r => r.Contains("命中需求關鍵字") && r.Contains("security") && r.Contains("payment"));
    }
}
