using PM.Project;
using PM.Wiki;

namespace PM.Application;

public sealed record WikiPageData(
    string Path,
    string Title,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    string FilePath,
    string Markdown,
    string Body);

public sealed record WikiPageSummary(
    string Path,
    string Title,
    DateTime ModifiedAt,
    string FilePath);

public sealed record WikiSearchResult(
    string Path,
    string Title,
    DateTime ModifiedAt,
    string FilePath,
    int MatchCount,
    string Snippet);

public sealed class WikiService(ProjectRoot projectRoot)
{
    public AppResult<WikiPageData> CreatePage(string path, string title, string body)
    {
        if (!projectRoot.Exists)
            return AppResult<WikiPageData>.Fail("missing_project", "Project not found. Run pm init first.");

        if (!projectRoot.TryResolveWikiPath(path, out var normalizedPath, out var filePath))
            return AppResult<WikiPageData>.Fail("invalid_wiki_path", "Wiki page path is invalid.");

        if (File.Exists(filePath))
            return AppResult<WikiPageData>.Fail("duplicate_wiki_page", $"Wiki page {normalizedPath} already exists.");

        if (string.IsNullOrWhiteSpace(title))
            return AppResult<WikiPageData>.Fail("invalid_wiki_page", "Wiki page title is required.");

        var now = DateTime.UtcNow;
        var page = new WikiPage
        {
            Path = normalizedPath,
            Title = title.Trim(),
            CreatedAt = now,
            ModifiedAt = now,
            Body = body ?? string.Empty,
        };

        projectRoot.WriteWikiPage(page);
        return AppResult<WikiPageData>.Ok(ToData(page, filePath));
    }

    public AppResult<WikiPageData> CreatePageMarkdown(string path, string markdown)
    {
        if (!projectRoot.Exists)
            return AppResult<WikiPageData>.Fail("missing_project", "Project not found. Run pm init first.");

        if (!projectRoot.TryResolveWikiPath(path, out var normalizedPath, out var filePath))
            return AppResult<WikiPageData>.Fail("invalid_wiki_path", "Wiki page path is invalid.");

        if (File.Exists(filePath))
            return AppResult<WikiPageData>.Fail("duplicate_wiki_page", $"Wiki page {normalizedPath} already exists.");

        var page = WikiPage.Parse(normalizedPath, markdown);
        if (page == null)
            return AppResult<WikiPageData>.Fail("invalid_wiki_markdown", "Edited wiki markdown is invalid.");

        projectRoot.WriteWikiPage(page);
        return AppResult<WikiPageData>.Ok(ToData(page, filePath));
    }

    public AppResult<WikiPageData> ReadPage(string path)
    {
        if (!projectRoot.Exists)
            return AppResult<WikiPageData>.Fail("missing_project", "Project not found. Run pm init first.");

        if (!projectRoot.TryResolveWikiPath(path, out var normalizedPath, out var filePath))
            return AppResult<WikiPageData>.Fail("invalid_wiki_path", "Wiki page path is invalid.");

        if (!File.Exists(filePath))
            return AppResult<WikiPageData>.Fail("missing_wiki_page", $"Wiki page {normalizedPath} not found.");

        var markdown = File.ReadAllText(filePath);
        var page = WikiPage.Parse(normalizedPath, markdown);
        if (page == null)
            return AppResult<WikiPageData>.Fail("invalid_wiki_markdown", $"Wiki page {normalizedPath} markdown is invalid.");

        return AppResult<WikiPageData>.Ok(ToData(page, filePath, markdown));
    }

    public AppResult<IReadOnlyList<WikiPageSummary>> ListPages()
    {
        if (!projectRoot.Exists)
            return AppResult<IReadOnlyList<WikiPageSummary>>.Fail("missing_project", "Project not found. Run pm init first.");

        return ListAllPages();
    }

    public AppResult<IReadOnlyList<WikiPageSummary>> ListPagesUnder(string path)
    {
        if (!projectRoot.Exists)
            return AppResult<IReadOnlyList<WikiPageSummary>>.Fail("missing_project", "Project not found. Run pm init first.");

        if (!projectRoot.TryResolveWikiPath(path, out var normalizedPath, out _))
            return AppResult<IReadOnlyList<WikiPageSummary>>.Fail("invalid_wiki_path", "Wiki page path is invalid.");

        var pages = ListAllPages();
        if (!pages.Success) return pages;

        return AppResult<IReadOnlyList<WikiPageSummary>>.Ok(pages.Payload!
            .Where(page => page.Path.StartsWith(normalizedPath + "/", StringComparison.Ordinal))
            .ToList());
    }

