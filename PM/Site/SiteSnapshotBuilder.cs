using PM.Api;
using PM.Application;
using PM.Project;

namespace PM.Site;

public sealed class SiteSnapshotBuilder(
    ProjectRoot projectRoot,
    OverviewService overviewService,
    ProjectConfigService configService,
    BoardService boardService,
    WikiService wikiService,
    MilestoneActivationResolver milestoneActivationResolver,
    MilestoneActivationValidationService validationService,
    LinkedProjectService linkedProjectService,
    LinkedProjectFamilyService linkedProjectFamilyService)
{
    public const int SchemaVersion = 6;
    private const string StaticRevision = "static-snapshot";

    public async Task<AppResult<SiteSnapshot>> BuildAsync(
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken = default)
    {
        var overviewResult = await overviewService.ResolveAsync(cancellationToken: cancellationToken);
        if (!overviewResult.Success)
            return Fail(overviewResult);

        var overview = overviewResult.Payload!;
        if (overview.Status == OverviewDocumentStatus.Invalid)
        {
            var issues = string.Join("; ", overview.Issues.Select(issue =>
                $"{issue.Code} at {issue.Path}: {issue.Message}"));
            return AppResult<SiteSnapshot>.Fail(
                "invalid_overview_configuration",
                $"The enabled Overview configuration is invalid: {issues}");
        }

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
                BoardApiEndpoints.ToActivation(task.Activation),
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
        var activationResult = milestoneActivationResolver.ResolveCurrentProject();
        if (!activationResult.Success)
            return Fail(activationResult);
        var validationResult = validationService.ValidateCurrentProject();
        if (!validationResult.Success)
            return Fail(validationResult);
        var linkedProjectsResult = await BuildLinkedProjectsAsync(cancellationToken);
        if (!linkedProjectsResult.Success)
            return Fail(linkedProjectsResult);

        var projectId = projectRoot.TryReadProjectId(out var stableProjectId)
            ? stableProjectId
            : null;
        var responseSettings = new SettingsResponse(
            settings.ProjectName,
            settings.Accent,
            settings.Statuses.Select(option => new SettingsOptionResponse(option.Key, option.Name)).ToList(),
            settings.Tracks.Select(option => new SettingsOptionResponse(option.Key, option.Name)).ToList(),
            settings.Milestones.Select(option =>
                new SettingsMilestoneResponse(
                    option.Key, option.Name, option.Priority, option.Description,
                    option.RequiredActivationTriggers)).ToList(),
            settings.ActivationTriggers.Select(trigger => new SettingsActivationTriggerResponse(
                trigger.Key,
                trigger.Title,
                trigger.Requirements.Select(requirement => new SettingsActivationRequirementResponse(
                    requirement.Kind.ToString().ToLowerInvariant(), requirement.Source)).ToList())).ToList(),
            PriorityLevel.Values,
            StaticRevision);
        var activation = activationResult.Payload!;
        var responseActivation = new ActivationSwitchboardResponse(
            activation.ActivationTriggers.Select(ToActivationTrigger).ToList(),
            activation.Milestones.Select(ToActivationMilestone).ToList(),
            validationResult.Payload!.Select(issue => new ActivationIssueResponse(
                issue.Severity, issue.Code, issue.Message)).ToList(),
            StaticRevision);
        var responseBoard = ToBoardResponse(board);
        var navigation = navigationResult.Payload;

        return AppResult<SiteSnapshot>.Ok(new SiteSnapshot(
            SchemaVersion,
            generatedAt.ToUniversalTime(),
            projectId,
            linkedProjectsResult.Payload!,
            new ProjectResponse(
                projectId ?? string.Empty,
                settings.ProjectName,
                settings.Accent,
                "current",
                true,
                StaticRevision),
            OverviewApiEndpoints.ToResponse(overview),
            responseSettings,
            responseActivation,
            new BoardNavigationResponse(
                navigation.RemainingCount,
                navigation.ActivationEligibleCount,
                navigation.Tracks.Select(ToNavigationOption).ToList(),
                navigation.Milestones.Select(ToNavigationOption).ToList(),
                StaticRevision),
            responseBoard,
            tasks,
            wikiIndex,
            wikiPages));
    }

    private static ActivationTriggerResponse ToActivationTrigger(ResolvedActivationTrigger trigger) => new(
        trigger.Key,
        trigger.Title,
        trigger.IsActive,
        trigger.Activation == null ? null : new ActivationProvenanceResponse(
            trigger.Activation.At,
            trigger.Activation.Mode.ToString().ToLowerInvariant(),
            trigger.Activation.Reason,
            trigger.Activation.WaivedRequirements.Select(requirement =>
                new ActivationRequirementReferenceResponse(
                    requirement.Kind.ToString().ToLowerInvariant(), requirement.Source)).ToList()),
        trigger.SatisfiedRequirementCount,
        trigger.RequirementCount,
        trigger.RequirementsSatisfied,
        trigger.IsLatchedDespiteUnmetRequirements,
        trigger.Requirements.Select(requirement => new ActivationRequirementResponse(
            requirement.Kind.ToString().ToLowerInvariant(), requirement.Source,
            requirement.IsSatisfied, requirement.WasWaivedAtActivation)).ToList(),
        trigger.ConsumingMilestones);

    private static ActivationMilestoneResponse ToActivationMilestone(ResolvedMilestone milestone) => new(
        milestone.Key,
        milestone.Title,
        milestone.Description,
        milestone.Priority,
        EnumValue(milestone.Lifecycle),
        milestone.AssignedTaskCount,
        milestone.DoneTaskCount,
        milestone.RequiredActivationTriggers,
        milestone.UnmetActivationTriggers,
        milestone.Delivery == null ? null : new MilestoneDeliveryResponse(
            milestone.Delivery.At,
            milestone.Delivery.Mode.ToString().ToLowerInvariant(),
            milestone.Delivery.Reason,
            milestone.Delivery.AcceptedTaskIds,
            milestone.Delivery.IsValid));

    private static string EnumValue<T>(T value) where T : struct, Enum =>
        string.Concat(value.ToString().Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? $"_{char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));

    private async Task<AppResult<IReadOnlyList<SiteLinkedProjectResponse>>> BuildLinkedProjectsAsync(
        CancellationToken cancellationToken)
    {
        var manifestResult = linkedProjectService.GetManifest();
        if (!manifestResult.Success)
            return AppResult<IReadOnlyList<SiteLinkedProjectResponse>>.Fail(
                manifestResult.ErrorCode!, manifestResult.Message!);
        if (!manifestResult.Payload!.Exists)
            return AppResult<IReadOnlyList<SiteLinkedProjectResponse>>.Ok([]);

        var familyResult = await linkedProjectFamilyService.ResolveAsync(cancellationToken);
        if (!familyResult.Success)
            return AppResult<IReadOnlyList<SiteLinkedProjectResponse>>.Fail(
                familyResult.ErrorCode!, familyResult.Message!);

        var projects = familyResult.Payload!.Members
            .Where(member => member.Relationship != LinkedProjectRelationship.Current)
            .Select(member => new SiteLinkedProjectResponse(
                member.ProjectId,
                member.Name,
                member.Alias,
                member.Relationship.ToString().ToLowerInvariant(),
                member.PublicSiteUrl))
            .ToList();
        return AppResult<IReadOnlyList<SiteLinkedProjectResponse>>.Ok(projects);
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
            group.Description,
            group.Lifecycle == null ? null : BoardApiEndpoints.ToLifecycleValue(group.Lifecycle.Value),
            group.RequiredActivationTriggers,
            group.UnmetActivationTriggers,
            group.States.Select(state => new BoardStateGroupResponse(
                state.Key,
                state.Name,
                state.Tasks.Select(BoardApiEndpoints.ToSummary).ToList())).ToList())).ToList(),
        StaticRevision);

    private static BoardOptionResponse ToOption(BoardOption option) =>
        new(option.Key, option.Name, option.Priority);

    private static BoardNavigationOptionResponse ToNavigationOption(BoardNavigationOption option) =>
        new(option.Key, option.Name, option.RemainingCount, option.ActivationEligibleCount);

    private static BoardMilestoneNavigationOptionResponse ToNavigationOption(
        BoardMilestoneNavigationOption option) =>
        new(option.Key, option.Name, option.RemainingCount, option.ActivationEligibleCount,
            BoardApiEndpoints.ToLifecycleValue(option.Lifecycle), option.UnmetActivationTriggers);

    private static AppResult<SiteSnapshot> Fail<T>(AppResult<T> result) =>
        AppResult<SiteSnapshot>.Fail(result.ErrorCode ?? "site_snapshot_failed",
            result.Message ?? "The project snapshot could not be built.");
}
