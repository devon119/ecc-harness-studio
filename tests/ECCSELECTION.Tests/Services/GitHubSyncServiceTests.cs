using System.Net;
using System.Text;
using System.Text.Json;
using ECCSELECTION.Tests.Helpers;
using ECCSELECTION.Web.Data;
using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Options;
using ECCSELECTION.Web.Services;
using ECCSELECTION.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECCSELECTION.Tests.Services;

public class GitHubSyncServiceTests
{
    [Fact]
    public async Task SyncAsync_InitialThenUnchangedThenUpdatedAndRemoved_HandlesIncrementalSyncCorrectly()
    {
        using var dbScope = new TestDbContextScope();
        var handler = new FakeGitHubHttpMessageHandler();

        handler.SetTree(
            ("README.md", "blob", "sha-readme"),
            ("agents/architect.md", "blob", "sha-arch-1"),
            ("skills/tdd-workflow/SKILL.md", "blob", "sha-tdd-1"),
            ("rules/common/security.md", "blob", "sha-sec-1"),
            ("commands/verify.md", "blob", "sha-ver-1"));

        handler.SetRaw("agents/architect.md", "---\nname: architect\ndescription: Arch v1\n---\nBody");
        handler.SetRaw("skills/tdd-workflow/SKILL.md", "---\nname: tdd-workflow\ndescription: TDD v1\n---\nBody");
        handler.SetRaw("rules/common/security.md", "# Security\nCommon security rules.");
        handler.SetRaw("commands/verify.md", "---\nname: verify\ndescription: Verify v1\n---\nBody");

        var syncService = CreateSyncService(dbScope, handler);

        // 1. 初次同步：應新增 4 筆（忽略 README.md）
        var log1 = await syncService.SyncAsync();
        Assert.Equal(SyncStatus.Success, log1.Status);
        Assert.Equal(4, log1.TotalFound);
        Assert.Equal(4, log1.AddedCount);
        Assert.Equal(0, log1.UpdatedCount);
        Assert.Equal(0, log1.RemovedCount);
        Assert.Equal(4, handler.RawRequestCount);

        // 模擬人工編輯 architect.md 的中文摘要與標籤
        var arch = await dbScope.Context.EccComponents.SingleAsync(c => c.Path == "agents/architect.md");
        arch.SummaryZh = "人工鎖定架構師摘要";
        arch.SdlcRoles = "SA,SD";
        arch.IsManuallyEdited = true;
        await dbScope.Context.SaveChangesAsync();

        // 2. 二次同步（SHA 完全未變）：不得重複下載 Raw
        handler.ResetCounters();
        var log2 = await syncService.SyncAsync();
        Assert.Equal(SyncStatus.Success, log2.Status);
        Assert.Equal(0, log2.AddedCount);
        Assert.Equal(0, log2.UpdatedCount);
        Assert.Equal(0, log2.RemovedCount);
        Assert.Equal(0, handler.RawRequestCount);

        // 3. 第三次同步：architect SHA 變更、verify 上游移除
        handler.ResetCounters();
        handler.SetTree(
            ("agents/architect.md", "blob", "sha-arch-2"),
            ("skills/tdd-workflow/SKILL.md", "blob", "sha-tdd-1"),
            ("rules/common/security.md", "blob", "sha-sec-1"));
        handler.SetRaw("agents/architect.md", "---\nname: architect\ndescription: Arch v2 updated\n---\nNew body");

        var log3 = await syncService.SyncAsync();
        Assert.Equal(SyncStatus.Success, log3.Status);
        Assert.Equal(3, log3.TotalFound);
        Assert.Equal(0, log3.AddedCount);
        Assert.Equal(1, log3.UpdatedCount);
        Assert.Equal(1, log3.RemovedCount);
        Assert.Equal(1, handler.RawRequestCount); // 僅下載變更的 architect.md

        var updatedArch = await dbScope.Context.EccComponents.SingleAsync(c => c.Path == "agents/architect.md");
        Assert.Equal("sha-arch-2", updatedArch.Sha);
        Assert.Equal("Arch v2 updated", updatedArch.DescriptionEn);
        Assert.Equal("人工鎖定架構師摘要", updatedArch.SummaryZh); // 人工編輯保留

        var removedVerify = await dbScope.Context.EccComponents.SingleAsync(c => c.Path == "commands/verify.md");
        Assert.False(removedVerify.IsActive); // 軟刪除
    }

