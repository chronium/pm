using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PM.Application;
using PM.Project;
using PM.Tasks;

namespace PM.Api;

public sealed class ResourceRevisionService(ProjectRoot projectRoot, BoardService boardService)
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AppResult<string> GetTaskRevision(string id)
    {
        if (!projectRoot.Exists)
            return AppResult<string>.Fail("missing_project", "Project not found. Run pm init first.");
        if (!projectRoot.TryReadTaskFile(id, out var markdown))
            return AppResult<string>.Fail("missing_task", $"Task {id} not found.");

        var task = TaskItem.Parse(markdown);
        if (task == null)
            return AppResult<string>.Fail("invalid_task", $"Task {id} is invalid.");
        if (!projectRoot.TryGetState(task, out var state))
            return AppResult<string>.Fail("missing_task_state", $"Task {id} has no state.");

        return AppResult<string>.Ok(Hash("task", markdown, state));
    }

    public AppResult<string> GetWikiPageRevision(string path)
    {
        if (!projectRoot.Exists)
            return AppResult<string>.Fail("missing_project", "Project not found. Run pm init first.");
        if (!projectRoot.TryResolveWikiPath(path, out var normalizedPath, out var filePath))
            return AppResult<string>.Fail("invalid_wiki_path", "Wiki page path is invalid.");
        if (!File.Exists(filePath))
            return AppResult<string>.Fail("missing_wiki_page", $"Wiki page {normalizedPath} not found.");

        var markdown = File.ReadAllText(filePath);
        return AppResult<string>.Ok(Hash("wiki-page", markdown));
    }

    public string GetWikiIndexRevision(IReadOnlyList<WikiPageSummary> pages)
    {
        var canonical = pages
            .OrderBy(page => page.Path, StringComparer.Ordinal)
            .Select(page => new WikiIndexRevisionPage(
                page.Path,
                page.Title,
                page.ModifiedAt.ToUniversalTime()))
            .ToList();
        return Hash("wiki-index", JsonSerializer.Serialize(canonical, CanonicalJsonOptions));
    }

    public AppResult<string> GetProjectConfigRevision()
    {
        if (!projectRoot.Exists || projectRoot.RootPath == null)
            return AppResult<string>.Fail("missing_project", "Project not found. Run pm init first.");

        try
        {
            return AppResult<string>.Ok(Hash("project-config", File.ReadAllText(projectRoot.ConfigPath)));
        }
        catch
        {
            return AppResult<string>.Fail("invalid_project", "The project configuration could not be read.");
        }
    }

    public AppResult<string> GetBoardRevision(
        BoardQuery query,
        int descriptionPreviewLength = BoardService.WebDescriptionPreviewLength)
    {
        var boardResult = boardService.GetBoard(query, descriptionPreviewLength);
        if (!boardResult.Success)
            return AppResult<string>.Fail(boardResult.ErrorCode!, boardResult.Message!);

        return GetBoardRevision(boardResult.Payload!);
    }

    public AppResult<string> GetBoardRevision(BoardData board)
    {
        var canonical = new BoardRevisionDocument(
            board.ProjectName,
            NormalizeQuery(board.Query),
            board.Tracks,
            board.Milestones,
            board.States,
            board.Tasks.Select(ToRevisionTask).ToList(),
            board.MilestoneGroups.Select(group => new BoardRevisionMilestoneGroup(
                group.Key,
                group.Name,
                group.States.Select(state => new BoardRevisionStateGroup(
                    state.Key,
                    state.Name,
                    state.Tasks.Select(task => task.Task.Id).ToList())).ToList())).ToList());
        var json = JsonSerializer.Serialize(canonical, CanonicalJsonOptions);
        return AppResult<string>.Ok(Hash("board", json));
    }

    private static BoardQuery NormalizeQuery(BoardQuery query) => new(
        NormalizeFilter(query.Track),
        NormalizeFilter(query.Milestone),
        NormalizeFilter(query.State));

    private static string? NormalizeFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static BoardRevisionTask ToRevisionTask(BoardTask task)
    {
        return new BoardRevisionTask(
            task.Task.Id,
            task.Task.Title,
            task.Track,
            task.Milestone,
            task.Priority,
            task.PrioritySource,
            task.State,
            task.Dependencies,
            task.DescriptionPreview,
            task.Task.CreatedAt,
            task.Task.ModifiedAt,
            task.FilePath);
    }

    private static string Hash(string domain, params string[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, domain);
        foreach (var value in values) Append(hash, value);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private sealed record BoardRevisionDocument(
        string ProjectName,
        BoardQuery Query,
        IReadOnlyList<BoardOption> Tracks,
        IReadOnlyList<BoardOption> Milestones,
        IReadOnlyList<BoardOption> States,
        IReadOnlyList<BoardRevisionTask> Tasks,
        IReadOnlyList<BoardRevisionMilestoneGroup> MilestoneGroups);

    private sealed record BoardRevisionMilestoneGroup(
        string? Key,
        string Name,
        IReadOnlyList<BoardRevisionStateGroup> States);

    private sealed record BoardRevisionStateGroup(
        string Key,
        string Name,
        IReadOnlyList<string> TaskIds);

    private sealed record BoardRevisionTask(
        string Id,
        string Title,
        string Track,
        string? Milestone,
        string Priority,
        string PrioritySource,
        string State,
        DependencyStatus Dependencies,
        string DescriptionPreview,
        DateTime CreatedAt,
        DateTime ModifiedAt,
        string FilePath);

    private sealed record WikiIndexRevisionPage(string Path, string Title, DateTime ModifiedAt);
}
