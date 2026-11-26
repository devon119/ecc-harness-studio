using ECCSELECTION.Web.Services.Interfaces;
using ECCSELECTION.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace ECCSELECTION.Web.Controllers;

public class CatalogController : Controller
{
    private readonly IEccCatalogService _catalogService;

    public CatalogController(IEccCatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] EccCatalogFilterViewModel filter, CancellationToken cancellationToken)
    {
        var result = await _catalogService.QueryAsync(filter, cancellationToken);
        return View(result);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var entity = await _catalogService.GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var vm = new EccComponentEditViewModel
        {
            Id = entity.Id,
            ComponentType = entity.ComponentType,
            Name = entity.Name,
            Path = entity.Path,
            RawUrl = entity.RawUrl,
            HtmlUrl = entity.HtmlUrl,
            DescriptionEn = entity.DescriptionEn,
            SummaryZh = entity.SummaryZh,
            SdlcRoles = entity.SdlcRoles,
            TechStacks = entity.TechStacks,
            ArchStyles = entity.ArchStyles,
            UsageGuide = entity.UsageGuide,
            IsActive = entity.IsActive,
            IsManuallyEdited = entity.IsManuallyEdited,
            LastSyncedAt = entity.LastSyncedAt
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EccComponentEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var updated = await _catalogService.UpdateManualAsync(model, cancellationToken);
        if (!updated)
        {
            return NotFound();
        }

        TempData["CatalogMessage"] = $"已儲存「{model.Name}」的中文摘要與標籤設定，並已鎖定人工編輯保護。";
        return RedirectToAction(nameof(Edit), new { id = model.Id });
    }
}
