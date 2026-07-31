using PM.Project;

namespace PM.Application;

public enum LinkedProjectRelationship
{
    Current,
    Parent,
    Child,
    Sibling,
}

public sealed record LinkedProjectFamilyWarning(
    string Code,
    string Message,
    string DeclaringProjectId,
    string TargetProjectId,
    string? Alias,
    LinkedProjectResolutionStatus Status,
    LinkedProjectRepairAction? RepairAction = null);

public sealed record LinkedProjectFamilyMember(
    string ProjectId,
    string Name,
    string? Alias,
    LinkedProjectRelationship Relationship,
    LinkedProjectResolutionStatus Status,
    LinkedProjectResolutionSource Source,
    bool Readable,
    bool WriteTrusted,
    ProjectRoot? Project,
    string? RepositoryPath,
    LinkedProjectRepairAction? RepairAction = null,
    string? PublicSiteUrl = null);

public sealed record LinkedProjectFamily(
    string ActiveProjectId,
    IReadOnlyList<LinkedProjectFamilyMember> Members,
    IReadOnlyList<LinkedProjectFamilyWarning> Warnings);

public sealed class LinkedProjectFamilyService(
    ProjectRoot activeProject,
    LinkedProjectService linkedProjects,
    LinkedProjectResolver resolver)
{
    public const int MaximumProjectCount = 32;
    public const int MaximumWarningCount = 64;

    public static AppResult<LinkedProjectFamilyMember> SelectMember(
        LinkedProjectFamily family,
        string selector)
    {
        selector = selector.Trim();
        if (string.Equals(selector, "current", StringComparison.OrdinalIgnoreCase))
            return AppResult<LinkedProjectFamilyMember>.Ok(
                family.Members.Single(member => member.Relationship == LinkedProjectRelationship.Current));
        if (string.Equals(selector, "parent", StringComparison.OrdinalIgnoreCase))
        {
            var parent = family.Members.SingleOrDefault(member =>
                member.Relationship == LinkedProjectRelationship.Parent);
            return parent == null
                ? AppResult<LinkedProjectFamilyMember>.Fail(
                    "unknown_linked_project", "This project has no linked parent.")
                : AppResult<LinkedProjectFamilyMember>.Ok(parent);
        }

        var byId = family.Members.SingleOrDefault(member =>
            string.Equals(member.ProjectId, selector, StringComparison.Ordinal));
        if (byId != null) return AppResult<LinkedProjectFamilyMember>.Ok(byId);

        var byAlias = family.Members
            .Where(member => member.Alias != null &&
                             string.Equals(member.Alias, selector, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byAlias.Count == 1) return AppResult<LinkedProjectFamilyMember>.Ok(byAlias[0]);
        if (byAlias.Count > 1)
            return AppResult<LinkedProjectFamilyMember>.Fail(
                "ambiguous_linked_project",
                $"Linked-project selector {selector} is ambiguous; use a stable project ID.");

        var candidates = string.Join(", ", family.Members.Take(12).Select(member =>
            member.Alias == null ? member.ProjectId : $"{member.Alias} ({member.ProjectId})"));
        return AppResult<LinkedProjectFamilyMember>.Fail(
            "unknown_linked_project",
            $"Linked project {selector} was not found. Available projects: {candidates}.");
    }

    public static LinkedProjectFamilyService CreateDefault(ProjectRoot projectRoot)
    {
        var linkedProjects = new LinkedProjectService(projectRoot);
        var resolver = new LinkedProjectResolver(
            new LinkedProjectRegistryStore(), new GitLinkedProjectSubmoduleInspector());
        return new LinkedProjectFamilyService(projectRoot, linkedProjects, resolver);
    }

    public async Task<AppResult<LinkedProjectFamily>> ResolveAsync(
        CancellationToken cancellationToken = default)
    {
        if (!activeProject.Exists || activeProject.Config == null)
            return AppResult<LinkedProjectFamily>.Fail(
                "missing_project", "Project not found. Run pm init first.");
        if (!activeProject.TryReadProjectId(out var activeProjectId))
            return AppResult<LinkedProjectFamily>.Fail(
                "missing_project_id", "The active project has no valid stable project ID.");

        var manifestResult = linkedProjects.GetManifest();
        if (!manifestResult.Success)
            return AppResult<LinkedProjectFamily>.Fail(manifestResult.ErrorCode!, manifestResult.Message!);

        var members = new List<LinkedProjectFamilyMember>
        {
            new(activeProjectId, activeProject.Config.Name, "current", LinkedProjectRelationship.Current,
                LinkedProjectResolutionStatus.Available, LinkedProjectResolutionSource.ActiveProject,
                true, true, activeProject, activeProject.RepositoryPath),
        };
        var warnings = new WarningCollector();
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["current"] = activeProjectId,
        };
        var roots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [activeProjectId] = NormalizePath(activeProject.RepositoryPath),
        };

        var manifest = manifestResult.Payload!.Manifest;
        if (!manifestResult.Payload.Exists)
            return Success(activeProjectId, members, warnings);

        if (manifest.Parent == null)
        {
            foreach (var child in manifest.Children)
            {
                if (!CanAddMember(members, warnings, activeProjectId, child)) break;
                var member = await ResolveMember(
                    activeProject, activeProjectId, child, LinkedProjectRelationship.Child, warnings,
                    cancellationToken);
                AddMember(member, members, warnings, aliases, roots, activeProjectId);
                if (member.Readable && !ValidateChildBackReference(member, activeProjectId, warnings))
                    ReplaceStatus(members, member.ProjectId, LinkedProjectResolutionStatus.Invalid);
            }

            return Success(activeProjectId, members, warnings);
        }

        if (!CanAddMember(members, warnings, activeProjectId, manifest.Parent))
            return Success(activeProjectId, members, warnings);

        var parent = await ResolveMember(
            activeProject, activeProjectId, manifest.Parent, LinkedProjectRelationship.Parent, warnings,
            cancellationToken);
        AddMember(parent, members, warnings, aliases, roots, activeProjectId);
        if (!parent.Readable || parent.Project == null)
            return Success(activeProjectId, members, warnings);

        var parentManifestResult = new LinkedProjectService(parent.Project).GetManifest();
        if (!parentManifestResult.Success)
        {
            ReplaceStatus(members, parent.ProjectId, LinkedProjectResolutionStatus.Invalid);
            warnings.Add(new LinkedProjectFamilyWarning(
                parentManifestResult.ErrorCode ?? "invalid_linked_projects_manifest",
                parentManifestResult.Message ?? "The linked parent manifest is invalid.",
                parent.ProjectId, parent.ProjectId, parent.Alias,
                LinkedProjectResolutionStatus.Invalid));
            return Success(activeProjectId, members, warnings);
        }

        var parentManifest = parentManifestResult.Payload!.Manifest;
        if (parentManifest.Parent != null)
        {
            var parentPointsToActive = string.Equals(
                parentManifest.Parent.ProjectId, activeProjectId, StringComparison.Ordinal);
            warnings.Add(new LinkedProjectFamilyWarning(
                parentPointsToActive ? "linked_project_cycle" : "linked_project_depth_exceeded",
                parentPointsToActive
                    ? $"Linked parent {parent.ProjectId} points back to active project {activeProjectId}."
                    : $"Linked parent {parent.ProjectId} declares another parent outside the supported family depth.",
                parent.ProjectId, parentManifest.Parent.ProjectId, parentManifest.Parent.Alias,
                LinkedProjectResolutionStatus.Invalid));
        }

        var reciprocal = parentManifest.Children.FirstOrDefault(child =>
            string.Equals(child.ProjectId, activeProjectId, StringComparison.Ordinal));
        if (reciprocal == null)
            warnings.Add(NonReciprocal(parent.ProjectId, activeProjectId, manifest.Parent.Alias));

        foreach (var sibling in parentManifest.Children)
        {
            if (string.Equals(sibling.ProjectId, activeProjectId, StringComparison.Ordinal)) continue;
            if (!CanAddMember(members, warnings, parent.ProjectId, sibling)) break;
            var member = await ResolveMember(
                parent.Project, parent.ProjectId, sibling, LinkedProjectRelationship.Sibling, warnings,
                cancellationToken);
            AddMember(member, members, warnings, aliases, roots, parent.ProjectId);
            if (member.Readable && !ValidateChildBackReference(member, parent.ProjectId, warnings))
                ReplaceStatus(members, member.ProjectId, LinkedProjectResolutionStatus.Invalid);
        }

        return Success(activeProjectId, members, warnings);
    }

    private async Task<LinkedProjectFamilyMember> ResolveMember(
        ProjectRoot declaringProject,
        string declaringProjectId,
        LinkedProjectDeclaration declaration,
        LinkedProjectRelationship relationship,
        WarningCollector warnings,
        CancellationToken cancellationToken)
    {
        var resolution = await resolver.ResolveAsync(declaringProject, declaration, cancellationToken);
        var readable = resolution.Project != null;
        var status = readable && !resolution.WriteTrusted
            ? LinkedProjectResolutionStatus.UntrustedForWrite
            : resolution.Status;
        var name = resolution.Project?.Config?.Name ?? declaration.Alias;
        foreach (var diagnostic in resolution.Diagnostics)
            warnings.Add(new LinkedProjectFamilyWarning(
                diagnostic.Code, diagnostic.Message, declaringProjectId, declaration.ProjectId,
                declaration.Alias, status));
        return new LinkedProjectFamilyMember(
            declaration.ProjectId, name, declaration.Alias, relationship, status, resolution.Source,
            readable, resolution.WriteTrusted, resolution.Project, resolution.RepositoryPath,
            resolution.RepairAction, declaration.PublicSiteUrl);
    }

    private static void AddMember(
        LinkedProjectFamilyMember member,
        List<LinkedProjectFamilyMember> members,
        WarningCollector warnings,
        Dictionary<string, string> aliases,
        Dictionary<string, string> roots,
        string declaringProjectId)
    {
        var existing = members.FirstOrDefault(item =>
            string.Equals(item.ProjectId, member.ProjectId, StringComparison.Ordinal));
        if (existing != null)
        {
            var code = existing.RepositoryPath != null && member.RepositoryPath != null &&
                       !PathsEqual(existing.RepositoryPath, member.RepositoryPath)
                ? "linked_project_root_conflict"
                : "linked_project_cycle";
            warnings.Add(new LinkedProjectFamilyWarning(
                code,
                code == "linked_project_cycle"
                    ? $"Linked project {member.ProjectId} was encountered more than once in the family topology."
                    : $"Linked project {member.ProjectId} resolved to conflicting repository roots.",
                declaringProjectId, member.ProjectId, member.Alias, LinkedProjectResolutionStatus.Invalid));
            return;
        }

        if (member.Alias != null && aliases.TryGetValue(member.Alias, out var aliasOwner) &&
            !string.Equals(aliasOwner, member.ProjectId, StringComparison.Ordinal))
            warnings.Add(new LinkedProjectFamilyWarning(
                "duplicate_linked_project_alias",
                $"Alias {member.Alias} identifies more than one project in this family.",
                declaringProjectId, member.ProjectId, member.Alias, LinkedProjectResolutionStatus.Invalid));
        else if (member.Alias != null)
            aliases[member.Alias] = member.ProjectId;

        if (member.RepositoryPath != null)
        {
            var normalizedRoot = NormalizePath(member.RepositoryPath);
            if (roots.TryGetValue(member.ProjectId, out var existingRoot) &&
                !PathsEqual(existingRoot, normalizedRoot))
                warnings.Add(new LinkedProjectFamilyWarning(
                    "linked_project_root_conflict",
                    $"Linked project {member.ProjectId} resolved to conflicting repository roots.",
                    declaringProjectId, member.ProjectId, member.Alias, LinkedProjectResolutionStatus.Invalid));
            else
                roots[member.ProjectId] = normalizedRoot;
        }

        members.Add(member);
        if (!member.Readable)
            warnings.Add(new LinkedProjectFamilyWarning(
                StatusCode(member.Status),
                $"Linked project {member.ProjectId} ({member.Alias}) is {Format(member.Status)}.",
                declaringProjectId, member.ProjectId, member.Alias, member.Status,
                member.RepairAction));
    }

    private static bool ValidateChildBackReference(
        LinkedProjectFamilyMember child,
        string expectedParentId,
        WarningCollector warnings)
    {
        if (child.Project == null) return false;
        var manifest = new LinkedProjectService(child.Project).GetManifest();
        if (!manifest.Success)
        {
            warnings.Add(new LinkedProjectFamilyWarning(
                manifest.ErrorCode ?? "invalid_linked_projects_manifest",
                manifest.Message ?? $"Linked project {child.ProjectId} has an invalid manifest.",
                child.ProjectId, child.ProjectId, child.Alias, LinkedProjectResolutionStatus.Invalid));
            return false;
        }

        var declaredParent = manifest.Payload!.Manifest.Parent;
        if (declaredParent == null ||
            !string.Equals(declaredParent.ProjectId, expectedParentId, StringComparison.Ordinal))
        {
            warnings.Add(NonReciprocal(child.ProjectId, expectedParentId, child.Alias));
            return false;
        }

        return true;
    }

    private static LinkedProjectFamilyWarning NonReciprocal(
        string declaringProjectId, string targetProjectId, string? alias) =>
        new("non_reciprocal_linked_project",
            $"Linked project {declaringProjectId} does not reciprocally declare {targetProjectId}.",
            declaringProjectId, targetProjectId, alias, LinkedProjectResolutionStatus.Invalid);

    private static bool CanAddMember(
        IReadOnlyCollection<LinkedProjectFamilyMember> members,
        WarningCollector warnings,
        string declaringProjectId,
        LinkedProjectDeclaration declaration)
    {
        if (members.Count < MaximumProjectCount) return true;
        warnings.Add(new LinkedProjectFamilyWarning(
            "linked_project_count_exceeded",
            $"The linked family exceeds the {MaximumProjectCount}-project traversal limit; remaining declarations were ignored.",
            declaringProjectId, declaration.ProjectId, declaration.Alias, LinkedProjectResolutionStatus.Invalid));
        return false;
    }

    private static void ReplaceStatus(
        List<LinkedProjectFamilyMember> members,
        string projectId,
        LinkedProjectResolutionStatus status)
    {
        var index = members.FindIndex(member => string.Equals(member.ProjectId, projectId, StringComparison.Ordinal));
        if (index >= 0) members[index] = members[index] with { Status = status };
    }

    private static AppResult<LinkedProjectFamily> Success(
        string activeProjectId,
        List<LinkedProjectFamilyMember> members,
        WarningCollector warnings) =>
        AppResult<LinkedProjectFamily>.Ok(new LinkedProjectFamily(activeProjectId, members, warnings.Items));

    private static string StatusCode(LinkedProjectResolutionStatus status) => status switch
    {
        LinkedProjectResolutionStatus.Unregistered => "linked_project_unregistered",
        LinkedProjectResolutionStatus.Missing => "linked_project_missing",
        LinkedProjectResolutionStatus.UninitializedSubmodule => "linked_project_uninitialized_submodule",
        LinkedProjectResolutionStatus.IdentityMismatch => "linked_project_identity_mismatch",
        _ => "linked_project_invalid",
    };

    public static string Format<T>(T value) where T : Enum =>
        string.Concat(value.ToString().Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $"-{char.ToLowerInvariant(character)}" :
            char.ToLowerInvariant(character).ToString()));

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool PathsEqual(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed class WarningCollector
    {
        private readonly List<LinkedProjectFamilyWarning> items = [];
        private bool truncated;

        public IReadOnlyList<LinkedProjectFamilyWarning> Items => items;

        public void Add(LinkedProjectFamilyWarning warning)
        {
            if (items.Count < MaximumWarningCount - 1)
            {
                items.Add(warning);
                return;
            }

            if (truncated) return;
            truncated = true;
            items.Add(new LinkedProjectFamilyWarning(
                "linked_project_warnings_truncated",
                $"Additional linked-project warnings were omitted after {MaximumWarningCount - 1} entries.",
                warning.DeclaringProjectId, warning.TargetProjectId, warning.Alias,
                LinkedProjectResolutionStatus.Invalid));
        }
    }
}
