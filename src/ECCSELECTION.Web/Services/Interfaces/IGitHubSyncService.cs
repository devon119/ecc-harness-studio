using ECCSELECTION.Web.Models;

namespace ECCSELECTION.Web.Services.Interfaces;

public interface IGitHubSyncService
{
    Task<SyncLog> SyncAsync(CancellationToken cancellationToken = default);
}
