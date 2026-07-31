using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using PM.AgentRuns;
using PM.Application;
using PM.Project;

namespace PM.Mcp;

public sealed record McpLinkedWikiContextEntry(
    string ProjectId,
    string Name,
    string Alias,
    string Revision,
    string Requirement,
    string Status,
    string Summary);

public sealed record McpLinkedWikiContextManifest(
    int Version,
    string PrimaryProjectId,
    IReadOnlyList<McpLinkedWikiContextEntry> Contexts);

public sealed class McpLinkedWikiContextStore
{
    private readonly string? root;
    private readonly McpLinkedWikiContextManifest? manifest;

    private McpLinkedWikiContextStore(string? root, McpLinkedWikiContextManifest? manifest)
    {
        this.root = root;
        this.manifest = manifest;
    }

    public bool Configured => manifest != null;

    public static AppResult<McpLinkedWikiContextStore> Load(string? path)
    {
        if (path == null)
            return AppResult<McpLinkedWikiContextStore>.Ok(new McpLinkedWikiContextStore(null, null));
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetDirectoryName(fullPath)!;
            var value = JsonSerializer.Deserialize<McpLinkedWikiContextManifest>(
                File.ReadAllBytes(fullPath), AgentRunJson.Options);
            if (value == null || value.Version != 1 || string.IsNullOrWhiteSpace(value.PrimaryProjectId) ||
                value.Contexts == null || value.Contexts.Count > 31 ||
                value.Contexts.Select(item => item.ProjectId).Distinct(StringComparer.Ordinal).Count() !=
                value.Contexts.Count)
                return Invalid();
            foreach (var entry in value.Contexts)
            {
                if (entry.ProjectId is not { } projectId ||
                    !System.Text.RegularExpressions.Regex.IsMatch(
                        projectId, "^[A-Za-z0-9][A-Za-z0-9._-]{0,255}$") ||
                    string.IsNullOrWhiteSpace(entry.Name) ||
                    string.IsNullOrWhiteSpace(entry.Alias) ||
                    entry.Requirement is not ("required" or "optional") ||
                    entry.Status is not ("available" or "unavailable") ||
                    !System.Text.RegularExpressions.Regex.IsMatch(entry.Revision, "^[0-9a-f]{40}([0-9a-f]{24})?$") ||
                    entry.Status == "available" && !TryProject(root, projectId, out _))
                    return Invalid();
            }
            return AppResult<McpLinkedWikiContextStore>.Ok(new McpLinkedWikiContextStore(root, value));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           JsonException or ArgumentException)
        {
            return Invalid();
        }
    }

    public bool Allows(string? selector, bool family)
    {
        if (manifest == null) return false;
        if (family) return true;
        if (string.IsNullOrWhiteSpace(selector) ||
            string.Equals(selector.Trim(), "current", StringComparison.OrdinalIgnoreCase)) return true;
        return Find(selector) != null;
    }

    public AppResult<LinkedProjectReadResult<WikiPageSummary>> List(string? selector, bool family)
    {
        var targets = Targets(selector, family);
        if (!targets.Success) return Fail<WikiPageSummary>(targets);
        var items = new List<LinkedProjectResource<WikiPageSummary>>();
        foreach (var target in targets.Payload!)
        {
            var service = new WikiService(target.Project);
            var pages = service.ListPages();
            if (!pages.Success) return AppResult<LinkedProjectReadResult<WikiPageSummary>>.Fail(
                pages.ErrorCode!, pages.Message!);
            items.AddRange(pages.Payload!.Select(page =>
                new LinkedProjectResource<WikiPageSummary>(target.Owner, page)));
        }
        return AppResult<LinkedProjectReadResult<WikiPageSummary>>.Ok(new(items, []));
    }

    public AppResult<LinkedProjectReadResult<WikiPageData>> Get(string path, string selector)
    {
        var targets = Targets(selector, false);
        if (!targets.Success) return Fail<WikiPageData>(targets);
        var target = targets.Payload!.Single();
        var page = new WikiService(target.Project).ReadPage(path);
        return page.Success
            ? AppResult<LinkedProjectReadResult<WikiPageData>>.Ok(new(
                [new LinkedProjectResource<WikiPageData>(target.Owner, page.Payload!)], []))
            : AppResult<LinkedProjectReadResult<WikiPageData>>.Fail(page.ErrorCode!, page.Message!);
    }

    public AppResult<LinkedProjectReadResult<WikiSearchResult>> Search(
        string query, int limit, string? selector, bool family)
    {
        var targets = Targets(selector, family);
        if (!targets.Success) return Fail<WikiSearchResult>(targets);
        var items = new List<LinkedProjectResource<WikiSearchResult>>();
        foreach (var target in targets.Payload!)
        {
            var pages = new WikiService(target.Project).SearchPages(query, limit);
            if (!pages.Success) return AppResult<LinkedProjectReadResult<WikiSearchResult>>.Fail(
                pages.ErrorCode!, pages.Message!);
            items.AddRange(pages.Payload!.Select(page =>
                new LinkedProjectResource<WikiSearchResult>(target.Owner, page)));
        }
        return AppResult<LinkedProjectReadResult<WikiSearchResult>>.Ok(new(
            items.OrderByDescending(item => item.Resource.MatchCount)
                .ThenBy(item => item.Owner.ProjectId, StringComparer.Ordinal)
                .ThenBy(item => item.Resource.Path, StringComparer.Ordinal)
                .Take(Math.Clamp(limit, 1, 100)).ToList(), []));
    }

    public AppResult<LinkedProjectReadResult<WikiPageOutlineData>> Outline(string path, string selector)
    {
        var targets = Targets(selector, false);
        if (!targets.Success) return Fail<WikiPageOutlineData>(targets);
        var target = targets.Payload!.Single();
        var outline = new WikiService(target.Project).OutlinePage(path);
        return outline.Success
            ? AppResult<LinkedProjectReadResult<WikiPageOutlineData>>.Ok(new(
                [new LinkedProjectResource<WikiPageOutlineData>(target.Owner, outline.Payload!)], []))
            : AppResult<LinkedProjectReadResult<WikiPageOutlineData>>.Fail(
                outline.ErrorCode!, outline.Message!);
    }

    private AppResult<IReadOnlyList<Target>> Targets(string? selector, bool family)
    {
        if (manifest == null || root == null)
            return AppResult<IReadOnlyList<Target>>.Fail(
                "mcp_project_scope_denied", "No linked wiki context was granted to this run.");
        var entries = family
            ? manifest.Contexts.Where(item => item.Status == "available").ToList()
            : Find(selector) is { Status: "available" } found ? [found] : [];
        if (entries.Count == 0)
            return AppResult<IReadOnlyList<Target>>.Fail(
                "linked_project_unavailable", "The selected linked wiki context is unavailable to this run.");
        var targets = entries.Select(entry =>
        {
            if (!TryProject(root, entry.ProjectId, out var project)) throw new InvalidOperationException();
            return new Target(project, new LinkedProjectResourceOwner(
                entry.ProjectId, entry.Name, entry.Alias, LinkedProjectRelationship.Sibling,
                entry.Revision, false));
        }).ToList();
        return AppResult<IReadOnlyList<Target>>.Ok(targets);
    }

    private McpLinkedWikiContextEntry? Find(string? selector)
    {
        var value = selector?.Trim();
        return manifest?.Contexts.SingleOrDefault(entry =>
            string.Equals(entry.ProjectId, value, StringComparison.Ordinal) ||
            string.Equals(entry.Alias, value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryProject(string root, string projectId,
        [NotNullWhen(true)] out ProjectRoot? project) =>
        ProjectRoot.TryOpenExact(Path.Combine(root, projectId), out project);

    private static AppResult<LinkedProjectReadResult<T>> Fail<T>(AppResult<IReadOnlyList<Target>> result) =>
        AppResult<LinkedProjectReadResult<T>>.Fail(result.ErrorCode!, result.Message!);

    private static AppResult<McpLinkedWikiContextStore> Invalid() =>
        AppResult<McpLinkedWikiContextStore>.Fail(
            "invalid_linked_context_manifest", "The runner-provided linked wiki context manifest is invalid.");

    private sealed record Target(ProjectRoot Project, LinkedProjectResourceOwner Owner);
}
