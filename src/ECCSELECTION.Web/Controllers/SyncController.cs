using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ECCSELECTION.Web.Controllers;

public class SyncController : Controller
{
    private readonly IEccCatalogService _catalogService;
    private readonly IGitHubSyncService _syncService;

    public SyncController(IEccCatalogService catalogService, IGitHubSyncService syncService)
    {
        _catalogService = catalogService;
        _syncService = syncService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var dashboard = await _catalogService.GetDashboardStatsAsync(cancellationToken);
        return View(dashboard);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Trigger(string? returnUrl, CancellationToken cancellationToken)
    {
        var log = await _syncService.SyncAsync(cancellationToken);

        TempData["SyncStatus"] = log.Status.ToString();
        TempData["SyncMessage"] = log.Status switch
        {
            SyncStatus.Success =>
                $"同步成功！共掃描 {log.TotalFound} 筆，新增 {log.AddedCount} 筆、更新 {log.UpdatedCount} 筆、下架 {log.RemovedCount} 筆。",
            SyncStatus.PartialSuccess =>
                $"部分同步成功：新增 {log.AddedCount}、更新 {log.UpdatedCount}、下架 {log.RemovedCount}。部分錯誤：{log.ErrorMessage}",
            _ =>
                $"同步失敗：{log.ErrorMessage}"
        };

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction(nameof(Index));
    }
}
