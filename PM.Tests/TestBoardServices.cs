using PM.Application;
using PM.Project;

namespace PM.Tests;

internal static class TestBoardServices
{
    public static BoardService Create(ProjectRoot projectRoot) =>
        new(projectRoot, new MilestoneActivationResolver(projectRoot));
}
