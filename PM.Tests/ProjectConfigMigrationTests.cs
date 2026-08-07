using PM.Application;
using PM.Project;

namespace PM.Tests;

public class ProjectConfigMigrationTests
{
    [Fact]
    public async Task LegacyFixtureMigratesToActiveStructuredDeliverablesAndRemainsIdempotent()
    {
        using var workspace = new TempWorkingDirectory();
        var initialRoot = await workspace.CreateProject();
        var fixture = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "legacy-milestones",
            "pm_config.yaml");
        File.Copy(fixture, initialRoot.ConfigPath, overwrite: true);
        var root = new ProjectRoot();

        var migration = new ProjectConfigService(root).MigrateMilestoneSchema();

        Assert.True(migration.Success);
        var migrated = ProjectConfig.ReadConfig(root);
        Assert.False(migrated.RequiresMilestoneSchemaMigration);
        Assert.Empty(migrated.ActivationTriggers);
        Assert.Equal(["foundation", "public-beta"], migrated.Milestones.Keys);
        Assert.All(migrated.Milestones.Values, milestone =>
        {
            Assert.Equal(string.Empty, milestone.Description);
            Assert.Empty(milestone.RequiredActivationTriggers);
            Assert.Null(milestone.Delivery);
        });
        var resolved = new MilestoneActivationResolver(root).ResolveCurrentProject();
        Assert.True(resolved.Success);
        Assert.All(resolved.Payload!.Milestones, milestone =>
            Assert.Equal(MilestoneLifecycle.Active, milestone.Lifecycle));

        var firstWrite = File.ReadAllText(root.ConfigPath);
        var repeated = new ProjectConfigService(root).MigrateMilestoneSchema();
        Assert.True(repeated.Success);
        Assert.False(repeated.Payload);
        Assert.Equal(firstWrite, File.ReadAllText(root.ConfigPath));
    }

    [Fact]
    public async Task LegacySchemaBlocksEveryProjectConfigMutationUntilMigration()
    {
        using var workspace = new TempWorkingDirectory();
        var initialRoot = await workspace.CreateProject();
        var legacy = ValidLegacyConfig();
        File.WriteAllText(initialRoot.ConfigPath, legacy);
        var projectRoot = new ProjectRoot();
        var service = new ProjectConfigService(projectRoot);

        var results = new[]
        {
            service.SetAccent("purple"),
            service.AddStatus("blocked", "Blocked"),
            service.RenameStatus("todo", "Ready"),
            service.RemoveStatus("review"),
            service.AddTrack("BUILD", "Build"),
            service.RenameTrack("PM", "Product"),
            service.RemoveTrack("BUILD"),
            service.AddMilestone("launch", "Launch"),
            service.RenameMilestone("beta", "Beta"),
            service.SetMilestonePriority("beta", "urgent"),
            service.RemoveMilestone("beta"),
        };

        Assert.All(results, result =>
        {
            Assert.False(result.Success);
            Assert.Equal("milestone_schema_migration_required", result.ErrorCode);
        });
        Assert.Equal(legacy, File.ReadAllText(projectRoot.ConfigPath));
        Assert.False(Directory.Exists(Path.Combine(projectRoot.StatesPath, "blocked")));
    }

    [Fact]
    public async Task MigrationRefusesUnknownLegacyPriorityWithoutWriting()
    {
        using var workspace = new TempWorkingDirectory();
        var initialRoot = await workspace.CreateProject();
        var invalid = ValidLegacyConfig() + "\n  missing: urgent\n";
        File.WriteAllText(initialRoot.ConfigPath, invalid);
        var projectRoot = new ProjectRoot();

        var result = new ProjectConfigService(projectRoot).MigrateMilestoneSchema();

        Assert.False(result.Success);
        Assert.Equal("unknown_milestone_priority", result.ErrorCode);
        Assert.Equal(invalid, File.ReadAllText(projectRoot.ConfigPath));
        Assert.True(projectRoot.Config!.RequiresMilestoneSchemaMigration);
    }

    private static string ValidLegacyConfig() => """
                                                 name: Legacy
                                                 idWidth: 4
                                                 idPrefix: PM
                                                 taskStates:
                                                   todo: Queued
                                                   review: Review
                                                   done: Done
                                                 tracks:
                                                   PM: Project
                                                   BUILD: Build
                                                 milestones:
                                                   beta: Public beta
                                                 milestonePriorities:
                                                   beta: high
                                                 """;
}
