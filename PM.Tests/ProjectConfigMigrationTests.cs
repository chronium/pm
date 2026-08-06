using PM.Application;
using PM.Project;

namespace PM.Tests;

public class ProjectConfigMigrationTests
{
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
