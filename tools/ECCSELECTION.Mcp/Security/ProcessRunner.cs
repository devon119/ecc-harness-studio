using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ECCSELECTION.Mcp.Security;

/// <summary>
/// Default <see cref="IProcessRunner"/> implementation using <see cref="ProcessStartInfo.ArgumentList"/>
/// with <c>UseShellExecute = false</c> to prevent command injection.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="ProcessRunner"/>.
    /// </summary>
    /// <param name="logger">Logger instance (writes to stderr in MCP host).</param>
    public ProcessRunner(ILogger<ProcessRunner> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (string arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{fileName}'.");
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            _logger.LogWarning("Process '{FileName}' timed out after {TimeoutSeconds}s; terminating tree.", fileName, timeout.TotalSeconds);
            TryKillProcessTree(process, fileName);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process, fileName);
            throw;
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        stopwatch.Stop();

        int exitCode = timedOut ? -1 : process.ExitCode;
        return new ProcessResult(exitCode, stdout, stderr, timedOut, stopwatch.ElapsedMilliseconds);
    }

    private void TryKillProcessTree(Process process, string fileName)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "Unable to terminate process tree for '{FileName}'.", fileName);
        }
    }
}
