using System.IO.Compression;
using System.Text;
using ECCSELECTION.Mcp.Options;
using ECCSELECTION.Mcp.Security;
using Microsoft.Extensions.Logging;

namespace ECCSELECTION.Mcp.Services;

/// <summary>
/// Structured result returned by <see cref="PagesSyncService.SyncAndPublishAsync"/>.
/// </summary>
public sealed record PagesSyncResult(
    bool Success,
    bool DryRun,
    string TargetBranch,
    string? MainCommitSha,
    long ZipSizeBytes,
    IReadOnlyList<string> UpdatedFiles,
    bool Pushed,
    string? GhPagesCommitSha,
    IReadOnlyList<string> Warnings,
    string Message,
    string? ErrorMessage = null);

/// <summary>
/// Packages <c>eccselection-full.zip</c> from <c>main</c> via <c>git archive</c> and safely syncs
/// to <c>gh-pages</c> using an isolated <c>git worktree</c>.
/// </summary>
public sealed class PagesSyncService
{
    private const string DefaultCommitMessage = "chore(gh-pages): sync full package and icon3d.css from main";
    private const string TargetBranchName = "gh-pages";
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(120);
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly string[] AllowedBundleRoots =
    [
        "ECCSELECTION.slnx",
        "AGENTS.md",
        "SPEC_ECCSELECTION.md",
        "DEVELOPMENT_STATUS.md",
        "README.md",
        "src",
        "tests",
        "scripts",
        ".ecc",
        ".agents"
    ];

