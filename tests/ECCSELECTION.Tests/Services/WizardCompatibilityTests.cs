using System.Text.Json;
using ECCSELECTION.Web.Services;
using ECCSELECTION.Web.ViewModels;

namespace ECCSELECTION.Tests.Services;

public class WizardCompatibilityTests
{
    [Fact]
    public void Validate_ValidCombinationsForEachProjectType_ReturnsNoErrors()
    {
        var validRequests = new[]
        {
            new WizardRequestViewModel
            {
                ProjectType = "Web",
                ArchStyle = "MVC",
                TechStacks = ["dotnet", "sqlserver"],
                SdlcRoles = ["SA", "SD", "PG_FE", "PG_BE", "PG_DB", "PG_TEST", "QA"]
            },
            new WizardRequestViewModel
            {
                ProjectType = "Mobile",
                ArchStyle = "MVVM",
                TechStacks = ["kotlin", "sqlite"],
                SdlcRoles = ["SA", "SD", "PG_FE", "PG_DB", "PG_TEST", "QA"]
            },
            new WizardRequestViewModel
            {
                ProjectType = "API",
                ArchStyle = "Clean",
                TechStacks = ["go", "postgres"],
                SdlcRoles = ["SA", "SD", "PG_BE", "PG_DB", "PG_TEST", "QA"]
            },
            new WizardRequestViewModel
            {
                ProjectType = "Fullstack",
                ProjectLifecycle = "Brownfield",
                ArchStyle = "Clean",
                TechStacks = ["dotnet", "react", "postgres"],
                SdlcRoles = ["SA", "SD", "PG_FE", "PG_BE", "PG_DB", "PG_TEST", "QA"]
            },
            new WizardRequestViewModel
            {
                ProjectType = "Game",
                ProjectLifecycle = "Greenfield",
                ArchStyle = "ECS",
                TechStacks = ["luau", "sqlite"],
                SdlcRoles = ["SA", "SD", "PG_FE", "PG_BE", "PG_DB", "PG_TEST", "QA"]
            }
        };

        foreach (var request in validRequests)
        {
            var errors = WizardCompatibility.Validate(request);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Validate_WebWithKotlin_ReturnsTechStacksError()
    {
        var request = new WizardRequestViewModel
        {
            ProjectType = "Web",
            ArchStyle = "MVC",
            TechStacks = ["dotnet", "kotlin"],
            SdlcRoles = ["SA", "PG_BE"]
        };

        var errors = WizardCompatibility.Validate(request);

        Assert.Single(errors);
        Assert.Equal(nameof(WizardRequestViewModel.TechStacks), errors[0].MemberName);
        Assert.Contains("kotlin", errors[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_MobileWithMvcSqlServerAndBackendRole_ReturnsAllThreeErrors()
    {
        var request = new WizardRequestViewModel
        {
            ProjectType = "Mobile",
            ArchStyle = "MVC",
            TechStacks = ["kotlin", "sqlserver"],
            SdlcRoles = ["SA", "PG_BE"]
        };

        var errors = WizardCompatibility.Validate(request);

        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.MemberName == nameof(WizardRequestViewModel.ArchStyle));
        Assert.Contains(errors, e => e.MemberName == nameof(WizardRequestViewModel.TechStacks));
        Assert.Contains(errors, e => e.MemberName == nameof(WizardRequestViewModel.SdlcRoles));
    }

    [Fact]
    public void Validate_ApiWithReactAndFrontendRole_ReturnsTechStacksAndRolesErrors()
    {
        var request = new WizardRequestViewModel
        {
            ProjectType = "API",
            ArchStyle = "Clean",
            TechStacks = ["dotnet", "react"],
            SdlcRoles = ["PG_FE", "PG_BE"]
        };

        var errors = WizardCompatibility.Validate(request);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.MemberName == nameof(WizardRequestViewModel.TechStacks));
        Assert.Contains(errors, e => e.MemberName == nameof(WizardRequestViewModel.SdlcRoles));
    }

    [Fact]
    public void Validate_EmptyRolesOrUnknownProjectType_ReturnsErrors()
    {
        var emptyRolesRequest = new WizardRequestViewModel
        {
            ProjectType = "Web",
            ArchStyle = "MVC",
            TechStacks = ["dotnet"],
            SdlcRoles = []
        };

        var unknownTypeRequest = new WizardRequestViewModel
        {
            ProjectType = "Desktop",
            ArchStyle = "MVVM",
            TechStacks = ["dotnet"],
            SdlcRoles = ["SA"]
        };

        var roleErrors = WizardCompatibility.Validate(emptyRolesRequest);
        var typeErrors = WizardCompatibility.Validate(unknownTypeRequest);

        Assert.Contains(roleErrors, e => e.MemberName == nameof(WizardRequestViewModel.SdlcRoles));
        Assert.Contains(typeErrors, e => e.MemberName == nameof(WizardRequestViewModel.ProjectType));
    }

    [Fact]
    public void ToClientRulesJson_ContainsAllProjectTypes()
    {
        var json = WizardCompatibility.ToClientRulesJson();
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("Web", out _));
        Assert.True(doc.RootElement.TryGetProperty("Mobile", out _));
        Assert.True(doc.RootElement.TryGetProperty("API", out _));
        Assert.True(doc.RootElement.TryGetProperty("Fullstack", out _));
        Assert.True(doc.RootElement.TryGetProperty("Game", out _));
    }

    [Fact]
    public void Validate_InvalidProjectLifecycle_ReturnsLifecycleError()
    {
        var request = new WizardRequestViewModel
        {
            ProjectType = "Game",
            ProjectLifecycle = "UnknownStage",
            ArchStyle = "ECS",
            TechStacks = ["luau"],
            SdlcRoles = ["SA", "PG_BE"]
        };

        var errors = WizardCompatibility.Validate(request);

        Assert.Contains(errors, e => e.MemberName == nameof(WizardRequestViewModel.ProjectLifecycle));
    }
}
