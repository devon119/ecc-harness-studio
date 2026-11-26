using System.Text.RegularExpressions;
using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Services.Interfaces;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ECCSELECTION.Web.Services;

public class EccParserService : IEccParserService
{
    private readonly ILogger<EccParserService> _logger;

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly Regex FrontmatterRegex = new(
        @"^\s*---\r?\n([\s\S]*?)\r?\n---\r?\n?",
        RegexOptions.Compiled);

    private static readonly Regex UsageHeadingRegex = new(
        @"^#{1,4}\s*(.*?(?:When to Use|Usage|Trigger|How to Use).*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly (string Stack, Regex Pattern)[] TechStackPatterns =
    {
        ("postgres", new Regex(@"\b(postgres|postgresql)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("mysql", new Regex(@"\bmysql\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("sqlite", new Regex(@"\bsqlite\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("sqlserver", new Regex(@"\b(sql\s*server|mssql)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("react", new Regex(@"\breact\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("vue", new Regex(@"\bvue(\.js|js)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("kotlin", new Regex(@"\b(android|kotlin)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("dotnet", new Regex(@"(?:\bdotnet\b|\bcsharp\b|\.net\b|c#|ef\s*core)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("python", new Regex(@"\b(python|django|fastapi|pytest)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("go", new Regex(@"\b(golang|go)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("java", new Regex(@"\b(java|spring|springboot)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("luau", new Regex(@"\b(roblox|luau|lua)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("godot", new Regex(@"\b(godot|gdscript)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("cpp", new Regex(@"(?:\bcpp\b|c\+\+)", RegexOptions.Compiled | RegexOptions.IgnoreCase))
    };

    private static readonly (string Role, string[] Keywords)[] SdlcRoleKeywords =
    {
        ("SA", new[] { "architect", "plan", "requirements", "spec", "design", "system", "prd" }),
        ("SD", new[] { "architect", "api-design", "schema", "database-design", "pattern", "clean", "mvc", "mvvm" }),
        ("PG_FE", new[] { "frontend", "react", "vue", "ui", "css", "accessibility", "e2e" }),
        ("PG_BE", new[] { "backend", "api", "dotnet", "go", "python", "java", "auth", "security", "service" }),
        ("PG_DB", new[] { "database", "sql", "postgres", "mysql", "sqlite", "migration", "query", "ef core" }),
        ("PG_TEST", new[] { "tdd", "test", "unit", "coverage", "mock", "xunit", "pytest" }),
        ("QA", new[] { "review", "code-review", "security-review", "verify", "qa", "e2e", "audit" })
    };

    public EccParserService(ILogger<EccParserService> logger)
    {
        _logger = logger;
    }

    public ParsedEccComponent Parse(ComponentType componentType, string path, string rawContent)
    {
        var normalizedContent = rawContent ?? string.Empty;
        var (frontmatter, body) = ExtractFrontmatter(normalizedContent, path);

        var name = ResolveName(componentType, path, frontmatter);
        var descriptionEn = ResolveDescription(frontmatter, body);
        var usageGuide = ExtractUsageGuide(componentType, name, body, descriptionEn);

        var analysisSample = $"{path}\n{name}\n{descriptionEn}\n{(body.Length > 1500 ? body[..1500] : body)}";

        var techStacks = InferTechStacks(path, analysisSample);
        var sdlcRoles = InferSdlcRoles(componentType, techStacks, analysisSample);
        var archStyles = InferArchStyles(analysisSample);
        var summaryZh = BuildDefaultSummaryZh(componentType, name, sdlcRoles, descriptionEn);

        return new ParsedEccComponent(
            componentType,
            name,
            path,
            descriptionEn,
            summaryZh,
            sdlcRoles,
            techStacks,
            archStyles,
            usageGuide);
    }

    private (FrontmatterDto? Frontmatter, string Body) ExtractFrontmatter(string content, string path)
    {
        var match = FrontmatterRegex.Match(content);
        if (!match.Success)
        {
            return (null, content);
        }

        var yamlText = match.Groups[1].Value;
        var body = content[match.Length..];

        try
        {
            var dto = YamlDeserializer.Deserialize<FrontmatterDto>(yamlText);
            return (dto, body);
        }
        catch (YamlException ex)
        {
            _logger.LogWarning(ex, "YAML frontmatter parsing failed for {Path}; falling back to markdown body.", path);
            return (null, body);
        }
    }

    private static string ResolveName(ComponentType componentType, string path, FrontmatterDto? frontmatter)
    {
        if (!string.IsNullOrWhiteSpace(frontmatter?.Name))
        {
            return frontmatter.Name.Trim();
        }

        var normalizedPath = path.Replace('\\', '/');
        if (componentType == ComponentType.Skill)
        {
            var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return parts[^2];
            }
        }

        return System.IO.Path.GetFileNameWithoutExtension(normalizedPath);
    }

    private static string ResolveDescription(FrontmatterDto? frontmatter, string body)
    {
        if (!string.IsNullOrWhiteSpace(frontmatter?.Description))
        {
            return Truncate(NormalizeWhitespace(frontmatter.Description), 300);
        }

        var lines = body.Split('\n');
        var paragraphLines = new List<string>();
        var inCodeBlock = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (inCodeBlock)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                if (paragraphLines.Count > 0)
                {
                    break;
                }
                continue;
            }

            if (line.StartsWith('#') || line.StartsWith("---", StringComparison.Ordinal))
            {
                if (paragraphLines.Count > 0)
                {
                    break;
                }
                continue;
            }

            paragraphLines.Add(line);
        }

        if (paragraphLines.Count == 0)
        {
            return string.Empty;
        }

        return Truncate(NormalizeWhitespace(string.Join(" ", paragraphLines)), 300);
    }

    private static string ExtractUsageGuide(ComponentType componentType, string name, string body, string descriptionEn)
    {
        var commandPrefix = componentType == ComponentType.Command
            ? $"以 `/{name}` 指令觸發。"
            : string.Empty;

        var match = UsageHeadingRegex.Match(body);
        if (match.Success)
        {
            var startIndex = match.Index + match.Length;
            var remaining = body[startIndex..];
            var nextHeading = Regex.Match(remaining, @"^#{1,4}\s+", RegexOptions.Multiline);
            var sectionContent = nextHeading.Success
                ? remaining[..nextHeading.Index]
                : remaining;

            var cleaned = Truncate(NormalizeWhitespace(sectionContent.Replace("- ", " ").Replace("* ", " ")), 400);
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                return string.IsNullOrEmpty(commandPrefix)
                    ? cleaned
                    : $"{commandPrefix} {cleaned}";
            }
        }

        if (!string.IsNullOrEmpty(commandPrefix))
        {
            return string.IsNullOrWhiteSpace(descriptionEn)
                ? commandPrefix
                : $"{commandPrefix} {descriptionEn}";
        }

        return string.IsNullOrWhiteSpace(descriptionEn)
            ? "依循該元件定義之工作流程與規範執行。"
            : descriptionEn;
    }

    private static string InferTechStacks(string path, string sample)
    {
        var stacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedPath = path.Replace('\\', '/').ToLowerInvariant();

        if (normalizedPath.StartsWith("rules/common/", StringComparison.Ordinal))
        {
            stacks.Add("common");
        }
        if (normalizedPath.StartsWith("rules/python/", StringComparison.Ordinal))
        {
            stacks.Add("python");
        }
        if (normalizedPath.StartsWith("rules/golang/", StringComparison.Ordinal))
        {
            stacks.Add("go");
        }
        if (normalizedPath.StartsWith("rules/typescript/", StringComparison.Ordinal))
        {
            stacks.Add("react");
            stacks.Add("vue");
            stacks.Add("typescript");
        }
        if (normalizedPath.StartsWith("rules/java/", StringComparison.Ordinal) || normalizedPath.Contains("springboot", StringComparison.Ordinal))
        {
            stacks.Add("java");
        }
        if (normalizedPath.StartsWith("rules/kotlin/", StringComparison.Ordinal))
        {
            stacks.Add("kotlin");
        }
        if (normalizedPath.StartsWith("rules/dotnet/", StringComparison.Ordinal) || normalizedPath.Contains("csharp", StringComparison.Ordinal))
        {
            stacks.Add("dotnet");
        }

        foreach (var (stack, pattern) in TechStackPatterns)
        {
            if (pattern.IsMatch(sample))
            {
                stacks.Add(stack);
            }
        }

        if (stacks.Count == 0)
        {
            stacks.Add("common");
        }

        return EccComponent.NormalizeTokens(stacks, upperCase: false);
    }

    private static string InferSdlcRoles(ComponentType componentType, string techStacks, string sample)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lowerSample = sample.ToLowerInvariant();

        foreach (var (role, keywords) in SdlcRoleKeywords)
        {
            foreach (var keyword in keywords)
            {
                if (Regex.IsMatch(lowerSample, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase))
                {
                    roles.Add(role);
                    break;
                }
            }
        }

        if (roles.Count == 0)
        {
            var stackTokens = techStacks.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (componentType == ComponentType.Rule && stackTokens.Contains("common", StringComparer.OrdinalIgnoreCase))
            {
                return "SA,SD,PG_FE,PG_BE,PG_DB,PG_TEST,QA";
            }

            return componentType switch
            {
                ComponentType.Agent => "SA,SD,PG_BE,QA",
                ComponentType.Skill => "PG_BE,PG_TEST,QA",
                ComponentType.Rule => "SA,SD,PG_BE",
                ComponentType.Command => "PG_BE,PG_TEST,QA",
                _ => "PG_BE"
            };
        }

        return EccComponent.NormalizeTokens(roles, upperCase: true);
    }

    private static string InferArchStyles(string sample)
    {
        var styles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Regex.IsMatch(sample, @"\bmvc\b", RegexOptions.IgnoreCase))
        {
            styles.Add("mvc");
        }
        if (Regex.IsMatch(sample, @"\bclean\b", RegexOptions.IgnoreCase))
        {
            styles.Add("clean");
        }
        if (Regex.IsMatch(sample, @"\bmvvm\b", RegexOptions.IgnoreCase))
        {
            styles.Add("mvvm");
        }

        if (styles.Count == 0)
        {
            styles.Add("general");
        }

        return EccComponent.NormalizeTokens(styles, upperCase: false);
    }

    private static string BuildDefaultSummaryZh(ComponentType componentType, string name, string sdlcRoles, string descriptionEn)
    {
        var typeLabel = componentType switch
        {
            ComponentType.Agent => "Agent 角色代理",
            ComponentType.Skill => "Skill 技能工作流",
            ComponentType.Rule => "Rule 開發規範",
            ComponentType.Command => "Command 快速指令",
            _ => componentType.ToString()
        };

        var roleText = string.IsNullOrWhiteSpace(sdlcRoles) ? "通用" : sdlcRoles.Replace(",", "、");
        var descText = string.IsNullOrWhiteSpace(descriptionEn)
            ? "提供對應開發階段之標準化指引與檢查。"
            : Truncate(descriptionEn, 180);

        return $"[{typeLabel}] {name}（適用角色：{roleText}）— {descText}";
    }

    private static string NormalizeWhitespace(string input)
    {
        return Regex.Replace(input, @"\s+", " ").Trim();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..(maxLength - 3)].TrimEnd() + "...";
    }

    private sealed class FrontmatterDto
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
    }
}