    private static readonly HashSet<string> ExcludedDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        "TestResults"
    };

    private readonly McpWorkspaceOptions _options;
    private readonly WorkspaceGuard _guard;
    private readonly IProcessRunner _processRunner;
    private readonly CatalogExportService _catalogExportService;
    private readonly ILogger<PagesSyncService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="PagesSyncService"/>.
    /// </summary>
    public PagesSyncService(
        McpWorkspaceOptions options,
        WorkspaceGuard guard,
        IProcessRunner processRunner,
        CatalogExportService catalogExportService,
        ILogger<PagesSyncService> logger)
    {
        _options = options;
        _guard = guard;
        _processRunner = processRunner;
        _catalogExportService = catalogExportService;
        _logger = logger;
    }

    /// <summary>
    /// Builds the full-edition package from <c>main</c> and syncs assets to an isolated <c>gh-pages</c> worktree.
    /// </summary>
    /// <param name="dryRun">When <c>true</c> (default), skips commit and push.</param>
    /// <param name="includeCatalogJson">Whether to export and include <c>data/catalog.json</c>.</param>
    /// <param name="commitMessage">Commit message used when <paramref name="dryRun"/> is <c>false</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A structured <see cref="PagesSyncResult"/>.</returns>
    public async Task<PagesSyncResult> SyncAndPublishAsync(
        bool dryRun = true,
        bool includeCatalogJson = false,
        string commitMessage = DefaultCommitMessage,
        CancellationToken cancellationToken = default)
    {
        string validatedCommitMessage = _guard.ValidateCommitMessage(commitMessage);
        string root = _options.EnsureValidWorkspaceRoot();
        string runId = Guid.NewGuid().ToString("N");
        string stagingRoot = Path.Combine(Path.GetTempPath(), $"eccselection-stage-{runId}");
        string worktreeDir = Path.Combine(Path.GetTempPath(), $"eccselection-ghpages-{runId}");

        var warnings = new List<string>();
        string? mainCommitSha = null;
        bool worktreeCreated = false;

        try
        {
            Directory.CreateDirectory(stagingRoot);

            (string? sha, string? branchError) = await VerifyBranchesAsync(root, cancellationToken);
            mainCommitSha = sha;
            if (branchError is not null)
            {
                return CreateFailure(dryRun, mainCommitSha, warnings, branchError);
            }

            (string? finalZipPath, long zipSizeBytes, string? bundleError) = await CreateBundleZipAsync(
                root,
                stagingRoot,
                cancellationToken);
            if (bundleError is not null || finalZipPath is null)
            {
                return CreateFailure(dryRun, mainCommitSha, warnings, bundleError ?? "Failed to create bundle zip.");
            }

            (bool created, string? worktreeError) = await CreateAndVerifyWorktreeAsync(
                root,
                worktreeDir,
                cancellationToken);
            worktreeCreated = created;
            if (worktreeError is not null)
            {
                return CreateFailure(dryRun, mainCommitSha, warnings, worktreeError);
            }

            (List<string> updatedFiles, string? syncError) = await SyncWorktreeAssetsAsync(
                root,
                worktreeDir,
                finalZipPath,
                includeCatalogJson,
                cancellationToken);
            if (syncError is not null)
            {
                return CreateFailure(dryRun, mainCommitSha, warnings, syncError);
            }

            if (dryRun)
            {
                return new PagesSyncResult(
                    Success: true,
                    DryRun: true,
                    TargetBranch: TargetBranchName,
                    MainCommitSha: mainCommitSha,
                    ZipSizeBytes: zipSizeBytes,
                    UpdatedFiles: updatedFiles,
                    Pushed: false,
                    GhPagesCommitSha: null,
                    Warnings: warnings,
                    Message: "Dry-run completed. Pass dryRun=false to commit and push to origin/gh-pages.");
            }

            return await CommitAndPushAsync(
                worktreeDir,
                mainCommitSha,
                zipSizeBytes,
                updatedFiles,
                validatedCommitMessage,
                warnings,
                cancellationToken);
        }
        finally
        {
            await CleanupWorktreeAsync(root, worktreeDir, worktreeCreated);
            TryDeleteDirectory(stagingRoot);
            TryDeleteDirectory(worktreeDir);
        }
    }

    private async Task<(string? MainCommitSha, string? ErrorMessage)> VerifyBranchesAsync(
        string root,
        CancellationToken cancellationToken)
    {
        ProcessResult mainRefResult = await RunGitAsync(root, ["rev-parse", "--verify", "main"], cancellationToken);
        if (mainRefResult.ExitCode != 0)
        {
            return (null, "Local 'main' branch was not found.");
        }

        string mainCommitSha = mainRefResult.StandardOutput.Trim();
        ProcessResult ghPagesRefResult = await RunGitAsync(
            root,
            ["rev-parse", "--verify", TargetBranchName],
            cancellationToken);
        if (ghPagesRefResult.ExitCode != 0)
        {
            return (mainCommitSha, "Local 'gh-pages' branch was not found.");
        }

        return (mainCommitSha, null);
    }

    private async Task<(string? FinalZipPath, long ZipSizeBytes, string? ErrorMessage)> CreateBundleZipAsync(
        string root,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        string rawZipPath = Path.Combine(stagingRoot, "raw-main.zip");
        ProcessResult archiveResult = await RunGitAsync(
            root,
            ["archive", "--format=zip", $"--output={rawZipPath}", "main"],
            cancellationToken);
        if (archiveResult.ExitCode != 0)
        {
            return (null, 0, $"git archive failed: {archiveResult.StandardError}");
        }

        if (!File.Exists(rawZipPath))
        {
            return (null, 0, "git archive did not produce expected zip archive.");
        }

        string finalZipPath = Path.Combine(stagingRoot, "eccselection-full.zip");
        long zipSizeBytes = BuildFilteredBundleZip(rawZipPath, stagingRoot, finalZipPath);
        return (finalZipPath, zipSizeBytes, null);
    }

    private async Task<(bool WorktreeCreated, string? ErrorMessage)> CreateAndVerifyWorktreeAsync(
        string root,
        string worktreeDir,
        CancellationToken cancellationToken)
    {
        ProcessResult worktreeAddResult = await RunGitAsync(
            root,
            ["worktree", "add", worktreeDir, TargetBranchName],
            cancellationToken);
        if (worktreeAddResult.ExitCode != 0)
        {
            return (false, $"git worktree add failed: {worktreeAddResult.StandardError}");
        }

        // Dual branch lock: verify worktree HEAD is strictly gh-pages before touching any files.
        ProcessResult headCheck = await RunGitAsync(
            worktreeDir,
            ["rev-parse", "--abbrev-ref", "HEAD"],
            cancellationToken);
        string currentBranch = headCheck.StandardOutput.Trim();
        if (headCheck.ExitCode != 0 || !string.Equals(currentBranch, TargetBranchName, StringComparison.Ordinal))
        {
            return (true, $"Branch lock failed: expected worktree HEAD '{TargetBranchName}', got '{currentBranch}'.");
        }

        return (true, null);
    }

    private async Task<(List<string> UpdatedFiles, string? ErrorMessage)> SyncWorktreeAssetsAsync(
        string root,
        string worktreeDir,
        string finalZipPath,
        bool includeCatalogJson,
        CancellationToken cancellationToken)
    {
        var updatedFiles = new List<string>();

        string downloadsDir = Path.Combine(worktreeDir, "downloads");
        Directory.CreateDirectory(downloadsDir);
        File.Copy(finalZipPath, Path.Combine(downloadsDir, "eccselection-full.zip"), overwrite: true);
        updatedFiles.Add("downloads/eccselection-full.zip");

        ProcessResult cssResult = await RunGitAsync(
            root,
            ["show", "main:src/ECCSELECTION.Web/wwwroot/css/icon3d.css"],
            cancellationToken);
        if (cssResult.ExitCode != 0)
        {
            return (updatedFiles, $"Failed to read icon3d.css from main: {cssResult.StandardError}");
        }

        string cssDir = Path.Combine(worktreeDir, "css");
        Directory.CreateDirectory(cssDir);
        await File.WriteAllTextAsync(Path.Combine(cssDir, "icon3d.css"), cssResult.StandardOutput, Utf8NoBom, cancellationToken);
        updatedFiles.Add("css/icon3d.css");

        if (includeCatalogJson)
        {
            (_, string catalogJson) = await _catalogExportService.ReadCatalogFromWorkspaceAsync(
                cancellationToken: cancellationToken);
            string dataDir = Path.Combine(worktreeDir, "data");
            Directory.CreateDirectory(dataDir);
            await File.WriteAllTextAsync(Path.Combine(dataDir, "catalog.json"), catalogJson, Utf8NoBom, cancellationToken);
            updatedFiles.Add("data/catalog.json");
        }

        return (updatedFiles, null);
    }

    private async Task<PagesSyncResult> CommitAndPushAsync(
        string worktreeDir,
        string? mainCommitSha,
        long zipSizeBytes,
        List<string> updatedFiles,
        string validatedCommitMessage,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        ProcessResult statusResult = await RunGitAsync(worktreeDir, ["status", "--porcelain"], cancellationToken);
        if (statusResult.ExitCode != 0)
        {
            return CreateFailure(false, mainCommitSha, warnings, $"git status failed: {statusResult.StandardError}");
        }

        if (string.IsNullOrWhiteSpace(statusResult.StandardOutput))
        {
            return new PagesSyncResult(
                Success: true,
                DryRun: false,
                TargetBranch: TargetBranchName,
                MainCommitSha: mainCommitSha,
                ZipSizeBytes: zipSizeBytes,
                UpdatedFiles: updatedFiles,
                Pushed: false,
                GhPagesCommitSha: null,
                Warnings: warnings,
                Message: "No changes detected on gh-pages; skipped commit and push.");
        }

        List<string> addArgs = ["add", .. updatedFiles];
        ProcessResult addResult = await RunGitAsync(worktreeDir, addArgs, cancellationToken);
        if (addResult.ExitCode != 0)
        {
            return CreateFailure(false, mainCommitSha, warnings, $"git add failed: {addResult.StandardError}");
        }

        ProcessResult commitResult = await RunGitAsync(
            worktreeDir,
            ["commit", "-m", validatedCommitMessage],
            cancellationToken);
        if (commitResult.ExitCode != 0)
        {
            return CreateFailure(false, mainCommitSha, warnings, $"git commit failed: {commitResult.StandardError}");
        }

        ProcessResult shaResult = await RunGitAsync(worktreeDir, ["rev-parse", "HEAD"], cancellationToken);
        string ghPagesCommitSha = shaResult.StandardOutput.Trim();

        ProcessResult pushResult = await RunGitAsync(
            worktreeDir,
            ["push", "origin", "gh-pages:gh-pages"],
            cancellationToken);
        if (pushResult.ExitCode != 0)
        {
            return CreateFailure(false, mainCommitSha, warnings, $"git push failed: {pushResult.StandardError}");
        }

        return new PagesSyncResult(
            Success: true,
            DryRun: false,
            TargetBranch: TargetBranchName,
            MainCommitSha: mainCommitSha,
            ZipSizeBytes: zipSizeBytes,
            UpdatedFiles: updatedFiles,
            Pushed: true,
            GhPagesCommitSha: ghPagesCommitSha,
            Warnings: warnings,
            Message: "Successfully committed and pushed synced assets to origin/gh-pages.");
    }

    private async Task CleanupWorktreeAsync(string root, string worktreeDir, bool worktreeCreated)
    {
        if (!worktreeCreated)
        {
            return;
        }

        try
        {
            await RunGitAsync(root, ["worktree", "remove", "--force", worktreeDir], CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove temporary worktree '{WorktreeDir}'.", worktreeDir);
        }
    }

    private Task<ProcessResult> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        return _processRunner.RunAsync("git", args, workingDirectory, GitTimeout, cancellationToken);
    }

    private static long BuildFilteredBundleZip(string rawZipPath, string stagingRoot, string finalZipPath)
    {
        string extractDir = Path.Combine(stagingRoot, "extracted");
        string filteredDir = Path.Combine(stagingRoot, "filtered");
        ZipFile.ExtractToDirectory(rawZipPath, extractDir, overwriteFiles: true);
        Directory.CreateDirectory(filteredDir);

        foreach (string item in AllowedBundleRoots)
        {
            string sourceFile = Path.Combine(extractDir, item);
            string destPath = Path.Combine(filteredDir, item);

            if (File.Exists(sourceFile))
            {
                File.Copy(sourceFile, destPath, overwrite: true);
            }
            else if (Directory.Exists(sourceFile))
            {
                CopyDirectoryFiltered(sourceFile, destPath);
            }
        }

        if (File.Exists(finalZipPath))
        {
            File.Delete(finalZipPath);
        }

        ZipFile.CreateFromDirectory(filteredDir, finalZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return new FileInfo(finalZipPath).Length;
    }

    private static void CopyDirectoryFiltered(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (string file in Directory.EnumerateFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            if (string.Equals(fileName, "appsettings.Development.local.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(file, Path.Combine(destinationDir, fileName), overwrite: true);
        }

        foreach (string subDir in Directory.EnumerateDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(subDir);
            if (ExcludedDirNames.Contains(dirName))
            {
                continue;
            }

            CopyDirectoryFiltered(subDir, Path.Combine(destinationDir, dirName));
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to delete temporary directory '{TempDir}'.", path);
        }
    }

    private static PagesSyncResult CreateFailure(
        bool dryRun,
        string? mainCommitSha,
        IReadOnlyList<string> warnings,
        string errorMessage) =>
        new(
            Success: false,
            DryRun: dryRun,
            TargetBranch: TargetBranchName,
            MainCommitSha: mainCommitSha,
            ZipSizeBytes: 0,
            UpdatedFiles: [],
            Pushed: false,
            GhPagesCommitSha: null,
            Warnings: warnings,
            Message: errorMessage,
            ErrorMessage: errorMessage);
}
