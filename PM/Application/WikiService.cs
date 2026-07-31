using PM.Project;
using PM.Wiki;
using PM.Files;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

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

public sealed record WikiPageOutlineData(
    string Path,
    string Title,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    string FilePath,
    string Version,
    IReadOnlyList<WikiHeadingOutline> Headings);

public sealed record WikiHeadingOutline(
    string Id,
    int Level,
    string Title,
    IReadOnlyList<string> Breadcrumb,
    string Preview);

public sealed class WikiService(ProjectRoot projectRoot)
{
    public ProjectRoot ProjectRoot => projectRoot;

    private static readonly Regex AtxHeadingPattern =
        new(@"^[ \t]{0,3}(?<marks>#{1,6})(?:[ \t]+|$)(?<title>.*?)(?:[ \t]+#+[ \t]*)?$",
            RegexOptions.Compiled);

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

    public AppResult<WikiPageOutlineData> OutlinePage(string path)
    {
        var read = ReadPage(path);
        if (!read.Success)
            return AppResult<WikiPageOutlineData>.Fail(read.ErrorCode ?? "unknown_error",
                read.Message ?? "Operation failed.");

        var page = read.Payload!;
        return AppResult<WikiPageOutlineData>.Ok(ToOutlineData(page));
    }

    public AppResult<(WikiPageData Page, string Version)> PatchPageSection(
        string path,
        string version,
        string headingId,
        string operation,
        string markdown)
    {
        if (string.IsNullOrWhiteSpace(version))
            return AppResult<(WikiPageData Page, string Version)>.Fail("stale_wiki_page",
                "Wiki page version is required.");

        if (string.IsNullOrWhiteSpace(headingId))
            return AppResult<(WikiPageData Page, string Version)>.Fail("missing_wiki_heading",
                "Wiki heading id is required.");

        if (string.IsNullOrWhiteSpace(markdown))
            return AppResult<(WikiPageData Page, string Version)>.Fail("invalid_wiki_patch_markdown",
                "Wiki patch markdown is required.");

        if (string.IsNullOrWhiteSpace(operation))
            return AppResult<(WikiPageData Page, string Version)>.Fail("invalid_wiki_patch_operation",
                "Wiki patch operation is invalid.");

        var read = ReadPage(path);
        if (!read.Success)
            return AppResult<(WikiPageData Page, string Version)>.Fail(read.ErrorCode ?? "unknown_error",
                read.Message ?? "Operation failed.");

        var page = read.Payload!;
        var currentVersion = ComputeBodyVersion(page.Body);
        if (!string.Equals(currentVersion, version.Trim(), StringComparison.Ordinal))
            return AppResult<(WikiPageData Page, string Version)>.Fail("stale_wiki_page",
                "Wiki page body changed since it was outlined.");

        var body = page.Body.ReplaceLineEndings("\n");
        var sections = ParseHeadingSections(body);
        var section = sections.FirstOrDefault(heading => string.Equals(heading.Id, headingId.Trim(), StringComparison.Ordinal));
        if (section == null)
            return AppResult<(WikiPageData Page, string Version)>.Fail("missing_wiki_heading",
                $"Wiki heading {headingId.Trim()} was not found.");

        var patchMarkdown = NormalizePatchMarkdown(markdown);
        string updatedBody;
        switch (operation.Trim())
        {
            case "append_to_section":
                updatedBody = InsertMarkdownBlock(body, section.DirectContentEnd, patchMarkdown);
                break;
            case "prepend_to_section":
                updatedBody = InsertMarkdownBlock(body, section.ContentStart, patchMarkdown);
                break;
            case "replace_section_body":
                updatedBody = ReplaceMarkdownBlock(body, section.ContentStart, section.SectionEnd, patchMarkdown);
                break;
            case "insert_before_heading":
                updatedBody = InsertMarkdownBlock(body, section.HeadingStart, patchMarkdown);
                break;
            case "insert_after_section":
                updatedBody = InsertMarkdownBlock(body, section.SectionEnd, patchMarkdown);
                break;
            default:
                return AppResult<(WikiPageData Page, string Version)>.Fail("invalid_wiki_patch_operation",
                    "Wiki patch operation is invalid.");
        }

        var updated = UpdatePageBody(path, updatedBody);
        if (!updated.Success)
            return AppResult<(WikiPageData Page, string Version)>.Fail(updated.ErrorCode ?? "unknown_error",
                updated.Message ?? "Operation failed.");

        var updatedPage = updated.Payload!;
        return AppResult<(WikiPageData Page, string Version)>.Ok(
            (updatedPage, ComputeBodyVersion(updatedPage.Body)));
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
            FileSystem.DeleteFile(filePath);
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

        FileSystem.DeleteFile(filePath);
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

    private static WikiPageOutlineData ToOutlineData(WikiPageData page)
    {
        var body = page.Body.ReplaceLineEndings("\n");
        var sections = ParseHeadingSections(body);
        return new WikiPageOutlineData(
            page.Path,
            page.Title,
            page.CreatedAt,
            page.ModifiedAt,
            page.FilePath,
            ComputeBodyVersion(page.Body),
            sections.Select(section => new WikiHeadingOutline(
                    section.Id,
                    section.Level,
                    section.Title,
                    section.Breadcrumb,
                    BuildSectionPreview(body, section)))
                .ToList());
    }

    private static string ComputeBodyVersion(string body)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizePatchMarkdown(string markdown)
    {
        return markdown.ReplaceLineEndings("\n").Trim('\n');
    }

    private static string InsertMarkdownBlock(string body, int offset, string markdown)
    {
        var before = body[..offset];
        var after = body[offset..];
        return before + BoundaryBefore(before) + markdown + BoundaryAfter(after) + after;
    }

    private static string ReplaceMarkdownBlock(string body, int start, int end, string markdown)
    {
        var before = body[..start];
        var after = body[end..];
        return before + BoundaryBefore(before) + markdown + BoundaryAfter(after) + after;
    }

    private static string BoundaryBefore(string before)
    {
        if (before.Length == 0 || before.EndsWith("\n\n", StringComparison.Ordinal)) return string.Empty;
        return before.EndsWith('\n') ? "\n" : "\n\n";
    }

    private static string BoundaryAfter(string after)
    {
        if (after.Length == 0 || after.StartsWith("\n\n", StringComparison.Ordinal)) return string.Empty;
        return after.StartsWith('\n') ? "\n" : "\n\n";
    }

    private static IReadOnlyList<WikiHeadingSection> ParseHeadingSections(string body)
    {
        body = body.ReplaceLineEndings("\n");
        var headings = new List<ParsedHeading>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var inFence = false;
        var fenceCharacter = '\0';
        var fenceLength = 0;
        var index = 0;

        while (index < body.Length)
        {
            var lineStart = index;
            var newlineIndex = body.IndexOf('\n', index);
            var lineEnd = newlineIndex < 0 ? body.Length : newlineIndex;
            var nextIndex = newlineIndex < 0 ? body.Length : newlineIndex + 1;
            var line = body[lineStart..lineEnd];

            if (TryParseFence(line, out var currentFenceCharacter, out var currentFenceLength, out var canCloseFence))
            {
                if (!inFence)
                {
                    inFence = true;
                    fenceCharacter = currentFenceCharacter;
                    fenceLength = currentFenceLength;
                }
                else if (currentFenceCharacter == fenceCharacter && currentFenceLength >= fenceLength && canCloseFence)
                {
                    inFence = false;
                    fenceCharacter = '\0';
                    fenceLength = 0;
                }

                index = nextIndex;
                continue;
            }

            if (!inFence && TryParseHeading(line, out var level, out var title))
            {
                var slug = SlugifyHeadingTitle(title);
                var occurrenceKey = $"{level}:{slug}";
                occurrences.TryGetValue(occurrenceKey, out var occurrence);
                occurrence++;
                occurrences[occurrenceKey] = occurrence;
                headings.Add(new ParsedHeading(
                    $"h{level}-{slug}-{occurrence}",
                    level,
                    title,
                    lineStart,
                    nextIndex));
            }

            index = nextIndex;
        }

        var sections = new List<WikiHeadingSection>(headings.Count);
        var breadcrumbs = new Dictionary<int, string>();
        for (var headingIndex = 0; headingIndex < headings.Count; headingIndex++)
        {
            var heading = headings[headingIndex];
            breadcrumbs[heading.Level] = heading.Title;
            foreach (var level in breadcrumbs.Keys.Where(level => level > heading.Level).ToList())
                breadcrumbs.Remove(level);

            var directContentEnd = body.Length;
            var sectionEnd = body.Length;
            for (var nextHeadingIndex = headingIndex + 1; nextHeadingIndex < headings.Count; nextHeadingIndex++)
            {
                var nextHeading = headings[nextHeadingIndex];
                if (directContentEnd == body.Length)
                    directContentEnd = nextHeading.HeadingStart;

                if (nextHeading.Level > heading.Level) continue;
                sectionEnd = nextHeading.HeadingStart;
                break;
            }

            sections.Add(new WikiHeadingSection(
                heading.Id,
                heading.Level,
                heading.Title,
                Enumerable.Range(1, heading.Level)
                    .Where(breadcrumbs.ContainsKey)
                    .Select(level => breadcrumbs[level])
                    .ToList(),
                heading.HeadingStart,
                heading.ContentStart,
                directContentEnd,
                sectionEnd));
        }

        return sections;
    }

    private static bool TryParseHeading(string line, out int level, out string title)
    {
        level = 0;
        title = string.Empty;
        var match = AtxHeadingPattern.Match(line);
        if (!match.Success) return false;

        level = match.Groups["marks"].Value.Length;
        title = match.Groups["title"].Value.Trim();
        return true;
    }

    private static bool TryParseFence(string line, out char fenceCharacter, out int fenceLength, out bool canClose)
    {
        fenceCharacter = '\0';
        fenceLength = 0;
        canClose = false;
        var index = 0;
        while (index < line.Length && index < 4 && (line[index] == ' ' || line[index] == '\t'))
            index++;

        if (index > 3 || index >= line.Length || (line[index] != '`' && line[index] != '~')) return false;

        var marker = line[index];
        var markerIndex = index;
        while (markerIndex < line.Length && line[markerIndex] == marker)
            markerIndex++;

        var count = markerIndex - index;
        if (count < 3) return false;

        fenceCharacter = marker;
        fenceLength = count;
        canClose = string.IsNullOrWhiteSpace(line[markerIndex..]);
        return true;
    }

    private static string SlugifyHeadingTitle(string title)
    {
        var builder = new StringBuilder();
        var previousWasSeparator = false;
        foreach (var character in title.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        if (builder.Length > 0 && builder[^1] == '-')
            builder.Length--;

        return builder.Length == 0 ? "section" : builder.ToString();
    }

    private static string BuildSectionPreview(string body, WikiHeadingSection section)
    {
        var sectionBody = body[section.ContentStart..section.SectionEnd];
        var previewLines = new List<string>();
        var inFence = false;
        var fenceCharacter = '\0';
        var fenceLength = 0;

        foreach (var rawLine in sectionBody.Split('\n'))
        {
            var parseLine = rawLine.TrimEnd();
            var line = rawLine.Trim();
            if (TryParseFence(parseLine, out var currentFenceCharacter, out var currentFenceLength, out var canCloseFence))
            {
                if (!inFence)
                {
                    inFence = true;
                    fenceCharacter = currentFenceCharacter;
                    fenceLength = currentFenceLength;
                }
                else if (currentFenceCharacter == fenceCharacter && currentFenceLength >= fenceLength && canCloseFence)
                {
                    inFence = false;
                    fenceCharacter = '\0';
                    fenceLength = 0;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!inFence && TryParseHeading(parseLine, out _, out _)) continue;
            previewLines.Add(line);
        }

        var preview = string.Join(" ", previewLines).Trim();

        return preview.Length <= 160 ? preview : preview[..157].TrimEnd() + "...";
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

    private sealed record ParsedHeading(
        string Id,
        int Level,
        string Title,
        int HeadingStart,
        int ContentStart);

    private sealed record WikiHeadingSection(
        string Id,
        int Level,
        string Title,
        IReadOnlyList<string> Breadcrumb,
        int HeadingStart,
        int ContentStart,
        int DirectContentEnd,
        int SectionEnd);
}
