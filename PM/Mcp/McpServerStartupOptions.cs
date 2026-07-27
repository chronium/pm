using PM.Application;

namespace PM.Mcp;

public enum McpCapabilityProfile
{
    Normal,
    RunWorker,
}

public sealed record McpServerStartupOptions(
    McpCapabilityProfile Profile,
    string? AssignedTaskId = null)
{
    public const string RunWorkerProfileName = "run-worker";

    public static AppResult<McpServerStartupOptions> Parse(IReadOnlyList<string> args)
    {
        string? profileName = null;
        string? taskId = null;
        var profileSeen = false;
        var taskIdSeen = false;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (TryReadOption(args, ref index, argument, "--profile", ref profileSeen, out var profileValue,
                    out var profileError))
            {
                if (profileError != null) return Invalid(profileError);
                profileName = profileValue;
                continue;
            }

            if (TryReadOption(args, ref index, argument, "--task-id", ref taskIdSeen, out var assignedTaskId,
                    out var taskIdError))
            {
                if (taskIdError != null) return Invalid(taskIdError);
                taskId = assignedTaskId;
                continue;
            }

            return Invalid($"Unknown MCP option: {argument}.");
        }

        var profile = profileName switch
        {
            null or "normal" => McpCapabilityProfile.Normal,
            RunWorkerProfileName => McpCapabilityProfile.RunWorker,
            _ => (McpCapabilityProfile?)null,
        };

        if (profile == null)
            return Invalid($"Unknown MCP profile: {profileName}.");

        if (profile == McpCapabilityProfile.Normal && taskId != null)
            return Invalid("--task-id is only valid with --profile run-worker.");

        if (profile == McpCapabilityProfile.RunWorker && taskId == null)
            return Invalid("--profile run-worker requires --task-id.");

        if (taskId != null && !IsSafeTaskId(taskId))
            return Invalid("--task-id must be a safe task identifier without path separators.");

        return AppResult<McpServerStartupOptions>.Ok(new McpServerStartupOptions(profile.Value, taskId));
    }

    private static bool TryReadOption(
        IReadOnlyList<string> args,
        ref int index,
        string argument,
        string optionName,
        ref bool seen,
        out string? value,
        out string? error)
    {
        value = null;
        error = null;
        if (!string.Equals(argument, optionName, StringComparison.Ordinal) &&
            !argument.StartsWith($"{optionName}=", StringComparison.Ordinal))
            return false;

        if (seen)
        {
            error = $"MCP option {optionName} may only be specified once.";
            return true;
        }

        seen = true;
        if (string.Equals(argument, optionName, StringComparison.Ordinal))
        {
            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"MCP option {optionName} requires a value.";
                return true;
            }

            value = args[++index].Trim();
        }
        else
        {
            value = argument[(optionName.Length + 1)..].Trim();
        }

        if (string.IsNullOrWhiteSpace(value))
            error = $"MCP option {optionName} requires a value.";

        return true;
    }

    private static bool IsSafeTaskId(string taskId) =>
        taskId is not "." and not ".." &&
        taskId.IndexOfAny(['/', '\\']) < 0 &&
        taskId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static AppResult<McpServerStartupOptions> Invalid(string message) =>
        AppResult<McpServerStartupOptions>.Fail("invalid_mcp_options", message);
}

public sealed record McpCapabilityContext(
    McpCapabilityProfile Profile,
    string? AssignedTaskId = null)
{
    public bool CanAppendNoteTo(string taskId) =>
        Profile == McpCapabilityProfile.Normal ||
        string.Equals(AssignedTaskId, taskId.Trim(), StringComparison.Ordinal);
}
