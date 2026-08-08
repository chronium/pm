using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PM.Application;
using PM.Project;

namespace PM.Api;

public sealed record SettingsOptionResponse(string Key, string Name);
public sealed record SettingsActivationRequirementResponse(string Kind, string Source);
public sealed record SettingsActivationTriggerResponse(
    string Key,
    string Title,
    IReadOnlyList<SettingsActivationRequirementResponse> Requirements);
public sealed record SettingsMilestoneResponse(
    string Key,
    string Title,
    string Priority,
    string Description,
    IReadOnlyList<string> RequiredActivationTriggers);
public sealed record SettingsResponse(
    string ProjectName,
    string Accent,
    IReadOnlyList<SettingsOptionResponse> Statuses,
    IReadOnlyList<SettingsOptionResponse> Tracks,
    IReadOnlyList<SettingsMilestoneResponse> Milestones,
    IReadOnlyList<SettingsActivationTriggerResponse> ActivationTriggers,
    IReadOnlyList<string> PriorityOptions,
    string Revision);
public sealed record CreateSettingsOptionRequest(
    [property: JsonRequired] string Key,
    [property: JsonRequired] string Name);
public sealed record RenameSettingsOptionRequest([property: JsonRequired] string Name);
public sealed record CreateMilestoneRequest(
    [property: JsonRequired] string Key,
    [property: JsonRequired] string Title,
    string? Priority = null,
    string? Description = null);
public sealed record RenameMilestoneRequest([property: JsonRequired] string Title);
public sealed record SetMilestoneDescriptionRequest([property: JsonRequired] string Description);
public sealed record SetMilestonePriorityRequest([property: JsonRequired] string Priority);
public sealed record SetProjectAccentRequest([property: JsonRequired] string Accent);

internal sealed record SettingsApiReadTarget(
    ProjectConfigService Config,
    ResourceRevisionService Revisions);

internal sealed record SettingsApiWriteTarget(
    ProjectConfigService Config,
    ResourceRevisionService Revisions,
    Func<LinkedProjectMutationTracker?> BeginMutation);

internal delegate (SettingsApiReadTarget? Target, IResult? Error) SettingsApiReadTargetResolver(
    HttpRequest request);

internal delegate (SettingsApiWriteTarget? Target, IResult? Error) SettingsApiWriteTargetResolver(
    HttpRequest request);

public static class SettingsApiEndpoints
{
    public static void MapSettingsApi(this RouteGroupBuilder api, ProjectConfigService configService,
        ResourceRevisionService revisions)
    {
        MapSettingsApi(
            api,
            _ => (new SettingsApiReadTarget(configService, revisions), null),
            _ => (new SettingsApiWriteTarget(configService, revisions, () => null), null),
            static name => name,
            false);
    }

