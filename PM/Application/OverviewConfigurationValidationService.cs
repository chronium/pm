using PM.Project;
using PM.Wiki;

namespace PM.Application;

public sealed record OverviewConfigurationIssue(string Code, string Message, string Path);

public sealed class OverviewConfigurationValidationService(ProjectRoot projectRoot)
{
    private const int MinimumTaskLimit = 1;
    private const int MaximumTaskLimit = 20;

    public IReadOnlyList<OverviewConfigurationIssue> Validate(ProjectConfig config)
    {
        var issues = new List<OverviewConfigurationIssue>();
        var site = config.Site;
        if (site is null) return issues;

        ValidateOptionalText(issues, site.Title, "site.title", "invalid_overview_site_title",
            "Overview site title must contain non-whitespace text when configured.");
        ValidateOptionalText(issues, site.Description, "site.description", "invalid_overview_site_description",
            "Overview site description must contain non-whitespace text when configured.");

        var home = site.Home;
        if (home is null) return issues;

        var layout = home.Layout ?? OverviewLayouts.Single;
        switch (layout)
        {
            case OverviewLayouts.Single:
                ValidateSingle(issues, home, config);
                break;
            case OverviewLayouts.Split:
                ValidateSplit(issues, home, config);
                break;
            default:
                AddError(issues, "invalid_overview_layout",
                    "site.home.layout",
                    $"Unsupported Overview layout {FormatValue(home.Layout)}; use single or split.");
                break;
        }

        return issues;
    }

    private void ValidateSingle(
        List<OverviewConfigurationIssue> issues,
        OverviewHomeDefinition home,
        ProjectConfig config)
    {
        if (home.Primary is not null || home.Secondary is not null || home.After is not null)
            AddError(issues, "invalid_overview_composition",
                "site.home", "Single layout cannot contain primary, secondary, or after regions.");

        if (home.Sections is null) return;
        if (home.Sections.Count == 0)
            AddError(issues, "empty_overview_region",
                "site.home.sections", "An explicitly configured section list must not be empty.");

        ValidateRegion(issues, home.Sections, "site.home.sections", config);
        ValidateHero(issues, [("site.home.sections", home.Sections)], "site.home.sections");
        ValidateCopyright(issues, [("site.home.sections", home.Sections)], OverviewLayouts.Single);
    }

    private void ValidateSplit(
        List<OverviewConfigurationIssue> issues,
        OverviewHomeDefinition home,
        ProjectConfig config)
    {
        if (home.Sections is not null)
            AddError(issues, "invalid_overview_composition",
                "site.home", "Split layout cannot contain sections.");

        ValidateRequiredRegion(issues, home.Primary, "site.home.primary", config);
        ValidateRequiredRegion(issues, home.Secondary, "site.home.secondary", config);
        if (home.After is { } after)
        {
            if (after.Count == 0)
                AddError(issues, "empty_overview_region",
                    "site.home.after", "An explicitly configured after region must not be empty.");
            ValidateRegion(issues, after, "site.home.after", config);
        }

        var regions = new List<(string Path, IReadOnlyList<OverviewSectionDefinition> Sections)>();
        if (home.Primary is not null) regions.Add(("site.home.primary", home.Primary));
        if (home.Secondary is not null) regions.Add(("site.home.secondary", home.Secondary));
        if (home.After is not null) regions.Add(("site.home.after", home.After));
        ValidateHero(issues, regions, "site.home.primary");
        ValidateCopyright(issues, regions, OverviewLayouts.Split);
    }

    private void ValidateRequiredRegion(
        List<OverviewConfigurationIssue> issues,
        IReadOnlyList<OverviewSectionDefinition>? sections,
        string path,
        ProjectConfig config)
    {
        if (sections is null)
        {
            AddError(issues, "missing_overview_region", path, "Split layout requires this region.");
            return;
        }

        if (sections.Count == 0)
            AddError(issues, "empty_overview_region", path, "Split layout requires a non-empty region.");
        ValidateRegion(issues, sections, path, config);
    }

