using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.ViewModels;

namespace ECCSELECTION.Web.Services.Interfaces;

public interface IEccCatalogService
{
    Task<EccCatalogFilterViewModel> QueryAsync(EccCatalogFilterViewModel filter, CancellationToken cancellationToken = default);
    Task<EccComponent?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> UpdateManualAsync(EccComponentEditViewModel model, CancellationToken cancellationToken = default);
    Task<SyncDashboardViewModel> GetDashboardStatsAsync(CancellationToken cancellationToken = default);
}
