using ECCSELECTION.Mcp.Options;
using ECCSELECTION.Mcp.Security;
using ECCSELECTION.Mcp.Services;

namespace ECCSELECTION.Tests.Mcp;

public sealed class UiContractCheckerServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _webRoot;

    public UiContractCheckerServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"ecc-ui-test-{Guid.NewGuid():N}");
        _webRoot = Path.Combine(_tempRoot, "src", "ECCSELECTION.Web");
        Directory.CreateDirectory(Path.Combine(_webRoot, "Views", "Home"));
        Directory.CreateDirectory(Path.Combine(_webRoot, "wwwroot", "js"));
        Directory.CreateDirectory(Path.Combine(_webRoot, "Controllers"));
        Directory.CreateDirectory(Path.Combine(_webRoot, "Services"));
        File.WriteAllText(Path.Combine(_tempRoot, "ECCSELECTION.slnx"), "<Solution />");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CheckAsync_CompliantWorkspace_ReturnsPassedTrueWithZeroViolations()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_webRoot, "Views", "Home", "Index.cshtml"),
            """
            @* <table> comment should be ignored *@
            <!-- <table> comment should be ignored -->
            <div class="grid">Card Layout</div>
            """);

        await File.WriteAllTextAsync(
            Path.Combine(_webRoot, "wwwroot", "js", "site.js"),
            """
            // el.innerHTML = "ignored";
            /* <table>ignored</table> */
            const el = document.getElementById("app");
            el.textContent = "Safe";
            """);

        await File.WriteAllTextAsync(
            Path.Combine(_webRoot, "Controllers", "HomeController.cs"),
            """
            public class HomeController
            {
                [HttpPost]
                [ValidateAntiForgeryToken]
                public IActionResult Save() => Ok();
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(_webRoot, "Services", "SampleService.cs"),
            """
            public class SampleService
            {
                public void Run()
                {
                    try { DoWork(); }
                    catch (Exception ex) { Log(ex); }
                }
            }
            """);

        UiContractCheckerService service = CreateService();
        UiContractCheckResult result = await service.CheckAsync("all");

        Assert.True(result.Passed);
        Assert.Equal(0, result.Summary.ErrorCount);
        Assert.Equal(0, result.Summary.WarningCount);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public async Task CheckAsync_TableTagInViewAndJs_DetectsUi001Errors()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_webRoot, "Views", "Home", "Index.cshtml"),
            "<div class=\"wrap\"><TABLE class=\"legacy\"><tr><td>x</td></tr></table></div>");

        await File.WriteAllTextAsync(
            Path.Combine(_webRoot, "wwwroot", "js", "legacy.js"),
            "const markup = '<table id=\"old\"></table>';");

        UiContractCheckerService service = CreateService();
        UiContractCheckResult result = await service.CheckAsync("all");

        Assert.False(result.Passed);
        Assert.Equal(2, result.Summary.ErrorCount);
        Assert.All(result.Violations, v => Assert.Equal("UI001", v.RuleId));
    }

    [Fact]
    public async Task CheckAsync_EmptyCatchBlocks_DetectsCs001AndIgnoresBinObj()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_webRoot, "Services", "BadService.cs"),
            """
            public class BadService
            {
                public void A() { try { X(); } catch { } }
                public void B()
                {
                    try { X(); }
                    catch (Exception)
                    {
                        // only comment
                    }
                }
            }
            """);

        string binDir = Path.Combine(_webRoot, "bin", "Debug");
        Directory.CreateDirectory(binDir);
        await File.WriteAllTextAsync(
            Path.Combine(binDir, "Generated.cs"),
            "public class Gen { void F() { try {} catch {} } }");

        UiContractCheckerService service = CreateService();
        UiContractCheckResult result = await service.CheckAsync("csharp");

        Assert.False(result.Passed);
        Assert.Equal(2, result.Summary.ErrorCount);
        Assert.All(result.Violations, v =>
        {
            Assert.Equal("CS001", v.RuleId);
            Assert.DoesNotContain("bin/", v.File);
        });
    }

    [Fact]
    public async Task CheckAsync_MutatingControllerActions_DetectsSec001ErrorAndIgnoreWarning()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_webRoot, "Controllers", "CatalogController.cs"),
            """
            public class CatalogController
            {
                [HttpPost]
                public IActionResult UnsafePost() => Ok();

                [HttpDelete]
                [IgnoreAntiforgeryToken]
                public IActionResult IgnoredDelete() => Ok();

                [HttpPost]
                [ValidateAntiForgeryToken]
                public IActionResult SafePost() => Ok();
            }
            """);

        UiContractCheckerService service = CreateService();
        UiContractCheckResult result = await service.CheckAsync("csharp");

        Assert.False(result.Passed);
        Assert.Equal(1, result.Summary.ErrorCount);
        Assert.Equal(1, result.Summary.WarningCount);
        Assert.Contains(result.Violations, v => v.RuleId == "SEC001" && v.Severity == "error");
        Assert.Contains(result.Violations, v => v.RuleId == "SEC001" && v.Severity == "warning");
    }

    [Fact]
    public async Task CheckAsync_InnerHtmlAssignment_ReportsUi002WarningWithoutFailing()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_webRoot, "wwwroot", "js", "render.js"),
            "container.innerHTML = '<span>Static</span>';");

        UiContractCheckerService service = CreateService();
        UiContractCheckResult result = await service.CheckAsync("wwwroot");

        Assert.True(result.Passed);
        Assert.Equal(0, result.Summary.ErrorCount);
        Assert.Equal(1, result.Summary.WarningCount);
        UiContractViolation violation = Assert.Single(result.Violations);
        Assert.Equal("UI002", violation.RuleId);
        Assert.Equal("warning", violation.Severity);
    }

    [Fact]
    public async Task CheckAsync_InvalidScope_ReturnsErrorResult()
    {
        UiContractCheckerService service = CreateService();
        UiContractCheckResult result = await service.CheckAsync("invalid-scope");

        Assert.False(result.Passed);
        Assert.Contains("targetScope", result.ErrorMessage);
    }

    private UiContractCheckerService CreateService() =>
        new(new McpWorkspaceOptions(_tempRoot), new WorkspaceGuard());
}
