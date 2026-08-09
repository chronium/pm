using PM.Application;
using PM.Project;

namespace PM.Tests;

public sealed class OverviewConfigurationValidationTests
{
    [Fact]
    public async Task AbsentAndImplicitSiteConfigurationsAreValid()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var validator = new OverviewConfigurationValidationService(root);
        var config = TestData.Config();

        Assert.Empty(validator.Validate(config));

        config.Site = new OverviewSiteDefinition { Enabled = true };
        Assert.Empty(validator.Validate(config));

        config.Site = new OverviewSiteDefinition
        {
            Enabled = false,
            Home = new OverviewHomeDefinition(),
        };
        Assert.Empty(validator.Validate(config));
    }

    [Fact]
    public async Task ValidSingleAndSplitCompositionsPass()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var validator = new OverviewConfigurationValidationService(root);
        var config = TestData.Config(
            milestones: new Dictionary<string, string> { ["beta"] = "Beta" });
        config.Site = new OverviewSiteDefinition
        {
            Enabled = true,
            Title = "PM",
            Description = "Local project management.",
            Home = new OverviewHomeDefinition
            {
                Sections =
                [
                    Section(OverviewSectionKinds.Hero),
                    Section(OverviewSectionKinds.Milestone, milestone: "beta"),
                    Section(OverviewSectionKinds.Tasks, filter: "state:todo track:PM in:all", limit: 6),
                    Section(OverviewSectionKinds.Copyright, notice: "Copyright 2026 Example."),
                ],
            },
        };

        Assert.Empty(validator.Validate(config));

        config.Site.Home = new OverviewHomeDefinition
        {
            Layout = OverviewLayouts.Split,
            Primary = [Section(OverviewSectionKinds.Hero), Section(OverviewSectionKinds.Milestone)],
            Secondary = [Section(OverviewSectionKinds.Tasks)],
            After = [Section(OverviewSectionKinds.Copyright, notice: "Copyright 2026 Example.")],
        };
        Assert.Empty(validator.Validate(config));
    }

    [Fact]
    public async Task DormantInvalidCompositionIsStillValidatedInDeterministicOrder()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var config = TestData.Config();
        config.Site = new OverviewSiteDefinition
        {
            Enabled = false,
            Title = " ",
            Home = new OverviewHomeDefinition
            {
                Layout = OverviewLayouts.Split,
                Sections = [],
                Primary = [],
                Secondary = null,
                After = [],
            },
        };

        var issues = new OverviewConfigurationValidationService(root).Validate(config);

        Assert.Equal(
            [
                "invalid_overview_site_title",
                "invalid_overview_composition",
                "empty_overview_region",
                "missing_overview_region",
                "empty_overview_region",
                "missing_overview_hero",
            ],
            issues.Select(issue => issue.Code));
        Assert.All(issues, issue => Assert.Equal(root.ConfigPath, issue.Path));
        Assert.Contains("site.home.primary", issues[2].Message);
    }

    [Fact]
    public async Task HeroAndCopyrightPlacementAreClosedAndExplicit()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var config = TestData.Config();
        config.Site = new OverviewSiteDefinition
        {
            Home = new OverviewHomeDefinition
            {
                Sections =
                [
                    Section(OverviewSectionKinds.Tasks),
                    Section(OverviewSectionKinds.Hero),
                    Section(OverviewSectionKinds.Hero),
                    Section(OverviewSectionKinds.Copyright, notice: "Copyright"),
                    Section(OverviewSectionKinds.Tasks),
                    Section(OverviewSectionKinds.Copyright, notice: "Copyright again"),
                ],
            },
        };

        var issues = new OverviewConfigurationValidationService(root).Validate(config);

        Assert.Contains(issues, issue => issue.Code == "duplicate_overview_hero");
        Assert.Contains(issues, issue => issue.Code == "misplaced_overview_hero");
        Assert.Contains(issues, issue => issue.Code == "duplicate_overview_copyright");
        Assert.Contains(issues, issue => issue.Code == "misplaced_overview_copyright");
    }

    [Fact]
    public async Task SectionFieldsAndTaskFiltersAreValidated()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var config = TestData.Config();
        config.Site = new OverviewSiteDefinition
        {
            Home = new OverviewHomeDefinition
            {
                Sections =
                [
                    Section(OverviewSectionKinds.Hero),
                    new OverviewSectionDefinition
                    {
                        Type = OverviewSectionKinds.Tasks,
                        Title = " ",
                        Filter = "state:missing track:NOPE milestone:later in:selection",
                        Limit = 21,
                        Notice = "not allowed",
                    },
                    Section(OverviewSectionKinds.Milestone, milestone: "missing"),
                    new OverviewSectionDefinition { Type = "unknown" },
                    Section(OverviewSectionKinds.Copyright, notice: " "),
                ],
            },
        };

        var issues = new OverviewConfigurationValidationService(root).Validate(config);

        Assert.Contains(issues, issue => issue.Code == "invalid_overview_section_title");
        Assert.Contains(issues, issue => issue.Code == "invalid_overview_section_fields");
        Assert.Contains(issues, issue => issue.Code == "invalid_overview_task_limit");
        Assert.Contains(issues, issue => issue.Code == "invalid_overview_task_scope");
        Assert.Contains(issues, issue => issue.Code == "unknown_overview_task_state");
        Assert.Contains(issues, issue => issue.Code == "unknown_overview_task_track");
        Assert.Contains(issues, issue => issue.Code == "unknown_overview_task_milestone");
        Assert.Contains(issues, issue => issue.Code == "missing_overview_milestone");
        Assert.Contains(issues, issue => issue.Code == "unknown_overview_section_type");
        Assert.Contains(issues, issue => issue.Code == "invalid_overview_copyright");
    }

    [Fact]
    public async Task WikiAndMarkdownReferencesUseExactNormalizedLocalPaths()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        var wiki = new WikiService(root);
        Assert.True(wiki.CreatePage("overview", "Overview", "Project introduction.").Success);
        var config = TestData.Config();
        config.Site = new OverviewSiteDefinition
        {
            Home = new OverviewHomeDefinition
            {
                Sections =
                [
                    Section(OverviewSectionKinds.Hero),
                    new OverviewSectionDefinition
                    {
                        Type = OverviewSectionKinds.Wiki,
                        Pages = ["overview"],
                    },
                    new OverviewSectionDefinition
                    {
                        Type = OverviewSectionKinds.Markdown,
                        Source = "wiki:overview",
                    },
                ],
            },
        };
        var validator = new OverviewConfigurationValidationService(root);

        Assert.Empty(validator.Validate(config));

        config.Site.Home.Sections[1].Pages = [" overview ", "missing", "missing"];
        config.Site.Home.Sections[2].Source = "wiki:missing";
        var issues = validator.Validate(config);

        Assert.Contains(issues, issue => issue.Code == "invalid_overview_wiki_path");
        Assert.Contains(issues, issue => issue.Code == "missing_overview_wiki_page");
        Assert.Contains(issues, issue => issue.Code == "duplicate_overview_wiki_page");
        Assert.Contains(issues, issue => issue.Code == "missing_overview_markdown_source");

        config.Site.Home.Sections[2].Source = "Wiki:overview";
        issues = validator.Validate(config);
        Assert.Contains(issues, issue => issue.Code == "invalid_overview_markdown_source");
    }

    [Fact]
    public async Task ProjectValidationIncludesOverviewConfigurationIssues()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        root.Config!.Site = new OverviewSiteDefinition
        {
            Enabled = true,
            Home = new OverviewHomeDefinition
            {
                Sections = [Section(OverviewSectionKinds.Tasks)],
            },
        };

        var result = new ProjectValidationService(root).ValidateProject();

        Assert.True(result.Success);
        Assert.False(result.Payload!.Valid);
        Assert.Contains(result.Payload.Issues, issue => issue.Code == "missing_overview_hero");
    }

    [Fact]
    public async Task UnrelatedConfigurationMutationPreservesOverviewSite()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await workspace.CreateProject();
        root.Config!.Site = new OverviewSiteDefinition
        {
            Enabled = true,
            Home = new OverviewHomeDefinition
            {
                Layout = OverviewLayouts.Split,
                Primary = [Section(OverviewSectionKinds.Hero)],
                Secondary = [Section(OverviewSectionKinds.Tasks)],
            },
        };
        root.Config.WriteConfig(root);

        var changed = new ProjectConfigService(root).SetAccent("purple");

        Assert.True(changed.Success);
        var stored = ProjectConfig.ReadConfig(root);
        Assert.True(stored.Site!.Enabled);
        Assert.Equal(OverviewLayouts.Split, stored.Site.Home!.Layout);
        Assert.Equal(OverviewSectionKinds.Hero, stored.Site.Home.Primary![0].Type);
    }

    private static OverviewSectionDefinition Section(
        string type,
        string? milestone = null,
        string? filter = null,
        int? limit = null,
        string? notice = null) =>
        new()
        {
            Type = type,
            Milestone = milestone,
            Filter = filter,
            Limit = limit,
            Notice = notice,
        };

}
