namespace PM;

public static class GlobalConfig
{
    public static string PmDirName = ".pm";
    public static string PmConfigFile = "pm_config.yaml";

    public static string ProjectIdFile = "project_id.txt";
    public static string LegacyNextIdFile = "next_id.txt";

    public static string TasksDirName = "tasks";
    public static string StatesDirName = "states";
    public static string WikiDirName = "wiki";
    public static string TaskOrderFile = "task_order.yaml";

    public static bool DryRun = false;

    public static Dictionary<string, string> DefaultTaskStates = new()
    {
        ["todo"] = "To Do",
        ["in-progress"] = "In Progress",
        ["done"] = "Done",
    };

    public static string ApplicationName => "Project Manager";
    public static string ApplicationVersion => "1.0.0";

    public static string DefaultTaskExtension => "md";

    public static string InitCommandName => "init";
    public static string TaskBranchName => "task";
    public static string TaskAddCommandName => "add";
    public static string TaskEditCommandName => "edit";
    public static string TaskMetadataCommandName => "metadata";
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
    public static string StatusBranchName => "status";
    public static string StatusAddCommandName => "add";
    public static string StatusRenameCommandName => "rename";
    public static string StatusRemoveCommandName => "remove";
    public static string ListCommandName => "list";
    public static string MoveCommandName => "move";
    public static string WebCommandName => "web";
    public static string DoctorCommandName => "doctor";
    public static string McpCommandName => "mcp";
    public static string ClaimCommandName => "claim";
    public static string SiteBranchName => "site";
    public static string SiteBuildCommandName => "build";
}