    internal static void MapSettingsApi(
        this RouteGroupBuilder api,
        SettingsApiReadTargetResolver resolveRead,
        SettingsApiWriteTargetResolver resolveWrite,
        Func<string, string> operationName,
        bool linkedProject)
    {
        api.MapGet("/settings", (HttpRequest request) =>
            {
                var resolved = resolveRead(request);
                return resolved.Error ?? Read(request, resolved.Target!.Config, resolved.Target.Revisions);
            })
            .WithName(operationName("GetSettings"))
            .WithSummary("Get project settings")
            .Produces<SettingsResponse>()
            .WithRevisionedReadMetadata()
            .WithSettingsReadProblems(linkedProject);

        MapCreateMutation<SetProjectAccentRequest>(api, "/settings/accent",
            operationName("SetProjectAccent"), "Set the project accent color", resolveWrite,
            (service, input) => service.SetAccent(input.Accent), linkedProject, HttpMethods.Put);

        MapCreateMutation<CreateSettingsOptionRequest>(api, "/settings/statuses",
            operationName("CreateStatus"), "Create a status", resolveWrite,
            (service, input) => service.AddStatus(input.Key, input.Name), linkedProject);
        MapItemMutation<RenameSettingsOptionRequest>(api, "/settings/statuses/{key}",
            operationName("RenameStatus"), "Rename a status", resolveWrite,
            (service, input, key) => service.RenameStatus(key, input.Name), linkedProject);
        MapDelete(api, "/settings/statuses/{key}", operationName("DeleteStatus"), "Delete a status",
            resolveWrite, (service, key) => service.RemoveStatus(key), linkedProject);

        MapCreateMutation<CreateSettingsOptionRequest>(api, "/settings/tracks",
            operationName("CreateTrack"), "Create a track", resolveWrite,
            (service, input) => service.AddTrack(input.Key, input.Name), linkedProject);
        MapItemMutation<RenameSettingsOptionRequest>(api, "/settings/tracks/{key}",
            operationName("RenameTrack"), "Rename a track", resolveWrite,
            (service, input, key) => service.RenameTrack(key, input.Name), linkedProject);
        MapDelete(api, "/settings/tracks/{key}", operationName("DeleteTrack"), "Delete a track",
            resolveWrite, (service, key) => service.RemoveTrack(key), linkedProject);

        MapCreateMutation<CreateMilestoneRequest>(api, "/settings/milestones",
            operationName("CreateMilestone"), "Create a milestone", resolveWrite,
            (service, input) => service.AddMilestone(input.Key, input.Title, input.Priority, input.Description),
            linkedProject);
        MapItemMutation<RenameMilestoneRequest>(api, "/settings/milestones/{key}",
            operationName("RenameMilestone"), "Rename a milestone", resolveWrite,
            (service, input, key) => service.RenameMilestone(key, input.Title), linkedProject);
        MapDelete(api, "/settings/milestones/{key}", operationName("DeleteMilestone"), "Delete a milestone",
            resolveWrite, (service, key) => service.RemoveMilestone(key), linkedProject);
        MapItemMutation<SetMilestonePriorityRequest>(api,
            "/settings/milestones/{key}/priority", operationName("SetMilestonePriority"),
            "Set a milestone priority", resolveWrite,
            (service, input, key) => service.SetMilestonePriority(key, input.Priority), linkedProject);
        MapItemMutation<SetMilestoneDescriptionRequest>(api,
            "/settings/milestones/{key}/description", operationName("SetMilestoneDescription"),
            "Set a milestone description", resolveWrite,
            (service, input, key) => service.SetMilestoneDescription(key, input.Description), linkedProject);
    }

    private static void MapCreateMutation<TRequest>(RouteGroupBuilder api, string pattern,
        string name, string summary, SettingsApiWriteTargetResolver resolveWrite,
        Func<ProjectConfigService, TRequest, AppResult> mutate, bool linkedProject, string method = "POST")
        where TRequest : class
    {
        api.MapMethods(pattern, [method], async (HttpRequest request, CancellationToken cancellationToken) =>
            {
                var resolved = resolveWrite(request);
                if (resolved.Error != null) return resolved.Error;
                var (input, error) = await ApiJsonRequest.Read<TRequest>(request, cancellationToken);
                if (error != null) return error;
                var target = resolved.Target!;
                var precondition = CheckPrecondition(request, target.Revisions);
                if (precondition != null) return precondition;
                using var tracker = target.BeginMutation();
                var result = mutate(target.Config, input!);
                if (!result.Success)
                    return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                var refreshed = Refreshed(request, target.Config, target.Revisions);
                SetMutationReceipt(request, tracker);
                return refreshed;
            })
            .WithName(name)
            .WithSummary(summary)
            .Accepts<TRequest>("application/json")
            .Produces<SettingsResponse>()
            .WithClientHeaderMetadata()
            .WithRevisionedMutationMetadata()
            .WithSettingsMutationProblems(linkedProject);
    }

    private static void MapItemMutation<TRequest>(RouteGroupBuilder api, string pattern,
        string name, string summary, SettingsApiWriteTargetResolver resolveWrite,
        Func<ProjectConfigService, TRequest, string, AppResult> mutate, bool linkedProject)
        where TRequest : class
    {
        api.MapPut(pattern, async (HttpRequest request, string key, CancellationToken cancellationToken) =>
            {
                var resolved = resolveWrite(request);
                if (resolved.Error != null) return resolved.Error;
                var (input, error) = await ApiJsonRequest.Read<TRequest>(request, cancellationToken);
                if (error != null) return error;
                var target = resolved.Target!;
                var precondition = CheckPrecondition(request, target.Revisions);
                if (precondition != null) return precondition;
                using var tracker = target.BeginMutation();
                var result = mutate(target.Config, input!, key);
                if (!result.Success)
                    return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                var refreshed = Refreshed(request, target.Config, target.Revisions);
                SetMutationReceipt(request, tracker);
                return refreshed;
            })
            .WithName(name)
            .WithSummary(summary)
            .Accepts<TRequest>("application/json")
            .Produces<SettingsResponse>()
            .WithClientHeaderMetadata()
            .WithRevisionedMutationMetadata()
            .WithSettingsMutationProblems(linkedProject);
    }

