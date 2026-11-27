using System.Text;
using ECCSELECTION.Web.Controllers;
using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.Services.Interfaces;
using ECCSELECTION.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ECCSELECTION.Tests.Controllers;

public sealed class WizardControllerTests
{
    [Fact]
    public void Index_SetsIsGeminiApiConfiguredAndReturnsView()
    {
        var controller = CreateController(isGeminiConfigured: true);

        IActionResult result = controller.Index();

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.IsType<WizardRequestViewModel>(viewResult.Model);
        Assert.True((bool)controller.ViewBag.IsGeminiApiConfigured);
    }

    [Fact]
    public async Task SaveGeminiKey_EmptyKey_ReturnsBadRequest()
    {
        var controller = CreateController();

        IActionResult result = await controller.SaveGeminiKey("   ", null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Recommend_IncompatibleRequest_ReturnsIndexViewWithModelStateError()
    {
        var controller = CreateController();
        var request = new WizardRequestViewModel
        {
            ProjectType = "CLI",
            ArchStyle = "MVC",
            TechStacks = ["dotnet"],
            SdlcRoles = ["PG_BE"]
        };

        IActionResult result = await controller.Recommend(request, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal(nameof(WizardController.Index), viewResult.ViewName);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Recommend_ValidRequest_ReturnsResultView()
    {
        var controller = CreateController();
        var request = new WizardRequestViewModel
        {
            ProjectType = "Web",
            ArchStyle = "MVC",
            TechStacks = ["dotnet"],
            SdlcRoles = ["SA"]
        };

        IActionResult result = await controller.Recommend(request, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Result", viewResult.ViewName);
        Assert.IsType<WizardResultViewModel>(viewResult.Model);
    }

    [Fact]
    public async Task ExportMarkdown_ValidRequest_ReturnsMarkdownFile()
    {
        var controller = CreateController();
        var request = new WizardRequestViewModel
        {
            ProjectType = "API",
            ArchStyle = "Clean",
            TechStacks = ["dotnet"],
            SdlcRoles = ["PG_BE"]
        };

        IActionResult result = await controller.ExportMarkdown(request, CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/markdown; charset=utf-8", fileResult.ContentType);
        Assert.Equal("ecc-harness-recommendation.md", fileResult.FileDownloadName);
        Assert.Equal("# Sample Markdown", Encoding.UTF8.GetString(fileResult.FileContents));
    }

    [Fact]
    public async Task DownloadBundle_EmptyIds_ReturnsBadRequest()
    {
        var controller = CreateController();

        IActionResult result = await controller.DownloadBundle([], null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DownloadBundle_ValidIds_ReturnsZipFileWithSanitizedSlug()
    {
        var controller = CreateController();

        IActionResult result = await controller.DownloadBundle(
            [1, 2],
            new HarnessAssemblyOptionsViewModel { ProjectName = "Billing App!" },
            CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/zip", fileResult.ContentType);
        Assert.Equal("billing-app-harness-bundle.zip", fileResult.FileDownloadName);
    }

    private static WizardController CreateController(bool isGeminiConfigured = false)
    {
        var httpContext = new DefaultHttpContext();
        return new WizardController(
            new StubRecommendationService(),
            new StubMarkdownExportService(),
            new StubHarnessBundleService(),
            new StubRequirementAnalyzerService(),
            new StubGeminiCurationService(isGeminiConfigured))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new EmptyTempDataProvider())
        };
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class StubRecommendationService : IRecommendationService
    {
        public Task<WizardResultViewModel> RecommendAsync(WizardRequestViewModel request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WizardResultViewModel { Request = request });

        public WizardResultViewModel Recommend(WizardRequestViewModel request, IEnumerable<EccComponent> components) =>
            new() { Request = request };
    }

    private sealed class StubMarkdownExportService : IMarkdownExportService
    {
        public string ExportToMarkdown(WizardResultViewModel result) => "# Sample Markdown";
    }

    private sealed class StubHarnessBundleService : IHarnessBundleService
    {
        public Task<byte[]> BuildBundleZipAsync(
            IReadOnlyCollection<int> selectedComponentIds,
            HarnessAssemblyOptionsViewModel? assemblyOptions = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new byte[] { 0x50, 0x4B, 0x05, 0x06 });
    }

    private sealed class StubRequirementAnalyzerService : IRequirementAnalyzerService
    {
        public Task<RequirementAnalysisResult> AnalyzeAsync(string? projectPurpose, IFormFile? requirementDocument, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RequirementAnalysisResult { IsSuccess = true });

        public RequirementAnalysisResult AnalyzeText(string? combinedText, string? sourceFileName = null) =>
            new() { IsSuccess = true };

        public void ApplySuggestedAssemblyDefaults(WizardRequestViewModel request, RequirementAnalysisResult? analysis) { }
    }

    private sealed class StubGeminiCurationService(bool isConfigured) : IGeminiCurationService
    {
        public bool IsApiConfigured => isConfigured;

        public Task<WizardResultViewModel> CurateAsync(WizardResultViewModel baseline, CancellationToken cancellationToken = default) =>
            Task.FromResult(baseline);

        public bool TryApplyExternalCurationJson(WizardResultViewModel result, string rawJson, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }

        public bool ApplyExternalCuration(WizardResultViewModel target, string? rawJson, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }

        public Task<string> SaveLocalGeminiKeyAsync(string apiKey, string? model, CancellationToken cancellationToken = default) =>
            Task.FromResult(model ?? "gemini-2.5-flash");
    }
}
