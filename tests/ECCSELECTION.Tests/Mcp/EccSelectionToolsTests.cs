using System.Text.Json;
using ECCSELECTION.Mcp.Options;
using ECCSELECTION.Mcp.Security;
using ECCSELECTION.Mcp.Services;
using ECCSELECTION.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace ECCSELECTION.Tests.Mcp;

public sealed class EccSelectionToolsTests : IDisposable
{
    private readonly string _tempRoot;

    public EccSelectionToolsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"ecc-tools-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "src", "ECCSELECTION.Web"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "scripts"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "docs", "specs"));
        File.WriteAllText(Path.Combine(_tempRoot, "ECCSELECTION.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(_tempRoot, "scripts", "quality-gate.ps1"), "# dummy");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunQualityGateAsync_ReturnsCamelCaseJson()
    {
        var runner = new StubProcessRunner(new ProcessResult(0, "[1/4] Build\n[2/4] Format\n[3/4] Test\nPassed! - Failed: 0, Passed: 5\n[4/4] AgentShield\n", string.Empty, false, 100));
        EccSelectionTools tools = CreateTools(runner);

        string json = await tools.RunQualityGateAsync(stopRunningWebProcess: false, timeoutSeconds: 60);
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task CheckUiContractsAsync_ReturnsStructuredJson()
    {
        EccSelectionTools tools = CreateTools(new StubProcessRunner(new ProcessResult(0, string.Empty, string.Empty, false, 10)));

        string json = await tools.CheckUiContractsAsync("all");
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("summary").GetProperty("errorCount").GetInt32());
    }

    [Fact]
    public async Task CreateSpecFileAsync_ValidInput_CreatesSpecAndReturnsJson()
    {
        EccSelectionTools tools = CreateTools(new StubProcessRunner(new ProcessResult(0, string.Empty, string.Empty, false, 10)));

        string json = await tools.CreateSpecFileAsync("audit_check", "Audit Check Spec", "TDD Agent", "20261127");
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("SPEC_20261127_AUDIT_CHECK.md", doc.RootElement.GetProperty("relativePath").GetString());
    }

    [Fact]
    public async Task ExportCatalogJsonAsync_PathTraversal_SerializesErrorJson()
    {
        EccSelectionTools tools = CreateTools(new StubProcessRunner(new ProcessResult(0, string.Empty, string.Empty, false, 10)));

        string json = await tools.ExportCatalogJsonAsync(databasePath: "../outside.db", writeFile: false);
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("errorMessage").GetString()));
    }

    [Fact]
    public async Task SyncAndPublishLiteAsync_InvalidCommitMessage_SerializesErrorJson()
    {
        EccSelectionTools tools = CreateTools(new StubProcessRunner(new ProcessResult(0, string.Empty, string.Empty, false, 10)));

        string json = await tools.SyncAndPublishLiteAsync(dryRun: true, commitMessage: "invalid\nmessage");
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("errorMessage").GetString()));
    }

    private EccSelectionTools CreateTools(IProcessRunner runner)
    {
        var options = new McpWorkspaceOptions(_tempRoot);
        var guard = new WorkspaceGuard();
        var catalogExport = new CatalogExportService(options, guard, NullLogger<CatalogExportService>.Instance);

        return new EccSelectionTools(
            new QualityGateService(options, guard, runner, NullLogger<QualityGateService>.Instance),
            new PagesSyncService(options, guard, runner, catalogExport, NullLogger<PagesSyncService>.Instance),
            catalogExport,
            new SpecScaffolderService(options, guard, NullLogger<SpecScaffolderService>.Instance),
            new UiContractCheckerService(options, guard),
            NullLogger<EccSelectionTools>.Instance);
    }

    private sealed class StubProcessRunner(ProcessResult result) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
