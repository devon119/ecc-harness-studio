using ECCSELECTION.Web.Controllers;
using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.Services.Interfaces;
using ECCSELECTION.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ECCSELECTION.Tests.Controllers;

public sealed class HomeControllerTests
{
    [Fact]
    public async Task Index_ReturnsViewWithDashboardStats()
    {
        var expected = new SyncDashboardViewModel { TotalComponents = 12, ActiveComponents = 10 };
        var controller = new HomeController(new StubCatalogService(expected));

        IActionResult result = await controller.Index(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SyncDashboardViewModel>(viewResult.Model);
        Assert.Equal(12, model.TotalComponents);
        Assert.Equal(10, model.ActiveComponents);
    }

    [Fact]
    public void Error_ReturnsViewWithTraceIdentifier()
    {
        var controller = new HomeController(new StubCatalogService(new SyncDashboardViewModel()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-123" }
            }
        };

        IActionResult result = controller.Error();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ErrorViewModel>(viewResult.Model);
        Assert.False(string.IsNullOrWhiteSpace(model.RequestId));
    }

    private sealed class StubCatalogService(SyncDashboardViewModel stats) : IEccCatalogService
    {
        public Task<SyncDashboardViewModel> GetDashboardStatsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(stats);

        public Task<EccCatalogFilterViewModel> QueryAsync(EccCatalogFilterViewModel filter, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<EccComponent?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> UpdateManualAsync(EccComponentEditViewModel model, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
