using System.Text.Json;
using ECCSELECTION.Web.ViewModels;

namespace ECCSELECTION.Web.Services;

public sealed record ProjectTypeCompatibilityRule(
    string DefaultArchStyle,
    IReadOnlyList<string> AllowedArchStyles,
    IReadOnlyList<string> DefaultTechStacks,
    IReadOnlyList<string> AllowedTechStacks,
    IReadOnlyList<string> AllowedSdlcRoles);

/// <summary>
/// 定義 Harness 問券之專案型態連動相容性規則，作為前後端單一真理來源。
/// </summary>
public static class WizardCompatibility
{
    private static readonly JsonSerializerOptions ClientJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IReadOnlyList<string> AllowedProjectLifecycles { get; } = ["Greenfield", "Brownfield"];

    public static IReadOnlyDictionary<string, ProjectTypeCompatibilityRule> Rules { get; } =
        new Dictionary<string, ProjectTypeCompatibilityRule>(StringComparer.OrdinalIgnoreCase)
        {
            ["Web"] = new ProjectTypeCompatibilityRule(
                DefaultArchStyle: "MVC",
                AllowedArchStyles: ["MVC", "Clean"],
                DefaultTechStacks: ["dotnet"],
                AllowedTechStacks: ["dotnet", "react", "vue", "python", "go", "java", "sqlserver", "sqlite", "postgres", "mysql"],
                AllowedSdlcRoles: ["SA", "SD", "PG_FE", "PG_BE", "PG_DB", "PG_TEST", "QA"]),

            ["Mobile"] = new ProjectTypeCompatibilityRule(
                DefaultArchStyle: "MVVM",
                AllowedArchStyles: ["MVVM", "Clean"],
                DefaultTechStacks: ["kotlin", "sqlite"],
                AllowedTechStacks: ["kotlin", "sqlite"],
                AllowedSdlcRoles: ["SA", "SD", "PG_FE", "PG_DB", "PG_TEST", "QA"]),

            ["API"] = new ProjectTypeCompatibilityRule(
                DefaultArchStyle: "Clean",
                AllowedArchStyles: ["Clean", "MVC"],
                DefaultTechStacks: ["dotnet"],
                AllowedTechStacks: ["dotnet", "python", "go", "java", "sqlserver", "sqlite", "postgres", "mysql"],
                AllowedSdlcRoles: ["SA", "SD", "PG_BE", "PG_DB", "PG_TEST", "QA"]),

            ["Fullstack"] = new ProjectTypeCompatibilityRule(
                DefaultArchStyle: "Clean",
                AllowedArchStyles: ["Clean", "MVC"],
                DefaultTechStacks: ["dotnet", "react"],
                AllowedTechStacks: ["dotnet", "react", "vue", "python", "go", "java", "sqlserver", "sqlite", "postgres", "mysql"],
                AllowedSdlcRoles: ["SA", "SD", "PG_FE", "PG_BE", "PG_DB", "PG_TEST", "QA"]),

            ["Game"] = new ProjectTypeCompatibilityRule(
                DefaultArchStyle: "ECS",
                AllowedArchStyles: ["ECS", "Clean", "MVC"],
                DefaultTechStacks: ["luau"],
                AllowedTechStacks: ["luau", "godot", "dotnet", "cpp", "sqlite"],
                AllowedSdlcRoles: ["SA", "SD", "PG_FE", "PG_BE", "PG_DB", "PG_TEST", "QA"])
        };

    public static ProjectTypeCompatibilityRule? GetRule(string? projectType)
    {
        if (string.IsNullOrWhiteSpace(projectType))
        {
            return null;
        }

        return Rules.TryGetValue(projectType.Trim(), out var rule) ? rule : null;
    }

    public static IReadOnlyList<(string MemberName, string ErrorMessage)> Validate(WizardRequestViewModel request)
    {
        var errors = new List<(string MemberName, string ErrorMessage)>();

        if (string.IsNullOrWhiteSpace(request.ProjectLifecycle) ||
            !AllowedProjectLifecycles.Contains(request.ProjectLifecycle.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            errors.Add((
                nameof(request.ProjectLifecycle),
                $"不支援的專案導入階段：{request.ProjectLifecycle}（適用：{string.Join("、", AllowedProjectLifecycles)}）。"));
        }

        var rule = GetRule(request.ProjectType);
        if (rule is null)
        {
            errors.Add((nameof(request.ProjectType), $"不支援的專案型態：{request.ProjectType}。"));
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.ArchStyle) ||
            !rule.AllowedArchStyles.Contains(request.ArchStyle.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            errors.Add((
                nameof(request.ArchStyle),
                $"專案型態「{request.ProjectType}」不適用架構風格「{request.ArchStyle}」（適用：{string.Join("、", rule.AllowedArchStyles)}）。"));
        }

        var invalidStacks = request.TechStacks
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Where(t => !rule.AllowedTechStacks.Contains(t, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (invalidStacks.Count > 0)
        {
            errors.Add((
                nameof(request.TechStacks),
                $"專案型態「{request.ProjectType}」不適用技術棧：{string.Join("、", invalidStacks)}。"));
        }

        var roles = request.SdlcRoles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (roles.Count == 0)
        {
            errors.Add((nameof(request.SdlcRoles), "請至少選擇一個 SDLC 角色。"));
        }
        else
        {
            var invalidRoles = roles
                .Where(r => !rule.AllowedSdlcRoles.Contains(r, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (invalidRoles.Count > 0)
            {
                errors.Add((
                    nameof(request.SdlcRoles),
                    $"專案型態「{request.ProjectType}」不適用 SDLC 角色：{string.Join("、", invalidRoles)}。"));
            }
        }

        return errors;
    }

    public static string ToClientRulesJson() => JsonSerializer.Serialize(Rules, ClientJsonOptions);
}
