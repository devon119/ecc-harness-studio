using System.Security;
using System.Text.Json;
using ECCSELECTION.Mcp.Options;
using ECCSELECTION.Mcp.Security;
using ECCSELECTION.Mcp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace ECCSELECTION.Tests.Mcp;

public sealed class CatalogExportServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dbRelativePath = Path.Combine("src", "ECCSELECTION.Web", "eccselection.db");
    private readonly string _dbFullPath;

    public CatalogExportServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"ecc-catalog-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "src", "ECCSELECTION.Web"));
        File.WriteAllText(Path.Combine(_tempRoot, "ECCSELECTION.slnx"), "<Solution />");
        _dbFullPath = Path.Combine(_tempRoot, _dbRelativePath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExportAsync_ReadOnlyPreview_ExportsOnlyActiveComponentsAndMapsTypes()
    {
        await SeedSampleDatabaseAsync(_dbFullPath);
        CatalogExportService service = CreateService();

        CatalogExportResult result = await service.ExportAsync(databasePath: _dbRelativePath, writeFile: false);

        Assert.True(result.Success);
        Assert.False(result.WroteFile);
        Assert.Null(result.OutputPath);
        Assert.Equal(4, result.TotalExported);
        Assert.Equal(1, result.CountsByType["Agent"]);
        Assert.Equal(1, result.CountsByType["Skill"]);
        Assert.Equal(1, result.CountsByType["Rule"]);
        Assert.Equal(1, result.CountsByType["Command"]);
        Assert.DoesNotContain(result.PreviewItems, p => p.Name == "inactive-agent");
    }

    [Fact]
    public async Task ExportAsync_WriteFileTrue_WritesPascalCaseJsonWithoutInternalFields()
    {
        await SeedSampleDatabaseAsync(_dbFullPath);
        CatalogExportService service = CreateService();
        string outputRelPath = Path.Combine("data", "catalog.json");

        CatalogExportResult result = await service.ExportAsync(
            databasePath: _dbRelativePath,
            outputPath: outputRelPath,
            writeFile: true);

        Assert.True(result.Success);
        Assert.True(result.WroteFile);
        Assert.NotNull(result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));

        byte[] rawBytes = await File.ReadAllBytesAsync(result.OutputPath);
        Assert.False(rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF);

        string json = await File.ReadAllTextAsync(result.OutputPath);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(4, doc.RootElement.GetArrayLength());

        JsonElement first = doc.RootElement[0];
        Assert.True(first.TryGetProperty("Id", out _));
        Assert.True(first.TryGetProperty("ComponentType", out JsonElement typeProp));
        Assert.Equal("Agent", typeProp.GetString());
        Assert.True(first.TryGetProperty("Name", out _));
        Assert.True(first.TryGetProperty("Path", out _));

        Assert.False(first.TryGetProperty("Sha", out _));
        Assert.False(first.TryGetProperty("LastSyncedAt", out _));
        Assert.False(first.TryGetProperty("IsActive", out _));
        Assert.False(first.TryGetProperty("IsManuallyEdited", out _));
    }

    [Fact]
    public async Task ExportAsync_DatabaseMissing_ReturnsStructuredFailure()
    {
        CatalogExportService service = CreateService();

        CatalogExportResult result = await service.ExportAsync(
            databasePath: Path.Combine("src", "ECCSELECTION.Web", "missing.db"),
            writeFile: false);

        Assert.False(result.Success);
        Assert.Equal(0, result.TotalExported);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportAsync_WriteFileTrueWithoutOutputPath_ReturnsFailure()
    {
        await SeedSampleDatabaseAsync(_dbFullPath);
        CatalogExportService service = CreateService();

        CatalogExportResult result = await service.ExportAsync(
            databasePath: _dbRelativePath,
            outputPath: null,
            writeFile: true);

        Assert.False(result.Success);
        Assert.Contains("outputPath", result.ErrorMessage);
    }

    [Fact]
    public async Task ExportAsync_DatabasePathTraversal_ThrowsSecurityException()
    {
        CatalogExportService service = CreateService();

        await Assert.ThrowsAsync<SecurityException>(() =>
            service.ExportAsync(databasePath: "../outside.db", writeFile: false));
    }

    [Fact]
    public async Task ExportAsync_OutputPathTraversalWhenWriteFileTrue_ThrowsSecurityException()
    {
        await SeedSampleDatabaseAsync(_dbFullPath);
        CatalogExportService service = CreateService();

        await Assert.ThrowsAsync<SecurityException>(() =>
            service.ExportAsync(
                databasePath: _dbRelativePath,
                outputPath: "../outside.json",
                writeFile: true));

        string outsideAbsolute = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.json");
        await Assert.ThrowsAsync<SecurityException>(() =>
            service.ExportAsync(
                databasePath: _dbRelativePath,
                outputPath: outsideAbsolute,
                writeFile: true));
    }

    [Fact]
    public async Task ExportAsync_StringComponentTypes_NormalizesCaseInsensitivelyWithUnknownFallback()
    {
        await SeedStringTypedDatabaseAsync(_dbFullPath);
        CatalogExportService service = CreateService();

        CatalogExportResult result = await service.ExportAsync(databasePath: _dbRelativePath, writeFile: false);

        Assert.True(result.Success);
        Assert.Equal(5, result.TotalExported);
        Assert.Equal(1, result.CountsByType["Agent"]);
        Assert.Equal(1, result.CountsByType["Skill"]);
        Assert.Equal(1, result.CountsByType["Rule"]);
        Assert.Equal(1, result.CountsByType["Command"]);
        Assert.Equal(1, result.CountsByType["Unknown"]);
        Assert.Contains(result.PreviewItems, p => p.Name == "a1" && p.ComponentType == "Agent");
        Assert.Contains(result.PreviewItems, p => p.Name == "s1" && p.ComponentType == "Skill");
        Assert.Contains(result.PreviewItems, p => p.Name == "r1" && p.ComponentType == "Rule");
        Assert.Contains(result.PreviewItems, p => p.Name == "c1" && p.ComponentType == "Command");
        Assert.Contains(result.PreviewItems, p => p.Name == "u1" && p.ComponentType == "Unknown");
    }

    private CatalogExportService CreateService() =>
        new(
            new McpWorkspaceOptions(_tempRoot),
            new WorkspaceGuard(),
            NullLogger<CatalogExportService>.Instance);

    private static async Task SeedStringTypedDatabaseAsync(string dbPath)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = false
        };

        await using var connection = new SqliteConnection(csb.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand createCmd = connection.CreateCommand();
        createCmd.CommandText = """
            CREATE TABLE EccComponents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ComponentType TEXT NOT NULL,
                Name TEXT NOT NULL,
                Path TEXT NOT NULL UNIQUE,
                Sha TEXT NOT NULL,
                RawUrl TEXT NULL,
                HtmlUrl TEXT NULL,
                DescriptionEn TEXT NULL,
                SummaryZh TEXT NULL,
                SdlcRoles TEXT NULL,
                TechStacks TEXT NULL,
                ArchStyles TEXT NULL,
                UsageGuide TEXT NULL,
                IsActive INTEGER NOT NULL,
                IsManuallyEdited INTEGER NOT NULL,
                LastSyncedAt TEXT NOT NULL
            );
            """;
        await createCmd.ExecuteNonQueryAsync();

        await using SqliteCommand insertCmd = connection.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO EccComponents (ComponentType, Name, Path, Sha, IsActive, IsManuallyEdited, LastSyncedAt)
            VALUES
            ('agent', 'a1', 'agents/a1.md', 's1', 1, 0, '2026-11-27T00:00:00Z'),
            ('SKILL', 's1', 'skills/s1/SKILL.md', 's2', 1, 0, '2026-11-27T00:00:00Z'),
            ('Rule', 'r1', 'rules/r1.md', 's3', 1, 0, '2026-11-27T00:00:00Z'),
            ('Command', 'c1', 'commands/c1.md', 's4', 1, 0, '2026-11-27T00:00:00Z'),
            ('CustomType', 'u1', 'other/u1.md', 's5', 1, 0, '2026-11-27T00:00:00Z');
            """;
        await insertCmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedSampleDatabaseAsync(string dbPath)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = false
        };

        await using var connection = new SqliteConnection(csb.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand createCmd = connection.CreateCommand();
        createCmd.CommandText = """
            CREATE TABLE EccComponents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ComponentType INTEGER NOT NULL,
                Name TEXT NOT NULL,
                Path TEXT NOT NULL UNIQUE,
                Sha TEXT NOT NULL,
                RawUrl TEXT NULL,
                HtmlUrl TEXT NULL,
                DescriptionEn TEXT NULL,
                SummaryZh TEXT NULL,
                SdlcRoles TEXT NULL,
                TechStacks TEXT NULL,
                ArchStyles TEXT NULL,
                UsageGuide TEXT NULL,
                IsActive INTEGER NOT NULL,
                IsManuallyEdited INTEGER NOT NULL,
                LastSyncedAt TEXT NOT NULL
            );
            """;
        await createCmd.ExecuteNonQueryAsync();

        await using SqliteCommand insertCmd = connection.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO EccComponents (ComponentType, Name, Path, Sha, RawUrl, HtmlUrl, DescriptionEn, SummaryZh, SdlcRoles, TechStacks, ArchStyles, UsageGuide, IsActive, IsManuallyEdited, LastSyncedAt)
            VALUES
            (0, 'architect', 'agents/architect.md', 'sha1', 'https://raw/1', 'https://html/1', 'En1', 'Zh1', 'SA', 'dotnet', 'MVC', 'Use1', 1, 0, '2026-11-26T00:00:00Z'),
            (1, 'tdd-workflow', 'skills/tdd-workflow/SKILL.md', 'sha2', 'https://raw/2', 'https://html/2', 'En2', 'Zh2', 'QA', 'dotnet', 'MVC', 'Use2', 1, 1, '2026-11-26T00:00:00Z'),
            (2, 'security-rule', 'rules/security.md', 'sha3', 'https://raw/3', 'https://html/3', 'En3', 'Zh3', 'Security', 'all', 'all', 'Use3', 1, 0, '2026-11-26T00:00:00Z'),
            (3, 'quality-gate', 'commands/quality-gate.md', 'sha4', 'https://raw/4', 'https://html/4', 'En4', 'Zh4', 'DevOps', 'dotnet', 'MVC', 'Use4', 1, 0, '2026-11-26T00:00:00Z'),
            (0, 'inactive-agent', 'agents/inactive.md', 'sha5', 'https://raw/5', 'https://html/5', 'En5', 'Zh5', 'SA', 'dotnet', 'MVC', 'Use5', 0, 0, '2026-11-26T00:00:00Z');
            """;
        await insertCmd.ExecuteNonQueryAsync();
    }
}
