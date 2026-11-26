using System.Net;
using System.Text;
using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Options;
using ECCSELECTION.Web.Services;
using ECCSELECTION.Web.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECCSELECTION.Tests.Services;

public class GeminiCurationServiceTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }

    private sealed class StubOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    [Fact]
    public async Task CurateAsync_WithNativeGeminiApi_FiltersSuitesMovesUnselectedToCandidatesAndSetsPlaybook()
    {
        var geminiResponse = """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      {
                        "text": "{\"selectedIds\":[1,2],\"reasons\":{\"1\":\"核心審查專家\",\"2\":\"必備測試流程\"},\"playbookMarkdown\":\"# Custom Playbook\\nStep 1\"}"
                      }
                    ]
                  }
                }
              ]
            }
            """;

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(geminiResponse, Encoding.UTF8, "application/json")
        });

        var service = new GeminiCurationService(
            new HttpClient(handler),
            new StubOptionsMonitor<LlmEnrichmentOptions>(new LlmEnrichmentOptions
            {
                Enabled = true,
                ApiKey = "AIza-test",
                Model = "gemini-2.5-flash"
            }),
            NullLogger<GeminiCurationService>.Instance);

        var baseline = BuildSampleResult();
        var curated = await service.CurateAsync(baseline);

        Assert.Equal("Gemini API", curated.CurationSource);
        Assert.Contains("# Custom Playbook", curated.HarnessPlaybookMarkdown);
        var suite = Assert.Single(curated.Suites);
        Assert.Single(suite.PairedSkills);
        Assert.Equal(2, suite.PairedSkills[0].ComponentId);
        Assert.Contains(curated.AdditionalCandidates, c => c.ComponentId == 3);
    }

    [Fact]
    public void TryApplyExternalCurationJson_WithValidChromePaste_AppliesSelectionAndPlaybook()
    {
        var service = new GeminiCurationService(
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            new StubOptionsMonitor<LlmEnrichmentOptions>(new LlmEnrichmentOptions()),
            NullLogger<GeminiCurationService>.Instance);

        var baseline = BuildSampleResult();
        var pasted = """
            ```json
            {
              "selectedIds": [1, 3],
              "reasons": { "3": "資安底線必選" },
              "playbookMarkdown": "# Chrome Gemini Playbook"
            }
            ```
            """;

        var ok = service.TryApplyExternalCurationJson(baseline, pasted, out var err);

        Assert.True(ok);
        Assert.Null(err);
        Assert.Equal("Chrome Gemini Web", baseline.CurationSource);
        Assert.Equal("# Chrome Gemini Playbook", baseline.HarnessPlaybookMarkdown);
        Assert.Empty(baseline.Suites[0].PairedSkills);
        Assert.Single(baseline.Suites[0].PairedRules);
        Assert.Contains(baseline.AdditionalCandidates, c => c.ComponentId == 2);
    }

    [Fact]
    public void BuildDeterministicPlaybook_IncludesAllFiveGuardrailCheckboxes()
    {
        var sample = BuildSampleResult();
        var playbook = GeminiCurationService.BuildDeterministicPlaybook(sample);

        Assert.Contains("規格先行卡點", playbook);
        Assert.Contains("品質閘門腳本", playbook);
        Assert.Contains("證據輸出契約", playbook);
        Assert.Contains("Git Pre-Commit 卡點", playbook);
        Assert.Contains("IDE 自動載入規則 & Hooks", playbook);
    }

    private static WizardResultViewModel BuildSampleResult() => new()
    {
        Request = new WizardRequestViewModel
        {
            ProjectType = "API",
            ProjectLifecycle = "Greenfield",
            ArchStyle = "Clean",
            TechStacks = ["dotnet"],
            SdlcRoles = ["QA"]
        },
        Suites =
        [
            new HarnessSuiteViewModel
            {
                SuiteId = "suite-1",
                LeadAgent = new RecommendedComponentItem { ComponentId = 1, ComponentType = ComponentType.Agent, Name = "kotlin-reviewer", Score = 90 },
                CoveredRoles = ["QA (品保審查)"],
                PairedSkills = [new RecommendedComponentItem { ComponentId = 2, ComponentType = ComponentType.Skill, Name = "kotlin-testing", Score = 80 }],
                PairedRules = [new RecommendedComponentItem { ComponentId = 3, ComponentType = ComponentType.Rule, Name = "security", Score = 70 }]
            }
        ]
    };
}
