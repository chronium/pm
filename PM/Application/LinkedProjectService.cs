using System.Text.RegularExpressions;
using PM.Project;

namespace PM.Application;

public sealed record LinkedProjectManifestState(bool Exists, LinkedProjectManifest Manifest);

public sealed class LinkedProjectService(ProjectRoot projectRoot)
{
    private const int MaximumAliasLength = 64;
    private const int MaximumUrlLength = 2048;
    private const int MaximumPathLength = 1024;

    private static readonly Regex AliasPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant);

    private static readonly Regex ScpRepositoryPattern = new(
        "^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+:[^\\s\\\\]+$", RegexOptions.CultureInvariant);

    public AppResult<LinkedProjectManifestState> GetManifest()
    {
        if (!projectRoot.Exists)
            return AppResult<LinkedProjectManifestState>.Fail(
                "missing_project", "Project not found. Run pm init first.");

        if (!File.Exists(projectRoot.LinkedProjectsPath))
            return AppResult<LinkedProjectManifestState>.Ok(
                new LinkedProjectManifestState(false, new LinkedProjectManifest()));

        var projectId = ReadProjectId();
        if (!projectId.Success)
            return AppResult<LinkedProjectManifestState>.Fail(projectId.ErrorCode!, projectId.Message!);

        LinkedProjectManifest? manifest;
        try
        {
            manifest = projectRoot.ReadLinkedProjectsManifest();
        }
        catch
        {
            return AppResult<LinkedProjectManifestState>.Fail(
                "invalid_linked_projects_manifest", "Linked-project manifest is not valid YAML.");
        }

        if (manifest == null)
            return AppResult<LinkedProjectManifestState>.Fail(
                "invalid_linked_projects_manifest", "Linked-project manifest is empty.");

        var validation = Validate(manifest, projectId.Payload!);
        return validation.Success
            ? AppResult<LinkedProjectManifestState>.Ok(new LinkedProjectManifestState(true, manifest))
            : AppResult<LinkedProjectManifestState>.Fail(validation.ErrorCode!, validation.Message!);
    }

    public AppResult<LinkedProjectManifestState> SetParent(LinkedProjectDeclaration declaration)
    {
        var current = ReadForMutation();
        if (!current.Success)
            return Failure(current);

        var manifest = Clone(current.Payload!.Manifest) with { Parent = Normalize(declaration) };
        return ValidateAndWrite(manifest, current.Payload.ProjectId);
    }

    public AppResult<LinkedProjectManifestState> RemoveParent()
    {
        var current = ReadForMutation();
        if (!current.Success)
            return Failure(current);
        if (current.Payload!.Manifest.Parent == null)
            return AppResult<LinkedProjectManifestState>.Fail(
                "missing_linked_project_parent", "Linked-project manifest has no parent declaration.");

        var manifest = Clone(current.Payload.Manifest) with { Parent = null };
        return ValidateAndWrite(manifest, current.Payload.ProjectId);
    }

    public AppResult<LinkedProjectManifestState> AddChild(LinkedProjectDeclaration declaration)
    {
        var current = ReadForMutation();
        if (!current.Success)
            return Failure(current);

        var normalized = Normalize(declaration);
        if (current.Payload!.Manifest.Children.Any(child =>
                string.Equals(child.ProjectId, normalized.ProjectId, StringComparison.Ordinal)))
            return AppResult<LinkedProjectManifestState>.Fail(
                "duplicate_linked_project", $"Linked child {normalized.ProjectId} already exists.");

        var manifest = Clone(current.Payload.Manifest);
        manifest.Children.Add(normalized);
        return ValidateAndWrite(manifest, current.Payload.ProjectId);
    }

    public AppResult<LinkedProjectManifestState> UpdateChild(
        string projectId, LinkedProjectDeclaration declaration)
    {
        var current = ReadForMutation();
        if (!current.Success)
            return Failure(current);

        var normalizedProjectId = projectId.Trim();
        var normalized = Normalize(declaration);
        if (!string.Equals(normalizedProjectId, normalized.ProjectId, StringComparison.Ordinal))
            return AppResult<LinkedProjectManifestState>.Fail(
                "linked_project_identity_change",
                "A linked child's stable project ID cannot be changed; remove and add the declaration instead.");

        var manifest = Clone(current.Payload!.Manifest);
        var index = manifest.Children.FindIndex(child =>
            string.Equals(child.ProjectId, normalizedProjectId, StringComparison.Ordinal));
        if (index < 0)
            return AppResult<LinkedProjectManifestState>.Fail(
                "missing_linked_project", $"Linked child {normalizedProjectId} was not found.");

        manifest.Children[index] = normalized;
        return ValidateAndWrite(manifest, current.Payload.ProjectId);
    }

    public AppResult<LinkedProjectManifestState> RemoveChild(string projectId)
    {
        var current = ReadForMutation();
        if (!current.Success)
            return Failure(current);

        var normalizedProjectId = projectId.Trim();
        var manifest = Clone(current.Payload!.Manifest);
        var removed = manifest.Children.RemoveAll(child =>
            string.Equals(child.ProjectId, normalizedProjectId, StringComparison.Ordinal));
        if (removed == 0)
            return AppResult<LinkedProjectManifestState>.Fail(
                "missing_linked_project", $"Linked child {normalizedProjectId} was not found.");

        return ValidateAndWrite(manifest, current.Payload.ProjectId);
    }

    public AppResult<LinkedProjectManifestState> ReorderChildren(IReadOnlyList<string> projectIds)
    {
        var current = ReadForMutation();
        if (!current.Success)
            return Failure(current);

        var normalized = projectIds.Select(projectId => projectId.Trim()).ToList();
        if (normalized.Count != normalized.Distinct(StringComparer.Ordinal).Count())
            return AppResult<LinkedProjectManifestState>.Fail(
                "invalid_linked_project_order", "Linked child order contains duplicate project IDs.");

        var children = current.Payload!.Manifest.Children.ToDictionary(
            child => child.ProjectId, StringComparer.Ordinal);
        if (normalized.Count != children.Count || normalized.Any(projectId => !children.ContainsKey(projectId)))
            return AppResult<LinkedProjectManifestState>.Fail(
                "invalid_linked_project_order", "Linked child order must contain every current child exactly once.");

        var manifest = Clone(current.Payload.Manifest) with
        {
            Children = normalized.Select(projectId => children[projectId]).ToList(),
        };
        return ValidateAndWrite(manifest, current.Payload.ProjectId);
    }

    private AppResult<MutationContext> ReadForMutation()
    {
        if (!projectRoot.Exists)
            return AppResult<MutationContext>.Fail("missing_project", "Project not found. Run pm init first.");

        var projectId = ReadProjectId();
        if (!projectId.Success)
            return AppResult<MutationContext>.Fail(projectId.ErrorCode!, projectId.Message!);

        var current = GetManifest();
        if (!current.Success)
            return AppResult<MutationContext>.Fail(current.ErrorCode!, current.Message!);

        return AppResult<MutationContext>.Ok(
            new MutationContext(projectId.Payload!, current.Payload!.Manifest));
    }

    private AppResult<LinkedProjectManifestState> ValidateAndWrite(
        LinkedProjectManifest manifest, string projectId)
    {
        var validation = Validate(manifest, projectId);
        if (!validation.Success)
            return AppResult<LinkedProjectManifestState>.Fail(validation.ErrorCode!, validation.Message!);

        if (manifest.Parent == null && manifest.Children.Count == 0)
        {
            projectRoot.DeleteLinkedProjectsManifest();
            return AppResult<LinkedProjectManifestState>.Ok(
                new LinkedProjectManifestState(false, new LinkedProjectManifest()));
        }

        projectRoot.WriteLinkedProjectsManifest(manifest);
        return AppResult<LinkedProjectManifestState>.Ok(new LinkedProjectManifestState(true, manifest));
    }

    private static AppResult Validate(LinkedProjectManifest manifest, string declaringProjectId)
    {
        if (manifest.Version != LinkedProjectManifest.CurrentVersion)
            return AppResult.Fail(
                "unsupported_linked_projects_version",
                $"Linked-project manifest version must be {LinkedProjectManifest.CurrentVersion}.");
        if (manifest.Children == null || manifest.Children.Any(child => child == null))
            return AppResult.Fail(
                "invalid_linked_projects_manifest", "Linked-project children must be a sequence of declarations.");

        var projectIds = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in EnumerateDeclarations(manifest))
        {
            var declarationValidation = ValidateDeclaration(declaration);
            if (!declarationValidation.Success)
                return declarationValidation;
            if (string.Equals(declaration.ProjectId, declaringProjectId, StringComparison.Ordinal))
                return AppResult.Fail(
                    "linked_project_self_reference", "A project cannot declare itself as a linked project.");
            if (!projectIds.Add(declaration.ProjectId))
                return AppResult.Fail(
                    "duplicate_linked_project_id",
                    $"Linked project ID {declaration.ProjectId} is declared more than once.");
            if (!aliases.Add(declaration.Alias))
                return AppResult.Fail(
                    "duplicate_linked_project_alias",
                    $"Linked project alias {declaration.Alias} is declared more than once.");
        }

        return AppResult.Ok();
    }

    private static AppResult ValidateDeclaration(LinkedProjectDeclaration declaration)
    {
        if (!IsNormalized(declaration.ProjectId) || !ProjectIdentifiers.IsValid(declaration.ProjectId))
            return AppResult.Fail(
                "invalid_linked_project_id", "Linked project ID is not a valid stable project identifier.");
        if (!IsNormalized(declaration.Alias) || declaration.Alias.Length > MaximumAliasLength ||
            !AliasPattern.IsMatch(declaration.Alias))
            return AppResult.Fail(
                "invalid_linked_project_alias", "Linked project alias is not valid.");
        if (!IsRepositoryUrl(declaration.RepositoryUrl))
            return AppResult.Fail(
                "invalid_linked_project_repository", "Linked project repository URL is not valid portable metadata.");
        if (declaration.PathHint != null && !IsPathHint(declaration.PathHint))
            return AppResult.Fail(
                "invalid_linked_project_path", "Linked project path hint must be a normalized relative path.");
        if (declaration.PublicSiteUrl != null && !IsPublicSiteUrl(declaration.PublicSiteUrl))
            return AppResult.Fail(
                "invalid_linked_project_public_site", "Linked project public site URL must be an HTTP(S) URL.");

        return AppResult.Ok();
    }

    private AppResult<string> ReadProjectId()
    {
        var path = Path.Combine(projectRoot.RootPath, GlobalConfig.ProjectIdFile);
        if (!File.Exists(path))
            return AppResult<string>.Fail(
                "missing_project_id", "This project must have a stable project ID before declaring links.");

        var projectId = File.ReadAllText(path).Trim();
        if (!ProjectIdentifiers.IsValid(projectId))
            return AppResult<string>.Fail(
                "invalid_project_id", "This project's stable project ID is invalid.");

        return AppResult<string>.Ok(projectId);
    }

    private static bool IsRepositoryUrl(string value)
    {
        if (!IsNormalized(value) || value.Length > MaximumUrlLength ||
            value.Any(char.IsControl) || value.Any(char.IsWhiteSpace) || value.Contains('\\') ||
            value.StartsWith('/') || value.StartsWith("./", StringComparison.Ordinal) ||
            value.StartsWith("../", StringComparison.Ordinal) || value.StartsWith('~'))
            return false;

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is not ("http" or "https" or "ssh" or "git") || string.IsNullOrWhiteSpace(uri.Host))
                return false;
            return string.IsNullOrEmpty(uri.UserInfo) || !uri.UserInfo.Contains(':');
        }

        return ScpRepositoryPattern.IsMatch(value);
    }

    private static bool IsPublicSiteUrl(string value)
    {
        return IsNormalized(value) && value.Length <= MaximumUrlLength &&
               !value.Any(char.IsControl) && !value.Any(char.IsWhiteSpace) &&
               Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);
    }

    private static bool IsPathHint(string value)
    {
        if (!IsNormalized(value) || value.Length > MaximumPathLength || Path.IsPathRooted(value) ||
            value.Contains('\\') || value.Contains(':') || value.Contains('$') || value.Contains('%') ||
            value.StartsWith('~') || Regex.IsMatch(value, "^[A-Za-z]:", RegexOptions.CultureInvariant))
            return false;

        var segments = value.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment == "."))
            return false;

        var normalSegmentSeen = false;
        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                if (normalSegmentSeen)
                    return false;
            }
            else
            {
                normalSegmentSeen = true;
            }
        }

        return true;
    }

    private static bool IsNormalized(string value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static IEnumerable<LinkedProjectDeclaration> EnumerateDeclarations(LinkedProjectManifest manifest)
    {
        if (manifest.Parent != null)
            yield return manifest.Parent;
        foreach (var child in manifest.Children)
            yield return child;
    }

    private static LinkedProjectDeclaration Normalize(LinkedProjectDeclaration declaration) => declaration with
    {
        ProjectId = declaration.ProjectId?.Trim() ?? string.Empty,
        Alias = declaration.Alias?.Trim() ?? string.Empty,
        RepositoryUrl = declaration.RepositoryUrl?.Trim() ?? string.Empty,
        PathHint = NormalizeOptional(declaration.PathHint),
        PublicSiteUrl = NormalizeOptional(declaration.PublicSiteUrl),
    };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static LinkedProjectManifest Clone(LinkedProjectManifest manifest) => manifest with
    {
        Parent = manifest.Parent is null ? null : manifest.Parent with { },
        Children = manifest.Children.Select(child => child with { }).ToList(),
    };

    private static AppResult<LinkedProjectManifestState> Failure(AppResult<MutationContext> result) =>
        AppResult<LinkedProjectManifestState>.Fail(result.ErrorCode!, result.Message!);

    private sealed record MutationContext(string ProjectId, LinkedProjectManifest Manifest);
}
