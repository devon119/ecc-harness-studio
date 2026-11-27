using System.Security;
using ECCSELECTION.Mcp.Security;

namespace ECCSELECTION.Tests.Mcp;

public sealed class WorkspaceGuardTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WorkspaceGuard _guard = new();

    public WorkspaceGuardTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"ecc-guard-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveSafePath_RelativeSubpath_ReturnsNormalizedFullPathInsideBase()
    {
        string resolved = _guard.ResolveSafePath(_tempRoot, Path.Combine("docs", "specs", "SPEC_20261126_TEST.md"));

        string expected = Path.GetFullPath(Path.Combine(_tempRoot, "docs", "specs", "SPEC_20261126_TEST.md"));
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void ResolveSafePath_BaseDirectoryItself_Succeeds()
    {
        string resolved = _guard.ResolveSafePath(_tempRoot, ".");

        Assert.Equal(Path.GetFullPath(_tempRoot), resolved.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData(@"..\..\outside.txt")]
    [InlineData("docs/../../outside.txt")]
    public void ResolveSafePath_ParentTraversal_ThrowsSecurityException(string maliciousPath)
    {
        Assert.Throws<SecurityException>(() => _guard.ResolveSafePath(_tempRoot, maliciousPath));
    }

    [Theory]
    [InlineData(@"\\fileserver\share\secret.db")]
    [InlineData("//fileserver/share/secret.db")]
    public void ResolveSafePath_UncNetworkPath_ThrowsSecurityException(string uncPath)
    {
        Assert.Throws<SecurityException>(() => _guard.ResolveSafePath(_tempRoot, uncPath));
    }

    [Fact]
    public void ResolveSafePath_AbsolutePathOutsideBase_ThrowsSecurityException()
    {
        string siblingDir = Path.Combine(Path.GetTempPath(), $"ecc-other-{Guid.NewGuid():N}", "file.txt");

        Assert.Throws<SecurityException>(() => _guard.ResolveSafePath(_tempRoot, siblingDir));
    }

    [Theory]
    [InlineData("", "file.txt")]
    [InlineData("   ", "file.txt")]
    public void ResolveSafePath_EmptyBaseOrTarget_ThrowsArgumentException(string baseDir, string targetPath)
    {
        Assert.Throws<ArgumentException>(() => _guard.ResolveSafePath(baseDir, targetPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveSafePath_EmptyTarget_ThrowsArgumentException(string targetPath)
    {
        Assert.Throws<ArgumentException>(() => _guard.ResolveSafePath(_tempRoot, targetPath));
    }

    [Theory]
    [InlineData("mcp-server", "MCP_SERVER")]
    [InlineData("EccSelection_Mcp_01", "ECCSELECTION_MCP_01")]
    [InlineData("ab", "AB")]
    public void ValidateSpecTopic_ValidInput_NormalizesToUppercaseWithUnderscores(string input, string expected)
    {
        string actual = _guard.ValidateSpecTopic(input);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("has space")]
    [InlineData("../escape")]
    [InlineData("中文主題")]
    [InlineData("invalid.dot")]
    public void ValidateSpecTopic_InvalidInput_ThrowsArgumentException(string input)
    {
        Assert.Throws<ArgumentException>(() => _guard.ValidateSpecTopic(input));
    }

    [Fact]
    public void ValidateSpecDate_NullOrWhitespace_DefaultsToToday()
    {
        string today = DateTime.Now.ToString("yyyyMMdd");

        Assert.Equal(today, _guard.ValidateSpecDate(null));
        Assert.Equal(today, _guard.ValidateSpecDate("   "));
    }

    [Theory]
    [InlineData("20261126")]
    [InlineData("20240229")]
    public void ValidateSpecDate_ValidCalendarDate_ReturnsInput(string date)
    {
        Assert.Equal(date, _guard.ValidateSpecDate(date));
    }

    [Theory]
    [InlineData("2026-11-26")]
    [InlineData("20261301")]
    [InlineData("20250229")]
    [InlineData("2026112")]
    [InlineData("abcdefgh")]
    public void ValidateSpecDate_InvalidDate_ThrowsArgumentException(string date)
    {
        Assert.Throws<ArgumentException>(() => _guard.ValidateSpecDate(date));
    }

    [Fact]
    public void ValidateSpecTitle_ValidTitle_ReturnsTrimmed()
    {
        string actual = _guard.ValidateSpecTitle("  ECCSELECTION MCP 測試規格  ");

        Assert.Equal("ECCSELECTION MCP 測試規格", actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Line1\nLine2")]
    [InlineData("Tab\tTitle")]
    public void ValidateSpecTitle_InvalidTitle_ThrowsArgumentException(string title)
    {
        Assert.Throws<ArgumentException>(() => _guard.ValidateSpecTitle(title));
    }

    [Theory]
    [InlineData("fix")]
    [InlineData("line1\nline2")]
    [InlineData("line1\rline2")]
    [InlineData("bad\0msg")]
    public void ValidateCommitMessage_InvalidMessage_ThrowsArgumentException(string message)
    {
        Assert.Throws<ArgumentException>(() => _guard.ValidateCommitMessage(message));
    }

    [Fact]
    public void ValidateCommitMessage_ValidMessage_ReturnsTrimmed()
    {
        string actual = _guard.ValidateCommitMessage("  chore(gh-pages): sync bundle  ");

        Assert.Equal("chore(gh-pages): sync bundle", actual);
    }
}
