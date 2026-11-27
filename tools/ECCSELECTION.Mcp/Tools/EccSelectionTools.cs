using System.ComponentModel;
using System.Text.Json;
using ECCSELECTION.Mcp.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace ECCSELECTION.Mcp.Tools;

/// <summary>
/// Exposes the 5 ECCSELECTION workflow automation tools over the MCP Stdio server.
/// </summary>
[McpServerToolType]
public sealed class EccSelectionTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly QualityGateService _qualityGateService;
    private readonly PagesSyncService _pagesSyncService;
    private readonly CatalogExportService _catalogExportService;
    private readonly SpecScaffolderService _specScaffolderService;
    private readonly UiContractCheckerService _uiContractCheckerService;
    private readonly ILogger<EccSelectionTools> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="EccSelectionTools"/>.
    /// </summary>
    public EccSelectionTools(
        QualityGateService qualityGateService,
        PagesSyncService pagesSyncService,
        CatalogExportService catalogExportService,
        SpecScaffolderService specScaffolderService,
        UiContractCheckerService uiContractCheckerService,
        ILogger<EccSelectionTools> logger)
    {
        _qualityGateService = qualityGateService;
        _pagesSyncService = pagesSyncService;
        _catalogExportService = catalogExportService;
        _specScaffolderService = specScaffolderService;
        _uiContractCheckerService = uiContractCheckerService;
        _logger = logger;
    }

    /// <summary>
    /// Stops locking ECCSELECTION.Web processes (optional) and runs scripts/quality-gate.ps1.
    /// </summary>
    [McpServerTool(Name = "run_quality_gate")]
    [Description("Stop locking ECCSELECTION.Web processes if requested and execute scripts/quality-gate.ps1 (build, format, test, agentshield).")]
    public async Task<string> RunQualityGateAsync(
        [Description("Whether to terminate running ECCSELECTION.Web processes before running the gate.")]
        bool stopRunningWebProcess = true,
        [Description("Execution timeout in seconds (30..900).")]
        int timeoutSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        try
        {
            QualityGateResult result = await _qualityGateService.RunAsync(
                stopRunningWebProcess,
                timeoutSeconds,
                cancellationToken);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "run_quality_gate failed unexpectedly.");
            return SerializeError(ex.Message);
        }
    }

    /// <summary>
    /// Packages eccselection-full.zip from main via git archive and syncs assets to an isolated gh-pages worktree.
    /// </summary>
    [McpServerTool(Name = "sync_and_publish_lite")]
    [Description("Build eccselection-full.zip from main and sync icon3d.css (and optionally catalog.json) to gh-pages using an isolated git worktree.")]
    public async Task<string> SyncAndPublishLiteAsync(
        [Description("Safety dry-run flag (default true). Pass false to commit and push to origin/gh-pages.")]
        bool dryRun = true,
        [Description("Whether to also export data/catalog.json from local SQLite and sync it.")]
        bool includeCatalogJson = false,
        [Description("Commit message used when dryRun is false (5..200 chars, no newlines).")]
        string commitMessage = "chore(gh-pages): sync full package and icon3d.css from main",
        CancellationToken cancellationToken = default)
    {
        try
        {
            PagesSyncResult result = await _pagesSyncService.SyncAndPublishAsync(
                dryRun,
                includeCatalogJson,
                commitMessage,
                cancellationToken);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "sync_and_publish_lite failed unexpectedly.");
            return SerializeError(ex.Message);
        }
    }

    /// <summary>
    /// Exports active ECC components from local SQLite in read-only mode to catalog.json format.
    /// </summary>
    [McpServerTool(Name = "export_catalog_json")]
    [Description("Export active (IsActive=1) components from local SQLite in read-only mode to gh-pages catalog.json format.")]
    public async Task<string> ExportCatalogJsonAsync(
        [Description("Path to local SQLite database relative to workspace root.")]
        string databasePath = "src/ECCSELECTION.Web/eccselection.db",
        [Description("Target output path relative to workspace root (required when writeFile is true).")]
        string? outputPath = null,
        [Description("Whether to write the exported JSON to outputPath (default false for read-only preview).")]
        bool writeFile = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            CatalogExportResult result = await _catalogExportService.ExportAsync(
                databasePath,
                outputPath,
                writeFile,
                cancellationToken);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "export_catalog_json failed unexpectedly.");
            return SerializeError(ex.Message);
        }
    }

    /// <summary>
    /// Scaffolds a standalone specification file under docs/specs/SPEC_&lt;YYYYMMDD&gt;_&lt;TOPIC&gt;.md.
    /// </summary>
    [McpServerTool(Name = "create_spec_file")]
    [Description("Create a standalone specification markdown skeleton under docs/specs/SPEC_<YYYYMMDD>_<TOPIC>.md.")]
    public async Task<string> CreateSpecFileAsync(
        [Description("Short topic identifier matching [A-Za-z0-9_-]{2,64} (normalized to uppercase with underscores).")]
        string topic,
        [Description("Specification title (2..120 chars, no control characters).")]
        string title,
        [Description("Author label (default 'SA Agent').")]
        string author = "SA Agent",
        [Description("Optional yyyyMMdd date string (defaults to today).")]
        string? date = null,
        [Description("Whether to overwrite an existing spec file (default false).")]
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            CreateSpecResult result = await _specScaffolderService.CreateSpecAsync(
                topic,
                title,
                author,
                date,
                overwrite,
                cancellationToken);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "create_spec_file failed unexpectedly.");
            return SerializeError(ex.Message);
        }
    }

    /// <summary>
    /// Runs static UI and security contract checks across src/ECCSELECTION.Web.
    /// </summary>
    [McpServerTool(Name = "check_ui_contracts")]
    [Description("Statically scan src/ECCSELECTION.Web for prohibited <table> tags (UI001), empty catch blocks (CS001), missing [ValidateAntiForgeryToken] (SEC001), and innerHTML assignments (UI002).")]
    public async Task<string> CheckUiContractsAsync(
        [Description("Target scan scope: all, views, wwwroot, or csharp.")]
        string targetScope = "all",
        CancellationToken cancellationToken = default)
    {
        try
        {
            UiContractCheckResult result = await _uiContractCheckerService.CheckAsync(
                targetScope,
                cancellationToken);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "check_ui_contracts failed unexpectedly.");
            return SerializeError(ex.Message);
        }
    }

    private static string SerializeError(string errorMessage) =>
        JsonSerializer.Serialize(new { success = false, errorMessage }, JsonOptions);
}
