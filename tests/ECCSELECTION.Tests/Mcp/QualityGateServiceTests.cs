using ECCSELECTION.Mcp.Options;
using ECCSELECTION.Mcp.Security;
using ECCSELECTION.Mcp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ECCSELECTION.Tests.Mcp;

public sealed class QualityGateServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public QualityGateServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"ecc-qg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "scripts"));
        File.WriteAllText(Path.Combine(_tempRoot, "ECCSELECTION.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(_tempRoot, "scripts", "quality-gate.ps1"), "# dummy");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_AllFourStagesPass_ReturnsPassedResultWithParsedCounts()
    {
        const string stdout = """
            [1/4] Building solution...
            Build succeeded.
            [2/4] Checking formatting...
            [3/4] Running tests...
            Passed!  - Failed:     0, Passed:    42, Skipped:     0, Total:    42
            [4/4] Running AgentShield scan...
            Scan completed with 0 critical findings.
            """;

        var runner = new FakeProcessRunner((_, _, _, _) =>
            Task.FromResult(new ProcessResult(0, stdout, string.Empty, false, 1234)));

        QualityGateService service = CreateService(runner);

        QualityGateResult result = await service.RunAsync(stopRunningWebProcess: false, timeoutSeconds: 120);

        Assert.True(result.Passed);
        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.ErrorMessage);
        Assert.Equal("passed", result.Checks.Build.Status);
        Assert.Equal("passed", result.Checks.Format.Status);
        Assert.Equal("passed", result.Checks.Test.Status);
        Assert.Equal("Passed: 42, Failed: 0", result.Checks.Test.Summary);
        Assert.Equal("passed", result.Checks.Agentshield.Status);

        RecordedProcessCall call = Assert.Single(runner.Calls);
        Assert.Equal(_tempRoot, call.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(120), call.Timeout);
        Assert.Contains("-NoProfile", call.Arguments);
        Assert.Contains("Bypass", call.Arguments);
        Assert.Contains(Path.Combine(_tempRoot, "scripts", "quality-gate.ps1"), call.Arguments);
    }

    [Fact]
    public async Task RunAsync_FormatStageFails_MarksLaterStagesSkipped()
    {
        const string stdout = """
            [1/4] Building solution...
            Build succeeded.
            [2/4] Checking formatting...
            Error: Formatting verification failed in Program.cs
            """;

        var runner = new FakeProcessRunner((_, _, _, _) =>
            Task.FromResult(new ProcessResult(1, stdout, string.Empty, false, 500)));

        QualityGateService service = CreateService(runner);

        QualityGateResult result = await service.RunAsync(stopRunningWebProcess: false);

        Assert.False(result.Passed);
        Assert.False(result.TimedOut);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("passed", result.Checks.Build.Status);
        Assert.Equal("failed", result.Checks.Format.Status);
        Assert.Contains("Formatting verification failed", result.Checks.Format.Summary);
        Assert.Equal("skipped", result.Checks.Test.Status);
        Assert.Equal("skipped", result.Checks.Agentshield.Status);
    }

    [Fact]
    public async Task RunAsync_ProcessTimesOut_ReturnsTimedOutResult()
    {
        const string stdout = """
            [1/4] Building solution...
            [2/4] Checking formatting...
            [3/4] Running tests...
            """;

        var runner = new FakeProcessRunner((_, _, _, _) =>
            Task.FromResult(new ProcessResult(-1, stdout, "Process timed out.", true, 30000)));

        QualityGateService service = CreateService(runner);

        QualityGateResult result = await service.RunAsync(stopRunningWebProcess: false, timeoutSeconds: 30);

        Assert.False(result.Passed);
        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("failed", result.Checks.Test.Status);
        Assert.Equal("skipped", result.Checks.Agentshield.Status);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(901)]
    public async Task RunAsync_InvalidTimeoutSeconds_ReturnsErrorWithoutInvokingRunner(int invalidTimeout)
    {
        var runner = new FakeProcessRunner((_, _, _, _) =>
            throw new InvalidOperationException("Should not be called."));

        QualityGateService service = CreateService(runner);

        QualityGateResult result = await service.RunAsync(stopRunningWebProcess: false, timeoutSeconds: invalidTimeout);

        Assert.False(result.Passed);
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("timeoutSeconds", result.ErrorMessage);
        Assert.Empty(runner.Calls);
    }

    private QualityGateService CreateService(IProcessRunner runner) =>
        new(
            new McpWorkspaceOptions(_tempRoot),
            new WorkspaceGuard(),
            runner,
            NullLogger<QualityGateService>.Instance);

    private sealed record RecordedProcessCall(
        string FileName,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        TimeSpan Timeout);

    private sealed class FakeProcessRunner(
        Func<string, IReadOnlyList<string>, string, TimeSpan, Task<ProcessResult>> handler) : IProcessRunner
    {
        public List<RecordedProcessCall> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new RecordedProcessCall(fileName, [.. arguments], workingDirectory, timeout));
            return handler(fileName, arguments, workingDirectory, timeout);
        }
    }
}
