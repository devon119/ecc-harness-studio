using System.Diagnostics;
using System.Text.RegularExpressions;
using ECCSELECTION.Mcp.Options;
using ECCSELECTION.Mcp.Security;
using Microsoft.Extensions.Logging;

namespace ECCSELECTION.Mcp.Services;

/// <summary>
/// Represents the status and summary of a single quality gate stage.
/// </summary>
/// <param name="Status">Stage outcome: <c>passed</c>, <c>failed</c>, or <c>skipped</c>.</param>
/// <param name="Summary">Concise summary extracted from the stage output.</param>
public sealed record QualityGateCheckStage(string Status, string Summary);

/// <summary>
/// Represents the 4-stage quality gate breakdown.
/// </summary>
public sealed record QualityGateChecks(
    QualityGateCheckStage Build,
    QualityGateCheckStage Format,
    QualityGateCheckStage Test,
    QualityGateCheckStage Agentshield);

/// <summary>
/// Structured result returned by <see cref="QualityGateService.RunAsync"/>.
/// </summary>
public sealed record QualityGateResult(
    bool Passed,
    bool TimedOut,
    int ExitCode,
    IReadOnlyList<int> StoppedPids,
    long TotalDurationMs,
    QualityGateChecks Checks,
    string RawOutputTail,
    string? ErrorMessage = null);

/// <summary>
/// Safely stops locking web processes and executes <c>scripts/quality-gate.ps1</c>.
/// </summary>
public sealed partial class QualityGateService
{
    private const int MinTimeoutSeconds = 30;
    private const int MaxTimeoutSeconds = 900;
    private const int MaxTailLength = 4000;

    private readonly McpWorkspaceOptions _options;
    private readonly WorkspaceGuard _guard;
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<QualityGateService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="QualityGateService"/>.
    /// </summary>
    public QualityGateService(
        McpWorkspaceOptions options,
        WorkspaceGuard guard,
        IProcessRunner processRunner,
        ILogger<QualityGateService> logger)
    {
        _options = options;
        _guard = guard;
        _processRunner = processRunner;
        _logger = logger;
    }

    [GeneratedRegex(@"Passed!\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TestSummaryRegex();

    /// <summary>
    /// Executes the 4-stage quality gate script and returns structured stage results.
    /// </summary>
    /// <param name="stopRunningWebProcess">Whether to terminate running <c>ECCSELECTION.Web</c> processes first.</param>
    /// <param name="timeoutSeconds">Execution timeout in seconds (30..900).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A structured <see cref="QualityGateResult"/>.</returns>
    public async Task<QualityGateResult> RunAsync(
        bool stopRunningWebProcess = true,
        int timeoutSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        if (timeoutSeconds is < MinTimeoutSeconds or > MaxTimeoutSeconds)
        {
            return CreateErrorResult(
                $"timeoutSeconds must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds}, got {timeoutSeconds}.");
        }

        string root = _options.EnsureValidWorkspaceRoot();
        string scriptPath = _guard.ResolveSafePath(root, Path.Combine("scripts", "quality-gate.ps1"));
        if (!File.Exists(scriptPath))
        {
            return CreateErrorResult($"Quality gate script not found at '{scriptPath}'.");
        }

        IReadOnlyList<int> stoppedPids = stopRunningWebProcess
            ? StopLockingWebProcesses()
            : [];

        string shellExe = ResolvePowerShellExecutable();
        string[] args =
        [
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath
        ];

        ProcessResult result = await _processRunner.RunAsync(
            shellExe,
            args,
            root,
            TimeSpan.FromSeconds(timeoutSeconds),
            cancellationToken);

        string combined = CombineOutput(result.StandardOutput, result.StandardError);
        bool passed = !result.TimedOut && result.ExitCode == 0;
        QualityGateChecks checks = ParseStageChecks(combined, passed);
        string tail = combined.Length > MaxTailLength
            ? combined[^MaxTailLength..]
            : combined;

        return new QualityGateResult(
            Passed: passed,
            TimedOut: result.TimedOut,
            ExitCode: result.ExitCode,
            StoppedPids: stoppedPids,
            TotalDurationMs: result.DurationMs,
            Checks: checks,
            RawOutputTail: tail,
            ErrorMessage: passed ? null : (result.TimedOut ? "Quality gate timed out." : $"Quality gate exited with code {result.ExitCode}."));
    }

    private IReadOnlyList<int> StopLockingWebProcesses()
    {
        var stoppedPids = new List<int>();
        Process[] processes = Process.GetProcessesByName("ECCSELECTION.Web");

        foreach (Process process in processes)
        {
            using (process)
            {
                try
                {
                    int pid = process.Id;
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                        stoppedPids.Add(pid);
                        _logger.LogInformation("Stopped locking ECCSELECTION.Web process (PID {Pid}).", pid);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
                {
                    _logger.LogWarning(ex, "Failed to terminate ECCSELECTION.Web process.");
                }
            }
        }

        return stoppedPids;
    }

    private static string ResolvePowerShellExecutable()
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            string exeName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
            foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(candidate))
                {
                    return "pwsh";
                }
            }
        }

