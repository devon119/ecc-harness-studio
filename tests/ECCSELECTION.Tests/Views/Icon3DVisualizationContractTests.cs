using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ECCSELECTION.Web.Models.Enums;

namespace ECCSELECTION.Tests.Views;

public class Icon3DVisualizationContractTests
{
    private static string GetWebProjectRoot([CallerFilePath] string sourceFilePath = "")
    {
        var candidates = new List<string?>
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Path.GetDirectoryName(sourceFilePath)
        };

        foreach (var startPath in candidates.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var dir = new DirectoryInfo(startPath!);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ECCSELECTION.slnx")))
                {
                    return Path.Combine(dir.FullName, "src", "ECCSELECTION.Web");
                }

                dir = dir.Parent;
            }
        }

        throw new DirectoryNotFoundException("無法從測試執行目錄、工作目錄或原始碼路徑往上定位 ECCSELECTION.slnx。");
    }

    private static string ReadWebFile(params string[] relativeSegments)
    {
        var fullPath = Path.Combine([GetWebProjectRoot(), .. relativeSegments]);
        Assert.True(File.Exists(fullPath), $"預期檔案應存在：{fullPath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void AllRazorViews_ShouldNotContainHtmlTableTags()
    {
        var viewsDir = Path.Combine(GetWebProjectRoot(), "Views");
        var cshtmlFiles = Directory.GetFiles(viewsDir, "*.cshtml", SearchOption.AllDirectories);

        Assert.NotEmpty(cshtmlFiles);

        foreach (var file in cshtmlFiles)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("<table", content, StringComparison.OrdinalIgnoreCase);
            Assert.False(
                Regex.IsMatch(content, @"<\s*(table|thead|tbody|tfoot)\b", RegexOptions.IgnoreCase),
                $"Razor 檢視不應殘留 HTML 表格標籤：{file}");
        }
    }

    [Fact]
    public void Icon3DPartial_ShouldSupportAllComponentTypesSyncStatusesAndSizes()
    {
        var partial = ReadWebFile("Views", "Shared", "_Icon3D.cshtml");
        var css = ReadWebFile("wwwroot", "css", "icon3d.css");

        foreach (var componentType in Enum.GetValues<ComponentType>())
        {
            var key = componentType.ToString().ToLowerInvariant();
            Assert.Contains($"case \"{key}\":", partial);
            Assert.Contains($"\"{key}\" =>", partial);
        }

        foreach (var syncStatus in Enum.GetValues<SyncStatus>())
        {
            var key = syncStatus.ToString().ToLowerInvariant();
            Assert.Contains($"case \"{key}\":", partial);
            Assert.Contains($"\"{key}\" =>", partial);
        }

        foreach (var sizeClass in new[] { "i3d-xs", "i3d-sm", "i3d-md", "i3d-lg", "i3d-xl" })
        {
            Assert.Contains(sizeClass, partial);
            Assert.Contains($".{sizeClass}", css);
        }
    }

    [Fact]
    public void Icon3DAssetsAndPartial_ShouldExistWithAccessibilityAndMotionContracts()
    {
        var css = ReadWebFile("wwwroot", "css", "icon3d.css");
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css);
        Assert.Contains(".i3d-stage", css);
        Assert.Contains(".i3d-pillar-card", css);
        Assert.Contains(".i3d-tile-grid", css);
        Assert.Contains(".i3d-timeline", css);

        var js = ReadWebFile("wwwroot", "js", "icon3d.js");
        Assert.Contains("data-i3d-toggle", js);
        Assert.Contains("data-select-target", js);
        Assert.Contains(".i3d-checkbox-tile", js);

        var partial = ReadWebFile("Views", "Shared", "_Icon3D.cshtml");
        Assert.Contains("role=\"img\"", partial);
        Assert.Contains("aria-label=\"@titleText\"", partial);
        Assert.Contains("class=\"i3d-stage\"", partial);
        Assert.Contains("<svg", partial);
        Assert.Contains("case \"agent\":", partial);
        Assert.Contains("case \"skill\":", partial);
        Assert.Contains("case \"rule\":", partial);
        Assert.Contains("case \"command\":", partial);

        var layout = ReadWebFile("Views", "Shared", "_Layout.cshtml");
        Assert.Contains("~/css/icon3d.css", layout);
        Assert.Contains("~/js/icon3d.js", layout);
        Assert.Contains("<partial name=\"_Icon3D\"", layout);
    }

    [Fact]
    public void HomeAndCatalogAndSyncViews_ShouldContainExpected3DSpatialComponents()
    {
        var homeIndex = ReadWebFile("Views", "Home", "Index.cshtml");
        Assert.Contains("<partial name=\"_Icon3D\"", homeIndex);
        Assert.Contains("i3d-hero-panel", homeIndex);
        Assert.Contains("i3d-pillar-card", homeIndex);
        Assert.Contains("i3d-pillar-bar-fill", homeIndex);
        Assert.Contains("i3d-pod", homeIndex);

        var catalogIndex = ReadWebFile("Views", "Catalog", "Index.cshtml");
        Assert.Contains("<partial name=\"_Icon3D\"", catalogIndex);
        Assert.Contains("i3d-tile-grid", catalogIndex);
        Assert.Contains("i3d-component-card", catalogIndex);
        Assert.Contains("i3d-pod", catalogIndex);

        var catalogEdit = ReadWebFile("Views", "Catalog", "Edit.cshtml");
        Assert.Contains("<partial name=\"_Icon3D\"", catalogEdit);
        Assert.Contains("i3d-float", catalogEdit);
        Assert.Contains("i3d-pod", catalogEdit);

        var syncIndex = ReadWebFile("Views", "Sync", "Index.cshtml");
        Assert.Contains("<partial name=\"_Icon3D\"", syncIndex);
        Assert.Contains("i3d-timeline", syncIndex);
        Assert.Contains("i3d-timeline-item", syncIndex);
        Assert.Contains("i3d-timeline-node", syncIndex);
        Assert.Contains("i3d-mini-pillars", syncIndex);
        Assert.Contains("data-i3d-toggle", syncIndex);
    }

    [Fact]
    public void WizardViews_ShouldContain3DTilesAndPreserveFormAndSelectionContracts()
    {
        var wizardIndex = ReadWebFile("Views", "Wizard", "Index.cshtml");
        Assert.Contains("<partial name=\"_Icon3D\"", wizardIndex);
        Assert.Contains("i3d-selector-tile", wizardIndex);
        Assert.Contains("i3d-checkbox-tile", wizardIndex);
        Assert.Contains("data-select-target=\"ProjectType\"", wizardIndex);
        Assert.Contains("data-select-target=\"ProjectLifecycle\"", wizardIndex);
        Assert.Contains("data-select-target=\"ArchStyle\"", wizardIndex);
        Assert.Contains("data-select-target=\"Mode\"", wizardIndex);
        Assert.Contains("data-stack-card=", wizardIndex);
        Assert.Contains("data-role-card=", wizardIndex);

        var wizardResult = ReadWebFile("Views", "Wizard", "Result.cshtml");
        Assert.Contains("<partial name=\"_Icon3D\"", wizardResult);
        Assert.Contains("i3d-tile-grid", wizardResult);
        Assert.Contains("i3d-component-card", wizardResult);
        Assert.Contains("i3d-score-gauge", wizardResult);
        Assert.Contains("i3d-flow-node", wizardResult);
        Assert.Contains("data-i3d-toggle", wizardResult);
        Assert.Contains("component-checkbox", wizardResult);
        Assert.Contains("suite-master-checkbox", wizardResult);
        Assert.Contains("name=\"selectedComponentIds\"", wizardResult);
    }
}
