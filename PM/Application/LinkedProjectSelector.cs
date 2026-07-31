using PM.Project;

namespace PM.Application;

public static class LinkedProjectSelector
{
    public static AppResult<string> ResolveProjectId(LinkedProjectManifest manifest, string selector)
        => ResolveProjectId(null, manifest, selector);

    public static AppResult<string> ResolveProjectId(
        string? activeProjectId,
        LinkedProjectManifest manifest,
        string selector)
    {
        var normalized = selector?.Trim() ?? string.Empty;
        if (string.Equals(normalized, "current", StringComparison.OrdinalIgnoreCase))
            return ProjectIdentifiers.IsValid(activeProjectId)
                ? AppResult<string>.Ok(activeProjectId!)
                : AppResult<string>.Fail(
                    "missing_project_id", "The active project has no valid stable project ID.");

        if (string.Equals(normalized, "parent", StringComparison.OrdinalIgnoreCase))
            return manifest.Parent == null
                ? AppResult<string>.Fail("missing_linked_project_parent", "This project has no declared parent.")
                : AppResult<string>.Ok(manifest.Parent.ProjectId);

        var declarations = EnumerateDeclarations(manifest).ToList();
        var byId = declarations.Where(declaration =>
            string.Equals(declaration.ProjectId, normalized, StringComparison.Ordinal));
        if (byId.Count() == 1)
            return AppResult<string>.Ok(byId.Single().ProjectId);

        var byAlias = declarations.Where(declaration =>
                string.Equals(declaration.Alias, normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byAlias.Count == 1)
            return AppResult<string>.Ok(byAlias[0].ProjectId);
        if (byAlias.Count > 1)
            return AppResult<string>.Fail(
                "ambiguous_linked_project",
                $"Selector {normalized} matches multiple projects: {FormatCandidates(byAlias)}.");

        return ProjectIdentifiers.IsValid(normalized)
            ? AppResult<string>.Ok(normalized)
            : AppResult<string>.Fail(
                "unknown_linked_project",
                declarations.Count == 0
                    ? "Use current, parent, a declared alias, or a stable project ID."
                    : $"Use current, parent, a declared alias, or a stable project ID. Candidates: {FormatCandidates(declarations)}.");
    }

    public static IEnumerable<(string Relationship, LinkedProjectDeclaration Declaration)> Enumerate(
        LinkedProjectManifest manifest)
    {
        if (manifest.Parent != null)
            yield return ("parent", manifest.Parent);
        foreach (var child in manifest.Children)
            yield return ("child", child);
    }

    private static IEnumerable<LinkedProjectDeclaration> EnumerateDeclarations(LinkedProjectManifest manifest) =>
        Enumerate(manifest).Select(item => item.Declaration);

    private static string FormatCandidates(IEnumerable<LinkedProjectDeclaration> declarations) =>
        string.Join(", ", declarations.Select(declaration => $"{declaration.Alias} ({declaration.ProjectId})"));
}
