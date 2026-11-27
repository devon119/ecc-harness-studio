using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ECCSELECTION.Web.Options;
using ECCSELECTION.Web.Services.Interfaces;
using ECCSELECTION.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECCSELECTION.Web.Services;

/// <summary>
/// 混合式需求用途與需求文件解析服務：優先使用 LLM 深度語意分析，未設定或失敗時非阻塞降級為確定性規則解析。
/// </summary>
public partial class RequirementAnalyzerService : IRequirementAnalyzerService
{
    public const long MaxDocumentBytes = 2 * 1024 * 1024; // 2 MB
    private const int MaxAnalyzedChars = 20_000;
    private const int MaxLlmPromptChars = 6_000;

    private readonly HttpClient? _httpClient;
    private readonly LlmEnrichmentOptions _llmOptions;
    private readonly ILogger<RequirementAnalyzerService> _logger;

    public RequirementAnalyzerService()
        : this(null, Microsoft.Extensions.Options.Options.Create(new LlmEnrichmentOptions()), NullLogger<RequirementAnalyzerService>.Instance)
    {
    }

    public RequirementAnalyzerService(
        HttpClient? httpClient,
        IOptions<LlmEnrichmentOptions> llmOptions,
        ILogger<RequirementAnalyzerService> logger)
    {
        _httpClient = httpClient;
        _llmOptions = llmOptions.Value;
        _logger = logger;
    }

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".json", ".yaml", ".yml", ".csv", ".xml"
    };

    private static readonly (string Keyword, string[] Triggers)[] DomainKeywordRules =
    [
        ("security", ["security", "auth", "oauth", "jwt", "rbac", "csrf", "xss", "安全", "權限", "驗證", "稽核", "加密"]),
        ("payment", ["payment", "billing", "checkout", "stripe", "儲值", "金流", "付款", "訂單", "交易"]),
        ("api", ["api", "rest", "restful", "graphql", "grpc", "endpoint", "microservice", "微服務", "介面"]),
        ("database", ["database", "schema", "migration", "ef core", "sql", "query", "資料庫", "資料表"]),
        ("tdd", ["tdd", "unit test", "xunit", "pytest", "jest", "coverage", "測試", "單元測試", "覆蓋率"]),
        ("e2e", ["e2e", "playwright", "cypress", "end-to-end", "整合測試", "端對端"]),
        ("refactor", ["refactor", "legacy", "brownfield", "cleanup", "重構", "舊系統", "既有系統", "技術債"]),
        ("architect", ["architect", "architecture", "system design", "spec", "prd", "架構", "規格", "藍圖"]),
        ("review", ["review", "audit", "lint", "quality", "審查", "品質", "程式碼檢查"]),
        ("performance", ["performance", "cache", "redis", "benchmark", "latency", "效能", "快取", "高併發"]),
        ("frontend", ["frontend", "ui", "ux", "css", "bootstrap", "tailwind", "畫面", "前端", "網頁"]),
        ("game", ["game", "roblox", "luau", "godot", "unity", "ecs", "遊戲", "玩家", "關卡"]),
        ("mobile", ["mobile", "android", "kotlin", "ios", "app", "手機", "行動裝置"])
    ];

    private static readonly (string Stack, string[] Triggers)[] TechStackRules =
    [
        ("dotnet", ["dotnet", ".net", "asp.net", "c#", "csharp", "ef core", "blazor"]),
        ("kotlin", ["kotlin", "android", "jetpack"]),
        ("react", ["react", "nextjs", "next.js", "jsx", "tsx"]),
        ("vue", ["vue", "vuejs", "vue.js", "nuxt"]),
        ("python", ["python", "django", "fastapi", "flask", "pytest"]),
        ("go", ["golang", "gin", "fiber"]),
        ("java", ["java", "spring", "springboot", "spring boot"]),
        ("luau", ["luau", "roblox", "lua"]),
        ("godot", ["godot", "gdscript"]),
        ("cpp", ["c++", "cpp", "unreal"]),
        ("sqlserver", ["sql server", "mssql", "sqlserver"]),
        ("sqlite", ["sqlite"]),
        ("postgres", ["postgres", "postgresql", "npgsql"]),
        ("mysql", ["mysql", "mariadb"])
    ];

    public async Task<RequirementAnalysisResult> AnalyzeAsync(
        string? purposeText,
        IFormFile? document,
        CancellationToken cancellationToken = default)
    {
        var combined = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(purposeText))
        {
            combined.AppendLine(purposeText.Trim());
        }

        string? fileName = null;
        if (document is not null && document.Length > 0)
        {
            fileName = Path.GetFileName(document.FileName);
            var ext = Path.GetExtension(fileName);

            if (string.IsNullOrWhiteSpace(ext) || !AllowedExtensions.Contains(ext))
            {
                return new RequirementAnalysisResult
                {
                    IsSuccess = false,
                    SourceFileName = fileName,
                    ErrorMessage = $"不支援的需求文件格式「{ext}」。僅允許：{string.Join("、", AllowedExtensions)}。"
                };
            }

            if (document.Length > MaxDocumentBytes)
            {
                return new RequirementAnalysisResult
                {
                    IsSuccess = false,
                    SourceFileName = fileName,
                    ErrorMessage = $"需求文件超過大小上限（最大 {MaxDocumentBytes / (1024 * 1024)} MB）。"
                };
            }

            await using var stream = document.OpenReadStream();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            var bytes = memoryStream.ToArray();

            if (bytes.Contains((byte)0))
            {
                return new RequirementAnalysisResult
                {
                    IsSuccess = false,
                    SourceFileName = fileName,
                    ErrorMessage = "需求文件內容似乎為二進位檔案，請上傳純文字或 Markdown 文件。"
                };
            }

            var docText = Encoding.UTF8.GetString(bytes);
            if (docText.Length > MaxAnalyzedChars)
            {
                docText = docText[..MaxAnalyzedChars];
            }

            combined.AppendLine(docText);
        }

        var combinedText = combined.ToString();
        var baseline = AnalyzeText(combinedText, fileName);
        if (!baseline.IsSuccess || string.IsNullOrWhiteSpace(combinedText))
        {
            return baseline;
        }

        return await TryEnrichWithLlmAsync(combinedText, baseline, cancellationToken);
    }

    private async Task<RequirementAnalysisResult> TryEnrichWithLlmAsync(
        string combinedText,
        RequirementAnalysisResult baseline,
        CancellationToken cancellationToken)
    {
        if (_httpClient is null ||
            !_llmOptions.Enabled ||
            string.IsNullOrWhiteSpace(_llmOptions.ApiKey))
        {
            baseline.AnalysisSource = "Deterministic";
            return baseline;
        }

        try
        {
            var snippet = combinedText.Length > MaxLlmPromptChars
                ? combinedText[..MaxLlmPromptChars]
                : combinedText;

            const string systemPrompt = """
                你是資深軟體系統架構師。請分析使用者的專案用途說明或需求文件，並只回傳一個合法的 JSON 物件（不要使用 markdown code fence），格式如下：
                {
                  "projectType": "Web|Mobile|API|Fullstack|Game",
                  "projectLifecycle": "Greenfield|Brownfield",
                  "archStyle": "MVC|Clean|MVVM|ECS",
                  "techStacks": ["dotnet","kotlin","react","vue","python","go","java","luau","godot","cpp","sqlserver","sqlite","postgres","mysql"],
                  "sdlcRoles": ["SA","SD","PG_FE","PG_BE","PG_DB","PG_TEST","QA"],
                  "keywords": ["英文小寫領域或工程關鍵字，最多10個，例如 security, payment, e2e, caching, rbac"],
                  "suggestedProjectName": "簡短英文 PascalCase 或 kebab-case 專案名稱",
                  "suggestedTargetEnvironment": "簡述目標執行環境與主要技術棧，例如 .NET 10 / ASP.NET Core / PostgreSQL",
                  "suggestedBuildCommand": "建置指令，例如 dotnet build -warnaserror",
                  "suggestedFormatCommand": "格式/Lint 檢查指令，例如 dotnet format --verify-no-changes --no-restore",
                  "suggestedTestCommand": "單元測試指令，例如 dotnet test --no-build --verbosity normal",
                  "suggestedSecurityScanCommand": "安全掃描指令，例如 npx --yes ecc-agentshield scan --path . --format text",
                  "summaryNote": "1-2 句繁體中文架構分析摘要"
                }
                """;

            var responseText = await LlmClientHelper.SendPromptAsync(
                _httpClient,
                _llmOptions,
                systemPrompt,
                snippet,
                temperature: 0.1,
                maxTokens: 450,
                responseMimeType: "application/json",
                TimeSpan.FromSeconds(15),
                _logger,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(responseText))
            {
                baseline.AnalysisSource = "Deterministic (LLM Fallback)";
                return baseline;
            }

            var rawJson = LlmClientHelper.StripCodeFence(responseText);
            using var parsed = JsonDocument.Parse(rawJson);
            MergeLlmInsights(parsed.RootElement, baseline);
            baseline.AnalysisSource = "LLM + Deterministic";
            return baseline;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "LLM requirement analysis timed out. Falling back to deterministic analysis.");
            baseline.AnalysisSource = "Deterministic (LLM Fallback)";
            return baseline;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "LLM requirement analysis failed. Falling back to deterministic analysis.");
            baseline.AnalysisSource = "Deterministic (LLM Fallback)";
            return baseline;
        }
    }

    private static void MergeLlmInsights(JsonElement root, RequirementAnalysisResult target)
    {
        if (root.TryGetProperty("projectType", out var ptElem))
        {
            var pt = ptElem.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(pt) && WizardCompatibility.Rules.ContainsKey(pt))
            {
                target.SuggestedProjectType = WizardCompatibility.Rules.Keys
                    .First(k => string.Equals(k, pt, StringComparison.OrdinalIgnoreCase));
            }
        }

        var rule = WizardCompatibility.GetRule(target.SuggestedProjectType) ?? WizardCompatibility.Rules["Web"];

        if (root.TryGetProperty("projectLifecycle", out var lcElem))
        {
            var lc = lcElem.GetString()?.Trim();
            if (WizardCompatibility.AllowedProjectLifecycles.Contains(lc ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                target.SuggestedProjectLifecycle = WizardCompatibility.AllowedProjectLifecycles
                    .First(x => string.Equals(x, lc, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (root.TryGetProperty("archStyle", out var archElem))
        {
            var arch = archElem.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(arch) &&
                rule.AllowedArchStyles.Contains(arch, StringComparer.OrdinalIgnoreCase))
            {
                target.SuggestedArchStyle = rule.AllowedArchStyles
                    .First(a => string.Equals(a, arch, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (root.TryGetProperty("techStacks", out var stacksElem) && stacksElem.ValueKind == JsonValueKind.Array)
        {
            var validStacks = stacksElem.EnumerateArray()
                .Select(x => x.GetString()?.Trim().ToLowerInvariant())
                .Where(x => !string.IsNullOrWhiteSpace(x) && rule.AllowedTechStacks.Contains(x!, StringComparer.OrdinalIgnoreCase))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (validStacks.Count > 0)
            {
                target.SuggestedTechStacks = validStacks;
            }
        }

        if (root.TryGetProperty("sdlcRoles", out var rolesElem) && rolesElem.ValueKind == JsonValueKind.Array)
        {
            var validRoles = rolesElem.EnumerateArray()
                .Select(x => x.GetString()?.Trim().ToUpperInvariant())
                .Where(x => !string.IsNullOrWhiteSpace(x) && rule.AllowedSdlcRoles.Contains(x!, StringComparer.OrdinalIgnoreCase))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (validRoles.Count > 0)
            {
                target.SuggestedSdlcRoles = validRoles;
            }
        }

        if (root.TryGetProperty("keywords", out var kwElem) && kwElem.ValueKind == JsonValueKind.Array)
        {
            var mergedKeywords = kwElem.EnumerateArray()
                .Select(x => x.GetString()?.Trim().ToLowerInvariant())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Concat(target.ExtractedKeywords)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(15)
                .ToList();

            target.ExtractedKeywords = mergedKeywords;
        }

        if (root.TryGetProperty("suggestedProjectName", out var nameElem))
        {
            var name = nameElem.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                target.SuggestedProjectName = name;
            }
        }

        target.SuggestedTargetEnvironment = ReadOptionalString(root, "suggestedTargetEnvironment") ?? target.SuggestedTargetEnvironment;
        target.SuggestedBuildCommand = ReadOptionalString(root, "suggestedBuildCommand") ?? target.SuggestedBuildCommand;
        target.SuggestedFormatCommand = ReadOptionalString(root, "suggestedFormatCommand") ?? target.SuggestedFormatCommand;
        target.SuggestedTestCommand = ReadOptionalString(root, "suggestedTestCommand") ?? target.SuggestedTestCommand;
        target.SuggestedSecurityScanCommand = ReadOptionalString(root, "suggestedSecurityScanCommand") ?? target.SuggestedSecurityScanCommand;

        if (root.TryGetProperty("summaryNote", out var noteElem))
        {
            var note = noteElem.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(note))
            {
                target.SummaryNote = $"[LLM 深度分析] {note}";
            }
        }
    }

    public void ApplySuggestedAssemblyDefaults(
        WizardRequestViewModel request,
        RequirementAnalysisResult? analysis)
    {
        var defaults = new HarnessAssemblyOptionsViewModel();
        var inferred = InferEngineeringCommands(request.ProjectType, request.TechStacks);

        if (string.Equals(request.AssemblyOptions.ProjectName, defaults.ProjectName, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(analysis?.SuggestedProjectName))
        {
            request.AssemblyOptions.ProjectName = analysis.SuggestedProjectName;
        }

        if (string.Equals(request.AssemblyOptions.TargetEnvironment, defaults.TargetEnvironment, StringComparison.Ordinal))
        {
            request.AssemblyOptions.TargetEnvironment = analysis?.SuggestedTargetEnvironment ?? inferred.TargetEnvironment;
        }

        if (string.Equals(request.AssemblyOptions.BuildCommand, defaults.BuildCommand, StringComparison.Ordinal))
        {
            request.AssemblyOptions.BuildCommand = analysis?.SuggestedBuildCommand ?? inferred.BuildCommand;
        }

        if (string.Equals(request.AssemblyOptions.FormatCommand, defaults.FormatCommand, StringComparison.Ordinal))
        {
            request.AssemblyOptions.FormatCommand = analysis?.SuggestedFormatCommand ?? inferred.FormatCommand;
        }

        if (string.Equals(request.AssemblyOptions.TestCommand, defaults.TestCommand, StringComparison.Ordinal))
        {
            request.AssemblyOptions.TestCommand = analysis?.SuggestedTestCommand ?? inferred.TestCommand;
        }

        if (string.Equals(request.AssemblyOptions.SecurityScanCommand, defaults.SecurityScanCommand, StringComparison.Ordinal))
        {
            request.AssemblyOptions.SecurityScanCommand = analysis?.SuggestedSecurityScanCommand ?? inferred.SecurityScanCommand;
        }
    }

    public RequirementAnalysisResult AnalyzeText(string? combinedText, string? sourceFileName = null)
    {
        var text = (combinedText ?? string.Empty).Trim();
        if (text.Length > MaxAnalyzedChars)
        {
            text = text[..MaxAnalyzedChars];
        }

        var lower = text.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lower))
        {
            return new RequirementAnalysisResult
            {
                IsSuccess = true,
                SourceFileName = sourceFileName,
                SummaryNote = "未提供需求說明或文件內容，將維持預設問券條件。"
            };
        }

        var keywords = ExtractKeywords(lower);
        var stacks = DetectTechStacks(lower);
        var projectType = InferProjectType(lower, stacks);
        var lifecycle = InferLifecycle(lower);
        var rule = WizardCompatibility.GetRule(projectType) ?? WizardCompatibility.Rules["Web"];

        var compatibleStacks = stacks
            .Where(s => rule.AllowedTechStacks.Contains(s, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (compatibleStacks.Count == 0)
        {
            compatibleStacks.AddRange(rule.DefaultTechStacks);
        }

        var archStyle = InferArchStyle(lower, rule);
        var roles = InferRoles(lower, rule);
        var (env, buildCmd, formatCmd, testCmd, secCmd) = InferEngineeringCommands(projectType, compatibleStacks);

        return new RequirementAnalysisResult
        {
            IsSuccess = true,
            SourceFileName = sourceFileName,
            SuggestedProjectType = projectType,
            SuggestedProjectLifecycle = lifecycle,
            SuggestedArchStyle = archStyle,
            SuggestedTechStacks = compatibleStacks,
            SuggestedSdlcRoles = roles,
            ExtractedKeywords = keywords,
            SuggestedTargetEnvironment = env,
            SuggestedBuildCommand = buildCmd,
            SuggestedFormatCommand = formatCmd,
            SuggestedTestCommand = testCmd,
            SuggestedSecurityScanCommand = secCmd,
            SummaryNote = BuildSummaryNote(sourceFileName, projectType, lifecycle, compatibleStacks, keywords)
        };
    }

    public static (string TargetEnvironment, string BuildCommand, string FormatCommand, string TestCommand, string SecurityScanCommand) InferEngineeringCommands(
        string? projectType,
        IReadOnlyCollection<string> techStacks)
    {
        var stackSet = new HashSet<string>(techStacks ?? [], StringComparer.OrdinalIgnoreCase);
        const string defaultSec = "npx --yes ecc-agentshield scan --path . --format text";

        if (stackSet.Contains("python"))
        {
            return ("Python 3.12 / FastAPI / pytest", "python -m compileall src", "ruff check .", "pytest", defaultSec);
        }

        if (stackSet.Contains("go"))
        {
            return ("Go 1.23", "go build ./...", "gofmt -l .", "go test ./...", defaultSec);
        }

        if (stackSet.Contains("kotlin"))
        {
            return ("Android / Kotlin / Gradle", "./gradlew assembleDebug", "./gradlew ktlintCheck", "./gradlew testDebugUnitTest", defaultSec);
        }

        if (stackSet.Contains("java"))
        {
            return ("Java 21 / Spring Boot", "./mvnw compile -q", "./mvnw spotless:check", "./mvnw test", defaultSec);
        }

        if (stackSet.Contains("luau"))
        {
            return ("Roblox Studio / Luau", "selene src/", "stylua --check src/", "busted tests/", defaultSec);
        }

        if (stackSet.Contains("godot"))
        {
            return ("Godot 4 / GDScript", "godot --headless --check-only --quit", "gdlint src/", "godot --headless -s addons/gut/gut_cmdln.gd", defaultSec);
        }

        if ((stackSet.Contains("react") || stackSet.Contains("vue")) && !stackSet.Contains("dotnet"))
        {
            return ("Node.js / TypeScript", "npm run build", "npm run lint", "npm test", defaultSec);
        }

        var envLabel = string.Equals(projectType, "API", StringComparison.OrdinalIgnoreCase)
            ? ".NET 10 / ASP.NET Core Web API"
            : ".NET 10 / ASP.NET Core MVC";

        return (
            envLabel,
            "dotnet build -warnaserror",
            "dotnet format --verify-no-changes --no-restore",
            "dotnet test --no-build --verbosity normal",
            defaultSec);
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var elem) && elem.ValueKind == JsonValueKind.String)
        {
            var val = elem.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(val) ? null : val;
        }

        return null;
    }

    private static List<string> ExtractKeywords(string lower)
    {
        var result = new List<string>();
        foreach (var (keyword, triggers) in DomainKeywordRules)
        {
            if (triggers.Any(t => ContainsTokenOrPhrase(lower, t)))
            {
                result.Add(keyword);
            }
        }

        foreach (var stack in DetectTechStacks(lower))
        {
            if (!result.Contains(stack, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(stack);
            }
        }

        return result.Take(12).ToList();
    }

    private static List<string> DetectTechStacks(string lower)
    {
        var stacks = new List<string>();
        foreach (var (stack, triggers) in TechStackRules)
        {
            if (triggers.Any(t => ContainsTokenOrPhrase(lower, t)))
            {
                stacks.Add(stack);
            }
        }

        // Special word-boundary check for "go" to avoid matching English verb "go"
        if (!stacks.Contains("go", StringComparer.OrdinalIgnoreCase) &&
            GoLanguageRegex().IsMatch(lower))
        {
            stacks.Add("go");
        }

        return stacks;
    }

    private static string InferProjectType(string lower, IReadOnlyList<string> stacks)
    {
        if (stacks.Any(s => s is "luau" or "godot") ||
            ContainsAny(lower, "roblox", "unity", "godot", "ecs", "遊戲", "玩家"))
        {
            return "Game";
        }

        if (stacks.Contains("kotlin", StringComparer.OrdinalIgnoreCase) ||
            ContainsAny(lower, "android", "ios", "mobile app", "行動裝置", "手機app"))
        {
            return "Mobile";
        }

        var hasFrontend = stacks.Any(s => s is "react" or "vue") ||
                          ContainsAny(lower, "frontend", "ui", "spa", "前端", "網頁介面");
        var hasApi = ContainsAny(lower, "microservice", "rest api", "graphql", "grpc", "微服務", "純後端", "api server");

        if (hasFrontend && hasApi)
        {
            return "Fullstack";
        }

        if (hasApi && !ContainsAny(lower, "mvc", "razor", "網頁", "網站", "portal"))
        {
            return "API";
        }

        if (hasFrontend && stacks.Any(s => s is "dotnet" or "python" or "go" or "java"))
        {
            return "Fullstack";
        }

        return "Web";
    }

    private static string InferLifecycle(string lower)
    {
        return ContainsAny(lower, "legacy", "brownfield", "refactor", "existing", "舊專案", "既有系統", "重構", "遷移")
            ? "Brownfield"
            : "Greenfield";
    }

    private static string InferArchStyle(string lower, ProjectTypeCompatibilityRule rule)
    {
        foreach (var arch in rule.AllowedArchStyles)
        {
            if (ContainsTokenOrPhrase(lower, arch.ToLowerInvariant()))
            {
                return arch;
            }
        }

        return rule.DefaultArchStyle;
    }

    private static List<string> InferRoles(string lower, ProjectTypeCompatibilityRule rule)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (ContainsAny(lower, "prd", "spec", "requirement", "需求", "規格", "分析")) roles.Add("SA");
        if (ContainsAny(lower, "design", "architect", "schema", "api", "架構", "設計")) roles.Add("SD");
        if (ContainsAny(lower, "frontend", "react", "vue", "ui", "razor", "前端", "畫面")) roles.Add("PG_FE");
        if (ContainsAny(lower, "backend", "api", "service", "auth", "payment", "後端", "微服務")) roles.Add("PG_BE");
        if (ContainsAny(lower, "database", "sql", "postgres", "mysql", "sqlite", "migration", "資料庫")) roles.Add("PG_DB");
        if (ContainsAny(lower, "test", "tdd", "xunit", "pytest", "e2e", "測試", "覆蓋率")) roles.Add("PG_TEST");
        if (ContainsAny(lower, "review", "security", "audit", "qa", "審查", "安全", "稽核")) roles.Add("QA");

        if (roles.Count == 0)
        {
            foreach (var r in rule.AllowedSdlcRoles)
            {
                roles.Add(r);
            }
        }
        else
        {
            // Always include QA and PG_TEST for guardrail completeness when roles were inferred
            roles.Add("PG_TEST");
            roles.Add("QA");
        }

        return rule.AllowedSdlcRoles
            .Where(r => roles.Contains(r))
            .ToList();
    }

    private static string BuildSummaryNote(
        string? fileName,
        string projectType,
        string lifecycle,
        IReadOnlyList<string> stacks,
        IReadOnlyList<string> keywords)
    {
        var source = string.IsNullOrWhiteSpace(fileName) ? "用途說明文字" : $"需求文件「{fileName}」";
        var stackText = stacks.Count > 0 ? string.Join(", ", stacks) : "無特定技術棧";
        var kwText = keywords.Count > 0 ? string.Join(", ", keywords) : "一般工程";
        return $"已解析{source}：建議 {projectType} / {lifecycle}，偵測技術棧 [{stackText}]，關鍵詞 [{kwText}]。";
    }

    private static bool ContainsAny(string lower, params string[] terms) =>
        terms.Any(t => ContainsTokenOrPhrase(lower, t));

    private static bool ContainsTokenOrPhrase(string lower, string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return false;
        }

        // For CJK or multi-word or symbol terms, substring match is reliable
        if (term.Any(c => c > 127) || term.Contains(' ') || term.Contains('#') || term.Contains('.'))
        {
            return lower.Contains(term, StringComparison.OrdinalIgnoreCase);
        }

        // Short ASCII words require boundary checks
        return Regex.IsMatch(lower, $@"\b{Regex.Escape(term)}\b", RegexOptions.IgnoreCase);
    }

    [GeneratedRegex(@"\bgolang\b|\bgo\s+(service|api|backend|1\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex GoLanguageRegex();
}
