namespace PM;

public static class GlobalConfig
{
    public static string PmDirName = ".dev-pm";
    public static string PmConfigFile = "pm_config.yaml";

    public static string NextIdFile = "next_id.txt";

    public static string TasksDirName = "tasks";
    public static string StatesDirName = "states";

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
    public static string ListCommandName => "list";
    public static string MoveCommandName => "move";
}