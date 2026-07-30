using PM.Project;

namespace PM.Application;

public static class LinkedProjectSelector
{
    public static AppResult<string> ResolveProjectId(LinkedProjectManifest manifest, string selector)
    {
        var normalized = selector?.Trim() ?? string.Empty;
        if (string.Equals(normalized, "parent", StringComparison.OrdinalIgnoreCase))
            return manifest.Parent == null
                ? AppResult<string>.Fail("missing_linked_project_parent", "This project has no declared parent.")
                : AppResult<string>.Ok(manifest.Parent.ProjectId);

        var declarations = EnumerateDeclarations(manifest).ToList();
        var byId = declarations.FirstOrDefault(declaration =>
            string.Equals(declaration.ProjectId, normalized, StringComparison.Ordinal));
        if (byId != null)
            return AppResult<string>.Ok(byId.ProjectId);

        var byAlias = declarations.FirstOrDefault(declaration =>
            string.Equals(declaration.Alias, normalized, StringComparison.OrdinalIgnoreCase));
        if (byAlias != null)
            return AppResult<string>.Ok(byAlias.ProjectId);

        return ProjectIdentifiers.IsValid(normalized)
            ? AppResult<string>.Ok(normalized)
            : AppResult<string>.Fail(
                "unknown_linked_project", "Use parent, a declared alias, or a stable project ID.");
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
}
