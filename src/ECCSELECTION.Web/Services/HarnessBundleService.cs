using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ECCSELECTION.Web.Data;
using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Options;
using ECCSELECTION.Web.Services.Interfaces;
using ECCSELECTION.Web.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECCSELECTION.Web.Services;

public class HarnessBundleService : IHarnessBundleService
{
    private const string BuiltInPrefix = "builtin/";
    private readonly AppDbContext _dbContext;
    private readonly HttpClient _httpClient;
    private readonly GitHubSyncOptions _options;
    private readonly ILogger<HarnessBundleService> _logger;
    private readonly string _contentRootPath;

    public HarnessBundleService(
        AppDbContext dbContext,
        HttpClient httpClient,
        IOptions<GitHubSyncOptions> options,
        ILogger<HarnessBundleService> logger,
        IWebHostEnvironment? environment = null)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _contentRootPath = string.IsNullOrWhiteSpace(environment?.ContentRootPath)
            ? AppContext.BaseDirectory
            : environment.ContentRootPath;
    }

    public Task<byte[]> BuildBundleZipAsync(
        IReadOnlyCollection<int> selectedComponentIds,
        CancellationToken cancellationToken = default)
        => BuildBundleZipAsync(selectedComponentIds, null, cancellationToken);

    public async Task<byte[]> BuildBundleZipAsync(
        IReadOnlyCollection<int> selectedComponentIds,
        HarnessAssemblyOptionsViewModel? assemblyOptions,
        CancellationToken cancellationToken = default)
    {
        var opts = NormalizeOptions(assemblyOptions);

        var distinctIds = selectedComponentIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var components = distinctIds.Count == 0
            ? new List<EccComponent>()
            : await _dbContext.EccComponents
                .AsNoTracking()
                .Where(c => c.IsActive && distinctIds.Contains(c.Id))
                .OrderBy(c => c.ComponentType)
                .ThenBy(c => c.Name)
                .ToListAsync(cancellationToken);

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = BuildManifestHeader(opts, components.Count);
            var errors = new List<string>();
            var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await AddSelectedComponentsToArchiveAsync(
                archive,
                opts,
                components,
                addedPaths,
                errors,
                manifest,
                cancellationToken);

            await WriteGuardrailArtifactsAsync(archive, opts, components, addedPaths, cancellationToken);
            if (!string.IsNullOrWhiteSpace(opts.HarnessPlaybookMarkdown) && addedPaths.Add("HARNESS_PLAYBOOK.md"))
            {
                await WriteTextEntryAsync(archive, "HARNESS_PLAYBOOK.md", opts.HarnessPlaybookMarkdown, cancellationToken);
            }

            await WriteTextEntryAsync(archive, "README_HARNESS.md", manifest.ToString(), cancellationToken);

            if (errors.Count > 0)
            {
                await WriteTextEntryAsync(
                    archive,
                    "DOWNLOAD_ERRORS.txt",
                    string.Join(Environment.NewLine, errors),
                    cancellationToken);
            }
        }

        return memoryStream.ToArray();
    }

    private static StringBuilder BuildManifestHeader(HarnessAssemblyOptionsViewModel opts, int componentCount)
    {
        var manifest = new StringBuilder();
        manifest.AppendLine($"# {opts.ProjectName} — ECC Harness 組建套件清單");
        manifest.AppendLine();
        manifest.AppendLine($"- **產生時間 (UTC)**：{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        manifest.AppendLine($"- **專案名稱**：`{opts.ProjectName}`");
        manifest.AppendLine($"- **目標環境**：`{opts.TargetEnvironment}`");
        manifest.AppendLine($"- **目標平台**：`{opts.TargetPlatform}`");
        manifest.AppendLine($"- **包含元件數量**：{componentCount}");
        manifest.AppendLine();
        manifest.AppendLine("## 一鍵啟用步驟");
        manifest.AppendLine("1. 將壓縮檔內容解壓縮至目標專案根目錄。");
        if (opts.IncludeGitPreCommitHook)
        {
            manifest.AppendLine("2. 啟用 Git 提交前卡點：`powershell -ExecutionPolicy Bypass -File scripts/install-hooks.ps1`（或 `bash scripts/install-hooks.sh`）。");
        }

        if (opts.RequireTestsBeforeDone || opts.IncludeIdeAutoRules)
        {
            manifest.AppendLine("3. 執行品質閘門驗證：`powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1`（或 `bash scripts/quality-gate.sh`）。");
        }

        manifest.AppendLine();
        AppendBeginnerGovernanceGuide(manifest, opts.SpecFilePath);
        manifest.AppendLine("## 已選取元件清單");
        manifest.AppendLine("| 類型 | 名稱 | 目標路徑 | 來源 Raw URL |");
        manifest.AppendLine("| :--- | :--- | :--- | :--- |");
        return manifest;
    }

    private async Task AddSelectedComponentsToArchiveAsync(
        ZipArchive archive,
        HarnessAssemblyOptionsViewModel opts,
        IReadOnlyList<EccComponent> components,
        HashSet<string> addedPaths,
        List<string> errors,
        StringBuilder manifest,
        CancellationToken cancellationToken)
    {
        var hasExternalSkills = components.Any(c =>
            c.ComponentType == ComponentType.Skill && !IsBuiltInPath(c.Path));
        var skillTreePaths = hasExternalSkills
            ? await FetchRepoBlobPathsAsync(cancellationToken)
            : [];
        var rootDir = string.Equals(opts.TargetPlatform, "Antigravity", StringComparison.OrdinalIgnoreCase)
            ? ".ecc"
            : ".claude";

        foreach (var component in components)
        {
            var entryPath = BuildSafeLocalPath(component, opts.TargetPlatform);
            if (!addedPaths.Add(entryPath))
            {
                continue;
            }

            var isBuiltIn = IsBuiltInPath(component.Path);
            var (content, errorMessage) = isBuiltIn
                ? await ReadBuiltInContentAsync(component.Path, cancellationToken)
                : await DownloadRawContentAsync(component.RawUrl, cancellationToken);
            if (content is null)
            {
                errors.Add($"{component.Name} ({component.RawUrl}): {errorMessage}");
                manifest.AppendLine($"| {component.ComponentType} | `{component.Name}` | `{entryPath}` (下載失敗) | {component.RawUrl} |");
                continue;
            }

            await WriteBytesEntryAsync(archive, entryPath, content, cancellationToken);
            manifest.AppendLine($"| {component.ComponentType} | `{component.Name}` | `{entryPath}` | {component.RawUrl} |");

            if (!isBuiltIn && component.ComponentType == ComponentType.Skill && skillTreePaths.Count > 0)
            {
                await BundleSkillAuxiliaryFilesAsync(
                    archive,
                    component,
                    rootDir,
                    skillTreePaths,
                    addedPaths,
                    errors,
                    manifest,
                    cancellationToken);
            }
        }
    }

    private static async Task WriteGuardrailArtifactsAsync(
        ZipArchive archive,
        HarnessAssemblyOptionsViewModel opts,
        IReadOnlyList<EccComponent> components,
        HashSet<string> addedPaths,
        CancellationToken cancellationToken)
    {
        if (opts.IncludeIdeAutoRules)
        {
            var guardrailDoc = BuildGuardrailMarkdown(opts, components);

            if (IsAntigravityEnabled(opts.TargetPlatform))
            {
                if (addedPaths.Add("AGENTS.md"))
                {
                    await WriteTextEntryAsync(archive, "AGENTS.md", guardrailDoc, cancellationToken);
                }

                if (addedPaths.Add(".agents/rules/workflow-guardrails.md"))
                {
                    await WriteTextEntryAsync(
                        archive,
                        ".agents/rules/workflow-guardrails.md",
                        BuildAntigravityAutoRule(opts),
                        cancellationToken);
                }
            }

            if (IsClaudeCodeEnabled(opts.TargetPlatform))
            {
                if (addedPaths.Add("CLAUDE.md"))
                {
                    await WriteTextEntryAsync(archive, "CLAUDE.md", guardrailDoc, cancellationToken);
                }

                if (addedPaths.Add(".claude/settings.json"))
                {
                    await WriteTextEntryAsync(
                        archive,
                        ".claude/settings.json",
                        BuildClaudeSettingsJson(opts),
                        cancellationToken);
                }
            }
        }

        if (opts.RequireTestsBeforeDone || opts.IncludeIdeAutoRules)
        {
            if (addedPaths.Add("scripts/quality-gate.ps1"))
            {
                await WriteTextEntryAsync(archive, "scripts/quality-gate.ps1", BuildQualityGatePs1(opts), cancellationToken);
            }

            if (addedPaths.Add("scripts/quality-gate.sh"))
            {
                await WriteTextEntryAsync(archive, "scripts/quality-gate.sh", BuildQualityGateSh(opts), cancellationToken);
            }
        }

        if (opts.IncludeGitPreCommitHook)
        {
            if (addedPaths.Add(".githooks/pre-commit"))
            {
                await WriteTextEntryAsync(archive, ".githooks/pre-commit", BuildGitPreCommitHook(opts), cancellationToken);
            }

            if (addedPaths.Add("scripts/install-hooks.ps1"))
            {
                await WriteTextEntryAsync(archive, "scripts/install-hooks.ps1", BuildInstallHooksPs1(), cancellationToken);
            }

            if (addedPaths.Add("scripts/install-hooks.sh"))
            {
                await WriteTextEntryAsync(archive, "scripts/install-hooks.sh", BuildInstallHooksSh(), cancellationToken);
            }
        }
    }

    private static HarnessAssemblyOptionsViewModel NormalizeOptions(HarnessAssemblyOptionsViewModel? input)
    {
        var opts = input ?? new HarnessAssemblyOptionsViewModel();
        return new HarnessAssemblyOptionsViewModel
        {
            ProjectName = string.IsNullOrWhiteSpace(opts.ProjectName) ? "MyProject" : opts.ProjectName.Trim(),
            TargetEnvironment = string.IsNullOrWhiteSpace(opts.TargetEnvironment) ? ".NET 10 / ASP.NET Core MVC" : opts.TargetEnvironment.Trim(),
            SpecFilePath = string.IsNullOrWhiteSpace(opts.SpecFilePath) ? "SPEC.md" : opts.SpecFilePath.Trim(),
            BuildCommand = opts.BuildCommand?.Trim() ?? string.Empty,
            FormatCommand = opts.FormatCommand?.Trim() ?? string.Empty,
            TestCommand = opts.TestCommand?.Trim() ?? string.Empty,
            SecurityScanCommand = opts.SecurityScanCommand?.Trim() ?? string.Empty,
            DevServerUrl = opts.DevServerUrl?.Trim(),
            ScopeGuardNote = string.IsNullOrWhiteSpace(opts.ScopeGuardNote)
                ? "僅限專案原始碼與測試目錄，禁止未授權修改部署與憑證設定"
                : opts.ScopeGuardNote.Trim(),
            ProtectedPaths = string.IsNullOrWhiteSpace(opts.ProtectedPaths)
                ? ".env*,**/*.pem,**/*.key,**/secrets.*,appsettings.Production.json"
                : opts.ProtectedPaths.Trim(),
            TargetPlatform = string.IsNullOrWhiteSpace(opts.TargetPlatform) ? "All" : opts.TargetPlatform.Trim(),
            HarnessPlaybookMarkdown = opts.HarnessPlaybookMarkdown?.Trim(),
            RequireSpecFirst = opts.RequireSpecFirst,
            RequireTestsBeforeDone = opts.RequireTestsBeforeDone,
            RequireEvidenceOutput = opts.RequireEvidenceOutput,
            IncludeGitPreCommitHook = opts.IncludeGitPreCommitHook,
            IncludeIdeAutoRules = opts.IncludeIdeAutoRules
        };
    }

    private static bool IsAntigravityEnabled(string platform) =>
        string.Equals(platform, "All", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(platform, "Antigravity", StringComparison.OrdinalIgnoreCase);

    private static bool IsClaudeCodeEnabled(string platform) =>
        string.Equals(platform, "All", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(platform, "ClaudeCode", StringComparison.OrdinalIgnoreCase);

    private const string WorkflowSevenSteps =
        "`1. SA 規格先行` → `2. 人類確認閘門（強制暫停）` → `3. 分層實作` → `4. TDD 與測試覆蓋` → `5. 程式碼與安全審查` → `6. 強制品質閘門（scripts/quality-gate + 實機驗證）` → `7. 文件同步`";

    private static string BuildGuardrailMarkdown(
        HarnessAssemblyOptionsViewModel opts,
        IReadOnlyList<EccComponent> components)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {opts.ProjectName} — AI Harness 強制規範與防堵卡點");
        sb.AppendLine();
        sb.AppendLine($"- **目標環境**：`{opts.TargetEnvironment}`");
        sb.AppendLine($"- **規格書路徑**：`{opts.SpecFilePath}`");
        sb.AppendLine($"- **架構邊界（Scope Guard）**：{opts.ScopeGuardNote}");
        sb.AppendLine($"- **受保護敏感路徑**：`{opts.ProtectedPaths}`");
        if (!string.IsNullOrWhiteSpace(opts.DevServerUrl))
        {
            sb.AppendLine($"- **實機驗證網址**：{opts.DevServerUrl}");
        }

        sb.AppendLine();
        AppendGovernancePrinciplesSection(sb, opts.SpecFilePath);
        sb.AppendLine("## HARD GATES（不可繞過的防堵卡點）");
        sb.AppendLine();

        var gateNo = 1;
        if (opts.RequireSpecFirst)
        {
            sb.AppendLine($"### Gate {gateNo++}：規格先行卡點（Spec-First ＋ 人類確認閘門）");
            sb.AppendLine($"- 任何功能新增或規則異動，**必須先更新 `{opts.SpecFilePath}`**，禁止未更新規格即直接修改核心程式碼。");
            sb.AppendLine($"- 更新 `{opts.SpecFilePath}` 後**必須暫停並向人類開發者呈報規格摘要，取得人類明確確認後才可進入實作階段**；嚴禁 AI 自行假設已確認並往下寫程式。");
            sb.AppendLine();
        }

        sb.AppendLine($"### Gate {gateNo++}：安全與防靜默吞錯卡點");
        sb.AppendLine("- 嚴禁空的 `catch` 區塊，外部 I/O 或 HTTP 失敗必須記錄日誌並回報明確錯誤。");
        sb.AppendLine("- 嚴禁將 API Key、Token 或密碼硬編碼於原始碼或設定檔中。");
        sb.AppendLine();

        if (opts.RequireTestsBeforeDone)
        {
            sb.AppendLine($"### Gate {gateNo++}：強制品質閘門（未全數通過禁止宣告完成）");
            sb.AppendLine("任何程式碼異動後，**必須實際執行** `scripts/quality-gate.ps1`（或 `scripts/quality-gate.sh`），一次通過以下檢查：");
            if (!string.IsNullOrWhiteSpace(opts.BuildCommand)) sb.AppendLine($"1. Build：`{opts.BuildCommand}`");
            if (!string.IsNullOrWhiteSpace(opts.FormatCommand)) sb.AppendLine($"2. Format：`{opts.FormatCommand}`");
            if (!string.IsNullOrWhiteSpace(opts.TestCommand)) sb.AppendLine($"3. Test：`{opts.TestCommand}`");
            if (!string.IsNullOrWhiteSpace(opts.SecurityScanCommand)) sb.AppendLine($"4. Security：`{opts.SecurityScanCommand}`");
            if (!string.IsNullOrWhiteSpace(opts.DevServerUrl)) sb.AppendLine($"5. UI 實機驗證：於 `{opts.DevServerUrl}` 實測確認功能正常。");
            sb.AppendLine();
        }

        if (opts.RequireEvidenceOutput)
        {
            sb.AppendLine($"### Gate {gateNo++}：證據輸出契約（禁止口頭宣稱）");
            sb.AppendLine("- 最終回覆必須逐項附上各檢查指令的**實際終端執行輸出與通過數量**。");
            sb.AppendLine("- 若未實際執行上述檢查，**嚴禁宣告任務完成**；若某項失敗，必須立即修復後重跑。");
            sb.AppendLine();
        }

        sb.AppendLine("## 已組建之 ECC 元件清單");
        foreach (var group in components.GroupBy(c => c.ComponentType).OrderBy(g => g.Key))
        {
            sb.AppendLine($"- **{group.Key}** ({group.Count()})：{string.Join(", ", group.Select(c => $"`{c.Name}`"))}");
        }

        return sb.ToString();
    }

    private static void AppendGovernancePrinciplesSection(StringBuilder sb, string specFilePath)
    {
        sb.AppendLine("## 多代理協作與開發治理原則（Coordinator ＋ 寫入邊界隔離）");
        sb.AppendLine();
        sb.AppendLine("### 原則一：專責協調員監控 ＋ 專業 Agent 職責與寫入邊界隔離");
        sb.AppendLine("- 主 Agent 僅擔任「專責協調員（Coordinator）」，負責需求拆解、派發子代理、監控進度、驗收與彙報，**不得越權直接修改業務程式碼、測試或規格**。");
        sb.AppendLine("- 每個任務依職責指派對應專責 Agent，且各角色**僅可寫入自己負責的範圍，對其他 Agent 負責的內容一律唯讀**：");
        sb.AppendLine();
        sb.AppendLine("| 角色 | 允許寫入範圍 | 唯讀邊界（嚴禁修改） |");
        sb.AppendLine("| :--- | :--- | :--- |");
        sb.AppendLine("| **專責協調員（Coordinator）** | 無（僅協調、派發、監控、驗收） | 所有規格、業務程式碼、測試與設定 |");
        sb.AppendLine($"| **SA / 規格 Agent（`planner` / `architect`）** | 規格文件（`{specFilePath}`） | 正式程式碼、測試目錄、設定檔 |");
        sb.AppendLine("| **後端實作 Agent** | 後端商業邏輯、資料模型、資料存取、服務層 | 規格文件、前端視圖/控制器、測試目錄 |");
        sb.AppendLine("| **前端 / MVC Agent** | 控制器、視圖、前端頁面與靜態資源 | 規格文件、後端核心服務、測試目錄 |");
        sb.AppendLine("| **測試 Agent（`tdd-guide`）** | 測試專案 / 測試目錄 | 規格文件、正式業務程式碼 |");
        sb.AppendLine("| **審查與驗證 Agent（`code-reviewer` / `security-reviewer` / `e2e-runner`）** | 無（全專案唯讀，僅執行審查、品質閘門與實機驗證） | 全專案所有檔案 |");
        sb.AppendLine("| **文件同步 Agent** | 進度與驗證文件（如 `DEVELOPMENT_STATUS.md`） | 規格文件、正式程式碼、測試目錄 |");
        sb.AppendLine();
        sb.AppendLine("- 若目標工具或專案規模未啟用多代理子派發，單一 AI 仍須嚴格遵守「階段隔離與先唯讀後寫入」紀律，禁止同一回合未經確認混改規格與程式碼。");
        sb.AppendLine();
        sb.AppendLine("### 原則二：SA / 規格先行 ＋ 人類確認閘門（Human-in-the-Loop）");
        sb.AppendLine($"- 任何需求或規則異動，必須先由 SA 產出或更新 `{specFilePath}`，完成後**必須暫停並向人類開發者呈報規格摘要，取得人類明確確認後才可進入實作階段**；嚴禁 AI 自行假設已確認並往下寫程式。");
        sb.AppendLine();
        sb.AppendLine("### 原則三：強制完整流程（嚴禁跳步）");
        sb.AppendLine("- 每項任務必須完整依序走完以下 7 個階段，**禁止任何 AI 自行判斷略過任何步驟**：");
        sb.AppendLine($"  {WorkflowSevenSteps}");
        sb.AppendLine();
        sb.AppendLine("### 給新手的協作操作指引");
        AppendBeginnerOperationSteps(sb);
        sb.AppendLine();
    }

    private static void AppendBeginnerOperationSteps(StringBuilder sb)
    {
        sb.AppendLine("1. 下達需求後，AI 會先更新規格文件並停下來等你確認（人類確認閘門）。");
        sb.AppendLine("2. 確認規格摘要無誤後回覆同意實作，AI 才會依序進行分層開發與補齊測試。");
        sb.AppendLine("3. 最後 AI 必須執行審查與品質閘門並附上實際終端輸出結果，才算完成任務。");
    }

    private static void AppendBeginnerGovernanceGuide(StringBuilder sb, string specFilePath)
    {
        sb.AppendLine("## 給新手的 AI 協作與治理原則（快速上手指南）");
        sb.AppendLine("本建議包已內建完整的 AI 開發協作紀律，避免 AI 未經確認便直接改寫程式碼或略過審查與測試：");
        sb.AppendLine($"- **專責協調員＋寫入邊界隔離**：主 AI（Coordinator）僅負責拆解與協調，依角色分派 SA、後端、前端、測試、審查與文件代理，各自僅能修改負責範圍，其餘一律唯讀。");
        sb.AppendLine($"- **規格先行＋人類確認閘門**：所有異動必須先更新 `{specFilePath}` 並**強制暫停等待人類確認**，確認無誤後才可進入實作。");
        sb.AppendLine($"- **7 步完整流程（嚴禁跳步）**：{WorkflowSevenSteps}");
        sb.AppendLine();
        sb.AppendLine("### 新手三步驟操作");
        AppendBeginnerOperationSteps(sb);
        sb.AppendLine();
    }

    private static string BuildAntigravityAutoRule(HarnessAssemblyOptionsViewModel opts)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {opts.ProjectName} Auto-Loaded Workflow Guardrails");
        sb.AppendLine();
        sb.AppendLine("在進行任何開發或修改任務時，必須嚴格遵守以下防堵卡點與治理原則：");
        var ruleNo = 1;
        sb.AppendLine($"{ruleNo++}. **專責協調員＋寫入邊界隔離**：主 Agent 僅擔任協調員（Coordinator），不得越權直接修改業務程式碼、測試或規格；各專責子代理僅可寫入各自負責範圍，對其他範圍一律唯讀（單一 AI 亦須嚴格遵守階段隔離）。");
        if (opts.RequireSpecFirst)
        {
            sb.AppendLine($"{ruleNo++}. **規格先行＋人類確認閘門（強制暫停）**：修改功能前必須先更新 `{opts.SpecFilePath}`，完成後**必須暫停並向人類呈報規格摘要，取得人類明確確認後方可進入實作**。");
        }
        else
        {
            sb.AppendLine($"{ruleNo++}. **人類確認閘門（強制暫停）**：規劃或規格確認完成後，**必須暫停並取得人類明確確認後方可進入實作**。");
        }

        sb.AppendLine($"{ruleNo++}. **7 步完整流程（嚴禁跳步）**：必須依序走完 {WorkflowSevenSteps}，禁止自行判斷略過任何步驟。");
        sb.AppendLine($"{ruleNo++}. **安全底線**：禁止空 `catch`、禁止硬編碼密鑰、狀態變更端點須有驗證與防偽保護。");
        if (opts.RequireTestsBeforeDone)
        {
            sb.AppendLine($"{ruleNo++}. **品質閘門**：修改後必須執行 `powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1`（或 `./scripts/quality-gate.sh`），未全數通過前禁止宣告完成。");
        }

        if (opts.RequireEvidenceOutput)
        {
            sb.AppendLine($"{ruleNo++}. **證據回報**：最終回覆必須逐項列出每道檢查的實際執行結果，不可僅口頭宣稱已檢查。");
        }

        return sb.ToString();
    }

    private static string BuildClaudeSettingsJson(HarnessAssemblyOptionsViewModel opts)
    {
        var stopCommand = opts.RequireTestsBeforeDone
            ? "echo '[HARNESS GUARDRAIL] Before finishing, verify scripts/quality-gate passed and output all command results.'"
            : "echo '[HARNESS GUARDRAIL] Verify spec and security rules were followed.'";

        var denyRules = new List<string>();
        foreach (var rawPattern in opts.ProtectedPaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            denyRules.Add($"Edit({rawPattern})");
            denyRules.Add($"Write({rawPattern})");
        }

        denyRules.Add("Bash(rm -rf *)");

        var payload = new
        {
            permissions = new
            {
                deny = denyRules
            },
            hooks = new
            {
                PreToolUse = new object[]
                {
                    new
                    {
                        matcher = "Edit|Write",
                        hooks = new object[]
                        {
                            new
                            {
                                type = "command",
                                command = $"echo '[HARNESS GUARDRAIL] Spec-First ({opts.SpecFilePath}) & Scope Guard active.'"
                            }
                        }
                    }
                },
                Stop = new object[]
                {
                    new
                    {
                        hooks = new object[]
                        {
                            new
                            {
                                type = "command",
                                command = stopCommand
                            }
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildQualityGatePs1(HarnessAssemblyOptionsViewModel opts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = \"Stop\"");
        sb.AppendLine($"Write-Host \"=== {opts.ProjectName} Quality Gate ===\" -ForegroundColor Cyan");
        sb.AppendLine();

        AppendPsStep(sb, "Build", opts.BuildCommand);
        AppendPsStep(sb, "Format", opts.FormatCommand);
        AppendPsStep(sb, "Test", opts.TestCommand);
        AppendPsStep(sb, "Security Scan", opts.SecurityScanCommand);

        sb.AppendLine("Write-Host \"`n=== ALL QUALITY GATE CHECKS PASSED ===\" -ForegroundColor Green");
        return sb.ToString();
    }

    private static void AppendPsStep(StringBuilder sb, string label, string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        sb.AppendLine($"Write-Host \"`n=== [{label}] {command} ===\" -ForegroundColor Cyan");
        sb.AppendLine(command);
        sb.AppendLine("if ($LASTEXITCODE -ne 0) {");
        sb.AppendLine($"    Write-Error \"{label} failed with exit code $LASTEXITCODE\"");
        sb.AppendLine("    exit $LASTEXITCODE");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static string BuildQualityGateSh(HarnessAssemblyOptionsViewModel opts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#!/usr/bin/env bash");
        sb.AppendLine("set -euo pipefail");
        sb.AppendLine($"echo \"=== {opts.ProjectName} Quality Gate ===\"");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(opts.BuildCommand))
        {
            sb.AppendLine($"echo \"=== [Build] {opts.BuildCommand} ===\"");
            sb.AppendLine(opts.BuildCommand);
        }

        if (!string.IsNullOrWhiteSpace(opts.FormatCommand))
        {
            sb.AppendLine($"echo \"=== [Format] {opts.FormatCommand} ===\"");
            sb.AppendLine(opts.FormatCommand);
        }

        if (!string.IsNullOrWhiteSpace(opts.TestCommand))
        {
            sb.AppendLine($"echo \"=== [Test] {opts.TestCommand} ===\"");
            sb.AppendLine(opts.TestCommand);
        }

        if (!string.IsNullOrWhiteSpace(opts.SecurityScanCommand))
        {
            sb.AppendLine($"echo \"=== [Security] {opts.SecurityScanCommand} ===\"");
            sb.AppendLine(opts.SecurityScanCommand);
        }

        sb.AppendLine("echo \"=== ALL QUALITY GATE CHECKS PASSED ===\"");
        return sb.ToString();
    }

    private static string BuildGitPreCommitHook(HarnessAssemblyOptionsViewModel opts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#!/usr/bin/env bash");
        sb.AppendLine("set -euo pipefail");
        sb.AppendLine("echo \"[Harness Pre-Commit Gate] Checking staged files...\"");
        sb.AppendLine();
        sb.AppendLine("# 1. Block common hardcoded secrets in staged diff");
        sb.AppendLine("if git diff --cached --unified=0 | grep -E \"^\\+.*(BEGIN PRIVATE KEY|ghp_[A-Za-z0-9]{20,}|sk-[A-Za-z0-9]{20,})\" >/dev/null; then");
        sb.AppendLine("  echo \"ERROR: Potential hardcoded secret detected in staged changes. Commit blocked.\"");
        sb.AppendLine("  exit 1");
        sb.AppendLine("fi");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(opts.FormatCommand))
        {
            sb.AppendLine($"echo \"[Harness Pre-Commit Gate] Running format check: {opts.FormatCommand}\"");
            sb.AppendLine(opts.FormatCommand);
        }

        sb.AppendLine("echo \"[Harness Pre-Commit Gate] Passed.\"");
        return sb.ToString();
    }

    private static string BuildInstallHooksPs1() =>
        """
        $ErrorActionPreference = "Stop"
        git config core.hooksPath .githooks
        Write-Host "Git pre-commit hooks enabled (.githooks)." -ForegroundColor Green
        """;

    private static string BuildInstallHooksSh() =>
        """
        #!/usr/bin/env bash
        set -euo pipefail
        chmod +x .githooks/pre-commit scripts/quality-gate.sh
        git config core.hooksPath .githooks
        echo "Git pre-commit hooks enabled (.githooks)."
        """;

    private async Task<(byte[]? Content, string? Error)> DownloadRawContentAsync(
        string rawUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return (null, "RawUrl 為空");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, rawUrl);
            request.Headers.UserAgent.ParseAdd("ECCSELECTION-Bundle/1.0");

            if (!string.IsNullOrWhiteSpace(_options.PersonalAccessToken) &&
                IsAllowedRawGitHubUri(rawUrl))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.PersonalAccessToken);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var statusError = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                _logger.LogWarning("Failed to download ECC raw file {RawUrl}: {StatusError}", rawUrl, statusError);
                return (null, statusError);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return (bytes, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Exception while downloading ECC raw file {RawUrl}", rawUrl);
            return (null, ex.Message);
        }
    }

    private async Task<List<string>> FetchRepoBlobPathsAsync(CancellationToken cancellationToken)
    {
        var treeUrl = $"https://api.github.com/repos/{_options.Owner}/{_options.Repo}/git/trees/{_options.Branch}?recursive=1";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, treeUrl);
            request.Headers.UserAgent.ParseAdd("ECCSELECTION-Bundle/1.0");
            if (!string.IsNullOrWhiteSpace(_options.PersonalAccessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.PersonalAccessToken);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub Trees API returned {StatusCode} when resolving Skill auxiliary files.", response.StatusCode);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("tree", out var treeElem) || treeElem.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var paths = new List<string>();
            foreach (var item in treeElem.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var typeElem) &&
                    string.Equals(typeElem.GetString(), "blob", StringComparison.OrdinalIgnoreCase) &&
                    item.TryGetProperty("path", out var pathElem))
                {
                    var path = pathElem.GetString();
                    if (!string.IsNullOrWhiteSpace(path) &&
                        path.StartsWith("skills/", StringComparison.OrdinalIgnoreCase) &&
                        !path.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(path);
                    }
                }
            }

            return paths;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to fetch GitHub tree for Skill auxiliary files. Continuing with SKILL.md only.");
            return [];
        }
    }

    private async Task BundleSkillAuxiliaryFilesAsync(
        ZipArchive archive,
        EccComponent skillComponent,
        string rootDir,
        IReadOnlyList<string> skillTreePaths,
        HashSet<string> addedPaths,
        List<string> errors,
        StringBuilder manifest,
        CancellationToken cancellationToken)
    {
        var skillRepoPath = (skillComponent.Path ?? string.Empty).Replace('\\', '/').TrimStart('/');
        var lastSlash = skillRepoPath.LastIndexOf('/');
        if (lastSlash <= 0)
        {
            return;
        }

        var folderPrefix = skillRepoPath[..(lastSlash + 1)];
        var auxPaths = skillTreePaths
            .Where(p => p.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var auxPath in auxPaths)
        {
            var safeAuxPath = $"{rootDir}/{auxPath}".Replace("..", string.Empty).TrimStart('/');
            if (!addedPaths.Add(safeAuxPath))
            {
                continue;
            }

            var auxRawUrl = $"https://raw.githubusercontent.com/{_options.Owner}/{_options.Repo}/{_options.Branch}/{auxPath}";
            var (auxContent, auxError) = await DownloadRawContentAsync(auxRawUrl, cancellationToken);
            if (auxContent is null)
            {
                errors.Add($"{skillComponent.Name} auxiliary ({auxRawUrl}): {auxError}");
                continue;
            }

            await WriteBytesEntryAsync(archive, safeAuxPath, auxContent, cancellationToken);
            manifest.AppendLine($"| SkillAsset | `{skillComponent.Name}` | `{safeAuxPath}` | {auxRawUrl} |");
        }
    }

    private static bool IsBuiltInPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.TrimStart().StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase);

    private async Task<(byte[]? Content, string? Error)> ReadBuiltInContentAsync(
        string componentPath,
        CancellationToken cancellationToken)
    {
        var normalized = (componentPath ?? string.Empty).Trim().Replace('\\', '/');
        if (!normalized.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return (null, "Invalid built-in component path prefix.");
        }

        var subPath = normalized[BuiltInPrefix.Length..].TrimStart('/');
        if (string.IsNullOrWhiteSpace(subPath) ||
            subPath.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(subPath))
        {
            _logger.LogWarning("Rejected unsafe built-in component path: {Path}", componentPath);
            return (null, "Unsafe built-in component path.");
        }

        var builtInRoot = Path.GetFullPath(Path.Combine(_contentRootPath, "BuiltInEcc"));
        var builtInRootWithSeparator = builtInRoot.EndsWith(Path.DirectorySeparatorChar)
            ? builtInRoot
            : builtInRoot + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(builtInRoot, subPath.Replace('/', Path.DirectorySeparatorChar)));

        if (!fullPath.StartsWith(builtInRootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Path traversal attempt blocked for built-in component path: {Path}", componentPath);
            return (null, "Built-in component path escapes root directory.");
        }

        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("Built-in component file not found at {FullPath}", fullPath);
            return (null, $"Built-in file not found: {subPath}");
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            return (bytes, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to read built-in component file at {FullPath}", fullPath);
            return (null, ex.Message);
        }
    }

    private static bool IsAllowedRawGitHubUri(string rawUrl) =>
        Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase);

    private static string BuildSafeLocalPath(EccComponent component, string targetPlatform)
    {
        var rootDir = string.Equals(targetPlatform, "Antigravity", StringComparison.OrdinalIgnoreCase)
            ? ".ecc"
            : ".claude";
        var safeName = SanitizeFileNameSegment(component.Name);
        var safeRepoPath = SanitizeRelativeRepoPath(component.Path);

        var suggested = component.ComponentType switch
        {
            ComponentType.Agent => $"{rootDir}/agents/{safeName}.md",
            ComponentType.Skill => $"{rootDir}/skills/{safeName}/SKILL.md",
            ComponentType.Command => $"{rootDir}/commands/{safeName}.md",
            ComponentType.Rule => $"{rootDir}/{safeRepoPath}",
            _ => $"{rootDir}/{safeRepoPath}"
        };

        return suggested.Replace("..", string.Empty, StringComparison.Ordinal).TrimStart('/');
    }

    private static string SanitizeFileNameSegment(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return "component";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(rawName.Length);
        foreach (var ch in rawName.Trim())
        {
            if (ch == '/' || ch == '\\' || Array.IndexOf(invalidChars, ch) >= 0)
            {
                sb.Append('-');
            }
            else
            {
                sb.Append(ch);
            }
        }

        var sanitized = sb.ToString().Replace("..", "-", StringComparison.Ordinal).Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "component" : sanitized;
    }

    private static string SanitizeRelativeRepoPath(string? rawPath)
    {
        var segments = (rawPath ?? string.Empty)
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s != "." && s != "..")
            .ToArray();

        return segments.Length == 0 ? "component.md" : string.Join('/', segments);
    }

    private static async Task WriteBytesEntryAsync(
        ZipArchive archive,
        string entryName,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(content, cancellationToken);
    }

    private static Task WriteTextEntryAsync(
        ZipArchive archive,
        string entryName,
        string content,
        CancellationToken cancellationToken) =>
        WriteBytesEntryAsync(archive, entryName, Encoding.UTF8.GetBytes(content), cancellationToken);
}
