using PM.Application;
using PM.Project;
using PM.Tasks;

namespace PM.Tests;

internal static class TestTaskServices
{
    public static TaskService Create(
        ProjectRoot projectRoot,
        INextIdService nextIdService,
        TimeProvider? timeProvider = null,
        IProjectConfigPersistence? persistence = null) =>
        new TaskServiceFactory(timeProvider ?? TimeProvider.System).Create(
            projectRoot, nextIdService, persistence);
}