        return OperatingSystem.IsWindows() ? "powershell" : "pwsh";
    }

    private static QualityGateChecks ParseStageChecks(string output, bool overallPassed)
    {
        int idx1 = output.IndexOf("[1/4]", StringComparison.Ordinal);
        int idx2 = output.IndexOf("[2/4]", StringComparison.Ordinal);
        int idx3 = output.IndexOf("[3/4]", StringComparison.Ordinal);
        int idx4 = output.IndexOf("[4/4]", StringComparison.Ordinal);

        QualityGateCheckStage build = BuildStage(output, idx1, idx2, overallPassed, "0 warnings, 0 errors", "Build failed");
        QualityGateCheckStage format = BuildStage(output, idx2, idx3, overallPassed, "No formatting changes required", "Formatting verification failed");
        QualityGateCheckStage test = BuildTestStage(output, idx3, idx4, overallPassed);
        QualityGateCheckStage agentshield = BuildStage(output, idx4, -1, overallPassed, "No critical findings", "AgentShield scan failed");

        return new QualityGateChecks(build, format, test, agentshield);
    }

    private static QualityGateCheckStage BuildStage(
        string output,
        int startIdx,
        int nextIdx,
        bool overallPassed,
        string passedSummary,
        string failedSummary)
    {
        if (startIdx < 0)
        {
            return new QualityGateCheckStage("skipped", "Skipped due to earlier failure");
        }

        bool hasNextStage = nextIdx > startIdx;
        if (hasNextStage || overallPassed)
        {
            return new QualityGateCheckStage("passed", passedSummary);
        }

        string section = output[startIdx..];
        string firstErrorLine = ExtractFirstRelevantLine(section) ?? failedSummary;
        return new QualityGateCheckStage("failed", firstErrorLine);
    }

    private static QualityGateCheckStage BuildTestStage(string output, int startIdx, int nextIdx, bool overallPassed)
    {
        if (startIdx < 0)
        {
            return new QualityGateCheckStage("skipped", "Skipped due to earlier failure");
        }

        bool hasNextStage = nextIdx > startIdx;
        string section = hasNextStage ? output[startIdx..nextIdx] : output[startIdx..];

        Match match = TestSummaryRegex().Match(section);
        string summary = match.Success
            ? $"Passed: {match.Groups[2].Value}, Failed: {match.Groups[1].Value}"
            : (hasNextStage || overallPassed ? "Tests passed" : "Tests failed");

        string status = (hasNextStage || overallPassed) ? "passed" : "failed";
        return new QualityGateCheckStage(status, summary);
    }

    private static string? ExtractFirstRelevantLine(string section)
    {
        foreach (string rawLine in section.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                return line.Length > 200 ? line[..200] : line;
            }
        }

        return null;
    }

    private static string CombineOutput(string stdout, string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return stdout;
        }

        if (string.IsNullOrWhiteSpace(stdout))
        {
            return stderr;
        }

        return string.Concat(stdout, Environment.NewLine, stderr);
    }

    private static QualityGateResult CreateErrorResult(string message)
    {
        var skipped = new QualityGateCheckStage("skipped", "Not executed");
        return new QualityGateResult(
            Passed: false,
            TimedOut: false,
            ExitCode: -1,
            StoppedPids: [],
            TotalDurationMs: 0,
            Checks: new QualityGateChecks(skipped, skipped, skipped, skipped),
            RawOutputTail: string.Empty,
            ErrorMessage: message);
    }
}
