using System.Diagnostics;
using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ECCSELECTION.Web.Controllers;

public class HomeController : Controller
{
    private readonly IEccCatalogService _catalogService;

    public HomeController(IEccCatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var dashboard = await _catalogService.GetDashboardStatsAsync(cancellationToken);
        return View(dashboard);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
