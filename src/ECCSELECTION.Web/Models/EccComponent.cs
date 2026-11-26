using System.ComponentModel.DataAnnotations;
using ECCSELECTION.Web.Models.Enums;

namespace ECCSELECTION.Web.Models;

public class EccComponent
{
    public int Id { get; set; }

    public ComponentType ComponentType { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string Path { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string Sha { get; set; } = string.Empty;

    [Required]
    public string RawUrl { get; set; } = string.Empty;

    [Required]
    public string HtmlUrl { get; set; } = string.Empty;

    public string DescriptionEn { get; set; } = string.Empty;

    public string SummaryZh { get; set; } = string.Empty;

    /// <summary>
    /// 標準化逗號分隔之 SDLC 角色清單，例如 "SA,SD,PG_BE"
    /// </summary>
    public string SdlcRoles { get; set; } = string.Empty;

    /// <summary>
    /// 標準化小寫逗號分隔之技術棧 Slug 清單，例如 "dotnet,react,common"
    /// </summary>
    public string TechStacks { get; set; } = string.Empty;

    /// <summary>
    /// 標準化小寫逗號分隔之架構風格 Slug 清單，例如 "mvc,clean,mvvm,general"
    /// </summary>
    public string ArchStyles { get; set; } = string.Empty;

    public string UsageGuide { get; set; } = string.Empty;

    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;

    public bool IsManuallyEdited { get; set; }

    public IReadOnlyList<string> GetSdlcRoleTokens() => SplitTokens(SdlcRoles, upperCase: true);

    public IReadOnlyList<string> GetTechStackTokens() => SplitTokens(TechStacks, upperCase: false);

    public IReadOnlyList<string> GetArchStyleTokens() => SplitTokens(ArchStyles, upperCase: false);

    public static string NormalizeTokens(IEnumerable<string> tokens, bool upperCase = false)
    {
        var normalized = tokens
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => upperCase ? t.ToUpperInvariant() : t.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join(",", normalized);
    }

    private static IReadOnlyList<string> SplitTokens(string raw, bool upperCase)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => upperCase ? t.ToUpperInvariant() : t.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
