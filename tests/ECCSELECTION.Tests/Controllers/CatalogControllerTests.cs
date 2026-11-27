using ECCSELECTION.Web.Controllers;
using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Services.Interfaces;
using ECCSELECTION.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ECCSELECTION.Tests.Controllers;

public sealed class CatalogControllerTests
{
    [Fact]
    public async Task Index_ReturnsViewWithQueryResult()
    {
        var filter = new EccCatalogFilterViewModel { Keyword = "arch" };
        var stub = new StubCatalogService();
        var controller = new CatalogController(stub);

        IActionResult result = await controller.Index(filter, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Same(filter, viewResult.Model);
    }

    [Fact]
    public async Task EditGet_MissingId_ReturnsNotFound()
    {
        var controller = new CatalogController(new StubCatalogService());

        IActionResult result = await controller.Edit(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task EditGet_ExistingId_MapsEntityToEditViewModel()
    {
        var entity = new EccComponent
        {
            Id = 5,
            ComponentType = ComponentType.Agent,
            Name = "architect",
            Path = "agents/architect.md",
            SummaryZh = "架構師",
            SdlcRoles = "SA,SD",
            TechStacks = "dotnet",
            ArchStyles = "mvc",
            UsageGuide = "Guide",
            IsActive = true,
            IsManuallyEdited = true
        };
        var controller = new CatalogController(new StubCatalogService(entity));

        IActionResult result = await controller.Edit(5, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<EccComponentEditViewModel>(viewResult.Model);
        Assert.Equal(5, vm.Id);
        Assert.Equal("architect", vm.Name);
        Assert.Equal("架構師", vm.SummaryZh);
        Assert.True(vm.IsManuallyEdited);
    }

    [Fact]
    public async Task EditPost_InvalidModelState_ReturnsViewWithoutCallingService()
    {
        var stub = new StubCatalogService();
        var controller = new CatalogController(stub);
        controller.ModelState.AddModelError("SummaryZh", "Required");
        var model = new EccComponentEditViewModel { Id = 1 };

        IActionResult result = await controller.Edit(model, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Same(model, viewResult.Model);
        Assert.False(stub.UpdateCalled);
    }

    [Fact]
    public async Task EditPost_ValidModel_UpdatesAndRedirectsWithTempData()
    {
        var stub = new StubCatalogService(updateResult: true);
        var controller = CreateControllerWithTempData(stub);
        var model = new EccComponentEditViewModel { Id = 7, Name = "tdd-guide" };

        IActionResult result = await controller.Edit(model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(CatalogController.Edit), redirect.ActionName);
        Assert.True(stub.UpdateCalled);
        Assert.Contains("tdd-guide", controller.TempData["CatalogMessage"]?.ToString());
    }

    private static CatalogController CreateControllerWithTempData(IEccCatalogService service) =>
        new(service)
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new EmptyTempDataProvider())
        };

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class StubCatalogService(EccComponent? entity = null, bool updateResult = false) : IEccCatalogService
    {
        public bool UpdateCalled { get; private set; }

        public Task<EccCatalogFilterViewModel> QueryAsync(EccCatalogFilterViewModel filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(filter);

        public Task<EccComponent?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(entity is not null && entity.Id == id ? entity : null);

        public Task<bool> UpdateManualAsync(EccComponentEditViewModel model, CancellationToken cancellationToken = default)
        {
            UpdateCalled = true;
            return Task.FromResult(updateResult);
        }

        public Task<SyncDashboardViewModel> GetDashboardStatsAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
