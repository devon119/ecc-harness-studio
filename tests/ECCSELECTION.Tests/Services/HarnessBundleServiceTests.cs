using System.IO.Compression;
using System.Net;
using System.Text;
using ECCSELECTION.Tests.Helpers;
using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Options;
using ECCSELECTION.Web.Services;
using ECCSELECTION.Web.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECCSELECTION.Tests.Services;

public class HarnessBundleServiceTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public async Task BuildBundleZipAsync_DownloadsCheckedFilesToSuggestedPathsAndHandlesErrors()
    {
        using var dbScope = new TestDbContextScope();
        dbScope.Context.EccComponents.AddRange(
            new EccComponent
            {
                Id = 1,
                ComponentType = ComponentType.Agent,
                Name = "architect",
                Path = "agents/architect.md",
                Sha = "sha1",
                RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/agents/architect.md",
                HtmlUrl = "https://github.com/affaan-m/ECC/blob/main/agents/architect.md",
                IsActive = true
            },
            new EccComponent
            {
                Id = 2,
                ComponentType = ComponentType.Skill,
                Name = "tdd-workflow",
                Path = "skills/tdd-workflow/SKILL.md",
                Sha = "sha2",
                RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/skills/tdd-workflow/SKILL.md",
                HtmlUrl = "https://github.com/affaan-m/ECC/blob/main/skills/tdd-workflow/SKILL.md",
                IsActive = true
            },
            new EccComponent
            {
                Id = 3,
                ComponentType = ComponentType.Rule,
                Name = "missing-rule",
                Path = "rules/common/missing.md",
                Sha = "sha3",
                RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/rules/common/missing.md",
                HtmlUrl = "https://github.com/affaan-m/ECC/blob/main/rules/common/missing.md",
                IsActive = true
            });
        await dbScope.Context.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.EndsWith("architect.md", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("# Architect Agent Content", Encoding.UTF8)
                };
            }

            if (url.EndsWith("SKILL.md", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("# TDD Workflow Skill Content", Encoding.UTF8)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = new HarnessBundleService(
            dbScope.Context,
            new HttpClient(handler),
            Options.Create(new GitHubSyncOptions()),
            NullLogger<HarnessBundleService>.Instance);

        var zipBytes = await service.BuildBundleZipAsync([1, 2, 2, 3]);

        using var ms = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var agentEntry = archive.GetEntry(".claude/agents/architect.md");
        Assert.NotNull(agentEntry);
        using (var reader = new StreamReader(agentEntry!.Open(), Encoding.UTF8))
        {
            Assert.Equal("# Architect Agent Content", await reader.ReadToEndAsync());
        }

        var skillEntry = archive.GetEntry(".claude/skills/tdd-workflow/SKILL.md");
        Assert.NotNull(skillEntry);

        var manifestEntry = archive.GetEntry("README_HARNESS.md");
        Assert.NotNull(manifestEntry);

        var errorEntry = archive.GetEntry("DOWNLOAD_ERRORS.txt");
        Assert.NotNull(errorEntry);
    }

    [Fact]
    public async Task BuildBundleZipAsync_WithGuardrailOptions_GeneratesGuardrailsScriptsAndHooks()
    {
        using var dbScope = new TestDbContextScope();
        dbScope.Context.EccComponents.Add(new EccComponent
        {
            Id = 10,
            ComponentType = ComponentType.Agent,
            Name = "security-reviewer",
            Path = "agents/security-reviewer.md",
            Sha = "sha10",
            RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/agents/security-reviewer.md",
            HtmlUrl = "https://github.com/affaan-m/ECC/blob/main/agents/security-reviewer.md",
            IsActive = true
        });
        await dbScope.Context.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("# Security Reviewer", Encoding.UTF8)
        });

        var service = new HarnessBundleService(
            dbScope.Context,
            new HttpClient(handler),
            Options.Create(new GitHubSyncOptions()),
            NullLogger<HarnessBundleService>.Instance);

        var options = new HarnessAssemblyOptionsViewModel
        {
            ProjectName = "BillingGuard",
            TargetEnvironment = ".NET 10 / SQLite",
            SpecFilePath = "SPEC_BILLING.md",
            BuildCommand = "dotnet build Billing.sln -warnaserror",
            FormatCommand = "dotnet format Billing.sln --verify-no-changes",
            TestCommand = "dotnet test Billing.sln --no-build",
            SecurityScanCommand = "npx --yes ecc-agentshield scan --path . --format text",
            DevServerUrl = "http://localhost:5109",
            TargetPlatform = "All",
            HarnessPlaybookMarkdown = "# BillingGuard Playbook",
            RequireSpecFirst = true,
            RequireTestsBeforeDone = true,
            RequireEvidenceOutput = true,
            IncludeGitPreCommitHook = true,
            IncludeIdeAutoRules = true
        };

        var zipBytes = await service.BuildBundleZipAsync([10], options);

        using var ms = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry(".claude/agents/security-reviewer.md"));
        Assert.Null(archive.GetEntry(".ecc/agents/security-reviewer.md"));
        Assert.NotNull(archive.GetEntry(".agents/rules/workflow-guardrails.md"));
        Assert.NotNull(archive.GetEntry(".claude/settings.json"));
        Assert.NotNull(archive.GetEntry(".githooks/pre-commit"));
        Assert.NotNull(archive.GetEntry("scripts/install-hooks.ps1"));
        Assert.NotNull(archive.GetEntry("scripts/quality-gate.sh"));
        Assert.NotNull(archive.GetEntry("HARNESS_PLAYBOOK.md"));

        var agentsEntry = archive.GetEntry("AGENTS.md");
        Assert.NotNull(agentsEntry);
        using (var reader = new StreamReader(agentsEntry!.Open(), Encoding.UTF8))
        {
            var agentsMd = await reader.ReadToEndAsync();
            Assert.Contains("BillingGuard", agentsMd);
            Assert.Contains("SPEC_BILLING.md", agentsMd);
            Assert.Contains("HARD GATES", agentsMd);
        }

        var gatePs1Entry = archive.GetEntry("scripts/quality-gate.ps1");
        Assert.NotNull(gatePs1Entry);
        using (var reader = new StreamReader(gatePs1Entry!.Open(), Encoding.UTF8))
        {
            var gatePs1 = await reader.ReadToEndAsync();
            Assert.Contains("dotnet build Billing.sln -warnaserror", gatePs1);
            Assert.Contains("dotnet test Billing.sln --no-build", gatePs1);
        }

        var claudeSettingsEntry = archive.GetEntry(".claude/settings.json");
        Assert.NotNull(claudeSettingsEntry);
        using (var reader = new StreamReader(claudeSettingsEntry!.Open(), Encoding.UTF8))
        {
            var settingsJson = await reader.ReadToEndAsync();
            Assert.Contains("Edit(.env*)", settingsJson);
            Assert.Contains("Bash(rm -rf *)", settingsJson);
        }
    }

    [Fact]
    public async Task BuildBundleZipAsync_IncludesSkillAuxiliaryFilesFromGitHubTree()
    {
        using var dbScope = new TestDbContextScope();
        dbScope.Context.EccComponents.Add(new EccComponent
        {
            Id = 20,
            ComponentType = ComponentType.Skill,
            Name = "tdd-workflow",
            Path = "skills/tdd-workflow/SKILL.md",
            Sha = "sha20",
            RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/skills/tdd-workflow/SKILL.md",
            HtmlUrl = "https://github.com/affaan-m/ECC/blob/main/skills/tdd-workflow/SKILL.md",
            IsActive = true
        });
        await dbScope.Context.SaveChangesAsync();

        var treeJson = """
            {
              "tree": [
                { "path": "skills/tdd-workflow/SKILL.md", "type": "blob" },
                { "path": "skills/tdd-workflow/references/checklist.md", "type": "blob" },
                { "path": "skills/tdd-workflow/scripts/verify.sh", "type": "blob" },
                { "path": "skills/other-skill/references/ignore.md", "type": "blob" }
              ]
            }
            """;

        var handler = new StubHttpMessageHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("/git/trees/", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(treeJson, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"content:{url}", Encoding.UTF8)
            };
        });

        var service = new HarnessBundleService(
            dbScope.Context,
            new HttpClient(handler),
            Options.Create(new GitHubSyncOptions()),
            NullLogger<HarnessBundleService>.Instance);

        var zipBytes = await service.BuildBundleZipAsync([20]);

        using var ms = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry(".claude/skills/tdd-workflow/SKILL.md"));
        Assert.NotNull(archive.GetEntry(".claude/skills/tdd-workflow/references/checklist.md"));
        Assert.NotNull(archive.GetEntry(".claude/skills/tdd-workflow/scripts/verify.sh"));
        Assert.Null(archive.GetEntry(".claude/skills/other-skill/references/ignore.md"));
    }

    [Theory]
    [InlineData("All", true, true)]
    [InlineData("Antigravity", true, false)]
    [InlineData("ClaudeCode", false, true)]
    public async Task BuildBundleZipAsync_WithIncludeIdeAutoRules_IncludesGovernanceMatrixGateAndBeginnerGuide(
        string targetPlatform,
        bool expectAgentsMd,
        bool expectClaudeMd)
    {
        using var dbScope = new TestDbContextScope();
        var service = await CreateServiceWithSingleComponentAsync(dbScope, id: 30);

        var options = new HarnessAssemblyOptionsViewModel
        {
            ProjectName = "OrderFlow",
            TargetEnvironment = ".NET 10 / SQLite",
            SpecFilePath = "SPEC_ORDERFLOW.md",
            TargetPlatform = targetPlatform,
            RequireSpecFirst = true,
            RequireTestsBeforeDone = true,
            RequireEvidenceOutput = true,
            IncludeIdeAutoRules = true
        };

        var zipBytes = await service.BuildBundleZipAsync([30], options);

        using var ms = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        if (expectAgentsMd)
        {
            var agentsMd = await ReadEntryTextAsync(archive, "AGENTS.md");
            AssertGuardrailDocumentContainsGovernance(agentsMd, "SPEC_ORDERFLOW.md");

            var autoRuleMd = await ReadEntryTextAsync(archive, ".agents/rules/workflow-guardrails.md");
            Assert.Contains("專責協調員＋寫入邊界隔離", autoRuleMd);
            Assert.Contains("規格先行＋人類確認閘門（強制暫停）", autoRuleMd);
            Assert.Contains("SPEC_ORDERFLOW.md", autoRuleMd);
            Assert.Contains("7 步完整流程（嚴禁跳步）", autoRuleMd);
            Assert.Contains("禁止自行判斷略過任何步驟", autoRuleMd);
        }
        else
        {
            Assert.Null(archive.GetEntry("AGENTS.md"));
            Assert.Null(archive.GetEntry(".agents/rules/workflow-guardrails.md"));
        }

        if (expectClaudeMd)
        {
            var claudeMd = await ReadEntryTextAsync(archive, "CLAUDE.md");
            AssertGuardrailDocumentContainsGovernance(claudeMd, "SPEC_ORDERFLOW.md");
        }
        else
        {
            Assert.Null(archive.GetEntry("CLAUDE.md"));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BuildBundleZipAsync_AlwaysIncludesBeginnerGovernanceGuideInReadmeHarness(bool includeIdeAutoRules)
    {
        using var dbScope = new TestDbContextScope();
        var service = await CreateServiceWithSingleComponentAsync(dbScope, id: 40);

        var options = new HarnessAssemblyOptionsViewModel
        {
            ProjectName = "StarterKit",
            TargetEnvironment = ".NET 10 / SQLite",
            SpecFilePath = "SPEC_STARTER.md",
            TargetPlatform = "All",
            RequireSpecFirst = true,
            IncludeIdeAutoRules = includeIdeAutoRules
        };

        var zipBytes = await service.BuildBundleZipAsync([40], options);

        using var ms = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var readme = await ReadEntryTextAsync(archive, "README_HARNESS.md");
        Assert.Contains("## 給新手的 AI 協作與治理原則（快速上手指南）", readme);
        Assert.Contains("專責協調員＋寫入邊界隔離", readme);
        Assert.Contains("規格先行＋人類確認閘門", readme);
        Assert.Contains("SPEC_STARTER.md", readme);
        Assert.Contains("7 步完整流程（嚴禁跳步）", readme);

        if (!includeIdeAutoRules)
        {
            Assert.Null(archive.GetEntry("AGENTS.md"));
            Assert.Null(archive.GetEntry("CLAUDE.md"));
            Assert.Null(archive.GetEntry(".agents/rules/workflow-guardrails.md"));
        }
    }

    private static async Task<HarnessBundleService> CreateServiceWithSingleComponentAsync(TestDbContextScope dbScope, int id)
    {
        dbScope.Context.EccComponents.Add(new EccComponent
        {
            Id = id,
            ComponentType = ComponentType.Agent,
            Name = "architect",
            Path = "agents/architect.md",
            Sha = $"sha{id}",
            RawUrl = "https://raw.githubusercontent.com/affaan-m/ECC/main/agents/architect.md",
            HtmlUrl = "https://github.com/affaan-m/ECC/blob/main/agents/architect.md",
            IsActive = true
        });
        await dbScope.Context.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("# Architect", Encoding.UTF8)
        });

        return new HarnessBundleService(
            dbScope.Context,
            new HttpClient(handler),
            Options.Create(new GitHubSyncOptions()),
            NullLogger<HarnessBundleService>.Instance);
    }

    private static async Task<string> ReadEntryTextAsync(ZipArchive archive, string entryPath)
    {
        var entry = archive.GetEntry(entryPath);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static void AssertGuardrailDocumentContainsGovernance(string content, string specFilePath)
    {
        Assert.Contains("## 多代理協作與開發治理原則（Coordinator ＋ 寫入邊界隔離）", content);
        Assert.Contains("專責協調員（Coordinator）", content);
        Assert.Contains("SA / 規格 Agent", content);
        Assert.Contains("後端實作 Agent", content);
        Assert.Contains("前端 / MVC Agent", content);
        Assert.Contains("測試 Agent", content);
        Assert.Contains("審查與驗證 Agent", content);
        Assert.Contains("文件同步 Agent", content);
        Assert.Contains("### Gate 1：規格先行卡點（Spec-First ＋ 人類確認閘門）", content);
        Assert.Contains("必須暫停並向人類開發者呈報規格摘要，取得人類明確確認後才可進入實作階段", content);
        Assert.Contains(specFilePath, content);
        Assert.Contains("1. SA 規格先行", content);
        Assert.Contains("2. 人類確認閘門（強制暫停）", content);
        Assert.Contains("3. 分層實作", content);
        Assert.Contains("4. TDD 與測試覆蓋", content);
        Assert.Contains("5. 程式碼與安全審查", content);
        Assert.Contains("6. 強制品質閘門", content);
        Assert.Contains("7. 文件同步", content);
        Assert.Contains("### 給新手的協作操作指引", content);
    }
}
