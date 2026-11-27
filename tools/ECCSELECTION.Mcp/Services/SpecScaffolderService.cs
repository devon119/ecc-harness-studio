using System.Text;
using ECCSELECTION.Mcp.Options;
using ECCSELECTION.Mcp.Security;
using Microsoft.Extensions.Logging;

namespace ECCSELECTION.Mcp.Services;

/// <summary>
/// Structured result returned by <see cref="SpecScaffolderService.CreateSpecAsync"/>.
/// </summary>
public sealed record CreateSpecResult(
    bool Success,
    string? RelativePath,
    string? FullPath,
    long BytesWritten,
    string? ErrorMessage = null);

/// <summary>
/// Creates standalone specification files under <c>docs/specs/SPEC_&lt;YYYYMMDD&gt;_&lt;TOPIC&gt;.md</c>.
/// </summary>
public sealed class SpecScaffolderService
{
    private const string ProtectedRootSpecFileName = "SPEC_ECCSELECTION.md";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly McpWorkspaceOptions _options;
    private readonly WorkspaceGuard _guard;
    private readonly ILogger<SpecScaffolderService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="SpecScaffolderService"/>.
    /// </summary>
    public SpecScaffolderService(
        McpWorkspaceOptions options,
        WorkspaceGuard guard,
        ILogger<SpecScaffolderService> logger)
    {
        _options = options;
        _guard = guard;
        _logger = logger;
    }

    /// <summary>
    /// Scaffolds a new specification markdown file inside <c>docs/specs/</c>.
    /// </summary>
    /// <param name="topic">Short alphanumeric/underscore/hyphen topic identifier (2..64 chars).</param>
    /// <param name="title">Human-readable Chinese or English spec title (2..120 chars).</param>
    /// <param name="author">Author label (defaults to <c>SA Agent</c>).</param>
    /// <param name="date">Optional <c>yyyyMMdd</c> date string (defaults to today).</param>
    /// <param name="overwrite">Whether to overwrite an existing spec file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A structured <see cref="CreateSpecResult"/>.</returns>
    public async Task<CreateSpecResult> CreateSpecAsync(
        string topic,
        string title,
        string author = "SA Agent",
        string? date = null,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        string normalizedTopic = _guard.ValidateSpecTopic(topic);
        string validatedTitle = _guard.ValidateSpecTitle(title);
        string validatedDate = _guard.ValidateSpecDate(date);
        string validatedAuthor = ValidateAuthor(author);

        string root = _options.EnsureValidWorkspaceRoot();
        string specsDir = _guard.ResolveSafePath(root, Path.Combine("docs", "specs"));
        string fileName = $"SPEC_{validatedDate}_{normalizedTopic}.md";

        if (string.Equals(fileName, ProtectedRootSpecFileName, StringComparison.OrdinalIgnoreCase))
        {
            return CreateFailure($"Refusing to write to protected root spec '{ProtectedRootSpecFileName}'.");
        }

        string targetPath = _guard.ResolveSafePath(specsDir, fileName);
        string relativePath = $"docs/specs/{fileName}";

        Directory.CreateDirectory(specsDir);

        if (File.Exists(targetPath) && !overwrite)
        {
            return CreateFailure($"Spec file '{relativePath}' already exists and overwrite is false.");
        }

        string content = BuildSpecSkeleton(
            specId: $"SPEC_{validatedDate}_{normalizedTopic}",
            title: validatedTitle,
            author: validatedAuthor,
            formattedDate: $"{validatedDate[..4]}-{validatedDate[4..6]}-{validatedDate[6..8]}");

        byte[] bytes = Utf8NoBom.GetBytes(content);
        await File.WriteAllBytesAsync(targetPath, bytes, cancellationToken);
        _logger.LogInformation("Created spec file '{RelativePath}' ({Bytes} bytes).", relativePath, bytes.Length);

        return new CreateSpecResult(
            Success: true,
            RelativePath: relativePath,
            FullPath: targetPath,
            BytesWritten: bytes.Length);
    }

    private static string ValidateAuthor(string? author)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            return "SA Agent";
        }

        string trimmed = author.Trim();
        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("Author must not contain control characters.", nameof(author));
        }

        return trimmed;
    }

    private static string BuildSpecSkeleton(string specId, string title, string author, string formattedDate)
    {
        return $$"""
            # {{specId}} — {{title}}

            - **文件編號**：`{{specId}}`
            - **建立日期**：{{formattedDate}}
            - **撰寫角色**：{{author}}
            - **目標專案**：`ECCSELECTION`（`ECCSELECTION.slnx`）
            - **狀態**：待人類確認（Human-in-the-Loop Gate）

            ---

            ## 1. 背景與目標

            （請補充需求背景與核心目標。）

            ---

            ## 2. 影響範圍與寫入邊界

            - **後端實作邊界**：`src/ECCSELECTION.Web/Services/`、`Models/`、`Data/`
            - **前端實作邊界**：`src/ECCSELECTION.Web/Controllers/`、`Views/`、`wwwroot/`
            - **測試邊界**：`tests/ECCSELECTION.Tests/`

            ---

            ## 3. 功能需求與資料契約

            （請定義輸入輸出、資料模型與核心商業規則。）

            ---

            ## 4. 安全性設計

            - 驗證所有外部輸入並防範路徑遍歷與注入風險。
            - Controller POST/PUT/DELETE/PATCH 須具備 `[ValidateAntiForgeryToken]`。
            - 嚴禁空 `catch` 區塊或硬編碼密鑰。

            ---

            ## 5. 單元測試計畫（`tests/ECCSELECTION.Tests/`）

            - （請列出對應 xUnit 測試案例。）

            ---

            ## 6. 人類確認閘門（Human-in-the-Loop Checklist）

            - [ ] 確認功能範圍、資料契約與寫入邊界。
            - [ ] 核准進入實作與測試階段。
            """;
    }

    private static CreateSpecResult CreateFailure(string errorMessage) =>
        new(
            Success: false,
            RelativePath: null,
            FullPath: null,
            BytesWritten: 0,
            ErrorMessage: errorMessage);
}
