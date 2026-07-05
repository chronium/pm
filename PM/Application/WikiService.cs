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
