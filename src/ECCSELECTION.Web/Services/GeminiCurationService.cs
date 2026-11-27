using System.Text;
using System.Text.Json;
using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Options;
using ECCSELECTION.Web.Services.Interfaces;
using ECCSELECTION.Web.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECCSELECTION.Web.Services;

public class GeminiCurationService : IGeminiCurationService
{
    private const string DefaultGeminiModel = "gemini-2.5-flash";

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<LlmEnrichmentOptions> _optionsMonitor;
    private readonly ILogger<GeminiCurationService> _logger;
    private readonly IWebHostEnvironment? _environment;

    public GeminiCurationService(
        HttpClient httpClient,
        IOptionsMonitor<LlmEnrichmentOptions> optionsMonitor,
        ILogger<GeminiCurationService> logger,
        IWebHostEnvironment? environment = null)
    {
        _httpClient = httpClient;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
        _environment = environment;
    }

    public bool IsApiConfigured
    {
        get
        {
            var opts = _optionsMonitor.CurrentValue;
            return opts.Enabled && !string.IsNullOrWhiteSpace(opts.ApiKey);
        }
    }

    public async Task<WizardResultViewModel> CurateAsync(
        WizardResultViewModel baselineResult,
        CancellationToken cancellationToken = default)
    {
        var candidates = CollectUniqueCandidates(baselineResult).Take(35).ToList();
        baselineResult.IsGeminiApiConfigured = IsApiConfigured;
        baselineResult.ChromeGeminiPrompt = BuildCurationPrompt(baselineResult.Request, candidates);
        baselineResult.HarnessPlaybookMarkdown = BuildDeterministicPlaybook(baselineResult);

        if (!IsApiConfigured || candidates.Count == 0)
        {
            baselineResult.CurationSource = "Deterministic";
            return baselineResult;
        }

        try
        {
            var rawResponse = await CallGeminiAsync(baselineResult.ChromeGeminiPrompt, cancellationToken);
            if (!string.IsNullOrWhiteSpace(rawResponse) &&
                TryApplyExternalCurationJson(baselineResult, rawResponse, out _))
            {
                baselineResult.CurationSource = "Gemini API";
                return baselineResult;
            }

            baselineResult.CurationSource = "Deterministic (Gemini Fallback)";
            return baselineResult;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Gemini curation timed out. Falling back to deterministic converged result.");
            baselineResult.CurationSource = "Deterministic (Gemini Fallback)";
            return baselineResult;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Gemini curation failed. Falling back to deterministic converged result.");
            baselineResult.CurationSource = "Deterministic (Gemini Fallback)";
            return baselineResult;
        }
    }

    public bool TryApplyExternalCurationJson(
        WizardResultViewModel result,
        string rawJson,
        out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            errorMessage = "請貼上 Gemini 回傳的 JSON 內容。";
            return false;
        }

