using ECCSELECTION.Web.Controllers;
using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Services.Interfaces;
using ECCSELECTION.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ECCSELECTION.Tests.Controllers;

public sealed class SyncControllerTests
{
    [Fact]
    public async Task Index_ReturnsViewWithDashboardStats()
    {
        var controller = CreateController(new SyncLog { Status = SyncStatus.Success }, localUrls: []);

        IActionResult result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.IsType<SyncDashboardViewModel>(viewResult.Model);
    }

    [Fact]
    public async Task Trigger_LocalReturnUrl_RedirectsToReturnUrlAndSetsTempData()
    {
        var controller = CreateController(
            new SyncLog { Status = SyncStatus.Success, TotalFound = 10, AddedCount = 2 },
            localUrls: ["/Home/Index"]);

        IActionResult result = await controller.Trigger("/Home/Index", CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/Home/Index", redirect.Url);
        Assert.Equal("Success", controller.TempData["SyncStatus"]);
        Assert.Contains("同步成功", controller.TempData["SyncMessage"]?.ToString());
    }

    [Fact]
    public async Task Trigger_ExternalReturnUrl_IgnoresOpenRedirectAndRedirectsToIndex()
    {
        var controller = CreateController(
            new SyncLog { Status = SyncStatus.PartialSuccess, ErrorMessage = "timeout" },
            localUrls: ["/Home/Index"]);

        IActionResult result = await controller.Trigger("https://evil.example.com/phish", CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SyncController.Index), redirect.ActionName);
        Assert.Equal("PartialSuccess", controller.TempData["SyncStatus"]);
    }

    private static SyncController CreateController(SyncLog log, HashSet<string> localUrls)
    {
        var httpContext = new DefaultHttpContext();
        return new SyncController(new StubCatalogService(), new StubSyncService(log))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new EmptyTempDataProvider()),
            Url = new StubUrlHelper(localUrls)
        };
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class StubUrlHelper(HashSet<string> localUrls) : IUrlHelper
    {
        public ActionContext ActionContext => new();
        public string? Action(UrlActionContext actionContext) => null;
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => url is not null && localUrls.Contains(url);
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }

    private sealed class StubCatalogService : IEccCatalogService
    {
        public Task<SyncDashboardViewModel> GetDashboardStatsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncDashboardViewModel());

        public Task<EccCatalogFilterViewModel> QueryAsync(EccCatalogFilterViewModel filter, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<EccComponent?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> UpdateManualAsync(EccComponentEditViewModel model, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class StubSyncService(SyncLog log) : IGitHubSyncService
    {
        public Task<SyncLog> SyncAsync(CancellationToken cancellationToken = default) => Task.FromResult(log);
    }
}
