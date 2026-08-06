using PM.Files;
using PM.Api;
using PM.Project;
using PM.Tasks;

namespace PM.Application;

public enum LinkedProjectMutationAccess
{
    TrustedLinkedProjects,
    CurrentProjectOnly,
}

public sealed record ProjectMutationReceipt(
    string ProjectId,
    IReadOnlyList<string> ChangedPaths);

public sealed record LinkedProjectMutationResult<T>(
    T Value,
    ProjectMutationReceipt Receipt);

public sealed record LinkedProjectMutationTarget(
    string ProjectId,
    bool IsCurrent,
    ProjectRoot Root,
    TaskService Tasks,
    BoardService Board,
    WikiService Wiki,
    ResourceRevisionService Revisions);

public sealed class LinkedProjectMutationTracker : IDisposable
{
    private readonly FileMutationScope scope;

    internal LinkedProjectMutationTracker(LinkedProjectMutationTarget target)
    {
        Target = target;
        scope = FileSystem.TrackMutations(target.Root.RepositoryPath);
    }

    public LinkedProjectMutationTarget Target { get; }

    public ProjectMutationReceipt Receipt => new(Target.ProjectId, scope.ChangedPaths);

    public void Dispose() => scope.Dispose();
}

public sealed class LinkedProjectMutationService(
    ProjectRoot activeProject,
    INextIdService nextIdService,
    LinkedProjectFamilyService familyService,
    LinkedProjectRegistryStore registry,
    TaskServiceFactory taskServices)
{
    public static LinkedProjectMutationService ForCurrent(TaskService tasks)
    {
        var root = tasks.ProjectRoot;
        return new LinkedProjectMutationService(
            root,
            tasks.NextIdService,
            LinkedProjectFamilyService.CreateDefault(root),
            new LinkedProjectRegistryStore(),
            tasks.Factory);
    }

    public static LinkedProjectMutationService ForCurrent(WikiService wiki)
    {
        var root = wiki.ProjectRoot;
        return new LinkedProjectMutationService(
            root,
            DisabledNextIdService.Instance,
            LinkedProjectFamilyService.CreateDefault(root),
            new LinkedProjectRegistryStore(),
            new TaskServiceFactory(TimeProvider.System));
    }

    public LinkedProjectMutationTracker Track(LinkedProjectMutationTarget target) => new(target);

    public TaskService CreateTaskService(ProjectRoot root, INextIdService nextIds) =>
        taskServices.Create(root, nextIds);

    public async Task<AppResult<LinkedProjectMutationTarget>> ResolveTargetAsync(
        string? selector,
        LinkedProjectMutationAccess access = LinkedProjectMutationAccess.TrustedLinkedProjects,
        CancellationToken cancellationToken = default)
    {
        if (!activeProject.Exists || activeProject.Config == null)
            return AppResult<LinkedProjectMutationTarget>.Fail(
                "missing_project", "Project not found. Run pm init first.");

        var normalized = selector?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(normalized, "current", StringComparison.OrdinalIgnoreCase))
        {
            _ = activeProject.TryReadProjectId(out var currentProjectId);
            currentProjectId ??= "current";
            return AppResult<LinkedProjectMutationTarget>.Ok(CreateTarget(currentProjectId, activeProject, true));
        }

        if (access == LinkedProjectMutationAccess.CurrentProjectOnly)
            return AppResult<LinkedProjectMutationTarget>.Fail(
                "linked_project_write_denied",
                "This execution profile may only mutate the active project.");

        if (!activeProject.TryReadProjectId(out var activeProjectId))
            return AppResult<LinkedProjectMutationTarget>.Fail(
                "missing_project_id", "The active project has no valid stable project ID.");
        if (string.Equals(normalized, activeProjectId, StringComparison.Ordinal))
            return AppResult<LinkedProjectMutationTarget>.Ok(CreateTarget(activeProjectId, activeProject, true));

        var family = await familyService.ResolveAsync(cancellationToken);
        if (!family.Success)
            return AppResult<LinkedProjectMutationTarget>.Fail(family.ErrorCode!, family.Message!);

        var selected = LinkedProjectFamilyService.SelectMember(family.Payload!, normalized);
        if (!selected.Success)
            return AppResult<LinkedProjectMutationTarget>.Fail(selected.ErrorCode!, selected.Message!);
        var member = selected.Payload!;
        if (!member.Readable || member.Project == null || member.RepositoryPath == null)
            return AppResult<LinkedProjectMutationTarget>.Fail(
                "linked_project_unavailable",
                $"Linked project {member.ProjectId} is unavailable and cannot be mutated.");
        if (!member.WriteTrusted)
            return AppResult<LinkedProjectMutationTarget>.Fail(
                "linked_project_write_untrusted",
                $"Linked project {member.ProjectId} is read-only until local write trust is granted.");

        var opened = registry.OpenWriteTrusted(member.ProjectId);
        if (!opened.Success)
            return AppResult<LinkedProjectMutationTarget>.Fail(opened.ErrorCode!, opened.Message!);
        if (!PathsEqual(opened.Payload!.RepositoryPath, member.RepositoryPath))
            return AppResult<LinkedProjectMutationTarget>.Fail(
                "linked_project_binding_mismatch",
                $"The trusted binding for {member.ProjectId} no longer matches the resolved project.");
        if (!opened.Payload.TryReloadConfig())
            return AppResult<LinkedProjectMutationTarget>.Fail(
                "linked_project_unavailable",
                $"Linked project {member.ProjectId} has an invalid project configuration.");

        return AppResult<LinkedProjectMutationTarget>.Ok(CreateTarget(member.ProjectId, opened.Payload, false));
    }

    public async Task<AppResult<LinkedProjectMutationResult<T>>> ExecuteAsync<T>(
        string? selector,
        Func<LinkedProjectMutationTarget, CancellationToken, Task<AppResult<T>>> operation,
        LinkedProjectMutationAccess access = LinkedProjectMutationAccess.TrustedLinkedProjects,
        CancellationToken cancellationToken = default)
    {
        var target = await ResolveTargetAsync(selector, access, cancellationToken);
        if (!target.Success)
            return AppResult<LinkedProjectMutationResult<T>>.Fail(target.ErrorCode!, target.Message!);

        using var mutations = FileSystem.TrackMutations(target.Payload!.Root.RepositoryPath);
        var result = await operation(target.Payload, cancellationToken);
        if (!result.Success)
            return AppResult<LinkedProjectMutationResult<T>>.Fail(result.ErrorCode!, result.Message!);

        return AppResult<LinkedProjectMutationResult<T>>.Ok(new LinkedProjectMutationResult<T>(
            result.Payload!,
            new ProjectMutationReceipt(target.Payload.ProjectId, mutations.ChangedPaths)));
    }

    public Task<AppResult<LinkedProjectMutationResult<T>>> ExecuteAsync<T>(
        string? selector,
        Func<LinkedProjectMutationTarget, AppResult<T>> operation,
        LinkedProjectMutationAccess access = LinkedProjectMutationAccess.TrustedLinkedProjects,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(selector, (target, _) => Task.FromResult(operation(target)), access, cancellationToken);

    private LinkedProjectMutationTarget CreateTarget(string projectId, ProjectRoot root, bool isCurrent)
    {
        var board = new BoardService(root);
        return new LinkedProjectMutationTarget(
            projectId,
            isCurrent,
            root,
            taskServices.Create(root, nextIdService),
            board,
            new WikiService(root),
            new ResourceRevisionService(root, board));
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed class DisabledNextIdService : INextIdService
    {
        public static readonly DisabledNextIdService Instance = new();

        public Task<int> GetNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) =>
            Task.FromException<int>(new NotSupportedException("Task allocation is unavailable."));

        public Task<int> PeekNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) =>
            Task.FromException<int>(new NotSupportedException("Task allocation is unavailable."));

        public Task<int?> PeekExistingNextId(ProjectRoot projectRoot, string track,
            CancellationToken cancellationToken = default) => Task.FromResult<int?>(null);

        public Task<ProjectRegistration> RegisterProject(ProjectRoot projectRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ProjectRegistration>(new NotSupportedException("Project registration is unavailable."));

        public Task<bool> Healthy(ProjectConfig config, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
