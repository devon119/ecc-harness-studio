using System.Text;
using System.Text.RegularExpressions;
using ECCSELECTION.Mcp.Options;
using ECCSELECTION.Mcp.Security;

namespace ECCSELECTION.Mcp.Services;

/// <summary>
/// Summary counts for <see cref="UiContractCheckResult"/>.
/// </summary>
public sealed record UiContractSummary(int ScannedFiles, int ErrorCount, int WarningCount);

/// <summary>
/// Represents a single UI or security contract violation.
/// </summary>
public sealed record UiContractViolation(
    string RuleId,
    string Severity,
    string File,
    int Line,
    string Snippet,
    string Message);

/// <summary>
/// Structured result returned by <see cref="UiContractCheckerService.CheckAsync"/>.
/// </summary>
public sealed record UiContractCheckResult(
    bool Passed,
    UiContractSummary Summary,
    IReadOnlyList<UiContractViolation> Violations,
    string? ErrorMessage = null);

/// <summary>
/// Performs read-only static contract checks across <c>src/ECCSELECTION.Web</c>.
/// </summary>
public sealed partial class UiContractCheckerService
{
    private static readonly HashSet<string> AllowedScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "all",
        "views",
        "wwwroot",
        "csharp"
    };

    private readonly McpWorkspaceOptions _options;
    private readonly WorkspaceGuard _guard;

    /// <summary>
    /// Initializes a new instance of <see cref="UiContractCheckerService"/>.
    /// </summary>
    public UiContractCheckerService(McpWorkspaceOptions options, WorkspaceGuard guard)
    {
        _options = options;
        _guard = guard;
    }

    [GeneratedRegex(@"<table(?:\s|>|/)", RegexOptions.IgnoreCase)]
    private static partial Regex TableTagRegex();

    [GeneratedRegex(@"\.innerHTML\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex InnerHtmlRegex();

    [GeneratedRegex(@"<!--[\s\S]*?-->|@\*[\s\S]*?\*@", RegexOptions.Compiled)]
    private static partial Regex HtmlAndRazorCommentRegex();

    [GeneratedRegex(@"/\*[\s\S]*?\*/|//[^\r\n]*", RegexOptions.Compiled)]
    private static partial Regex CStyleCommentRegex();

    [GeneratedRegex(@"\bcatch\b(?:\s*\([^)]*\))?(?:\s*when\s*\([^)]*\))?\s*\{", RegexOptions.Compiled)]
    private static partial Regex CatchHeaderRegex();

    [GeneratedRegex(@"^\s*\[(?:HttpPost|HttpPut|HttpDelete|HttpPatch)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MutatingHttpAttributeRegex();

    [GeneratedRegex(@"\[ValidateAntiForgeryToken\b", RegexOptions.IgnoreCase)]
    private static partial Regex ValidateAntiforgeryRegex();

    [GeneratedRegex(@"\[IgnoreAntiforgeryToken\b", RegexOptions.IgnoreCase)]
    private static partial Regex IgnoreAntiforgeryRegex();

    [GeneratedRegex(@"\bclass\s+\w+", RegexOptions.Compiled)]
    private static partial Regex ClassDeclarationRegex();

    /// <summary>
    /// Scans <c>src/ECCSELECTION.Web</c> for UI and security contract violations.
    /// </summary>
    /// <param name="targetScope">Scan scope: <c>all</c>, <c>views</c>, <c>wwwroot</c>, or <c>csharp</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A structured <see cref="UiContractCheckResult"/>.</returns>
    public async Task<UiContractCheckResult> CheckAsync(
        string targetScope = "all",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetScope) || !AllowedScopes.Contains(targetScope))
        {
            return CreateErrorResult("targetScope must be one of: all, views, wwwroot, csharp.");
        }

        string root = _options.EnsureValidWorkspaceRoot();
        string webRoot = _guard.ResolveSafePath(root, Path.Combine("src", "ECCSELECTION.Web"));
        if (!Directory.Exists(webRoot))
        {
            return CreateErrorResult($"Web project directory not found at '{webRoot}'.");
        }

        var violations = new List<UiContractViolation>();
        var scannedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool scanAll = targetScope.Equals("all", StringComparison.OrdinalIgnoreCase);

        if (scanAll || targetScope.Equals("views", StringComparison.OrdinalIgnoreCase))
        {
            await ScanViewsAsync(root, webRoot, scannedFiles, violations, cancellationToken);
        }

        if (scanAll || targetScope.Equals("wwwroot", StringComparison.OrdinalIgnoreCase))
        {
            await ScanWwwrootAsync(root, webRoot, scannedFiles, violations, cancellationToken);
        }

        if (scanAll || targetScope.Equals("csharp", StringComparison.OrdinalIgnoreCase))
        {
            await ScanCSharpAsync(root, webRoot, scannedFiles, violations, cancellationToken);
        }

        int errorCount = violations.Count(v => v.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
        int warningCount = violations.Count(v => v.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase));

        return new UiContractCheckResult(
            Passed: errorCount == 0,
            Summary: new UiContractSummary(scannedFiles.Count, errorCount, warningCount),
            Violations: violations);
    }

    private static async Task ScanViewsAsync(
        string root,
        string webRoot,
        HashSet<string> scannedFiles,
        List<UiContractViolation> violations,
        CancellationToken cancellationToken)
    {
        string viewsDir = Path.Combine(webRoot, "Views");
        if (!Directory.Exists(viewsDir))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(viewsDir, "*.cshtml", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scannedFiles.Add(file);
            string content = await File.ReadAllTextAsync(file, cancellationToken);
            CheckTableTags(root, file, content, isJs: false, violations);
        }
    }

    private static async Task ScanWwwrootAsync(
        string root,
        string webRoot,
        HashSet<string> scannedFiles,
        List<UiContractViolation> violations,
        CancellationToken cancellationToken)
    {
        string jsDir = Path.Combine(webRoot, "wwwroot", "js");
        if (!Directory.Exists(jsDir))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(jsDir, "*.js", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scannedFiles.Add(file);
            string content = await File.ReadAllTextAsync(file, cancellationToken);
            CheckTableTags(root, file, content, isJs: true, violations);
            CheckInnerHtml(root, file, content, violations);
        }
    }

    private static async Task ScanCSharpAsync(
        string root,
        string webRoot,
        HashSet<string> scannedFiles,
        List<UiContractViolation> violations,
        CancellationToken cancellationToken)
    {
        foreach (string file in EnumerateWebCSharpFiles(webRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scannedFiles.Add(file);
            string content = await File.ReadAllTextAsync(file, cancellationToken);
            CheckEmptyCatchBlocks(root, file, content, violations);
        }

        string controllersDir = Path.Combine(webRoot, "Controllers");
        if (!Directory.Exists(controllersDir))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(controllersDir, "*.cs", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scannedFiles.Add(file);
            string content = await File.ReadAllTextAsync(file, cancellationToken);
            CheckControllerAntiforgery(root, file, content, violations);
        }
    }

    private static IEnumerable<string> EnumerateWebCSharpFiles(string webRoot)
    {
        foreach (string file in Directory.EnumerateFiles(webRoot, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(webRoot, file);
            string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(s =>
                s.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("obj", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            yield return file;
        }
    }

    private static void CheckTableTags(
        string root,
        string file,
        string content,
        bool isJs,
        List<UiContractViolation> violations)
    {
        string sanitized = isJs
            ? StripPreservingNewlines(content, CStyleCommentRegex())
            : StripPreservingNewlines(content, HtmlAndRazorCommentRegex());

        string[] lines = sanitized.Split('\n');
        string[] rawLines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            if (TableTagRegex().IsMatch(lines[i]))
            {
                violations.Add(new UiContractViolation(
                    RuleId: "UI001",
                    Severity: "error",
                    File: ToRelativeUnixPath(root, file),
                    Line: i + 1,
                    Snippet: TrimSnippet(rawLines[i]),
                    Message: "Prohibited <table> tag detected; use CSS Grid/Flexbox card layout."));
            }
        }
    }

    private static void CheckInnerHtml(
        string root,
        string file,
        string content,
        List<UiContractViolation> violations)
    {
        string sanitized = StripPreservingNewlines(content, CStyleCommentRegex());
        string[] lines = sanitized.Split('\n');
        string[] rawLines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            if (InnerHtmlRegex().IsMatch(lines[i]))
            {
                violations.Add(new UiContractViolation(
                    RuleId: "UI002",
                    Severity: "warning",
                    File: ToRelativeUnixPath(root, file),
                    Line: i + 1,
                    Snippet: TrimSnippet(rawLines[i]),
                    Message: "Detected innerHTML assignment; verify content is sanitized or static."));
            }
        }
    }

    private static void CheckEmptyCatchBlocks(
        string root,
        string file,
        string content,
        List<UiContractViolation> violations)
    {
        string sanitized = StripPreservingNewlines(content, CStyleCommentRegex());

        foreach (Match match in CatchHeaderRegex().Matches(sanitized))
        {
            int openBraceIndex = match.Index + match.Length - 1;
            int closeBraceIndex = FindMatchingCloseBrace(sanitized, openBraceIndex);
            if (closeBraceIndex < 0)
            {
                continue;
            }

            string innerBody = sanitized[(openBraceIndex + 1)..closeBraceIndex];
            if (string.IsNullOrWhiteSpace(innerBody))
            {
                int lineNumber = CountLinesUpTo(content, match.Index);
                string snippet = ExtractLineAt(content, lineNumber);
                violations.Add(new UiContractViolation(
                    RuleId: "CS001",
                    Severity: "error",
                    File: ToRelativeUnixPath(root, file),
                    Line: lineNumber,
                    Snippet: snippet,
                    Message: "Empty catch block detected; log the error or handle explicitly."));
            }
        }
    }

    private static void CheckControllerAntiforgery(
        string root,
        string file,
        string content,
        List<UiContractViolation> violations)
    {
        string[] lines = content.Split('\n');
        bool classHasValidate = false;
        bool classHasIgnore = false;
        bool insideClass = false;
        var pendingAttributes = new List<(int Line, string Text)>();

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith('['))
            {
                pendingAttributes.Add((i + 1, trimmed));
                continue;
            }

            if (!insideClass && ClassDeclarationRegex().IsMatch(trimmed))
            {
                insideClass = true;
                classHasValidate = pendingAttributes.Any(a => ValidateAntiforgeryRegex().IsMatch(a.Text));
                classHasIgnore = pendingAttributes.Any(a => IgnoreAntiforgeryRegex().IsMatch(a.Text));
                pendingAttributes.Clear();
                continue;
            }

            if (insideClass && pendingAttributes.Count > 0)
            {
                EvaluateMutatingActionAntiforgery(
                    root,
                    file,
                    trimmed,
                    pendingAttributes,
                    classHasValidate,
                    classHasIgnore,
                    violations);
                pendingAttributes.Clear();
            }
        }
    }

    private static void EvaluateMutatingActionAntiforgery(
        string root,
        string file,
        string declarationLine,
        List<(int Line, string Text)> pendingAttributes,
        bool classHasValidate,
        bool classHasIgnore,
        List<UiContractViolation> violations)
    {
        (int Line, string Text) mutatingAttr = pendingAttributes
            .FirstOrDefault(a => MutatingHttpAttributeRegex().IsMatch(a.Text));

        if (mutatingAttr.Line <= 0)
        {
            return;
        }

        bool methodHasValidate = pendingAttributes.Any(a => ValidateAntiforgeryRegex().IsMatch(a.Text));
        bool methodHasIgnore = pendingAttributes.Any(a => IgnoreAntiforgeryRegex().IsMatch(a.Text));

        if (classHasValidate || methodHasValidate)
        {
            return;
        }

        bool isIgnored = classHasIgnore || methodHasIgnore;
        violations.Add(new UiContractViolation(
            RuleId: "SEC001",
            Severity: isIgnored ? "warning" : "error",
            File: ToRelativeUnixPath(root, file),
            Line: mutatingAttr.Line,
            Snippet: TrimSnippet(declarationLine),
            Message: isIgnored
                ? "Mutating controller action uses [IgnoreAntiforgeryToken]; verify CSRF exemption is intentional."
                : "Mutating controller action is missing [ValidateAntiForgeryToken]."));
    }

    private static string StripPreservingNewlines(string input, Regex regex)
    {
        return regex.Replace(input, match =>
        {
            var sb = new StringBuilder(match.Length);
            foreach (char c in match.Value)
            {
                sb.Append(c == '\n' ? '\n' : ' ');
            }

            return sb.ToString();
        });
    }

    private static int FindMatchingCloseBrace(string text, int openBraceIndex)
    {
        int depth = 0;
        for (int i = openBraceIndex; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static int CountLinesUpTo(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static string ExtractLineAt(string text, int oneBasedLine)
    {
        string[] lines = text.Split('\n');
        if (oneBasedLine < 1 || oneBasedLine > lines.Length)
        {
            return string.Empty;
        }

        return TrimSnippet(lines[oneBasedLine - 1]);
    }

    private static string TrimSnippet(string line)
    {
        string trimmed = line.Trim();
        return trimmed.Length > 160 ? trimmed[..160] : trimmed;
    }

    private static string ToRelativeUnixPath(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    private static UiContractCheckResult CreateErrorResult(string errorMessage) =>
        new(
            Passed: false,
            Summary: new UiContractSummary(0, 0, 0),
            Violations: [],
            ErrorMessage: errorMessage);
}
