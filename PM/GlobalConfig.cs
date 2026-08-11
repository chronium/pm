using System.Reflection;

namespace PM;

public static class GlobalConfig
{
    public static string PmDirName = ".pm";
    public static string PmConfigFile = "pm_config.yaml";

    public static string ProjectIdFile = "project_id.txt";
    public static string ReleaseVersionFile = "release_version.txt";
    public static string LinkedProjectsFile = "linked_projects.yaml";

    public static string TasksDirName = "tasks";
    public static string StatesDirName = "states";
    public static string WikiDirName = "wiki";
    public static string TaskOrderFile = "task_order.yaml";
    public static string DirectoryPlaceholderFile = ".gitkeep";

    public static bool DryRun = false;

    public static Dictionary<string, string> DefaultTaskStates = new()
    {
        ["todo"] = "To Do",
        ["in-progress"] = "In Progress",
        ["done"] = "Done",
    };

    public static string ApplicationName => "Project Manager";
    public static string ApplicationVersion =>
        typeof(GlobalConfig).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? throw new InvalidOperationException("The PM assembly does not declare an informational version.");

    public static string DefaultTaskExtension => "md";

    public static string InitCommandName => "init";
    public static string TaskBranchName => "task";
    public static string TaskAddCommandName => "add";
    public static string TaskEditCommandName => "edit";
    public static string TaskMetadataCommandName => "metadata";
    public static string TaskNoteCommandName => "note";
    public static string TaskNextCommandName => "next";
    public static string TaskRemoveCommandName => "remove";
    public static string TaskSearchCommandName => "search";
    public static string WikiBranchName => "wiki";
    public static string WikiListCommandName => "list";
    public static string WikiSearchCommandName => "search";
    public static string WikiShowCommandName => "show";
    public static string WikiCreateCommandName => "create";
    public static string WikiEditCommandName => "edit";
    public static string WikiRenameCommandName => "rename";
    public static string WikiRemoveCommandName => "remove";
    public static string TrackBranchName => "track";
    public static string TrackAddCommandName => "add";
    public static string TrackRenameCommandName => "rename";
    public static string TrackRemoveCommandName => "remove";
    public static string MilestoneBranchName => "milestone";
    public static string MilestoneAddCommandName => "add";
    public static string MilestoneRenameCommandName => "rename";
    public static string MilestoneRemoveCommandName => "remove";
    public static string MilestonePriorityCommandName => "priority";
    public static string MilestoneListCommandName => "list";
    public static string MilestoneDeliverCommandName => "deliver";
    public static string MilestoneReopenCommandName => "reopen";
    public static string TriggerBranchName => "trigger";
    public static string TriggerAddCommandName => "add";
    public static string TriggerRenameCommandName => "rename";
    public static string TriggerRemoveCommandName => "remove";
    public static string TriggerSetRequirementsCommandName => "set-requirements";
    public static string TriggerRedefineCommandName => "redefine";
    public static string TriggerActivateCommandName => "activate";
    public static string TriggerResetCommandName => "reset";
    public static string TriggerAttachCommandName => "attach";
    public static string TriggerDetachCommandName => "detach";
    public static string TriggerListCommandName => "list";
    public static string TriggerReconcileCommandName => "reconcile";
    public static string StatusBranchName => "status";
    public static string StatusAddCommandName => "add";
    public static string StatusRenameCommandName => "rename";
    public static string StatusRemoveCommandName => "remove";
    public static string ListCommandName => "list";
    public static string MoveCommandName => "move";
    public static string WebCommandName => "web";
    public static string DoctorCommandName => "doctor";
    public static string McpCommandName => "mcp";
    public static string SiteBranchName => "site";
    public static string SiteBuildCommandName => "build";
    public static string ProjectBranchName => "project";
    public static string RunnerBranchName => "runner";
}
