using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.ViewModels;

namespace ECCSELECTION.Web.Services.Interfaces;

public interface IRecommendationService
{
    Task<WizardResultViewModel> RecommendAsync(WizardRequestViewModel request, CancellationToken cancellationToken = default);
    WizardResultViewModel Recommend(WizardRequestViewModel request, IEnumerable<EccComponent> components);
}
