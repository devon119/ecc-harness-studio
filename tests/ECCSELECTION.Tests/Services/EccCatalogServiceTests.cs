using ECCSELECTION.Tests.Helpers;
using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Options;
using ECCSELECTION.Web.Services;
using ECCSELECTION.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECCSELECTION.Tests.Services;

public sealed class EccCatalogServiceTests
{
    [Fact]
    public async Task QueryAsync_FiltersByActiveTypeKeywordAndExactRoleAndStackTokens()
    {
        using var dbScope = new TestDbContextScope();
        dbScope.Context.EccComponents.AddRange(
            new EccComponent
            {
                ComponentType = ComponentType.Agent,
                Name = "java-architect",
                Path = "agents/java-architect.md",
                Sha = "s1",
                DescriptionEn = "Java backend architect",
                SummaryZh = "Java 架構師",
                SdlcRoles = "SA,PG_BE",
                TechStacks = "java",
                IsActive = true
            },
            new EccComponent
            {
                ComponentType = ComponentType.Agent,
                Name = "js-frontend",
                Path = "agents/js-frontend.md",
                Sha = "s2",
                DescriptionEn = "JavaScript frontend specialist",
                SummaryZh = "JS 前端",
                SdlcRoles = "PG_FE",
                TechStacks = "javascript",
                IsActive = true
            },
            new EccComponent
            {
                ComponentType = ComponentType.Rule,
                Name = "archived-rule",
                Path = "rules/archived.md",
                Sha = "s3",
                SdlcRoles = "SA",
                TechStacks = "java",
                IsActive = false
            });
        await dbScope.Context.SaveChangesAsync();

        var service = CreateService(dbScope);

        // Exact token matching: TechStack = "java" must NOT match "javascript"
        EccCatalogFilterViewModel filtered = await service.QueryAsync(new EccCatalogFilterViewModel
        {
            ComponentType = ComponentType.Agent,
            TechStack = "java",
            SdlcRole = "SA"
        });

        EccComponent item = Assert.Single(filtered.Items);
        Assert.Equal("java-architect", item.Name);
        Assert.Equal(3, filtered.TotalCount);
        Assert.Equal(2, filtered.ActiveCount);

        // IncludeInactive = true + Keyword
        EccCatalogFilterViewModel inactiveResult = await service.QueryAsync(new EccCatalogFilterViewModel
        {
            IncludeInactive = true,
            Keyword = "archived"
        });
        Assert.Single(inactiveResult.Items);
    }

    [Fact]
    public async Task UpdateManualAsync_SetsIsManuallyEditedAndNormalizesTokens()
    {
        using var dbScope = new TestDbContextScope();
        var component = new EccComponent
        {
            ComponentType = ComponentType.Skill,
            Name = "tdd-workflow",
            Path = "skills/tdd-workflow/SKILL.md",
            Sha = "s1",
            IsActive = true,
            IsManuallyEdited = false
        };
        dbScope.Context.EccComponents.Add(component);
        await dbScope.Context.SaveChangesAsync();

        var service = CreateService(dbScope);

        bool updated = await service.UpdateManualAsync(new EccComponentEditViewModel
        {
            Id = component.Id,
            SummaryZh = "  人工編輯摘要  ",
            SdlcRoles = "qa, sa",
            TechStacks = "DotNet, Python",
            ArchStyles = "MVC, Clean",
            UsageGuide = "  用於測試驅動開發  "
        });

        Assert.True(updated);

        EccComponent saved = await dbScope.Context.EccComponents.SingleAsync(c => c.Id == component.Id);
        Assert.True(saved.IsManuallyEdited);
        Assert.Equal("人工編輯摘要", saved.SummaryZh);
        Assert.Equal("QA,SA", saved.SdlcRoles);
        Assert.Equal("dotnet,python", saved.TechStacks);
        Assert.Equal("mvc,clean", saved.ArchStyles);
        Assert.Equal("用於測試驅動開發", saved.UsageGuide);

        Assert.False(await service.UpdateManualAsync(new EccComponentEditViewModel { Id = 99999 }));
    }

    [Fact]
    public async Task GetDashboardStatsAsync_ReturnsAccurateCountsAndLatestSync()
    {
        using var dbScope = new TestDbContextScope();
        dbScope.Context.EccComponents.AddRange(
            new EccComponent { ComponentType = ComponentType.Agent, Name = "a1", Path = "agents/a1.md", Sha = "1", IsActive = true },
            new EccComponent { ComponentType = ComponentType.Skill, Name = "s1", Path = "skills/s1/SKILL.md", Sha = "2", IsActive = true },
            new EccComponent { ComponentType = ComponentType.Rule, Name = "r1", Path = "rules/r1.md", Sha = "3", IsActive = true },
            new EccComponent { ComponentType = ComponentType.Command, Name = "c1", Path = "commands/c1.md", Sha = "4", IsActive = true },
            new EccComponent { ComponentType = ComponentType.Agent, Name = "a2", Path = "agents/a2.md", Sha = "5", IsActive = false });

        dbScope.Context.SyncLogs.Add(new SyncLog
        {
            StartedAt = DateTime.UtcNow,
            Status = SyncStatus.Success,
            TotalFound = 4,
            AddedCount = 4
        });
        await dbScope.Context.SaveChangesAsync();

        var service = CreateService(dbScope, pat: "ghp_token", llmEnabled: true, llmApiKey: "key");

        SyncDashboardViewModel stats = await service.GetDashboardStatsAsync();

        Assert.Equal(5, stats.TotalComponents);
        Assert.Equal(4, stats.ActiveComponents);
        Assert.Equal(1, stats.AgentCount);
        Assert.Equal(1, stats.SkillCount);
        Assert.Equal(1, stats.RuleCount);
        Assert.Equal(1, stats.CommandCount);
        Assert.NotNull(stats.LatestSync);
        Assert.Single(stats.RecentLogs);
        Assert.True(stats.HasGitHubToken);
        Assert.True(stats.LlmEnabled);
        Assert.Equal("affaan-m/ECC", stats.GitHubRepoFullName);
    }

    private static EccCatalogService CreateService(
        TestDbContextScope dbScope,
        string pat = "",
        bool llmEnabled = false,
        string llmApiKey = "") =>
        new(
            dbScope.Context,
            Options.Create(new GitHubSyncOptions { Owner = "affaan-m", Repo = "ECC", PersonalAccessToken = pat }),
            Options.Create(new LlmEnrichmentOptions { Enabled = llmEnabled, ApiKey = llmApiKey }));
}
