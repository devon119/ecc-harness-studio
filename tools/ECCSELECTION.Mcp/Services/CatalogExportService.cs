using System.Text;
using System.Text.Json;
using ECCSELECTION.Mcp.Options;
using ECCSELECTION.Mcp.Security;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ECCSELECTION.Mcp.Services;

/// <summary>
/// Represents an exported active ECC component matching <c>gh-pages:data/catalog.json</c>.
/// </summary>
public sealed record CatalogExportItem(
    int Id,
    string ComponentType,
    string Name,
    string Path,
    string? RawUrl,
    string? HtmlUrl,
    string? DescriptionEn,
    string? SummaryZh,
    string? SdlcRoles,
    string? TechStacks,
    string? ArchStyles,
    string? UsageGuide);

/// <summary>
/// Concise preview item returned in <see cref="CatalogExportResult"/>.
/// </summary>
public sealed record CatalogPreviewItem(
    int Id,
    string ComponentType,
    string Name,
    string Path);

/// <summary>
/// Structured result returned by <see cref="CatalogExportService.ExportAsync"/>.
/// </summary>
public sealed record CatalogExportResult(
    bool Success,
    int TotalExported,
    IReadOnlyDictionary<string, int> CountsByType,
    bool WroteFile,
    string? OutputPath,
    IReadOnlyList<CatalogPreviewItem> PreviewItems,
    string? ErrorMessage = null);

/// <summary>
/// Reads active components from the local SQLite database in read-only mode and exports <c>catalog.json</c>.
/// </summary>
public sealed class CatalogExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly McpWorkspaceOptions _options;
    private readonly WorkspaceGuard _guard;
    private readonly ILogger<CatalogExportService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CatalogExportService"/>.
    /// </summary>
    public CatalogExportService(
        McpWorkspaceOptions options,
        WorkspaceGuard guard,
        ILogger<CatalogExportService> logger)
    {
        _options = options;
        _guard = guard;
        _logger = logger;
    }

    /// <summary>
    /// Exports active components from SQLite and optionally writes them to <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="databasePath">Path to SQLite database (relative to workspace root or absolute inside workspace).</param>
    /// <param name="outputPath">Target output path when <paramref name="writeFile"/> is <c>true</c>.</param>
    /// <param name="writeFile">Whether to write the JSON file to disk.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A structured <see cref="CatalogExportResult"/>.</returns>
    public async Task<CatalogExportResult> ExportAsync(
        string databasePath = "src/ECCSELECTION.Web/eccselection.db",
        string? outputPath = null,
        bool writeFile = false,
        CancellationToken cancellationToken = default)
    {
        string root = _options.EnsureValidWorkspaceRoot();

        string resolvedDbPath = _guard.ResolveSafePath(root, databasePath);
        if (!File.Exists(resolvedDbPath))
        {
            return CreateFailure($"SQLite database not found at '{resolvedDbPath}'.");
        }

        string? resolvedOutputPath = null;
        if (writeFile)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return CreateFailure("outputPath is required when writeFile is true.");
            }

            resolvedOutputPath = _guard.ResolveSafePath(root, outputPath);
        }

        (IReadOnlyList<CatalogExportItem> items, string json) = await ReadCatalogAsync(resolvedDbPath, cancellationToken);

        if (writeFile && resolvedOutputPath is not null)
        {
            string? parentDir = Path.GetDirectoryName(resolvedOutputPath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            await File.WriteAllTextAsync(resolvedOutputPath, json, Utf8NoBom, cancellationToken);
            _logger.LogInformation("Exported {Count} catalog items to '{OutputPath}'.", items.Count, resolvedOutputPath);
        }

        Dictionary<string, int> counts = BuildCountsByType(items);
        IReadOnlyList<CatalogPreviewItem> preview = items
            .Take(5)
            .Select(i => new CatalogPreviewItem(i.Id, i.ComponentType, i.Name, i.Path))
            .ToArray();

        return new CatalogExportResult(
            Success: true,
            TotalExported: items.Count,
            CountsByType: counts,
            WroteFile: writeFile,
            OutputPath: resolvedOutputPath,
            PreviewItems: preview);
    }

    /// <summary>
    /// Exports active components as a serialized JSON string without writing to disk.
    /// </summary>
    /// <param name="databasePath">Path to SQLite database.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Serialized JSON string and item count.</returns>
    public async Task<(IReadOnlyList<CatalogExportItem> Items, string Json)> ReadCatalogFromWorkspaceAsync(
        string databasePath = "src/ECCSELECTION.Web/eccselection.db",
        CancellationToken cancellationToken = default)
    {
        string root = _options.EnsureValidWorkspaceRoot();
        string resolvedDbPath = _guard.ResolveSafePath(root, databasePath);
        if (!File.Exists(resolvedDbPath))
        {
            throw new FileNotFoundException($"SQLite database not found at '{resolvedDbPath}'.", resolvedDbPath);
        }

        return await ReadCatalogAsync(resolvedDbPath, cancellationToken);
    }

    private static async Task<(IReadOnlyList<CatalogExportItem> Items, string Json)> ReadCatalogAsync(
        string resolvedDbPath,
        CancellationToken cancellationToken)
    {
        var items = new List<CatalogExportItem>();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = resolvedDbPath,
            Mode = SqliteOpenMode.ReadOnly
        };

        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ComponentType, Name, Path, RawUrl, HtmlUrl,
                   DescriptionEn, SummaryZh, SdlcRoles, TechStacks, ArchStyles, UsageGuide
            FROM EccComponents
            WHERE IsActive = 1
            ORDER BY ComponentType ASC, Name ASC
            """;

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            int typeValue = reader.GetInt32(1);
            items.Add(new CatalogExportItem(
                Id: reader.GetInt32(0),
                ComponentType: MapComponentType(typeValue),
                Name: reader.GetString(2),
                Path: reader.GetString(3),
                RawUrl: reader.IsDBNull(4) ? null : reader.GetString(4),
                HtmlUrl: reader.IsDBNull(5) ? null : reader.GetString(5),
                DescriptionEn: reader.IsDBNull(6) ? null : reader.GetString(6),
                SummaryZh: reader.IsDBNull(7) ? null : reader.GetString(7),
                SdlcRoles: reader.IsDBNull(8) ? null : reader.GetString(8),
                TechStacks: reader.IsDBNull(9) ? null : reader.GetString(9),
                ArchStyles: reader.IsDBNull(10) ? null : reader.GetString(10),
                UsageGuide: reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        string json = JsonSerializer.Serialize(items, JsonOptions);
        return (items, json);
    }

    private static string MapComponentType(int componentType) => componentType switch
    {
        0 => "Agent",
        1 => "Skill",
        2 => "Rule",
        3 => "Command",
        _ => "Unknown"
    };

    private static Dictionary<string, int> BuildCountsByType(IReadOnlyList<CatalogExportItem> items)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Agent"] = 0,
            ["Skill"] = 0,
            ["Rule"] = 0,
            ["Command"] = 0
        };

        foreach (CatalogExportItem item in items)
        {
            counts[item.ComponentType] = counts.GetValueOrDefault(item.ComponentType) + 1;
        }

        return counts;
    }

    private static CatalogExportResult CreateFailure(string errorMessage) =>
        new(
            Success: false,
            TotalExported: 0,
            CountsByType: new Dictionary<string, int>(StringComparer.Ordinal),
            WroteFile: false,
            OutputPath: null,
            PreviewItems: [],
            ErrorMessage: errorMessage);
}
