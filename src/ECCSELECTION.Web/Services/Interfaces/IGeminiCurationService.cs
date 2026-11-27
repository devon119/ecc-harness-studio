using ECCSELECTION.Web.ViewModels;

namespace ECCSELECTION.Web.Services.Interfaces;

public interface IGeminiCurationService
{
    bool IsApiConfigured { get; }

    Task<WizardResultViewModel> CurateAsync(
        WizardResultViewModel baselineResult,
        CancellationToken cancellationToken = default);

    bool TryApplyExternalCurationJson(
        WizardResultViewModel result,
        string rawJson,
        out string? errorMessage);

    bool ApplyExternalCuration(
        WizardResultViewModel baselineResult,
        string? rawJson,
        out string? errorMessage);

    Task<string> SaveLocalGeminiKeyAsync(
        string apiKey,
        string? model,
        CancellationToken cancellationToken = default);
}
