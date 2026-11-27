using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace ECCSELECTION.Web.Data;

/// <summary>
/// Idempotently seeds Studio built-in ECC components (such as Roblox / Luau) into <see cref="AppDbContext"/>.
/// </summary>
public static class BuiltInEccSeeder
{
    private const string BuiltInPrefix = "builtin/";
    private const string RawBaseUrl = "https://raw.githubusercontent.com/devon119/ecc-harness-studio/main/docs/builtin-ecc/";
    private const string HtmlBaseUrl = "https://github.com/devon119/ecc-harness-studio/blob/main/docs/builtin-ecc/";

    private sealed record BuiltInDefinition(
        ComponentType ComponentType,
        string Name,
        string SubPath,
        string SdlcRoles,
        string TechStacks,
        string ArchStyles,
        string DescriptionEn,
        string SummaryZh,
        string UsageGuide);

    private static readonly BuiltInDefinition[] Definitions =
    [
        new(
            ComponentType.Agent,
            "roblox-luau-architect",
            "roblox/agents/roblox-luau-architect.md",
            "SA,SD,PG_BE",
            "luau",
            "ecs,clean",
            "Roblox Studio / Luau system architect specializing in Server-Client authority boundaries, Rojo project structure, CollectionService ECS / Knit modularization, and typed RemoteEvent/RemoteFunction contracts.",
            "[Agent 角色代理] roblox-luau-architect（適用角色：SA、SD、PG_BE）— 專精 Roblox Studio Server-Client 權限邊界、Rojo 專案結構、CollectionService ECS 模組化與 Remote 契約設計。",
            "當設計 Roblox 遊戲系統架構、規劃 ServerScriptService / ReplicatedStorage / StarterPlayerScripts 邊界或定義 Remote 契約時啟用。"),
        new(
            ComponentType.Agent,
            "roblox-luau-reviewer",
            "roblox/agents/roblox-luau-reviewer.md",
            "PG_FE,PG_BE,PG_TEST,QA",
            "luau",
            "ecs,general",
            "Roblox Luau code reviewer enforcing --!strict type safety, RBXScriptConnection leak prevention, Server-side Remote validation, Selene lint, and StyLua formatting.",
            "[Agent 角色代理] roblox-luau-reviewer（適用角色：PG_FE、PG_BE、PG_TEST、QA）— 審查 Luau --!strict 型別安全、Connection 洩漏、Server 權限驗證與 Selene/StyLua 規範。",
            "在提交 Luau 程式碼或完成 Roblox 功能模組後執行，檢查型別嚴格性、記憶體洩漏與 Server 防作弊驗證。"),
        new(
            ComponentType.Rule,
            "luau-strict-coding",
            "roblox/rules/luau-strict-coding.md",
            "SA,SD,PG_FE,PG_BE,PG_TEST,QA",
            "luau",
            "ecs,general",
            "Enforces --!strict Luau typing, modern task.* scheduling, Trove/Maid connection cleanup, and Selene/StyLua compliance across Roblox Studio projects.",
            "[Rule 開發規範] luau-strict-coding（適用角色：SA、SD、PG_FE、PG_BE、PG_TEST、QA）— 強制 --!strict 型別、task.* 排程取代 wait/spawn 與 Trove/Maid 清理。",
            "所有 Roblox Luau 腳本開發必備規範：檔案頂端必須加上 --!strict，全面使用 task.* 與 Trove/Maid。"),
        new(
            ComponentType.Rule,
            "roblox-server-security",
            "roblox/rules/roblox-server-security.md",
            "SA,SD,PG_BE,PG_DB,QA",
            "luau",
            "ecs,general",
            "Zero-trust Roblox server security rules covering RemoteEvent/RemoteFunction payload validation, rate limiting, anti-exploit sanity checks, and DataStore concurrency locks.",
            "[Rule 開發規範] roblox-server-security（適用角色：SA、SD、PG_BE、PG_DB、QA）— 永不信任 Client、RemoteEvent 型別與速率限制、防外掛驗證與 DataStore 併發鎖。",
            "凡涉及 RemoteEvent、RemoteFunction、經濟交易或玩家狀態變更之伺服器腳本皆須遵守此零信任安全規範。"),
        new(
            ComponentType.Rule,
            "roblox-ecs-patterns",
            "roblox/rules/roblox-ecs-patterns.md",
            "SA,SD,PG_FE,PG_BE",
            "luau",
            "ecs",
            "Roblox Entity-Component-System patterns using CollectionService tags, ReplicatedStorage/ServerScriptService separation, and deterministic lifecycle management.",
            "[Rule 開發規範] roblox-ecs-patterns（適用角色：SA、SD、PG_FE、PG_BE）— 以 CollectionService Tags 實現 ECS 實體生命週期與 Server/Client 職責分離。",
            "設計可互動世界物件、NPC 或遊戲實體時使用 CollectionService 標籤驅動，避免在 Model 內複製多個 Script。"),
        new(
            ComponentType.Skill,
            "roblox-datastore-patterns",
            "roblox/skills/roblox-datastore-patterns/SKILL.md",
            "SD,PG_BE,PG_DB",
            "luau",
            "ecs,clean",
            "Production Roblox DataStore patterns using UpdateAsync, exponential retry backoff, session locking, BindToClose flushing, and schema migration.",
            "[Skill 技能工作流] roblox-datastore-patterns（適用角色：SD、PG_BE、PG_DB）— UpdateAsync 指數退避重試、Session Lock、BindToClose 安全存檔與 Schema 遷移。",
            "實作玩家存檔、ProfileStore/DataStoreService 讀寫、伺服器關閉存檔與資料版本遷移時套用。"),
        new(
            ComponentType.Skill,
            "roblox-luau-testing",
            "roblox/skills/roblox-luau-testing/SKILL.md",
            "PG_TEST,QA",
            "luau",
            "ecs,general",
            "Roblox Luau testing workflow using TestEZ, Busted, Selene static analysis, StyLua formatting, and Roblox Service mocking in CI.",
            "[Skill 技能工作流] roblox-luau-testing（適用角色：PG_TEST、QA）— TestEZ / Busted 單元測試、Selene 靜態分析、StyLua 檢查與 Roblox Service Mock。",
            "撰寫 Luau 單元測試、分離純邏輯與 Roblox API、建立 CI 品質閘門時套用。"),
        new(
            ComponentType.Command,
            "roblox-check",
            "roblox/commands/roblox-check.md",
            "PG_BE,PG_TEST,QA",
            "luau",
            "ecs,general",
            "Run the complete Roblox Luau quality gate: Selene lint, StyLua format check, Busted/TestEZ unit tests, Rojo build verification, and server security audit.",
            "[Command 快速指令] roblox-check（適用角色：PG_BE、PG_TEST、QA）— 一鍵執行 selene、stylua --check、busted 測試、Rojo 建置驗證與安全稽核。",
            "執行 /roblox-check 以一次驗證 Selene、StyLua、Busted 測試、Rojo 建置與伺服器安全項目。"),
        new(
            ComponentType.Skill,
            "roblox-ui-patterns",
            "roblox/skills/roblox-ui-patterns/SKILL.md",
            "PG_FE,SD",
            "luau",
            "ecs,general",
            "Roblox UI engineering patterns for React-Lua / Fusion declarative state, SafeArea & UIScale responsiveness, TweenService animations, and input cleanup.",
            "[Skill 技能工作流] roblox-ui-patterns（適用角色：PG_FE、SD）— React-Lua / Fusion 宣告式 UI、SafeArea 與 UIScale 響應式版面、TweenService 動畫與清理。",
            "開發 Roblox HUD、商店、背包與跨裝置（桌面/行動/手把）介面時套用。"),
        new(
            ComponentType.Skill,
            "roblox-rojo-wally-workflow",
            "roblox/skills/roblox-rojo-wally-workflow/SKILL.md",
            "SA,SD,PG_BE",
            "luau",
            "ecs,clean",
            "Roblox Rojo project mapping, Wally package management, Aftman/Rokit toolchain pinning, and CI/CD Open Cloud deployment workflow.",
            "[Skill 技能工作流] roblox-rojo-wally-workflow（適用角色：SA、SD、PG_BE）— Rojo default.project.json 映射、Wally 套件管理、Aftman 工具鏈與 CI/CD 部署。",
            "建立或維護 Rojo + Wally + Aftman/Rokit 專案結構與 Roblox Open Cloud 自動化發布流程時套用。"),
        new(
            ComponentType.Rule,
            "roblox-combat-physics",
            "roblox/rules/roblox-combat-physics.md",
            "PG_BE,PG_FE,QA",
            "luau",
            "ecs,general",
            "Roblox combat and physics rules for client-side prediction, server-side hitbox and raycast validation, SpatialQuery usage, and anti-speed/teleport checks.",
            "[Rule 開發規範] roblox-combat-physics（適用角色：PG_BE、PG_FE、QA）— Client 預判 + Server Hitbox 驗證、Raycast/SpatialQuery 實踐與防瞬移校驗。",
            "實作近戰/遠程戰鬥系統、投射物物理或移動防作弊驗證時遵守此規範。"),
        new(
            ComponentType.Skill,
            "roblox-network-performance",
            "roblox/skills/roblox-network-performance/SKILL.md",
            "SD,PG_BE,PG_FE,QA",
            "luau",
            "ecs,general",
            "Roblox networking and performance optimization covering StreamingEnabled, UnreliableRemoteEvent bandwidth reduction, ScriptProfiler, and memory leak diagnostics.",
            "[Skill 技能工作流] roblox-network-performance（適用角色：SD、PG_BE、PG_FE、QA）— StreamingEnabled 相容、UnreliableRemoteEvent 頻寬壓縮、ScriptProfiler 與記憶體洩漏排查。",
            "優化多人連線頻寬、處理 StreamingEnabled 動態載入與診斷 60 FPS 效能瓶頸時套用。"),
        new(
            ComponentType.Rule,
            "roblox-monetization-patterns",
            "roblox/rules/roblox-monetization-patterns.md",
            "PG_BE,PG_DB,QA",
            "luau",
            "ecs,general",
            "Roblox MarketplaceService monetization rules covering idempotent ProcessReceipt handling, GamePass caching, Developer Product fulfillment, and audit logging.",
            "[Rule 開發規範] roblox-monetization-patterns（適用角色：PG_BE、PG_DB、QA）— MarketplaceService ProcessReceipt 冪等防重複發貨、GamePass 快取與交易稽核。",
            "實作 Robux Developer Product、GamePass 權限快取與內購發貨稽核時必須遵守。")
    ];