    private static void MapDelete(RouteGroupBuilder api, string pattern, string name, string summary,
        SettingsApiWriteTargetResolver resolveWrite,
        Func<ProjectConfigService, string, AppResult> mutate, bool linkedProject)
    {
        api.MapDelete(pattern, (HttpRequest request, string key) =>
            {
                var resolved = resolveWrite(request);
                if (resolved.Error != null) return resolved.Error;
                var target = resolved.Target!;
                var precondition = CheckPrecondition(request, target.Revisions);
                if (precondition != null) return precondition;
                using var tracker = target.BeginMutation();
                var result = mutate(target.Config, key);
                if (!result.Success)
                    return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                var refreshed = Refreshed(request, target.Config, target.Revisions);
                SetMutationReceipt(request, tracker);
                return refreshed;
            })
            .WithName(name)
            .WithSummary(summary)
            .Produces<SettingsResponse>()
            .WithClientHeaderMetadata()
            .WithRevisionedMutationMetadata()
            .WithSettingsMutationProblems(linkedProject);
    }

    internal static IResult Read(HttpRequest request, ProjectConfigService configService,
        ResourceRevisionService revisions)
    {
        var response = GetResponse(configService, revisions, request);
        if (response.Error != null) return response.Error;
        var conditional = ApiPreconditions.EvaluateIfNoneMatch(request, response.Value!.Revision);
        if (conditional != null) return conditional;
        ApiPreconditions.SetETag(request.HttpContext.Response, response.Value.Revision);
        return Results.Ok(response.Value);
    }

    private static IResult Refreshed(HttpRequest request, ProjectConfigService configService,
        ResourceRevisionService revisions)
    {
        var response = GetResponse(configService, revisions, request);
        if (response.Error != null) return response.Error;
        ApiPreconditions.SetETag(request.HttpContext.Response, response.Value!.Revision);
        return Results.Ok(response.Value);
    }

    private static (SettingsResponse? Value, IResult? Error) GetResponse(ProjectConfigService configService,
        ResourceRevisionService revisions, HttpRequest request)
    {
        var settings = configService.GetSettings();
        if (!settings.Success)
            return (null, ApiResults.Failure(settings.ErrorCode, settings.Message, request.Path));
        var revision = revisions.GetProjectConfigRevision();
        if (!revision.Success)
            return (null, ApiResults.Failure(revision.ErrorCode, revision.Message, request.Path));

        var value = settings.Payload!;
        return (new SettingsResponse(
            value.ProjectName,
            value.Accent,
            value.Statuses.Select(option => new SettingsOptionResponse(option.Key, option.Name)).ToList(),
            value.Tracks.Select(option => new SettingsOptionResponse(option.Key, option.Name)).ToList(),
            value.Milestones.Select(option =>
                new SettingsMilestoneResponse(
                    option.Key,
                    option.Name,
                    option.Priority,
                    option.Description,
                    option.RequiredActivationTriggers)).ToList(),
            value.ActivationTriggers.Select(trigger => new SettingsActivationTriggerResponse(
                trigger.Key,
                trigger.Title,
                trigger.Requirements.Select(requirement => new SettingsActivationRequirementResponse(
                    requirement.Kind.ToString().ToLowerInvariant(),
                    requirement.Source)).ToList())).ToList(),
            PriorityLevel.Values,
            revision.Payload!), null);
    }

    private static IResult? CheckPrecondition(HttpRequest request, ResourceRevisionService revisions)
    {
        var revision = revisions.GetProjectConfigRevision();
        return revision.Success
            ? ApiPreconditions.RequireIfMatch(request, revision.Payload!)
            : ApiResults.Failure(revision.ErrorCode, revision.Message, request.Path);
    }

    private static void SetMutationReceipt(HttpRequest request, LinkedProjectMutationTracker? tracker)
    {
        if (tracker != null) ProjectMutationApiHeaders.Set(request.HttpContext.Response, tracker.Receipt);
    }

    private static RouteHandlerBuilder WithSettingsReadProblems(
        this RouteHandlerBuilder builder, bool linkedProject)
    {
        builder
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json");
        if (linkedProject)
            builder.Produces<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");
        return builder;
    }

    private static RouteHandlerBuilder WithSettingsMutationProblems(
        this RouteHandlerBuilder builder, bool linkedProject)
    {
        builder
            .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json");
        if (linkedProject)
            builder.Produces<ApiProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        return builder;
    }
}