    public AppResult<IReadOnlyList<WikiSearchResult>> SearchPages(string query, int limit = 20)
    {
        if (!projectRoot.Exists)
            return AppResult<IReadOnlyList<WikiSearchResult>>.Fail("missing_project", "Project not found. Run pm init first.");

        if (string.IsNullOrWhiteSpace(query))
            return AppResult<IReadOnlyList<WikiSearchResult>>.Fail("invalid_wiki_query", "Wiki search query is required.");

        var normalizedQuery = query.Trim();
        limit = Math.Clamp(limit, 1, 100);
        var results = new List<WikiSearchResult>();
        foreach (var (path, filePath, content) in projectRoot.GetWikiMarkdownFiles())
        {
            var page = WikiPage.Parse(path, content);
            if (page == null)
                return AppResult<IReadOnlyList<WikiSearchResult>>.Fail("invalid_wiki_markdown",
                    $"Wiki page {path} markdown is invalid.");

            var matchCount =
                CountMatches(page.Title, normalizedQuery) +
                CountMatches(page.Path, normalizedQuery) +
                CountMatches(page.Body, normalizedQuery);
            if (matchCount == 0) continue;

            results.Add(new WikiSearchResult(
                page.Path,
                page.Title,
                page.ModifiedAt,
                filePath,
                matchCount,
                BuildSnippet(page, normalizedQuery)));
        }

        return AppResult<IReadOnlyList<WikiSearchResult>>.Ok(results
            .OrderByDescending(result => result.MatchCount)
            .ThenBy(result => result.Path, StringComparer.Ordinal)
            .Take(limit)
            .ToList());
    }

    private AppResult<IReadOnlyList<WikiPageSummary>> ListAllPages()
    {
        var pages = new List<WikiPageSummary>();
        foreach (var (path, filePath, content) in projectRoot.GetWikiMarkdownFiles())
        {
            var page = WikiPage.Parse(path, content);
            if (page == null)
                return AppResult<IReadOnlyList<WikiPageSummary>>.Fail("invalid_wiki_markdown",
                    $"Wiki page {path} markdown is invalid.");

            pages.Add(new WikiPageSummary(page.Path, page.Title, page.ModifiedAt, filePath));
        }

        return AppResult<IReadOnlyList<WikiPageSummary>>.Ok(pages);
    }

    public AppResult<WikiPageData> UpdatePageMarkdown(string path, string markdown)
    {
        if (!projectRoot.Exists)
            return AppResult<WikiPageData>.Fail("missing_project", "Project not found. Run pm init first.");

        if (!projectRoot.TryResolveWikiPath(path, out var normalizedPath, out var filePath))
            return AppResult<WikiPageData>.Fail("invalid_wiki_path", "Wiki page path is invalid.");

        if (!File.Exists(filePath))
            return AppResult<WikiPageData>.Fail("missing_wiki_page", $"Wiki page {normalizedPath} not found.");

        var editedPage = WikiPage.Parse(normalizedPath, markdown);
        if (editedPage == null)
            return AppResult<WikiPageData>.Fail("invalid_wiki_markdown", "Edited wiki markdown is invalid.");

        var updatedPage = editedPage with { ModifiedAt = DateTime.UtcNow };
        projectRoot.WriteWikiPage(updatedPage);
        return AppResult<WikiPageData>.Ok(ToData(updatedPage, filePath));
    }

    public AppResult<WikiPageData> UpdatePageBody(string path, string body)
    {
        if (!projectRoot.Exists)
            return AppResult<WikiPageData>.Fail("missing_project", "Project not found. Run pm init first.");

        if (!projectRoot.TryResolveWikiPath(path, out var normalizedPath, out var filePath))
            return AppResult<WikiPageData>.Fail("invalid_wiki_path", "Wiki page path is invalid.");

        if (!File.Exists(filePath))
            return AppResult<WikiPageData>.Fail("missing_wiki_page", $"Wiki page {normalizedPath} not found.");

        var markdown = File.ReadAllText(filePath);
        var page = WikiPage.Parse(normalizedPath, markdown);
        if (page == null)
            return AppResult<WikiPageData>.Fail("invalid_wiki_markdown", $"Wiki page {normalizedPath} markdown is invalid.");

        var updatedPage = page with
        {
            ModifiedAt = DateTime.UtcNow,
            Body = body ?? string.Empty,
        };

        projectRoot.WriteWikiPage(updatedPage);
        return AppResult<WikiPageData>.Ok(ToData(updatedPage, filePath));
    }

