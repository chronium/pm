using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PM.Project;

namespace PM.Application;

public sealed class OverviewService(LinkedProjectReadService linkedProjectReads)
{
    private const int DefaultTaskLimit = 6;
    private const int DefaultWikiLimit = 6;

    public async Task<AppResult<OverviewDocument>> ResolveAsync(
        string? projectSelector = null,
        CancellationToken cancellationToken = default)
    {
        var selected = await linkedProjectReads.GetProjectAsync(projectSelector, cancellationToken);
        if (!selected.Success)
            return AppResult<OverviewDocument>.Fail(selected.ErrorCode!, selected.Message!);

        var resource = selected.Payload!.Items.Single();
        var project = resource.Resource;
        _ = project.TryReadProjectId(out var projectId);
        return await ResolveProjectAsync(project, projectId, cancellationToken);
    }

    private async Task<AppResult<OverviewDocument>> ResolveProjectAsync(
        ProjectRoot project,
        string? projectId,
        CancellationToken cancellationToken)
    {
        if (!project.Exists || project.Config == null)
            return AppResult<OverviewDocument>.Fail(
                "missing_project", "Project not found. Run pm init first.");

        var config = project.Config;
        var validation = new OverviewConfigurationValidationService(project).Validate(config);
        if (config.Site?.Enabled != true)
            return AppResult<OverviewDocument>.Ok(OverviewRevision.Finalize(new OverviewDocument(
                OverviewDocumentStatus.Disabled,
                projectId,
                config.Name,
                config.Name,
                null,
                [],
                string.Empty)));

        var documentTitle = string.IsNullOrWhiteSpace(config.Site.Title)
            ? config.Name
            : config.Site.Title;
        if (validation.Count > 0)
        {
            var issues = validation
                .Select(issue => new OverviewIssue(issue.Code, issue.Message, issue.Path))
                .ToList();
            var invalid = new OverviewDocument(
                OverviewDocumentStatus.Invalid,
                projectId,
                config.Name,
                documentTitle,
                null,
                issues,
                string.Empty);
            return AppResult<OverviewDocument>.Ok(
                OverviewRevision.Finalize(invalid, YamlSerde.Serialize(config.Site)));
        }

        var home = config.Site.Home;
        var layout = home?.Layout ?? OverviewLayouts.Single;
        var definitions = layout == OverviewLayouts.Single
            ? home?.Sections ?? ImplicitSections()
            : (home!.Primary ?? []).Concat(home.Secondary ?? []).Concat(home.After ?? []).ToList();
        var needsBoard = definitions.Any(section =>
            section.Type is OverviewSectionKinds.Milestone or OverviewSectionKinds.Tasks);
        var needsWikiIndex = definitions.Any(section => section.Type == OverviewSectionKinds.Wiki);

        BoardData? board = null;
        if (needsBoard)
        {
            var boardResult = new BoardService(project, new MilestoneActivationResolver(project))
                .GetBoard(new BoardQuery());
            if (!boardResult.Success)
                return AppResult<OverviewDocument>.Fail(boardResult.ErrorCode!, boardResult.Message!);

            var enriched = await linkedProjectReads.EnrichBoardAsync(
                boardResult.Payload!, projectId, cancellationToken);
            if (!enriched.Success)
                return AppResult<OverviewDocument>.Fail(enriched.ErrorCode!, enriched.Message!);
            board = enriched.Payload!;
        }

        var wikiService = new WikiService(project);
        IReadOnlyList<WikiPageSummary>? wikiIndex = null;
        if (needsWikiIndex)
        {
            var wikiResult = wikiService.ListPages();
            if (!wikiResult.Success)
                return AppResult<OverviewDocument>.Fail(wikiResult.ErrorCode!, wikiResult.Message!);
            wikiIndex = wikiResult.Payload!;
        }

        var context = new ResolutionContext(
            project,
            config,
            documentTitle,
            board,
            wikiService,
            wikiIndex,
            new Dictionary<string, WikiPageData>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));
        var composition = await ResolveCompositionAsync(layout, home, context, cancellationToken);
        if (!composition.Success)
            return AppResult<OverviewDocument>.Fail(composition.ErrorCode!, composition.Message!);

