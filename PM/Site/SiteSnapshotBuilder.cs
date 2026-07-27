using PM.Api;
using PM.Application;
using PM.Project;

namespace PM.Site;

public sealed class SiteSnapshotBuilder(
    ProjectConfigService configService,
    BoardService boardService,
    WikiService wikiService)
{
    public const int SchemaVersion = 1;
    private const string StaticRevision = "static-snapshot";

    public AppResult<SiteSnapshot> Build(DateTimeOffset generatedAt)
    {
        var settingsResult = configService.GetSettings();
        if (!settingsResult.Success)
            return Fail(settingsResult);

        var navigationResult = boardService.GetNavigation();
        if (!navigationResult.Success)
            return Fail(navigationResult);

        var board = navigationResult.Payload!.Board;
        var tasks = new List<SiteTaskResponse>();
        foreach (var summary in board.Tasks)
        {
            var taskResult = boardService.GetTask(summary.Task.Id);
            if (!taskResult.Success)
                return Fail(taskResult);

            var task = taskResult.Payload!;
            tasks.Add(new SiteTaskResponse(
                task.Task.Id,
                task.Task.Title,
                task.Track,
                task.Milestone,
                task.Priority,
                task.PrioritySource,
                task.Task.Priority ?? "inherit",
                task.State,
                BoardApiEndpoints.ToDependencies(task.Dependencies),
                BoardApiEndpoints.ToUtc(task.Task.CreatedAt),
                BoardApiEndpoints.ToUtc(task.Task.ModifiedAt),
                task.Task.Description,
                StaticRevision));
        }

        var wikiIndexResult = wikiService.ListPages();
        if (!wikiIndexResult.Success)
            return Fail(wikiIndexResult);

        var wikiIndex = wikiIndexResult.Payload!
            .Select(page => new WikiPageSummaryResponse(
                page.Path,
                page.Title,
                BoardApiEndpoints.ToUtc(page.ModifiedAt)))
            .ToList();
        var wikiPages = new List<SiteWikiPageResponse>();
        foreach (var summary in wikiIndexResult.Payload!)
        {
            var pageResult = wikiService.ReadPage(summary.Path);
            if (!pageResult.Success)
                return Fail(pageResult);

            var page = pageResult.Payload!;
            wikiPages.Add(new SiteWikiPageResponse(
                page.Path,
                page.Title,
                BoardApiEndpoints.ToUtc(page.CreatedAt),
                BoardApiEndpoints.ToUtc(page.ModifiedAt),
                page.Body,
                StaticRevision));
        }

        var settings = settingsResult.Payload!;
        var responseSettings = new SettingsResponse(
            settings.ProjectName,
            settings.Statuses.Select(option => new SettingsOptionResponse(option.Key, option.Name)).ToList(),
            settings.Tracks.Select(option => new SettingsOptionResponse(option.Key, option.Name)).ToList(),
            settings.Milestones.Select(option =>
                new SettingsMilestoneResponse(option.Key, option.Name, option.Priority)).ToList(),
            PriorityLevel.Values,
            StaticRevision);
        var responseBoard = ToBoardResponse(board);
        var navigation = navigationResult.Payload;

        return AppResult<SiteSnapshot>.Ok(new SiteSnapshot(
            SchemaVersion,
            generatedAt.ToUniversalTime(),
            new ProjectResponse(settings.ProjectName, StaticRevision),
            responseSettings,
            new BoardNavigationResponse(
                navigation.RemainingCount,
                navigation.Tracks.Select(ToNavigationOption).ToList(),
                navigation.Milestones.Select(ToNavigationOption).ToList(),
                StaticRevision),
            responseBoard,
            tasks,
            wikiIndex,
            wikiPages));
    }

    private static BoardResponse ToBoardResponse(BoardData board) => new(
        board.ProjectName,
        new BoardFilterResponse(board.Query.Track, board.Query.Milestone, board.Query.State),
        board.Tracks.Select(ToOption).ToList(),
        board.Milestones.Select(ToOption).ToList(),
        board.States.Select(ToOption).ToList(),
        board.MilestoneGroups.Select(group => new BoardMilestoneGroupResponse(
            group.Key,
            group.Name,
            group.States.Select(state => new BoardStateGroupResponse(
                state.Key,
                state.Name,
                state.Tasks.Select(BoardApiEndpoints.ToSummary).ToList())).ToList())).ToList(),
        StaticRevision);

    private static BoardOptionResponse ToOption(BoardOption option) =>
        new(option.Key, option.Name, option.Priority);

    private static BoardNavigationOptionResponse ToNavigationOption(BoardNavigationOption option) =>
        new(option.Key, option.Name, option.RemainingCount);

    private static AppResult<SiteSnapshot> Fail<T>(AppResult<T> result) =>
        AppResult<SiteSnapshot>.Fail(result.ErrorCode ?? "site_snapshot_failed",
            result.Message ?? "The project snapshot could not be built.");
}