    public AppResult<WikiPageData> RenamePage(string path, string newPath, string title)
    {
        if (!projectRoot.Exists)
            return AppResult<WikiPageData>.Fail("missing_project", "Project not found. Run pm init first.");

        if (!projectRoot.TryResolveWikiPath(path, out var normalizedPath, out var filePath))
            return AppResult<WikiPageData>.Fail("invalid_wiki_path", "Wiki page path is invalid.");

        if (!projectRoot.TryResolveWikiPath(newPath, out var normalizedNewPath, out var newFilePath))
            return AppResult<WikiPageData>.Fail("invalid_wiki_path", "Wiki page path is invalid.");

        if (string.IsNullOrWhiteSpace(title))
            return AppResult<WikiPageData>.Fail("invalid_wiki_page", "Wiki page title is required.");

        if (!File.Exists(filePath))
            return AppResult<WikiPageData>.Fail("missing_wiki_page", $"Wiki page {normalizedPath} not found.");

        if (!string.Equals(filePath, newFilePath, StringComparison.Ordinal) && File.Exists(newFilePath))
            return AppResult<WikiPageData>.Fail("duplicate_wiki_page", $"Wiki page {normalizedNewPath} already exists.");

        var markdown = File.ReadAllText(filePath);
        var page = WikiPage.Parse(normalizedPath, markdown);
        if (page == null)
            return AppResult<WikiPageData>.Fail("invalid_wiki_markdown", $"Wiki page {normalizedPath} markdown is invalid.");

        var updatedPage = page with
        {
            Path = normalizedNewPath,
            Title = title.Trim(),
            ModifiedAt = DateTime.UtcNow,
        };

        projectRoot.WriteWikiPage(updatedPage);
        if (!string.Equals(filePath, newFilePath, StringComparison.Ordinal))
        {
            File.Delete(filePath);
            RemoveEmptyWikiParentDirectories(filePath);
        }

        return AppResult<WikiPageData>.Ok(ToData(updatedPage, newFilePath));
    }

    public AppResult RemovePage(string path)
    {
        if (!projectRoot.Exists)
            return AppResult.Fail("missing_project", "Project not found. Run pm init first.");

        if (!projectRoot.TryResolveWikiPath(path, out var normalizedPath, out var filePath))
            return AppResult.Fail("invalid_wiki_path", "Wiki page path is invalid.");

        if (!File.Exists(filePath))
            return AppResult.Fail("missing_wiki_page", $"Wiki page {normalizedPath} not found.");

        File.Delete(filePath);
        RemoveEmptyWikiParentDirectories(filePath);
        return AppResult.Ok();
    }

    private void RemoveEmptyWikiParentDirectories(string filePath)
    {
        var wikiRoot = Path.GetFullPath(projectRoot.WikiPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));

        while (!string.IsNullOrWhiteSpace(directory) &&
               !string.Equals(directory, wikiRoot, StringComparison.Ordinal))
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any()) return;

            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static int CountMatches(string value, string query)
    {
        var count = 0;
        var index = 0;
        while (true)
        {
            index = value.IndexOf(query, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return count;
            count++;
            index += query.Length;
        }
    }

    private static string BuildSnippet(WikiPage page, string query)
    {
        var haystack = string.IsNullOrWhiteSpace(page.Body)
            ? $"{page.Title} {page.Path}"
            : page.Body.ReplaceLineEndings("\n").Replace('\n', ' ');
        var index = haystack.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0) index = 0;

        var start = Math.Max(0, index - 40);
        var length = Math.Min(120, haystack.Length - start);
        var snippet = haystack.Substring(start, length).Trim();
        if (start > 0) snippet = "..." + snippet;
        if (start + length < haystack.Length) snippet += "...";
        return snippet;
    }

    private static WikiPageData ToData(WikiPage page, string filePath, string? markdown = null)
    {
        markdown ??= page.ToMarkdown();
        return new WikiPageData(
            page.Path,
            page.Title,
            page.CreatedAt,
            page.ModifiedAt,
            filePath,
            markdown,
            page.Body);
    }
}
