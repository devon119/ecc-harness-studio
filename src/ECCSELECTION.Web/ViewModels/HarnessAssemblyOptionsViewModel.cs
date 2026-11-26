using System.ComponentModel.DataAnnotations;

namespace ECCSELECTION.Web.ViewModels;

public class HarnessAssemblyOptionsViewModel
{
    [StringLength(120)]
    public string ProjectName { get; set; } = "MyProject";

    [StringLength(200)]
    public string TargetEnvironment { get; set; } = ".NET 10 / ASP.NET Core MVC";

    [StringLength(200)]
    public string SpecFilePath { get; set; } = "SPEC.md";

    [StringLength(300)]
    public string BuildCommand { get; set; } = "dotnet build -warnaserror";

    [StringLength(300)]
    public string FormatCommand { get; set; } = "dotnet format --verify-no-changes --no-restore";

    [StringLength(300)]
    public string TestCommand { get; set; } = "dotnet test --no-build --verbosity normal";

    [StringLength(300)]
    public string SecurityScanCommand { get; set; } = "npx --yes ecc-agentshield scan --path . --format text";

    [StringLength(300)]
    public string? DevServerUrl { get; set; }

    [StringLength(400)]
    public string ScopeGuardNote { get; set; } = "僅限專案原始碼與測試目錄，禁止未授權修改部署與憑證設定";

    [StringLength(400)]
    public string ProtectedPaths { get; set; } = ".env*,**/*.pem,**/*.key,**/secrets.*,appsettings.Production.json";

    [StringLength(40)]
    public string TargetPlatform { get; set; } = "All";

    public string? HarnessPlaybookMarkdown { get; set; }

    public bool RequireSpecFirst { get; set; } = true;
    public bool RequireTestsBeforeDone { get; set; } = true;
    public bool RequireEvidenceOutput { get; set; } = true;
    public bool IncludeGitPreCommitHook { get; set; } = true;
    public bool IncludeIdeAutoRules { get; set; } = true;
}

public class RequirementAnalysisResult
{
    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public string? SourceFileName { get; set; }
    public string? SuggestedProjectType { get; set; }
    public string? SuggestedProjectLifecycle { get; set; }
    public string? SuggestedArchStyle { get; set; }
    public List<string> SuggestedTechStacks { get; set; } = new();
    public List<string> SuggestedSdlcRoles { get; set; } = new();
    public List<string> ExtractedKeywords { get; set; } = new();
    public string AnalysisSource { get; set; } = "Deterministic";
    public string? SuggestedProjectName { get; set; }
    public string? SuggestedTargetEnvironment { get; set; }
    public string? SuggestedBuildCommand { get; set; }
    public string? SuggestedFormatCommand { get; set; }
    public string? SuggestedTestCommand { get; set; }
    public string? SuggestedSecurityScanCommand { get; set; }
    public string SummaryNote { get; set; } = string.Empty;
}
