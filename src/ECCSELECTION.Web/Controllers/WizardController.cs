using System.Text;
using System.Text.RegularExpressions;
using ECCSELECTION.Web.Services;
using ECCSELECTION.Web.Services.Interfaces;
using ECCSELECTION.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace ECCSELECTION.Web.Controllers;

public partial class WizardController : Controller
{
    private readonly IRecommendationService _recommendationService;
    private readonly IMarkdownExportService _markdownExportService;
    private readonly IHarnessBundleService _harnessBundleService;
    private readonly IRequirementAnalyzerService _requirementAnalyzerService;
    private readonly IGeminiCurationService _geminiCurationService;

    public WizardController(
        IRecommendationService recommendationService,
        IMarkdownExportService markdownExportService,
        IHarnessBundleService harnessBundleService,
        IRequirementAnalyzerService requirementAnalyzerService,
        IGeminiCurationService geminiCurationService)
    {
        _recommendationService = recommendationService;
        _markdownExportService = markdownExportService;
        _harnessBundleService = harnessBundleService;
        _requirementAnalyzerService = requirementAnalyzerService;
        _geminiCurationService = geminiCurationService;
    }

    [HttpGet]
    public IActionResult Index()
    {
        ViewBag.IsGeminiApiConfigured = _geminiCurationService.IsApiConfigured;
        return View(new WizardRequestViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGeminiKey(
        string? apiKey,
        string? model,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BadRequest(new { success = false, message = "請輸入有效的 Gemini API Key。" });
        }

        try
        {
            var selectedModel = await _geminiCurationService.SaveLocalGeminiKeyAsync(apiKey, model, cancellationToken);
            return Json(new
            {
                success = true,
                message = $"已安全儲存至本機 appsettings.Development.local.json（模型：{selectedModel}，已排除於 Git 版控之外）。"
            });
        }
        catch (ArgumentException ex)
        {
            TempData["Error"] = ex.Message;
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnalyzeRequirements(
        string? projectPurpose,
        IFormFile? requirementDocument,
        CancellationToken cancellationToken)
    {
        var analysis = await _requirementAnalyzerService.AnalyzeAsync(
            projectPurpose,
            requirementDocument,
            cancellationToken);

        if (!analysis.IsSuccess)
        {
            return BadRequest(analysis);
        }

        return Json(analysis);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Recommend(WizardRequestViewModel request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ProjectPurpose) || request.RequirementDocument is { Length: > 0 })
        {
            var analysis = await _requirementAnalyzerService.AnalyzeAsync(
                request.ProjectPurpose,
                request.RequirementDocument,
                cancellationToken);

            if (!analysis.IsSuccess)
            {
                ModelState.AddModelError(
                    nameof(request.RequirementDocument),
                    analysis.ErrorMessage ?? "需求文件解析失敗。");
                ViewBag.IsGeminiApiConfigured = _geminiCurationService.IsApiConfigured;
                return View(nameof(Index), request);
            }

            request.UploadedDocumentName = analysis.SourceFileName ?? request.UploadedDocumentName;
            request.RequirementKeywords = request.RequirementKeywords
                .Concat(analysis.ExtractedKeywords)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _requirementAnalyzerService.ApplySuggestedAssemblyDefaults(request, analysis);
        }
        else
        {
            _requirementAnalyzerService.ApplySuggestedAssemblyDefaults(request, null);
        }

        if (!ValidateCompatibility(request))
        {
            ViewBag.IsGeminiApiConfigured = _geminiCurationService.IsApiConfigured;
            return View(nameof(Index), request);
        }

        var baseline = await _recommendationService.RecommendAsync(request, cancellationToken);
        var curated = await _geminiCurationService.CurateAsync(baseline, cancellationToken);
        return View("Result", curated);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyGeminiCuration(
        WizardRequestViewModel request,
        string? geminiResponseJson,
        CancellationToken cancellationToken)
    {
        if (!ValidateCompatibility(request))
        {
            ViewBag.IsGeminiApiConfigured = _geminiCurationService.IsApiConfigured;
            return View(nameof(Index), request);
        }

        var result = await _recommendationService.RecommendAsync(request, cancellationToken);
        if (!_geminiCurationService.ApplyExternalCuration(result, geminiResponseJson, out var errorMessage))
        {
            TempData["GeminiCurationError"] = errorMessage ?? "無法套用 Gemini 回覆結果。";
        }
        else
        {
            TempData["GeminiCurationSuccess"] = "已成功套用 Chrome Gemini 精選組合與客製化運作說明書！";
        }

        return View("Result", result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportMarkdown(WizardRequestViewModel request, CancellationToken cancellationToken)
    {
        if (!ValidateCompatibility(request))
        {
            ViewBag.IsGeminiApiConfigured = _geminiCurationService.IsApiConfigured;
            return View(nameof(Index), request);
        }

        var result = await _recommendationService.RecommendAsync(request, cancellationToken);
        var markdown = _markdownExportService.ExportToMarkdown(result);
        var bytes = Encoding.UTF8.GetBytes(markdown);

        return File(bytes, "text/markdown; charset=utf-8", "ecc-harness-recommendation.md");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadBundle(
        List<int> selectedComponentIds,
        HarnessAssemblyOptionsViewModel? assemblyOptions,
        CancellationToken cancellationToken)
    {
        if (selectedComponentIds is null || selectedComponentIds.Count == 0)
        {
            return BadRequest("請至少勾選一個要下載的元件檔案。");
        }

        var zipBytes = await _harnessBundleService.BuildBundleZipAsync(
            selectedComponentIds,
            assemblyOptions,
            cancellationToken);

        var rawName = string.IsNullOrWhiteSpace(assemblyOptions?.ProjectName)
            ? "ecc"
            : assemblyOptions.ProjectName.Trim();
        var safeSlug = SafeSlugRegex().Replace(rawName.ToLowerInvariant(), "-").Trim('-');
        if (string.IsNullOrWhiteSpace(safeSlug))
        {
            safeSlug = "ecc";
        }

        return File(zipBytes, "application/zip", $"{safeSlug}-harness-bundle.zip");
    }

    private bool ValidateCompatibility(WizardRequestViewModel request)
    {
        foreach (var (memberName, errorMessage) in WizardCompatibility.Validate(request))
        {
            ModelState.AddModelError(memberName, errorMessage);
        }

        return ModelState.IsValid;
    }

    [GeneratedRegex("[^a-z0-9_-]+")]
    private static partial Regex SafeSlugRegex();
}