        return AppResult<OverviewDocument>.Ok(OverviewRevision.Finalize(new OverviewDocument(
            OverviewDocumentStatus.Ready,
            projectId,
            config.Name,
            documentTitle,
            composition.Payload,
            [],
            string.Empty)));
    }

    private static async Task<AppResult<OverviewComposition>> ResolveCompositionAsync(
        string layout,
        OverviewHomeDefinition? home,
        ResolutionContext context,
        CancellationToken cancellationToken)
    {
        if (layout == OverviewLayouts.Single)
        {
            var sections = await ResolveSectionsAsync(
                home?.Sections ?? ImplicitSections(), context, cancellationToken);
            return sections.Success
                ? AppResult<OverviewComposition>.Ok(new SingleOverviewComposition(sections.Payload!))
                : AppResult<OverviewComposition>.Fail(sections.ErrorCode!, sections.Message!);
        }

        var primary = await ResolveContentSectionsAsync(home!.Primary!, context, cancellationToken);
        if (!primary.Success)
            return AppResult<OverviewComposition>.Fail(primary.ErrorCode!, primary.Message!);
        var secondary = await ResolveContentSectionsAsync(home.Secondary!, context, cancellationToken);
        if (!secondary.Success)
            return AppResult<OverviewComposition>.Fail(secondary.ErrorCode!, secondary.Message!);
        var after = await ResolveSectionsAsync(home.After ?? [], context, cancellationToken);
        return after.Success
            ? AppResult<OverviewComposition>.Ok(new SplitOverviewComposition(
                primary.Payload!, secondary.Payload!, after.Payload!))
            : AppResult<OverviewComposition>.Fail(after.ErrorCode!, after.Message!);
    }

    private static async Task<AppResult<IReadOnlyList<OverviewContentSection>>> ResolveContentSectionsAsync(
        IReadOnlyList<OverviewSectionDefinition> definitions,
        ResolutionContext context,
        CancellationToken cancellationToken)
    {
        var sections = await ResolveSectionsAsync(definitions, context, cancellationToken);
        if (!sections.Success)
            return AppResult<IReadOnlyList<OverviewContentSection>>.Fail(
                sections.ErrorCode!, sections.Message!);

        var resolvedSections = sections.Payload!;
        var content = resolvedSections.OfType<OverviewContentSection>().ToList();
        return content.Count == resolvedSections.Count
            ? AppResult<IReadOnlyList<OverviewContentSection>>.Ok(content)
            : AppResult<IReadOnlyList<OverviewContentSection>>.Fail(
                "invalid_overview_composition",
                "Copyright cannot be resolved inside a split content region.");
    }

    private static async Task<AppResult<IReadOnlyList<OverviewSection>>> ResolveSectionsAsync(
        IReadOnlyList<OverviewSectionDefinition> definitions,
        ResolutionContext context,
        CancellationToken cancellationToken)
    {
        var sections = new List<OverviewSection>();
        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var section = await ResolveSectionAsync(definition, context, cancellationToken);
            if (!section.Success)
                return AppResult<IReadOnlyList<OverviewSection>>.Fail(
                    section.ErrorCode!, section.Message!);
            sections.Add(section.Payload!);
        }

        return AppResult<IReadOnlyList<OverviewSection>>.Ok(sections);
    }

    private static async Task<AppResult<OverviewSection>> ResolveSectionAsync(
        OverviewSectionDefinition definition,
        ResolutionContext context,
        CancellationToken cancellationToken)
    {
        switch (definition.Type)
        {
            case OverviewSectionKinds.Hero:
                return AppResult<OverviewSection>.Ok(new HeroOverviewSection(
                    context.DocumentTitle,
                    context.Config.Site!.Description));
            case OverviewSectionKinds.Milestone:
                return AppResult<OverviewSection>.Ok(new MilestoneOverviewSection(
                    definition.Title ?? "Current milestone",
                    ResolveMilestone(definition, context.Board!)));
            case OverviewSectionKinds.Tasks:
            {
                var tasks = ResolveTasks(definition, context);
                return tasks.Success
                    ? AppResult<OverviewSection>.Ok(new TasksOverviewSection(
                        definition.Title ?? "Current work", tasks.Payload!))
                    : AppResult<OverviewSection>.Fail(tasks.ErrorCode!, tasks.Message!);
            }
            case OverviewSectionKinds.Wiki:
                return AppResult<OverviewSection>.Ok(new WikiOverviewSection(
                    definition.Title ?? "Documentation",
                    ResolveWikiPages(definition, context.WikiIndex!)));
            case OverviewSectionKinds.Markdown:
            {
                var sourcePath = definition.Source!["wiki:".Length..];
                var page = await ReadWikiPageAsync(context, sourcePath, cancellationToken);
                if (!page.Success)
                    return AppResult<OverviewSection>.Fail(page.ErrorCode!, page.Message!);
                var resolvedPage = page.Payload!;
                return AppResult<OverviewSection>.Ok(new MarkdownOverviewSection(
                    definition.Title ?? resolvedPage.Title,
                    resolvedPage.Path,
                    resolvedPage.Body));
            }
            case OverviewSectionKinds.Copyright:
                return AppResult<OverviewSection>.Ok(new CopyrightOverviewSection(definition.Notice!));
            default:
                return AppResult<OverviewSection>.Fail(
                    "invalid_overview_section", "Overview section type is invalid.");
        }
    }

    private static OverviewMilestone? ResolveMilestone(
        OverviewSectionDefinition definition,
        BoardData board)
    {
        var milestone = definition.Milestone == null
            ? board.MilestoneActivation.Milestones
                .Where(item => item.Lifecycle == MilestoneLifecycle.Active)
                .OrderByDescending(item => PriorityLevel.Rank(item.Priority))
                .FirstOrDefault()
            : board.MilestoneActivation.Milestones.Single(item =>
                string.Equals(item.Key, definition.Milestone, StringComparison.Ordinal));
        return milestone == null ? null : new OverviewMilestone(
            milestone.Key,
            milestone.Title,
            milestone.Description,
            milestone.Priority,
            milestone.Lifecycle,
            milestone.AssignedTaskCount,
            milestone.DoneTaskCount,
            milestone.RequiredActivationTriggers,
            milestone.UnmetActivationTriggers);
    }

    private static AppResult<IReadOnlyList<OverviewTask>> ResolveTasks(
        OverviewSectionDefinition definition,
        ResolutionContext context)
    {
        TaskSearchQuery? query = null;
        if (definition.Filter != null)
        {
            var parsed = TaskSearchQueryParser.Parse(definition.Filter);
            if (!parsed.Success)
                return AppResult<IReadOnlyList<OverviewTask>>.Fail(parsed.ErrorCode!, parsed.Message!);
            query = parsed.Payload!;
        }

        var tasks = new List<OverviewTask>();
        foreach (var task in context.Board!.Tasks)
        {
            var include = query == null
                ? AppResult<bool>.Ok(!string.Equals(task.State, "done", StringComparison.Ordinal))
                : MatchesTask(task, query, context);
            if (!include.Success)
                return AppResult<IReadOnlyList<OverviewTask>>.Fail(include.ErrorCode!, include.Message!);
            if (!include.Payload) continue;

            tasks.Add(new OverviewTask(
                task.Task.Id,
                task.Task.Title,
                task.Track,
                task.Milestone,
                task.Priority,
                task.PrioritySource,
                task.State,
                task.Dependencies,
                task.Activation,
                task.DescriptionPreview,
                task.Task.ModifiedAt.ToUniversalTime()));
            if (tasks.Count == definition.Limit.GetValueOrDefault(DefaultTaskLimit)) break;
        }

        return AppResult<IReadOnlyList<OverviewTask>>.Ok(tasks);
    }

    private static AppResult<bool> MatchesTask(
        BoardTask task,
        TaskSearchQuery query,
        ResolutionContext context)
    {
        var markdown = string.Empty;
        if (query.HasFreeText && !context.TaskMarkdown.TryGetValue(task.Task.Id, out markdown))
        {
            if (!context.Project.TryReadTaskFile(task.Task.Id, out markdown))
                return AppResult<bool>.Fail(
                    "overview_task_read_failed",
                    $"Task {task.Task.Id} could not be read while resolving Overview.");
            context.TaskMarkdown[task.Task.Id] = markdown;
        }

        return AppResult<bool>.Ok(TaskSearchEvaluator.Evaluate(
            new TaskSearchDocument(task.Task, markdown, task.Track, task.State, task.Priority),
            query,
            new TaskSearchContext()).Matches);
    }

    private static IReadOnlyList<OverviewWikiPage> ResolveWikiPages(
        OverviewSectionDefinition definition,
        IReadOnlyList<WikiPageSummary> wikiIndex)
    {
        IEnumerable<WikiPageSummary> selected;
        if (definition.Pages == null)
        {
            selected = wikiIndex
                .Where(page => !page.Path.Contains('/', StringComparison.Ordinal))
                .OrderBy(page => page.Path, StringComparer.Ordinal)
                .Take(DefaultWikiLimit);
        }
        else
        {
            var byPath = wikiIndex.ToDictionary(page => page.Path, StringComparer.Ordinal);
            selected = definition.Pages.Select(path => byPath[path]);
        }

        return selected.Select(page => new OverviewWikiPage(
            page.Path,
            page.Title,
            page.ModifiedAt.ToUniversalTime())).ToList();
    }

    private static Task<AppResult<WikiPageData>> ReadWikiPageAsync(
        ResolutionContext context,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.WikiPages.TryGetValue(sourcePath, out var cached))
            return Task.FromResult(AppResult<WikiPageData>.Ok(cached));

        var result = context.Wiki.ReadPage(sourcePath);
        if (result.Success) context.WikiPages[sourcePath] = result.Payload!;
        return Task.FromResult(result);
    }

    private static List<OverviewSectionDefinition> ImplicitSections() =>
    [
        new() { Type = OverviewSectionKinds.Hero },
        new() { Type = OverviewSectionKinds.Milestone },
        new() { Type = OverviewSectionKinds.Tasks },
        new() { Type = OverviewSectionKinds.Wiki },
    ];

    private sealed record ResolutionContext(
        ProjectRoot Project,
        ProjectConfig Config,
        string DocumentTitle,
        BoardData? Board,
        WikiService Wiki,
        IReadOnlyList<WikiPageSummary>? WikiIndex,
        Dictionary<string, WikiPageData> WikiPages,
        Dictionary<string, string> TaskMarkdown);
}

