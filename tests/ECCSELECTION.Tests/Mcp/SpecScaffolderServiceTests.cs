using ECCSELECTION.Mcp.Options;
using ECCSELECTION.Mcp.Security;
using ECCSELECTION.Mcp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ECCSELECTION.Tests.Mcp;

public sealed class SpecScaffolderServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public SpecScaffolderServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"ecc-spec-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        File.WriteAllText(Path.Combine(_tempRoot, "ECCSELECTION.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(_tempRoot, "SPEC_ECCSELECTION.md"), "# Protected Root Spec");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreateSpecAsync_ValidInput_CreatesStandaloneSpecUnderDocsSpecs()
    {
        SpecScaffolderService service = CreateService();

        CreateSpecResult result = await service.CreateSpecAsync(
            topic: "mcp-unit-tests",
            title: "MCP 單元測試規格",
            author: "SA Agent",
            date: "20261126",
            overwrite: false);

        Assert.True(result.Success);
        Assert.Equal("docs/specs/SPEC_20261126_MCP_UNIT_TESTS.md", result.RelativePath);
        Assert.NotNull(result.FullPath);
        Assert.True(File.Exists(result.FullPath));
        Assert.True(result.BytesWritten > 0);

        string content = await File.ReadAllTextAsync(result.FullPath);
        Assert.Contains("# SPEC_20261126_MCP_UNIT_TESTS — MCP 單元測試規格", content);
        Assert.Contains("2026-11-26", content);
        Assert.Contains("人類確認閘門", content);
        Assert.Equal("# Protected Root Spec", await File.ReadAllTextAsync(Path.Combine(_tempRoot, "SPEC_ECCSELECTION.md")));
    }

    [Fact]
    public async Task CreateSpecAsync_ExistingFileAndOverwriteFalse_RefusesOverwrite()
    {
        SpecScaffolderService service = CreateService();
        string specsDir = Path.Combine(_tempRoot, "docs", "specs");
        Directory.CreateDirectory(specsDir);
        string existingPath = Path.Combine(specsDir, "SPEC_20261126_EXISTING.md");
        await File.WriteAllTextAsync(existingPath, "original content");

        CreateSpecResult result = await service.CreateSpecAsync(
            topic: "existing",
            title: "既有規格測試",
            date: "20261126",
            overwrite: false);

        Assert.False(result.Success);
        Assert.Contains("already exists", result.ErrorMessage);
        Assert.Equal("original content", await File.ReadAllTextAsync(existingPath));
    }

    [Fact]
    public async Task CreateSpecAsync_ExistingFileAndOverwriteTrue_OverwritesFile()
    {
        SpecScaffolderService service = CreateService();
        string specsDir = Path.Combine(_tempRoot, "docs", "specs");
        Directory.CreateDirectory(specsDir);
        string existingPath = Path.Combine(specsDir, "SPEC_20261126_EXISTING.md");
        await File.WriteAllTextAsync(existingPath, "original content");

        CreateSpecResult result = await service.CreateSpecAsync(
            topic: "existing",
            title: "覆寫更新規格",
            date: "20261126",
            overwrite: true);

        Assert.True(result.Success);
        string updatedContent = await File.ReadAllTextAsync(existingPath);
        Assert.Contains("覆寫更新規格", updatedContent);
    }

    [Theory]
    [InlineData("../SPEC_ECCSELECTION")]
    [InlineData("bad/topic")]
    [InlineData("a")]
    public async Task CreateSpecAsync_InvalidTopic_ThrowsArgumentExceptionAndNeverTouchesRootSpec(string invalidTopic)
    {
        SpecScaffolderService service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateSpecAsync(topic: invalidTopic, title: "合法標題", date: "20261126"));

        Assert.Equal("# Protected Root Spec", await File.ReadAllTextAsync(Path.Combine(_tempRoot, "SPEC_ECCSELECTION.md")));
    }

    private SpecScaffolderService CreateService() =>
        new(
            new McpWorkspaceOptions(_tempRoot),
            new WorkspaceGuard(),
            NullLogger<SpecScaffolderService>.Instance);
}
