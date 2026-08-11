using PM.Project;
using PM.Tasks;

namespace PM.Application;

public sealed class TaskServiceFactory(TimeProvider timeProvider)
{
    public TaskService Create(
        ProjectRoot projectRoot,
        INextIdService nextIdService,
        IProjectConfigPersistence? persistence = null)
    {
        var graph = new MilestoneActivationGraphService();
        var resolver = new MilestoneActivationResolver(projectRoot);
        var automaticActivations = new AutomaticActivationService(resolver, timeProvider);
        var lifecycle = new TaskLifecycleMutationService(
            projectRoot,
            resolver,
            automaticActivations,
            persistence ?? new ProjectConfigPersistence(projectRoot),
            new ReleaseVersionService(projectRoot, timeProvider));
        return new TaskService(projectRoot, nextIdService, resolver, graph, lifecycle, this);
    }
}