    [Fact]
    public async Task SyncAsync_SingleFileTimeout_DegradesToPartialSuccessAndRecordsError()
    {
        using var dbScope = new TestDbContextScope();
        var handler = new FakeGitHubHttpMessageHandler();
        handler.SetTree(
            ("agents/architect.md", "blob", "sha-arch"),
            ("agents/timeout-agent.md", "blob", "sha-timeout"));
        handler.SetRaw("agents/architect.md", "---\nname: architect\ndescription: Arch\n---\nBody");
        handler.SetTimeout("agents/timeout-agent.md");

        var syncService = CreateSyncService(dbScope, handler);

        var log = await syncService.SyncAsync();

        Assert.Equal(SyncStatus.PartialSuccess, log.Status);
        Assert.Equal(1, log.AddedCount);
        Assert.Contains("timeout-agent.md", log.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SyncAsync_TreeFetchFailsWithPendingDbChanges_PersistsFailedSyncLog()
    {
        using var dbScope = new TestDbContextScope();
        dbScope.Context.EccComponents.Add(new Web.Models.EccComponent
        {
            ComponentType = ComponentType.Rule,
            Name = "unsaved-rule",
            Path = "rules/unsaved.md",
            Sha = "sha-unsaved",
            IsActive = true
        });
        // Intentionally do NOT call SaveChangesAsync() before SyncAsync()

        var handler = new FakeGitHubHttpMessageHandler();
        handler.FailTreeWith(HttpStatusCode.InternalServerError);

        var syncService = CreateSyncService(dbScope, handler);

        var log = await syncService.SyncAsync();

        Assert.Equal(SyncStatus.Failed, log.Status);
        Assert.False(string.IsNullOrWhiteSpace(log.ErrorMessage));

        var savedLog = await dbScope.Context.SyncLogs.SingleAsync();
        Assert.Equal(SyncStatus.Failed, savedLog.Status);
        Assert.False(string.IsNullOrWhiteSpace(savedLog.ErrorMessage));
    }

    [Fact]
    public async Task SyncAsync_PreservesBuiltInRobloxComponentsWhenMissingFromUpstreamTree()
    {
        using var dbScope = new TestDbContextScope();
        await BuiltInEccSeeder.SeedAsync(dbScope.Context);

        dbScope.Context.EccComponents.Add(new EccComponent
        {
            ComponentType = ComponentType.Command,
            Name = "obsolete-upstream",
            Path = "commands/obsolete-upstream.md",
            Sha = "sha-obsolete",
            RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/commands/obsolete-upstream.md",
            HtmlUrl = "https://github.com/affaan-m/ECC/blob/main/commands/obsolete-upstream.md",
            IsActive = true
        });
        await dbScope.Context.SaveChangesAsync();

        var handler = new FakeGitHubHttpMessageHandler();
        handler.SetTree(("agents/architect.md", "blob", "sha-arch-1"));
        handler.SetRaw("agents/architect.md", "---\nname: architect\ndescription: Arch v1\n---\nBody");

        var syncService = CreateSyncService(dbScope, handler);
        var log = await syncService.SyncAsync();

        Assert.Equal(SyncStatus.Success, log.Status);
        Assert.Equal(1, log.AddedCount);
        Assert.Equal(1, log.RemovedCount);

        var robloxComponents = await dbScope.Context.EccComponents
            .AsNoTracking()
            .Where(c => c.Path.StartsWith("builtin/roblox/"))
            .ToListAsync();
        Assert.Equal(13, robloxComponents.Count);
        Assert.All(robloxComponents, c => Assert.True(c.IsActive));

        var removedUpstream = await dbScope.Context.EccComponents
            .AsNoTracking()
            .SingleAsync(c => c.Path == "commands/obsolete-upstream.md");
        Assert.False(removedUpstream.IsActive);
    }

    private static GitHubSyncService CreateSyncService(TestDbContextScope dbScope, FakeGitHubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var parser = new EccParserService(NullLogger<EccParserService>.Instance);
        var llm = new NullLlmEnrichmentService();
        var options = Microsoft.Extensions.Options.Options.Create(new GitHubSyncOptions
        {
            Owner = "affaan-m",
            Repo = "ECC",
            Branch = "main"
        });

        return new GitHubSyncService(
            dbScope.Context,
            httpClient,
            parser,
            llm,
            options,
            NullLogger<GitHubSyncService>.Instance);
    }

    private sealed class NullLlmEnrichmentService : ILlmEnrichmentService
    {
        public Task<string?> EnrichSummaryZhAsync(ParsedEccComponent parsed, string rawContent, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class FakeGitHubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _rawContents = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _timeoutPaths = new(StringComparer.OrdinalIgnoreCase);
        private HttpStatusCode? _treeFailureStatus;
        private string _treeJson = "{\"tree\":[]}";

        public int RawRequestCount { get; private set; }

        public void ResetCounters() => RawRequestCount = 0;

        public void FailTreeWith(HttpStatusCode statusCode) => _treeFailureStatus = statusCode;

        public void SetTimeout(string path) => _timeoutPaths.Add(path);

        public void SetTree(params (string Path, string Type, string Sha)[] items)
        {
            var payload = new
            {
                tree = items.Select(i => new { path = i.Path, type = i.Type, sha = i.Sha }).ToArray()
            };
            _treeJson = JsonSerializer.Serialize(payload);
        }

        public void SetRaw(string path, string content)
        {
            _rawContents[path] = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();

            if (uri.Contains("api.github.com/repos/", StringComparison.OrdinalIgnoreCase))
            {
                if (_treeFailureStatus.HasValue)
                {
                    return Task.FromResult(new HttpResponseMessage(_treeFailureStatus.Value));
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_treeJson, Encoding.UTF8, "application/json")
                });
            }

            if (uri.Contains("raw.githubusercontent.com/", StringComparison.OrdinalIgnoreCase))
            {
                RawRequestCount++;
                const string marker = "/main/";
                var idx = uri.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                var path = idx >= 0 ? uri[(idx + marker.Length)..] : uri;

                if (_timeoutPaths.Contains(path))
                {
                    throw new TaskCanceledException("Simulated request timeout.");
                }

                if (_rawContents.TryGetValue(path, out var content))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(content, Encoding.UTF8, "text/markdown")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        }
    }
}