internal static class OverviewRevision
{
    public static OverviewDocument Finalize(OverviewDocument document, string? invalidConfiguration = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "overview-document-v1");
        Append(hash, document.Status.ToString().ToLowerInvariant());
        Append(hash, document.ProjectId);
        Append(hash, document.ProjectName);
        Append(hash, document.DocumentTitle);

        switch (document.Status)
        {
            case OverviewDocumentStatus.Disabled:
                break;
            case OverviewDocumentStatus.Invalid:
                Append(hash, invalidConfiguration);
                Append(hash, document.Issues.Count);
                foreach (var issue in document.Issues)
                {
                    Append(hash, issue.Code);
                    Append(hash, issue.Message);
                    Append(hash, issue.Path);
                }
                break;
            case OverviewDocumentStatus.Ready:
                AppendComposition(hash, document.Composition!);
                break;
        }

        return document with
        {
            Revision = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
        };
    }

    private static void AppendComposition(IncrementalHash hash, OverviewComposition composition)
    {
        Append(hash, composition.Layout);
        switch (composition)
        {
            case SingleOverviewComposition single:
                AppendSections(hash, single.Sections);
                break;
            case SplitOverviewComposition split:
                AppendSections(hash, split.Primary);
                AppendSections(hash, split.Secondary);
                AppendSections(hash, split.After);
                break;
        }
    }

    private static void AppendSections<T>(IncrementalHash hash, IReadOnlyList<T> sections)
        where T : OverviewSection
    {
        Append(hash, sections.Count);
        foreach (var section in sections) AppendSection(hash, section);
    }

    private static void AppendSection(IncrementalHash hash, OverviewSection section)
    {
        Append(hash, section.Type);
        switch (section)
        {
            case HeroOverviewSection hero:
                Append(hash, hero.Title);
                Append(hash, hero.Description);
                break;
            case MilestoneOverviewSection milestone:
                Append(hash, milestone.Title);
                AppendMilestone(hash, milestone.Milestone);
                break;
            case TasksOverviewSection tasks:
                Append(hash, tasks.Title);
                Append(hash, tasks.Tasks.Count);
                foreach (var task in tasks.Tasks) AppendTask(hash, task);
                break;
            case WikiOverviewSection wiki:
                Append(hash, wiki.Title);
                Append(hash, wiki.Pages.Count);
                foreach (var page in wiki.Pages)
                {
                    Append(hash, page.Path);
                    Append(hash, page.Title);
                    Append(hash, page.ModifiedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                }
                break;
            case MarkdownOverviewSection markdown:
                Append(hash, markdown.Title);
                Append(hash, markdown.SourcePath);
                Append(hash, markdown.Body);
                break;
            case CopyrightOverviewSection copyright:
                Append(hash, copyright.Notice);
                break;
        }
    }

    private static void AppendMilestone(IncrementalHash hash, OverviewMilestone? milestone)
    {
        Append(hash, milestone != null);
        if (milestone == null) return;
        Append(hash, milestone.Key);
        Append(hash, milestone.Title);
        Append(hash, milestone.Description);
        Append(hash, milestone.Priority);
        Append(hash, milestone.Lifecycle.ToString());
        Append(hash, milestone.AssignedTaskCount);
        Append(hash, milestone.DoneTaskCount);
        Append(hash, milestone.RequiredActivationTriggers);
        Append(hash, milestone.UnmetActivationTriggers);
    }

    private static void AppendTask(IncrementalHash hash, OverviewTask task)
    {
        Append(hash, task.Id);
        Append(hash, task.Title);
        Append(hash, task.Track);
        Append(hash, task.Milestone);
        Append(hash, task.Priority);
        Append(hash, task.PrioritySource);
        Append(hash, task.State);
        AppendDependency(hash, task.Dependencies);
        AppendActivation(hash, task.Activation);
        Append(hash, task.DescriptionPreview);
        Append(hash, task.ModifiedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    }

    private static void AppendDependency(IncrementalHash hash, DependencyStatus status)
    {
        Append(hash, status.Ready);
        Append(hash, status.DependsOn);
        Append(hash, status.Completed);
        Append(hash, status.WaitingOn);
        Append(hash, status.Missing);
        Append(hash, status.Unavailable);
        Append(hash, status.Invalid);
        Append(hash, status.Summary);
    }

    private static void AppendActivation(IncrementalHash hash, TaskActivationEligibility activation)
    {
        Append(hash, activation.IsEligible);
        Append(hash, activation.MilestoneLifecycle?.ToString());
        Append(hash, activation.RequiredActivationTriggers);
        Append(hash, activation.UnmetActivationTriggers);
        Append(hash, activation.Summary);
    }

    private static void Append(IncrementalHash hash, IReadOnlyList<string> values)
    {
        Append(hash, values.Count);
        foreach (var value in values) Append(hash, value);
    }

    private static void Append(IncrementalHash hash, bool value) => Append(hash, value ? "true" : "false");

    private static void Append(IncrementalHash hash, int value) =>
        Append(hash, value.ToString(CultureInfo.InvariantCulture));

    private static void Append(IncrementalHash hash, string? value)
    {
        if (value == null)
        {
            Span<byte> missing = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(missing, -1);
            hash.AppendData(missing);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
