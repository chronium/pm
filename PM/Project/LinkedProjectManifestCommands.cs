using System.ComponentModel;
using PM.Application;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.Project;

public sealed class ProjectSetParentCommand(LinkedProjectService linkedProjects)
    : Command<ProjectSetParentCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var validation = settings.Validate();
        if (!validation.Successful)
            return LinkedProjectManifestCommandOutput.Fail(validation.Message);

        var result = linkedProjects.SetParent(settings.ToDeclaration());
        if (!result.Success)
            return LinkedProjectManifestCommandOutput.Fail(result.Message);

        return LinkedProjectManifestCommandOutput.Changed("Set parent", settings.ProjectId);
    }

    public sealed class Settings : LinkedProjectDeclarationSettings;
}

public sealed class ProjectRemoveParentCommand(LinkedProjectService linkedProjects)
    : Command<ProjectRemoveParentCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = linkedProjects.RemoveParent();
        if (!result.Success)
            return LinkedProjectManifestCommandOutput.Fail(result.Message);

        AnsiConsole.MarkupLine("Removed linked-project parent declaration.");
        return 0;
    }

    public sealed class Settings : CommonSettings;
}

public sealed class ProjectAddChildCommand(LinkedProjectService linkedProjects)
    : Command<ProjectAddChildCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var validation = settings.Validate();
        if (!validation.Successful)
            return LinkedProjectManifestCommandOutput.Fail(validation.Message);

        var result = linkedProjects.AddChild(settings.ToDeclaration());
        if (!result.Success)
            return LinkedProjectManifestCommandOutput.Fail(result.Message);

        return LinkedProjectManifestCommandOutput.Changed("Added child", settings.ProjectId);
    }

    public sealed class Settings : LinkedProjectDeclarationSettings;
}

public sealed class ProjectUpdateChildCommand(LinkedProjectService linkedProjects)
    : Command<ProjectUpdateChildCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var validation = settings.Validate();
        if (!validation.Successful)
            return LinkedProjectManifestCommandOutput.Fail(validation.Message);

        var current = linkedProjects.GetManifest();
        if (!current.Success)
            return LinkedProjectManifestCommandOutput.Fail(current.Message);

        var projectId = settings.ProjectId.Trim();
        var existing = current.Payload!.Manifest.Children.FirstOrDefault(child =>
            string.Equals(child.ProjectId, projectId, StringComparison.Ordinal));
        if (existing == null)
            return LinkedProjectManifestCommandOutput.Fail($"Linked child {projectId} was not found.");

        var declaration = existing with
        {
            Alias = settings.Alias ?? existing.Alias,
            RepositoryUrl = settings.RepositoryUrl ?? existing.RepositoryUrl,
            PathHint = settings.ClearPathHint
                ? null
                : settings.PathHint ?? existing.PathHint,
            PublicSiteUrl = settings.ClearPublicSiteUrl
                ? null
                : settings.PublicSiteUrl ?? existing.PublicSiteUrl,
        };
        var result = linkedProjects.UpdateChild(projectId, declaration);
        if (!result.Success)
            return LinkedProjectManifestCommandOutput.Fail(result.Message);

        return LinkedProjectManifestCommandOutput.Changed("Updated child", projectId);
    }

    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<project-id>")]
        [Description("Stable project ID of the declared child")]
        public string ProjectId { get; init; } = string.Empty;

        [CommandOption("--alias <alias>")]
        [Description("New local selector and display alias")]
        public string? Alias { get; init; }

        [CommandOption("--repository-url <url>")]
        [Description("New portable Git repository URL")]
        public string? RepositoryUrl { get; init; }

        [CommandOption("--path-hint <path>")]
        [Description("New relative path from this repository to the child")]
        public string? PathHint { get; init; }

        [CommandOption("--clear-path-hint")]
        [Description("Remove the child's relative path hint")]
        public bool ClearPathHint { get; init; }

        [CommandOption("--public-site-url <url>")]
        [Description("New public static-site URL for the child")]
        public string? PublicSiteUrl { get; init; }

        [CommandOption("--clear-public-site-url")]
        [Description("Remove the child's public static-site URL")]
        public bool ClearPublicSiteUrl { get; init; }

        public override ValidationResult Validate()
        {
            if (PathHint != null && ClearPathHint)
                return ValidationResult.Error("--path-hint and --clear-path-hint cannot be used together.");
            if (PublicSiteUrl != null && ClearPublicSiteUrl)
                return ValidationResult.Error(
                    "--public-site-url and --clear-public-site-url cannot be used together.");
            return Alias == null && RepositoryUrl == null && PathHint == null && !ClearPathHint &&
                   PublicSiteUrl == null && !ClearPublicSiteUrl
                ? ValidationResult.Error("Specify at least one child declaration change.")
                : ValidationResult.Success();
        }
    }
}