    public static async Task SeedAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var paths = Definitions.Select(d => $"{BuiltInPrefix}{d.SubPath}").ToArray();
        var existingByPath = await dbContext.EccComponents
            .Where(c => paths.Contains(c.Path))
            .ToDictionaryAsync(c => c.Path, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var now = DateTime.UtcNow;
        var hasChanges = false;

        foreach (var def in Definitions)
        {
            var path = $"{BuiltInPrefix}{def.SubPath}";
            var sha = $"studio-builtin-v1-{def.Name}";
            var rawUrl = $"{RawBaseUrl}{def.SubPath}";
            var htmlUrl = $"{HtmlBaseUrl}{def.SubPath}";

            if (!existingByPath.TryGetValue(path, out var existing))
            {
                dbContext.EccComponents.Add(new EccComponent
                {
                    ComponentType = def.ComponentType,
                    Name = def.Name,
                    Path = path,
                    Sha = sha,
                    RawUrl = rawUrl,
                    HtmlUrl = htmlUrl,
                    DescriptionEn = def.DescriptionEn,
                    SummaryZh = def.SummaryZh,
                    SdlcRoles = def.SdlcRoles,
                    TechStacks = def.TechStacks,
                    ArchStyles = def.ArchStyles,
                    UsageGuide = def.UsageGuide,
                    LastSyncedAt = now,
                    IsActive = true,
                    IsManuallyEdited = false
                });
                hasChanges = true;
                continue;
            }

            if (existing.IsManuallyEdited)
            {
                continue;
            }

            if (!string.Equals(existing.Sha, sha, StringComparison.Ordinal))
            {
                existing.ComponentType = def.ComponentType;
                existing.Name = def.Name;
                existing.Sha = sha;
                existing.RawUrl = rawUrl;
                existing.HtmlUrl = htmlUrl;
                existing.DescriptionEn = def.DescriptionEn;
                existing.SummaryZh = def.SummaryZh;
                existing.SdlcRoles = def.SdlcRoles;
                existing.TechStacks = def.TechStacks;
                existing.ArchStyles = def.ArchStyles;
                existing.UsageGuide = def.UsageGuide;
                existing.LastSyncedAt = now;
                existing.IsActive = true;
                hasChanges = true;
            }
        }

        if (hasChanges)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