        try
        {
            var cleaned = LlmClientHelper.StripCodeFence(rawJson);
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            if (!root.TryGetProperty("selectedIds", out var idsElem) || idsElem.ValueKind != JsonValueKind.Array)
            {
                errorMessage = "JSON 缺少 selectedIds 陣列欄位。";
                return false;
            }

            var selectedIds = new HashSet<int>();
            foreach (var item in idsElem.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var id))
                {
                    selectedIds.Add(id);
                }
            }

            if (selectedIds.Count == 0)
            {
                errorMessage = "selectedIds 不可為空，請至少包含一個元件 ID。";
                return false;
            }

            var reasonMap = new Dictionary<int, string>();
            if (root.TryGetProperty("reasons", out var reasonsElem) && reasonsElem.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in reasonsElem.EnumerateObject())
                {
                    if (int.TryParse(prop.Name, out var id) && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var reason = prop.Value.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(reason))
                        {
                            reasonMap[id] = reason;
                        }
                    }
                }
            }

            ApplyCurationSelection(result, selectedIds, reasonMap);

            if (root.TryGetProperty("playbookMarkdown", out var pbElem) && pbElem.ValueKind == JsonValueKind.String)
            {
                var playbook = pbElem.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(playbook))
                {
                    result.HarnessPlaybookMarkdown = playbook;
                }
            }

            result.CurationSource = "Chrome Gemini Web";
            return true;
        }
        catch (JsonException ex)
        {
            errorMessage = $"JSON 格式解析失敗：{ex.Message}";
            return false;
        }
    }

    public bool ApplyExternalCuration(
        WizardResultViewModel baselineResult,
        string? rawJson,
        out string? errorMessage)
    {
        var candidates = CollectUniqueCandidates(baselineResult).Take(35).ToList();
        baselineResult.IsGeminiApiConfigured = IsApiConfigured;
        baselineResult.ChromeGeminiPrompt = BuildCurationPrompt(baselineResult.Request, candidates);
        baselineResult.HarnessPlaybookMarkdown = BuildDeterministicPlaybook(baselineResult);

        return TryApplyExternalCurationJson(baselineResult, rawJson ?? string.Empty, out errorMessage);
    }

    public async Task<string> SaveLocalGeminiKeyAsync(
        string apiKey,
        string? model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("請輸入有效的 Gemini API Key。", nameof(apiKey));
        }

        var selectedModel = string.IsNullOrWhiteSpace(model) ? DefaultGeminiModel : model.Trim();
        var contentRoot = _environment?.ContentRootPath ?? Directory.GetCurrentDirectory();
        var localConfigPath = Path.Combine(contentRoot, "appsettings.Development.local.json");

        var payload = new
        {
            LlmEnrichment = new
            {
                Enabled = true,
                Endpoint = "https://generativelanguage.googleapis.com/v1beta",
                ApiKey = apiKey.Trim(),
                Model = selectedModel
            }
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(localConfigPath, json, Encoding.UTF8, cancellationToken);
        return selectedModel;
    }

    private async Task<string?> CallGeminiAsync(string prompt, CancellationToken cancellationToken)
    {
        var opts = _optionsMonitor.CurrentValue;
        return await LlmClientHelper.SendPromptAsync(
            _httpClient,
            opts,
            systemPrompt: null,
            userPrompt: prompt,
            temperature: 0.1,
            maxTokens: null,
            responseMimeType: "application/json",
            TimeSpan.FromSeconds(20),
            _logger,
            cancellationToken);
    }

    private static void ApplyCurationSelection(
        WizardResultViewModel result,
        HashSet<int> selectedIds,
        Dictionary<int, string> reasonMap)
    {
        var allItems = CollectUniqueCandidates(result).ToList();
        var movedToCandidates = new List<RecommendedComponentItem>();

        foreach (var item in allItems)
        {
            if (reasonMap.TryGetValue(item.ComponentId, out var geminiReason))
            {
                item.MatchReasons.Insert(0, $"[Gemini 精選] {geminiReason}");
            }
        }

        var curatedSuites = new List<HarnessSuiteViewModel>();
        foreach (var suite in result.Suites)
        {
            var keepLead = selectedIds.Contains(suite.LeadAgent.ComponentId);
            if (!keepLead)
            {
                foreach (var item in suite.AllItems)
                {
                    if (!selectedIds.Contains(item.ComponentId))
                    {
                        movedToCandidates.Add(item);
                    }
                }

                continue;
            }

            var keptSkills = new List<RecommendedComponentItem>();
            foreach (var s in suite.PairedSkills)
            {
                if (selectedIds.Contains(s.ComponentId)) keptSkills.Add(s);
                else movedToCandidates.Add(s);
            }

            var keptRules = new List<RecommendedComponentItem>();
            foreach (var r in suite.PairedRules)
            {
                if (selectedIds.Contains(r.ComponentId)) keptRules.Add(r);
                else movedToCandidates.Add(r);
            }

            var keptCommands = new List<RecommendedComponentItem>();
            foreach (var c in suite.PairedCommands)
            {
                if (selectedIds.Contains(c.ComponentId)) keptCommands.Add(c);
                else movedToCandidates.Add(c);
            }

            suite.PairedSkills = keptSkills;
            suite.PairedRules = keptRules;
            suite.PairedCommands = keptCommands;
            curatedSuites.Add(suite);
        }

        if (curatedSuites.Count > 0)
        {
            result.Suites = curatedSuites;
        }

        var suiteIds = result.Suites.SelectMany(s => s.AllItems).Select(i => i.ComponentId).ToHashSet();
        result.AdditionalCandidates = result.AdditionalCandidates
            .Concat(movedToCandidates)
            .Where(i => !suiteIds.Contains(i.ComponentId))
            .GroupBy(i => i.ComponentId)
            .Select(g => g.First())
            .OrderByDescending(i => i.Score)
            .ToList();
    }

    private static List<RecommendedComponentItem> CollectUniqueCandidates(WizardResultViewModel result)
    {
        return result.Suites
            .SelectMany(s => s.AllItems)
            .Concat(result.AdditionalCandidates)
            .GroupBy(i => i.ComponentId)
            .Select(g => g.First())
            .ToList();
    }

    private static string BuildCurationPrompt(
        WizardRequestViewModel req,
        IReadOnlyList<RecommendedComponentItem> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("你是資深 AI Engineering Harness 架構師。請從下方候選 ECC 元件中，為此專案「精挑細選」真正必要的精簡核心組合，避免把不必要或重複的通用檔案塞進專案 Context。");
        sb.AppendLine();
        sb.AppendLine("## 專案條件");
        sb.AppendLine($"- 專案名稱：{req.AssemblyOptions.ProjectName}");
        sb.AppendLine($"- 型態 / 階段 / 架構：{req.ProjectType} / {req.ProjectLifecycle} / {req.ArchStyle}");
        sb.AppendLine($"- 技術棧：{string.Join(", ", req.TechStacks)}");
        sb.AppendLine($"- 目標角色：{string.Join(", ", req.SdlcRoles)}");
        if (!string.IsNullOrWhiteSpace(req.ProjectPurpose))
        {
            sb.AppendLine($"- 用途說明：{req.ProjectPurpose}");
        }

        var opts = req.AssemblyOptions;
        sb.AppendLine($"- 已啟用防堵卡點：規格先行卡點={opts.RequireSpecFirst}、品質閘門腳本={opts.RequireTestsBeforeDone}、證據輸出契約={opts.RequireEvidenceOutput}、Git Pre-Commit 卡點={opts.IncludeGitPreCommitHook}、IDE 自動載入規則與Hooks={opts.IncludeIdeAutoRules}");

        sb.AppendLine();
        sb.AppendLine("## 收斂規則（務必遵守）");
        sb.AppendLine("- 僅挑選 2~3 個最核心的 Agent，每個 Agent 僅搭配 1~2 個最關鍵的 Skill、1~2 個 Rule、0~1 個 Command。");
        sb.AppendLine("- 僅可從下方候選清單的 ID 挑選，嚴禁編造不存在的 ID。");
        sb.AppendLine();
        sb.AppendLine("## 候選元件清單");
        foreach (var c in candidates)
        {
            var desc = string.IsNullOrWhiteSpace(c.SummaryZh) ? c.DescriptionEn : c.SummaryZh;
            if (desc.Length > 140) desc = desc[..140];
            sb.AppendLine($"- ID={c.ComponentId} | {c.ComponentType} | {c.Name} | Stacks={c.TechStacks} | {desc}");
        }

        sb.AppendLine();
        sb.AppendLine("## 回傳格式（請只輸出純 JSON，不要加多餘文字）");
        sb.AppendLine("""
            {
              "selectedIds": [1, 2, 3],
              "reasons": {
                "1": "入選原因（繁體中文 1 句）"
              },
              "playbookMarkdown": "# Harness 運作說明書\n（用繁體中文說明此專案各階段如何呼叫選中的 Agent/Command、Rule 如何自動生效、以及防堵卡點何時攔截）"
            }
            """);

        return sb.ToString();
    }

    public static string BuildDeterministicPlaybook(WizardResultViewModel result)
    {
        var opts = result.Request.AssemblyOptions;
        var sb = new StringBuilder();
        sb.AppendLine($"# {opts.ProjectName} — Harness 運作說明書（Operational Playbook）");
        sb.AppendLine();
        sb.AppendLine($"- **專案配置**：`{result.Request.ProjectType}` / `{result.Request.ProjectLifecycle}` / `{result.Request.ArchStyle}`（技術棧：`{string.Join(", ", result.Request.TechStacks)}`）");
        sb.AppendLine($"- **目標環境**：`{opts.TargetEnvironment}`");
        sb.AppendLine($"- **規格書卡點**：`{opts.SpecFilePath}`");
        sb.AppendLine();
        sb.AppendLine("## 一、SDLC 日常開發動線");
        sb.AppendLine($"1. **規格先行（Spec-First）**：任何需求異動前，先更新 `{opts.SpecFilePath}`。");
        sb.AppendLine("2. **AI 協作執行**：依任務階段呼叫以下精選 Lead Agent 與 Command：");

        foreach (var suite in result.Suites)
        {
            var cmds = suite.PairedCommands.Count > 0
                ? string.Join(", ", suite.PairedCommands.Select(c => $"`/{c.Name}`"))
                : "（無額外 Command）";
            sb.AppendLine($"   - **`{suite.LeadAgent.Name}`**（涵蓋：{string.Join("、", suite.CoveredRoles)}）— 搭配指令：{cmds}");
        }

        sb.AppendLine("3. **自動背景規範**：已選的 `Rules` 與根目錄 `AGENTS.md` / `CLAUDE.md` 會於每次編輯與對話自動載入，不需手動呼叫。");
        sb.AppendLine();
        sb.AppendLine("## 二、已啟用之防堵卡點（Guardrails — 何時會攔截錯誤）");
        sb.AppendLine($"- **規格先行卡點**：{(opts.RequireSpecFirst ? $"已啟用 — 修改核心程式前未更新 `{opts.SpecFilePath}` 時阻擋。" : "未啟用")}");
        sb.AppendLine($"- **品質閘門腳本**：{(opts.RequireTestsBeforeDone ? $"已啟用 — 必須執行 `scripts/quality-gate.ps1` / `.sh`（建置：`{opts.BuildCommand}`、格式：`{opts.FormatCommand}`、測試：`{opts.TestCommand}`、安全：`{opts.SecurityScanCommand}`），任一失敗即非零結束碼阻擋。" : "未啟用")}");
        sb.AppendLine($"- **證據輸出契約**：{(opts.RequireEvidenceOutput ? "已啟用 — 未實際執行品質閘門並逐項附上終端實測輸出與通過數量前，嚴禁宣告任務完成。" : "未啟用")}");
        sb.AppendLine($"- **Git Pre-Commit 卡點**：{(opts.IncludeGitPreCommitHook ? "已啟用 — `.githooks/pre-commit`（需先執行 `scripts/install-hooks.ps1` 或 `.sh`）於 `git commit` 前攔截密鑰外洩與格式違規。" : "未啟用")}");
        sb.AppendLine($"- **IDE 自動載入規則 & Hooks**：{(opts.IncludeIdeAutoRules ? $"已啟用 — 自動載入 `AGENTS.md`、`CLAUDE.md`、`.agents/rules/workflow-guardrails.md`，並透過 `.claude/settings.json` 的 `permissions.deny`（阻擋 `{opts.ProtectedPaths}` 與 `rm -rf`）及 `PreToolUse` / `Stop` Hooks 防堵。" : "未啟用")}");

        return sb.ToString();
    }
}