public sealed class ProjectRemoveChildCommand(LinkedProjectService linkedProjects)
    : Command<ProjectRemoveChildCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = linkedProjects.RemoveChild(settings.ProjectId);
        if (!result.Success)
            return LinkedProjectManifestCommandOutput.Fail(result.Message);

        return LinkedProjectManifestCommandOutput.Changed("Removed child", settings.ProjectId);
    }

    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<project-id>")]
        [Description("Stable project ID of the declared child")]
        public string ProjectId { get; init; } = string.Empty;
    }
}

public sealed class ProjectReorderChildrenCommand(LinkedProjectService linkedProjects)
    : Command<ProjectReorderChildrenCommand.Settings>
{
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var result = linkedProjects.ReorderChildren(settings.ProjectIds);
        if (!result.Success)
            return LinkedProjectManifestCommandOutput.Fail(result.Message);

        AnsiConsole.MarkupLineInterpolated(
            $"Reordered [green]{settings.ProjectIds.Length}[/] linked-project child declaration(s).");
        return 0;
    }

    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<project-ids>")]
        [Description("Every declared child project ID in the desired order")]
        public string[] ProjectIds { get; init; } = [];
    }
}

public abstract class LinkedProjectDeclarationSettings : CommonSettings
{
    [CommandArgument(0, "<project-id>")]
    [Description("Stable project ID of the linked project")]
    public string ProjectId { get; init; } = string.Empty;

    [CommandOption("--alias <alias>")]
    [Description("Local selector and display alias")]
    public string Alias { get; init; } = string.Empty;

    [CommandOption("--repository-url <url>")]
    [Description("Portable Git repository URL")]
    public string RepositoryUrl { get; init; } = string.Empty;

    [CommandOption("--path-hint <path>")]
    [Description("Relative path from this repository to the linked project")]
    public string? PathHint { get; init; }

    [CommandOption("--public-site-url <url>")]
    [Description("Public static-site URL for the linked project")]
    public string? PublicSiteUrl { get; init; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Alias))
            return ValidationResult.Error("--alias is required.");
        return string.IsNullOrWhiteSpace(RepositoryUrl)
            ? ValidationResult.Error("--repository-url is required.")
            : ValidationResult.Success();
    }

    public LinkedProjectDeclaration ToDeclaration() => new()
    {
        ProjectId = ProjectId,
        Alias = Alias,
        RepositoryUrl = RepositoryUrl,
        PathHint = PathHint,
        PublicSiteUrl = PublicSiteUrl,
    };
}

internal static class LinkedProjectManifestCommandOutput
{
    public static int Changed(string operation, string projectId)
    {
        AnsiConsole.MarkupLineInterpolated($"{operation} [green]{projectId.Trim().EscapeMarkup()}[/].");
        return 0;
    }

    public static int Fail(string? message)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"[red]{(message ?? "Linked-project declaration could not be changed.").EscapeMarkup()}[/]");
        return 1;
    }
}
