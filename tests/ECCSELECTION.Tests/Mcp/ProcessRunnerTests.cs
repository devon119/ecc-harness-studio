using ECCSELECTION.Mcp.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace ECCSELECTION.Tests.Mcp;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_ValidCommand_CapturesStdoutAndZeroExitCode()
    {
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);

        ProcessResult result = await runner.RunAsync(
            "dotnet",
            ["--version"],
            Path.GetTempPath(),
            TimeSpan.FromSeconds(15));

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
    }

    [Fact]
    public async Task RunAsync_FailingCommand_CapturesNonZeroExitCodeAndStderr()
    {
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);

        ProcessResult result = await runner.RunAsync(
            "dotnet",
            ["nonexistent-subcommand-for-test"],
            Path.GetTempPath(),
            TimeSpan.FromSeconds(15));

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardError) && string.IsNullOrWhiteSpace(result.StandardOutput));
    }

    [Fact]
    public async Task RunAsync_TimeoutExceeded_KillsProcessAndSetsTimedOutTrue()
    {
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        string shell = OperatingSystem.IsWindows() ? "powershell" : "sh";
        IReadOnlyList<string> args = OperatingSystem.IsWindows()
            ? ["-NoProfile", "-Command", "Start-Sleep -Seconds 5"]
            : ["-c", "sleep 5"];

        ProcessResult result = await runner.RunAsync(
            shell,
            args,
            Path.GetTempPath(),
            TimeSpan.FromMilliseconds(200));

        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_InvalidArguments_ThrowsArgumentException()
    {
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            runner.RunAsync(string.Empty, [], Path.GetTempPath(), TimeSpan.FromSeconds(5)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            runner.RunAsync("dotnet", [], string.Empty, TimeSpan.FromSeconds(5)));
    }
}
