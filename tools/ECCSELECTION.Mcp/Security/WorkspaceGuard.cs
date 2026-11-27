using System.Globalization;
using System.Security;
using System.Text.RegularExpressions;

namespace ECCSELECTION.Mcp.Security;

/// <summary>
/// Provides path traversal protection and strict input whitelist validation for MCP operations.
/// </summary>
public sealed partial class WorkspaceGuard
{
    private const string DateFormat = "yyyyMMdd";

    [GeneratedRegex("^[A-Za-z0-9_-]{2,64}$", RegexOptions.Compiled)]
    private static partial Regex TopicRegex();

    [GeneratedRegex("^\\d{8}$", RegexOptions.Compiled)]
    private static partial Regex DateRegex();

    /// <summary>
    /// Resolves <paramref name="targetPath"/> relative to <paramref name="baseDir"/> and ensures
    /// it does not escape <paramref name="baseDir"/> via traversal, UNC paths, or cross-drive references.
    /// </summary>
    /// <param name="baseDir">The trusted base directory.</param>
    /// <param name="targetPath">The relative or absolute path to validate.</param>
    /// <returns>The validated absolute path.</returns>
    /// <exception cref="ArgumentException">Thrown when inputs are null, empty, or malformed.</exception>
    /// <exception cref="SecurityException">Thrown when the resolved path escapes <paramref name="baseDir"/> or is a UNC path.</exception>
    public string ResolveSafePath(string baseDir, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            throw new ArgumentException("Base directory must not be empty.", nameof(baseDir));
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("Target path must not be empty.", nameof(targetPath));
        }

        if (targetPath.StartsWith(@"\\", StringComparison.Ordinal) ||
            targetPath.StartsWith("//", StringComparison.Ordinal))
        {
            throw new SecurityException($"UNC network paths are prohibited: '{targetPath}'.");
        }

        string normalizedBase = Path.GetFullPath(baseDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string baseWithSeparator = normalizedBase + Path.DirectorySeparatorChar;

        string candidatePath = Path.IsPathRooted(targetPath)
            ? Path.GetFullPath(targetPath)
            : Path.GetFullPath(Path.Combine(baseWithSeparator, targetPath));

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        bool isWithinBase = candidatePath.StartsWith(baseWithSeparator, comparison) ||
            string.Equals(
                candidatePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                normalizedBase,
                comparison);

        if (!isWithinBase)
        {
            throw new SecurityException(
                $"Path '{targetPath}' resolves outside the allowed base directory '{normalizedBase}'.");
        }

        return candidatePath;
    }

    /// <summary>
    /// Validates and normalizes a spec topic identifier to uppercase with underscores.
    /// </summary>
    /// <param name="topic">The raw topic string.</param>
    /// <returns>The normalized uppercase topic string.</returns>
    /// <exception cref="ArgumentException">Thrown when the topic does not match <c>^[A-Za-z0-9_-]{2,64}$</c>.</exception>
    public string ValidateSpecTopic(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic) || !TopicRegex().IsMatch(topic))
        {
            throw new ArgumentException(
                "Topic must be 2..64 characters matching [A-Za-z0-9_-].",
                nameof(topic));
        }

        return topic.ToUpperInvariant().Replace('-', '_');
    }

    /// <summary>
    /// Validates a spec date in <c>yyyyMMdd</c> format, defaulting to today's date when omitted.
    /// </summary>
    /// <param name="date">The optional date string.</param>
    /// <returns>A validated <c>yyyyMMdd</c> date string.</returns>
    /// <exception cref="ArgumentException">Thrown when the date is not a valid <c>yyyyMMdd</c> date.</exception>
    public string ValidateSpecDate(string? date)
    {
        if (string.IsNullOrWhiteSpace(date))
        {
            return DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        if (!DateRegex().IsMatch(date) ||
            !DateTime.TryParseExact(date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ArgumentException(
                "Date must be a valid calendar date in yyyyMMdd format.",
                nameof(date));
        }

        return date;
    }

    /// <summary>
    /// Validates a spec title for length (2..120) and absence of control characters.
    /// </summary>
    /// <param name="title">The spec title.</param>
    /// <returns>The trimmed, validated title.</returns>
    /// <exception cref="ArgumentException">Thrown when the title is invalid or contains control characters.</exception>
    public string ValidateSpecTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title must not be empty.", nameof(title));
        }

        string trimmed = title.Trim();
        if (trimmed.Length is < 2 or > 120)
        {
            throw new ArgumentException("Title length must be between 2 and 120 characters.", nameof(title));
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("Title must not contain control characters.", nameof(title));
        }

        return trimmed;
    }

    /// <summary>
    /// Validates a git commit message for length (5..200) and absence of newlines or control characters.
    /// </summary>
    /// <param name="commitMessage">The commit message.</param>
    /// <returns>The trimmed, validated commit message.</returns>
    /// <exception cref="ArgumentException">Thrown when the commit message is invalid.</exception>
    public string ValidateCommitMessage(string commitMessage)
    {
        if (string.IsNullOrWhiteSpace(commitMessage))
        {
            throw new ArgumentException("Commit message must not be empty.", nameof(commitMessage));
        }

        string trimmed = commitMessage.Trim();
        if (trimmed.Length is < 5 or > 200)
        {
            throw new ArgumentException("Commit message length must be between 5 and 200 characters.", nameof(commitMessage));
        }

        if (trimmed.Any(c => c is '\r' or '\n' || char.IsControl(c)))
        {
            throw new ArgumentException(
                "Commit message must not contain newlines or control characters.",
                nameof(commitMessage));
        }

        return trimmed;
    }
}
