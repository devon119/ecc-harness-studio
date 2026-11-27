using ECCSELECTION.Web.Models.Enums;
using ECCSELECTION.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ECCSELECTION.Tests.Services;

public class EccParserServiceTests
{
    private readonly EccParserService _parser = new(NullLogger<EccParserService>.Instance);

    [Fact]
    public void Parse_AgentWithYamlFrontmatter_ExtractsNameDescriptionUsageAndRoles()
    {
        var markdown = """
            ---
            name: architect
            description: System architecture and API design specialist for clean and MVC backends.
            ---
            # Architect Agent

            ## When to Use
            Use when designing system architecture, database schema, or backend services in dotnet.
            """;

        var result = _parser.Parse(ComponentType.Agent, "agents/architect.md", markdown);

        Assert.Equal("architect", result.Name);
        Assert.Contains("System architecture", result.DescriptionEn);
        Assert.Contains("designing system architecture", result.UsageGuide);
        Assert.Contains("SA", result.SdlcRoles);
        Assert.Contains("SD", result.SdlcRoles);
        Assert.Contains("dotnet", result.TechStacks);
        Assert.Contains("clean", result.ArchStyles);
        Assert.Contains("mvc", result.ArchStyles);
        Assert.Contains("architect", result.SummaryZh);
    }

    [Fact]
    public void Parse_SkillWithoutFrontmatter_UsesParentFolderNameAndFirstParagraph()
    {
        var markdown = """
            # TDD Workflow

            Follow strict red-green-refactor cycles with xUnit and high unit test coverage.

            ## Trigger
            Run before implementing any new backend service.
            """;

        var result = _parser.Parse(ComponentType.Skill, "skills/tdd-workflow/SKILL.md", markdown);

        Assert.Equal("tdd-workflow", result.Name);
        Assert.Contains("red-green-refactor", result.DescriptionEn);
        Assert.Contains("Run before implementing", result.UsageGuide);
        Assert.Contains("PG_TEST", result.SdlcRoles);
    }

    [Fact]
    public void Parse_Command_PrefixesSlashCommandInUsageGuide()
    {
        var markdown = """
            ---
            name: code-review
            description: Perform comprehensive code review and security audit.
            ---
            Run this command before merging pull requests.
            """;

        var result = _parser.Parse(ComponentType.Command, "commands/code-review.md", markdown);

        Assert.Equal("code-review", result.Name);
        Assert.StartsWith("以 `/code-review` 指令觸發。", result.UsageGuide);
        Assert.Contains("QA", result.SdlcRoles);
    }

    [Fact]
    public void Parse_CommonRule_InfersCommonTechStackAndAllRolesWhenNoSpecificKeywords()
    {
        var markdown = """
            # Coding Style

            Keep functions small, cohesive, and readable across every module.
            """;

        var result = _parser.Parse(ComponentType.Rule, "rules/common/coding-style.md", markdown);

        Assert.Equal("coding-style", result.Name);
        Assert.Equal("common", result.TechStacks);
        Assert.Equal("general", result.ArchStyles);
        Assert.Contains("SA", result.SdlcRoles);
        Assert.Contains("QA", result.SdlcRoles);
    }

    [Theory]
    [InlineData("Design world systems using ECS with CollectionService tags.")]
    [InlineData("Use an Entity Component architecture for deterministic game loop updates.")]
    public void Parse_EcsAndEntityComponentKeywords_InfersEcsArchStyle(string body)
    {
        var markdown = $"""
            # Game System Architecture

            {body}
            """;

        var result = _parser.Parse(ComponentType.Rule, "rules/common/game-architecture.md", markdown);

        Assert.Contains("ecs", result.ArchStyles.Split(',', StringSplitOptions.RemoveEmptyEntries));
    }

    [Theory]
    [InlineData("builtin/roblox/rules/luau-strict-coding.md")]
    [InlineData("rules/luau/strict.md")]
    public void Parse_RobloxOrLuauPaths_InfersLuauTechStack(string path)
    {
        var markdown = """
            # Strict Typing

            Enforce strict static typing and safe task scheduling.
            """;

        var result = _parser.Parse(ComponentType.Rule, path, markdown);

        Assert.Contains("luau", result.TechStacks.Split(',', StringSplitOptions.RemoveEmptyEntries));
    }
}