    private void ValidateRegion(
        List<OverviewConfigurationIssue> issues,
        IReadOnlyList<OverviewSectionDefinition> sections,
        string path,
        ProjectConfig config)
    {
        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            var sectionPath = $"{path}[{index}]";
            if (section is null)
            {
                AddError(issues, "invalid_overview_section", sectionPath, "Section must be a mapping.");
                continue;
            }

            ValidateSection(issues, section, sectionPath, config);
        }
    }

    private void ValidateSection(
        List<OverviewConfigurationIssue> issues,
        OverviewSectionDefinition section,
        string path,
        ProjectConfig config)
    {
        if (string.IsNullOrWhiteSpace(section.Type))
        {
            AddError(issues, "missing_overview_section_type", $"{path}.type", "Section type is required.");
            return;
        }

        if (!OverviewSectionKinds.IsSupported(section.Type))
        {
            AddError(issues, "unknown_overview_section_type",
                $"{path}.type", $"Unsupported Overview section type {section.Type}.");
            return;
        }

        if (section.Type is not OverviewSectionKinds.Hero and not OverviewSectionKinds.Copyright)
            ValidateOptionalText(issues, section.Title, $"{path}.title", "invalid_overview_section_title",
                "Overview section title must contain non-whitespace text when configured.");

        switch (section.Type)
        {
            case OverviewSectionKinds.Hero:
                ValidateAllowedFields(issues, section, path, []);
                break;
            case OverviewSectionKinds.Milestone:
                ValidateAllowedFields(issues, section, path, ["title", "milestone"]);
                ValidateMilestone(issues, section, path, config);
                break;
            case OverviewSectionKinds.Tasks:
                ValidateAllowedFields(issues, section, path, ["title", "filter", "limit"]);
                ValidateTasks(issues, section, path, config);
                break;
            case OverviewSectionKinds.Wiki:
                ValidateAllowedFields(issues, section, path, ["title", "pages"]);
                ValidateWiki(issues, section, path);
                break;
            case OverviewSectionKinds.Markdown:
                ValidateAllowedFields(issues, section, path, ["title", "source"]);
                ValidateMarkdown(issues, section, path);
                break;
            case OverviewSectionKinds.Copyright:
                ValidateAllowedFields(issues, section, path, ["notice"]);
                if (string.IsNullOrWhiteSpace(section.Notice))
                    AddError(issues, "invalid_overview_copyright",
                        $"{path}.notice", "Copyright notice must contain non-whitespace plain text.");
                break;
        }
    }

    private static void ValidateMilestone(
        List<OverviewConfigurationIssue> issues,
        OverviewSectionDefinition section,
        string path,
        ProjectConfig config)
    {
        if (section.Milestone is null) return;
        if (string.IsNullOrWhiteSpace(section.Milestone) || !config.Milestones.ContainsKey(section.Milestone))
            AddError(issues, "missing_overview_milestone",
                $"{path}.milestone", $"Milestone {FormatValue(section.Milestone)} was not found.");
    }

    private static void ValidateTasks(
        List<OverviewConfigurationIssue> issues,
        OverviewSectionDefinition section,
        string path,
        ProjectConfig config)
    {
        if (section.Limit is < MinimumTaskLimit or > MaximumTaskLimit)
            AddError(issues, "invalid_overview_task_limit",
                $"{path}.limit", $"Task limit must be from {MinimumTaskLimit} through {MaximumTaskLimit}.");

        if (section.Filter is null) return;
        var parsed = TaskSearchQueryParser.Parse(section.Filter);
        if (!parsed.Success)
        {
            AddError(issues, "invalid_overview_task_query",
                $"{path}.filter", parsed.Message ?? "Task filter is invalid.");
            return;
        }

        var query = parsed.Payload!;
        if (query.HasScopePredicate && query.Scope == TaskSearchScope.Selection)
            AddError(issues, "invalid_overview_task_scope",
                $"{path}.filter", "in:selection is not available because Overview has no board selection context.");

        foreach (var state in query.States.Distinct(StringComparer.Ordinal))
            if (!config.TaskStates.ContainsKey(state))
                AddError(issues, "unknown_overview_task_state",
                    $"{path}.filter", $"Task state {state} was not found.");
        foreach (var track in query.Tracks.Distinct(StringComparer.Ordinal))
            if (!config.Tracks.ContainsKey(track))
                AddError(issues, "unknown_overview_task_track",
                    $"{path}.filter", $"Task track {track} was not found.");
        foreach (var milestone in query.Milestones.Distinct(StringComparer.Ordinal))
            if (!config.Milestones.ContainsKey(milestone))
                AddError(issues, "unknown_overview_task_milestone",
                    $"{path}.filter", $"Task milestone {milestone} was not found.");

    }

    private void ValidateWiki(
        List<OverviewConfigurationIssue> issues,
        OverviewSectionDefinition section,
        string path)
    {
        if (section.Pages is null) return;
        if (section.Pages.Count == 0)
        {
            AddError(issues, "invalid_overview_wiki_pages",
                $"{path}.pages", "Explicitly configured wiki pages must not be empty.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < section.Pages.Count; index++)
        {
            var pagePath = section.Pages[index];
            var itemPath = $"{path}.pages[{index}]";
            if (!TryResolveExactWikiPath(pagePath, out var normalizedPath, out var filePath))
            {
                AddError(issues, "invalid_overview_wiki_path",
                    itemPath, $"{FormatValue(pagePath)} is not a normalized local wiki path.");
                continue;
            }

            if (!seen.Add(normalizedPath))
                AddError(issues, "duplicate_overview_wiki_page",
                    itemPath, $"Wiki page {normalizedPath} is duplicated in this section.");
            ValidateWikiFile(issues, normalizedPath, filePath, itemPath, "missing_overview_wiki_page");
        }
    }

    private void ValidateMarkdown(
        List<OverviewConfigurationIssue> issues,
        OverviewSectionDefinition section,
        string path)
    {
        const string prefix = "wiki:";
        if (section.Source is null || !section.Source.StartsWith(prefix, StringComparison.Ordinal))
        {
            AddError(issues, "invalid_overview_markdown_source",
                $"{path}.source", "Markdown source must use wiki:<normalized-local-path>.");
            return;
        }

        var pagePath = section.Source[prefix.Length..];
        if (!TryResolveExactWikiPath(pagePath, out var normalizedPath, out var filePath))
        {
            AddError(issues, "invalid_overview_markdown_source",
                $"{path}.source", "Markdown source must use wiki:<normalized-local-path>.");
            return;
        }

        ValidateWikiFile(issues, normalizedPath, filePath, $"{path}.source",
            "missing_overview_markdown_source");
    }

    private bool TryResolveExactWikiPath(
        string? value,
        out string normalizedPath,
        out string filePath)
    {
        normalizedPath = string.Empty;
        filePath = string.Empty;
        return value is not null &&
               projectRoot.TryResolveWikiPath(value, out var resolvedPath, out var resolvedFile) &&
               string.Equals(value, resolvedPath, StringComparison.Ordinal) &&
               Assign(resolvedPath, resolvedFile, out normalizedPath, out filePath);
    }

    private static bool Assign(
        string resolvedPath,
        string resolvedFile,
        out string normalizedPath,
        out string filePath)
    {
        normalizedPath = resolvedPath;
        filePath = resolvedFile;
        return true;
    }

    private static void ValidateWikiFile(
        List<OverviewConfigurationIssue> issues,
        string normalizedPath,
        string filePath,
        string path,
        string missingCode)
    {
        if (!File.Exists(filePath))
        {
            AddError(issues, missingCode, path, $"Wiki page {normalizedPath} was not found.");
            return;
        }

        if (WikiPage.Parse(normalizedPath, File.ReadAllText(filePath)) is null)
            AddError(issues, "invalid_overview_wiki_page",
                path, $"Wiki page {normalizedPath} contains invalid Markdown metadata.");
    }

    private static void ValidateHero(
        List<OverviewConfigurationIssue> issues,
        IReadOnlyList<(string Path, IReadOnlyList<OverviewSectionDefinition> Sections)> regions,
        string requiredRegion)
    {
        var heroes = Find(regions, OverviewSectionKinds.Hero);
        if (heroes.Count == 0)
        {
            AddError(issues, "missing_overview_hero",
                $"{requiredRegion}[0]", "Overview composition must begin with exactly one hero section.");
            return;
        }

        if (heroes.Count > 1)
            AddError(issues, "duplicate_overview_hero",
                heroes[1].Path, "Overview composition contains more than one hero section.");
        if (!string.Equals(heroes[0].Region, requiredRegion, StringComparison.Ordinal) || heroes[0].Index != 0)
            AddError(issues, "misplaced_overview_hero",
                heroes[0].Path, $"Hero must be the first section in {requiredRegion}.");
    }

    private static void ValidateCopyright(
        List<OverviewConfigurationIssue> issues,
        IReadOnlyList<(string Path, IReadOnlyList<OverviewSectionDefinition> Sections)> regions,
        string layout)
    {
        var sections = Find(regions, OverviewSectionKinds.Copyright);
        if (sections.Count == 0) return;
        if (sections.Count > 1)
            AddError(issues, "duplicate_overview_copyright",
                sections[1].Path, "Overview composition contains more than one copyright section.");

        var first = sections[0];
        var expectedRegion = layout == OverviewLayouts.Single ? "site.home.sections" : "site.home.after";
        var region = regions.FirstOrDefault(item => string.Equals(item.Path, first.Region, StringComparison.Ordinal));
        if (!string.Equals(first.Region, expectedRegion, StringComparison.Ordinal) ||
            first.Index != region.Sections.Count - 1)
            AddError(issues, "misplaced_overview_copyright",
                first.Path, $"Copyright must be the final section in {expectedRegion}.");
    }

    private static List<(string Region, int Index, string Path)> Find(
        IReadOnlyList<(string Path, IReadOnlyList<OverviewSectionDefinition> Sections)> regions,
        string type)
    {
        var found = new List<(string Region, int Index, string Path)>();
        foreach (var region in regions)
        for (var index = 0; index < region.Sections.Count; index++)
            if (string.Equals(region.Sections[index]?.Type, type, StringComparison.Ordinal))
                found.Add((region.Path, index, $"{region.Path}[{index}]"));
        return found;
    }

    private static void ValidateAllowedFields(
        List<OverviewConfigurationIssue> issues,
        OverviewSectionDefinition section,
        string path,
        IReadOnlyList<string> allowed)
    {
        var present = new List<string>();
        if (section.Title is not null && !allowed.Contains("title")) present.Add("title");
        if (section.Milestone is not null && !allowed.Contains("milestone")) present.Add("milestone");
        if (section.Filter is not null && !allowed.Contains("filter")) present.Add("filter");
        if (section.Limit is not null && !allowed.Contains("limit")) present.Add("limit");
        if (section.Pages is not null && !allowed.Contains("pages")) present.Add("pages");
        if (section.Source is not null && !allowed.Contains("source")) present.Add("source");
        if (section.Notice is not null && !allowed.Contains("notice")) present.Add("notice");
        if (present.Count == 0) return;

        AddError(issues, "invalid_overview_section_fields",
            path, $"Section type {section.Type} does not support {string.Join(", ", present)}.");
    }

    private static void ValidateOptionalText(
        List<OverviewConfigurationIssue> issues,
        string? value,
        string path,
        string code,
        string message)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            AddError(issues, code, path, message);
    }

    private static string FormatValue(string? value) =>
        string.IsNullOrEmpty(value) ? "<empty>" : value;

    private static void AddError(
        List<OverviewConfigurationIssue> issues,
        string code,
        string path,
        string message) =>
        issues.Add(new OverviewConfigurationIssue(code, message, path));
}
