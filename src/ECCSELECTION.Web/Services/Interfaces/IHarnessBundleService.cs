using ECCSELECTION.Web.ViewModels;

namespace ECCSELECTION.Web.Services.Interfaces;

public interface IHarnessBundleService
{
    Task<byte[]> BuildBundleZipAsync(
        IReadOnlyCollection<int> selectedComponentIds,
        CancellationToken cancellationToken = default)
        => BuildBundleZipAsync(selectedComponentIds, null, cancellationToken);

    Task<byte[]> BuildBundleZipAsync(
        IReadOnlyCollection<int> selectedComponentIds,
        HarnessAssemblyOptionsViewModel? assemblyOptions,
        CancellationToken cancellationToken = default);
}
