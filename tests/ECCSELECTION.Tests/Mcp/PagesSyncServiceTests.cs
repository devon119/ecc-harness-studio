using System.IO.Compression;
using ECCSELECTION.Mcp.Options;
using ECCSELECTION.Mcp.Security;
using ECCSELECTION.Mcp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ECCSELECTION.Tests.Mcp;

public sealed class PagesSyncServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public PagesSyncServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"ecc-pages-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
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
    public async Task SyncAndPublishAsync_DryRunTrue_NeverCommitsOrPushesAndAlwaysCleansWorktree()
    {
        var runner = new FakeGitRunner(worktreeBranch: "gh-pages", porcelainStatus: "M downloads/eccselection-full.zip");
        PagesSyncService service = CreateService(runner);

        PagesSyncResult result = await service.SyncAndPublishAsync(dryRun: true);

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        Assert.False(result.Pushed);
        Assert.Equal("gh-pages", result.TargetBranch);
        Assert.Equal("1111111111111111111111111111111111111111", result.MainCommitSha);
        Assert.True(result.ZipSizeBytes > 0);
        Assert.Equal(["downloads/eccselection-full.zip", "css/icon3d.css"], result.UpdatedFiles);

        Assert.DoesNotContain(runner.GitInvocations, args => args.Contains("commit"));
        Assert.DoesNotContain(runner.GitInvocations, args => args.Contains("push"));
        Assert.Contains(runner.GitInvocations, args => args is ["worktree", "remove", "--force", _]);
    }

    [Fact]
    public async Task SyncAndPublishAsync_WorktreeHeadNotGhPages_AbortsWithoutPushAndCleansWorktree()
    {
        var runner = new FakeGitRunner(worktreeBranch: "main", porcelainStatus: "M downloads/eccselection-full.zip");
        PagesSyncService service = CreateService(runner);

        PagesSyncResult result = await service.SyncAndPublishAsync(dryRun: false);

        Assert.False(result.Success);
        Assert.False(result.Pushed);
        Assert.Contains("Branch lock failed", result.ErrorMessage);
        Assert.DoesNotContain(runner.GitInvocations, args => args.Contains("commit"));
        Assert.DoesNotContain(runner.GitInvocations, args => args.Contains("push"));
        Assert.Contains(runner.GitInvocations, args => args is ["worktree", "remove", "--force", _]);
    }

    [Fact]
    public async Task SyncAndPublishAsync_DryRunFalseWithChanges_CommitsAndPushesExplicitGhPagesRefspec()
    {
        var runner = new FakeGitRunner(worktreeBranch: "gh-pages", porcelainStatus: "M downloads/eccselection-full.zip");
        PagesSyncService service = CreateService(runner);

        PagesSyncResult result = await service.SyncAndPublishAsync(
            dryRun: false,
            commitMessage: "chore(gh-pages): sync bundle from main");

        Assert.True(result.Success);
        Assert.False(result.DryRun);
        Assert.True(result.Pushed);
        Assert.Equal("2222222222222222222222222222222222222222", result.GhPagesCommitSha);

        Assert.Contains(runner.GitInvocations, args => args is ["add", "downloads/eccselection-full.zip", "css/icon3d.css"]);
        Assert.Contains(runner.GitInvocations, args => args is ["commit", "-m", "chore(gh-pages): sync bundle from main"]);
        Assert.Contains(runner.GitInvocations, args => args is ["push", "origin", "gh-pages:gh-pages"]);
        Assert.Contains(runner.GitInvocations, args => args is ["worktree", "remove", "--force", _]);
    }

    [Fact]
    public async Task SyncAndPublishAsync_InvalidCommitMessage_ThrowsArgumentExceptionBeforeGitCalls()
    {
        var runner = new FakeGitRunner(worktreeBranch: "gh-pages", porcelainStatus: string.Empty);
        PagesSyncService service = CreateService(runner);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SyncAndPublishAsync(dryRun: false, commitMessage: "line1\nline2"));

        Assert.Empty(runner.GitInvocations);
    }

    private PagesSyncService CreateService(IProcessRunner runner)
    {
        var options = new McpWorkspaceOptions(_tempRoot);
        var guard = new WorkspaceGuard();
        var catalogExportService = new CatalogExportService(
            options,
            guard,
            NullLogger<CatalogExportService>.Instance);

        return new PagesSyncService(
            options,
            guard,
            runner,
            catalogExportService,
            NullLogger<PagesSyncService>.Instance);
    }

    private sealed class FakeGitRunner(string worktreeBranch, string porcelainStatus) : IProcessRunner
    {
        public List<string[]> GitInvocations { get; } = [];

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            string[] args = [.. arguments];
            GitInvocations.Add(args);

            if (args is ["rev-parse", "--verify", "main"])
            {
                return Task.FromResult(new ProcessResult(0, "1111111111111111111111111111111111111111", string.Empty, false, 10));
            }

            if (args is ["rev-parse", "--verify", "gh-pages"])
            {
                return Task.FromResult(new ProcessResult(0, "2222222222222222222222222222222222222222", string.Empty, false, 10));
            }

            if (args.Length == 4 && args[0] == "archive" && args[2].StartsWith("--output=", StringComparison.Ordinal))
            {
                string outputZip = args[2]["--output=".Length..];
                CreateSampleRawZip(outputZip);
                return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, false, 20));
            }

            if (args is ["worktree", "add", _, "gh-pages"])
            {
                Directory.CreateDirectory(args[2]);
                return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, false, 15));
            }

            if (args is ["rev-parse", "--abbrev-ref", "HEAD"])
            {
                return Task.FromResult(new ProcessResult(0, worktreeBranch, string.Empty, false, 5));
            }

            if (args is ["show", "main:src/ECCSELECTION.Web/wwwroot/css/icon3d.css"])
            {
                return Task.FromResult(new ProcessResult(0, ".ecc-icon-3d { color: #38bdf8; }", string.Empty, false, 5));
            }

            if (args is ["status", "--porcelain"])
            {
                return Task.FromResult(new ProcessResult(0, porcelainStatus, string.Empty, false, 5));
            }

            if (args is ["rev-parse", "HEAD"])
            {
                return Task.FromResult(new ProcessResult(0, "2222222222222222222222222222222222222222", string.Empty, false, 5));
            }

            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, false, 5));
        }

        private static void CreateSampleRawZip(string outputZipPath)
        {
            using FileStream stream = File.Create(outputZipPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

            AddEntry(archive, "ECCSELECTION.slnx", "<Solution />");
            AddEntry(archive, "src/ECCSELECTION.Web/Program.cs", "// program");
            AddEntry(archive, "docs/specs/internal.md", "# excluded");
        }

        private static void AddEntry(ZipArchive archive, string entryName, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }
}
