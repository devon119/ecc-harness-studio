namespace ECCSELECTION.Mcp.Security;

/// <summary>
/// Represents the outcome of an external process execution.
/// </summary>
/// <param name="ExitCode">The process exit code (-1 when timed out).</param>
/// <param name="StandardOutput">Captured standard output text.</param>
/// <param name="StandardError">Captured standard error text.</param>
/// <param name="TimedOut">Whether the process exceeded the configured timeout.</param>
/// <param name="DurationMs">Total elapsed execution time in milliseconds.</param>
public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    long DurationMs);

/// <summary>
/// Executes external processes safely without shell string interpolation.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs an external executable with explicit argument list items.
    /// </summary>
    /// <param name="fileName">The executable name or full path.</param>
    /// <param name="arguments">Individual arguments passed via <c>ProcessStartInfo.ArgumentList</c>.</param>
    /// <param name="workingDirectory">Working directory for the child process.</param>
    /// <param name="timeout">Maximum allowed execution time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="ProcessResult"/> containing exit code, output, and timing.</returns>
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
