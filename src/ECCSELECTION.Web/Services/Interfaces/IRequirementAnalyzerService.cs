using ECCSELECTION.Web.ViewModels;
using Microsoft.AspNetCore.Http;

namespace ECCSELECTION.Web.Services.Interfaces;

public interface IRequirementAnalyzerService
{
    Task<RequirementAnalysisResult> AnalyzeAsync(
        string? purposeText,
        IFormFile? document,
        CancellationToken cancellationToken = default);

    RequirementAnalysisResult AnalyzeText(
        string? combinedText,
        string? sourceFileName = null);
}
