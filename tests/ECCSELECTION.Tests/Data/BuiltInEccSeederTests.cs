using ECCSELECTION.Tests.Helpers;
using ECCSELECTION.Web.Data;
using ECCSELECTION.Web.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace ECCSELECTION.Tests.Data;

public class BuiltInEccSeederTests
{
    [Fact]
    public async Task SeedAsync_EmptyDatabaseExecutedTwice_Seeds13RobloxComponentsIdempotently()
    {
        using var dbScope = new TestDbContextScope();

        await BuiltInEccSeeder.SeedAsync(dbScope.Context);
        await BuiltInEccSeeder.SeedAsync(dbScope.Context);

        var robloxComponents = await dbScope.Context.EccComponents
            .AsNoTracking()
            .Where(c => c.Path.StartsWith("builtin/roblox/"))
            .ToListAsync();

        Assert.Equal(13, robloxComponents.Count);
        Assert.Equal(13, await dbScope.Context.EccComponents.CountAsync());
        Assert.All(robloxComponents, component =>
        {
            Assert.True(component.IsActive);
            Assert.False(component.IsManuallyEdited);
            Assert.StartsWith("studio-builtin-v1-", component.Sha, StringComparison.Ordinal);
            Assert.Contains("luau", component.GetTechStackTokens(), StringComparer.OrdinalIgnoreCase);
        });
        Assert.Contains(robloxComponents, c => c.ComponentType == ComponentType.Agent);
        Assert.Contains(robloxComponents, c => c.ComponentType == ComponentType.Skill);
        Assert.Contains(robloxComponents, c => c.ComponentType == ComponentType.Rule);
        Assert.Contains(robloxComponents, c => c.ComponentType == ComponentType.Command);
    }

    [Fact]
    public async Task SeedAsync_WhenBuiltInComponentIsManuallyEdited_DoesNotOverwriteManualFields()
    {
        using var dbScope = new TestDbContextScope();

        await BuiltInEccSeeder.SeedAsync(dbScope.Context);

        var architect = await dbScope.Context.EccComponents
            .SingleAsync(c => c.Path == "builtin/roblox/agents/roblox-luau-architect.md");
        architect.SummaryZh = "人工自訂 Roblox 架構摘要";
        architect.TechStacks = "luau,custom";
        architect.Sha = "manual-sha-override";
        architect.IsManuallyEdited = true;
        await dbScope.Context.SaveChangesAsync();

        await BuiltInEccSeeder.SeedAsync(dbScope.Context);

        var reloaded = await dbScope.Context.EccComponents
            .AsNoTracking()
            .SingleAsync(c => c.Path == "builtin/roblox/agents/roblox-luau-architect.md");

        Assert.Equal("人工自訂 Roblox 架構摘要", reloaded.SummaryZh);
        Assert.Equal("luau,custom", reloaded.TechStacks);
        Assert.Equal("manual-sha-override", reloaded.Sha);
        Assert.True(reloaded.IsManuallyEdited);
        Assert.Equal(13, await dbScope.Context.EccComponents.CountAsync());
    }
}
