using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PM.Application;
using PM.Project;

namespace PM.Api;

public sealed record SettingsOptionResponse(string Key, string Name);
public sealed record SettingsMilestoneResponse(string Key, string Title, string Priority);
public sealed record SettingsResponse(
    string ProjectName,
    IReadOnlyList<SettingsOptionResponse> Statuses,
    IReadOnlyList<SettingsOptionResponse> Tracks,
    IReadOnlyList<SettingsMilestoneResponse> Milestones,
    IReadOnlyList<string> PriorityOptions,
    string Revision);
public sealed record CreateSettingsOptionRequest(
    [property: JsonRequired] string Key,
    [property: JsonRequired] string Name);
public sealed record RenameSettingsOptionRequest([property: JsonRequired] string Name);
public sealed record CreateMilestoneRequest(
    [property: JsonRequired] string Key,
    [property: JsonRequired] string Title,
    string? Priority = null);
public sealed record RenameMilestoneRequest([property: JsonRequired] string Title);
public sealed record SetMilestonePriorityRequest([property: JsonRequired] string Priority);

public static class SettingsApiEndpoints
{
    public static void MapSettingsApi(this RouteGroupBuilder api, ProjectConfigService configService,
        ResourceRevisionService revisions)
    {
        api.MapGet("/settings", (HttpRequest request) => Read(request, configService, revisions))
            .WithName("GetSettings")
            .WithSummary("Get project settings")
            .Produces<SettingsResponse>()
            .WithRevisionedReadMetadata()
            .WithSettingsReadProblems();

        MapCreateMutation<CreateSettingsOptionRequest>(api, "/settings/statuses",
            "CreateStatus", "Create a status", configService, revisions,
            (service, input) => service.AddStatus(input.Key, input.Name));
        MapItemMutation<RenameSettingsOptionRequest>(api, "/settings/statuses/{key}",
            "RenameStatus", "Rename a status", configService, revisions,
            (service, input, key) => service.RenameStatus(key, input.Name));
        MapDelete(api, "/settings/statuses/{key}", "DeleteStatus", "Delete a status",
            configService, revisions, (service, key) => service.RemoveStatus(key));

        MapCreateMutation<CreateSettingsOptionRequest>(api, "/settings/tracks",
            "CreateTrack", "Create a track", configService, revisions,
            (service, input) => service.AddTrack(input.Key, input.Name));
        MapItemMutation<RenameSettingsOptionRequest>(api, "/settings/tracks/{key}",
            "RenameTrack", "Rename a track", configService, revisions,
            (service, input, key) => service.RenameTrack(key, input.Name));
        MapDelete(api, "/settings/tracks/{key}", "DeleteTrack", "Delete a track",
            configService, revisions, (service, key) => service.RemoveTrack(key));

        MapCreateMutation<CreateMilestoneRequest>(api, "/settings/milestones",
            "CreateMilestone", "Create a milestone", configService, revisions,
            (service, input) => service.AddMilestone(input.Key, input.Title, input.Priority));
        MapItemMutation<RenameMilestoneRequest>(api, "/settings/milestones/{key}",
            "RenameMilestone", "Rename a milestone", configService, revisions,
            (service, input, key) => service.RenameMilestone(key, input.Title));
        MapDelete(api, "/settings/milestones/{key}", "DeleteMilestone", "Delete a milestone",
            configService, revisions, (service, key) => service.RemoveMilestone(key));
        MapItemMutation<SetMilestonePriorityRequest>(api,
            "/settings/milestones/{key}/priority", "SetMilestonePriority",
            "Set a milestone priority", configService, revisions,
            (service, input, key) => service.SetMilestonePriority(key, input.Priority));
    }

    private static void MapCreateMutation<TRequest>(RouteGroupBuilder api, string pattern,
        string name, string summary, ProjectConfigService configService, ResourceRevisionService revisions,
        Func<ProjectConfigService, TRequest, AppResult> mutate)
        where TRequest : class
    {
        api.MapPost(pattern, async (HttpRequest request, CancellationToken cancellationToken) =>
            {
                var (input, error) = await ApiJsonRequest.Read<TRequest>(request, cancellationToken);
                if (error != null) return error;
                var precondition = CheckPrecondition(request, revisions);
                if (precondition != null) return precondition;
                var result = mutate(configService, input!);
                return result.Success
                    ? Refreshed(request, configService, revisions)
                    : ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            })
            .WithName(name)
            .WithSummary(summary)
            .Accepts<TRequest>("application/json")
            .Produces<SettingsResponse>()
            .WithClientHeaderMetadata()
            .WithRevisionedMutationMetadata()
            .WithSettingsMutationProblems();
    }

    private static void MapItemMutation<TRequest>(RouteGroupBuilder api, string pattern,
        string name, string summary, ProjectConfigService configService, ResourceRevisionService revisions,
        Func<ProjectConfigService, TRequest, string, AppResult> mutate)
        where TRequest : class
    {
        api.MapPut(pattern, async (HttpRequest request, string key, CancellationToken cancellationToken) =>
            {
                var (input, error) = await ApiJsonRequest.Read<TRequest>(request, cancellationToken);
                if (error != null) return error;
                var precondition = CheckPrecondition(request, revisions);
                if (precondition != null) return precondition;
                var result = mutate(configService, input!, key);
                return result.Success
                    ? Refreshed(request, configService, revisions)
                    : ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            })
            .WithName(name)
            .WithSummary(summary)
            .Accepts<TRequest>("application/json")
            .Produces<SettingsResponse>()
            .WithClientHeaderMetadata()
            .WithRevisionedMutationMetadata()
            .WithSettingsMutationProblems();
    }

    private static void MapDelete(RouteGroupBuilder api, string pattern, string name, string summary,
        ProjectConfigService configService, ResourceRevisionService revisions,
        Func<ProjectConfigService, string, AppResult> mutate)
    {
        api.MapDelete(pattern, (HttpRequest request, string key) =>
            {
                var precondition = CheckPrecondition(request, revisions);
                if (precondition != null) return precondition;
                var result = mutate(configService, key);
                return result.Success
                    ? Refreshed(request, configService, revisions)
                    : ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
            })
            .WithName(name)
            .WithSummary(summary)
            .Produces<SettingsResponse>()
            .WithClientHeaderMetadata()
            .WithRevisionedMutationMetadata()
            .WithSettingsMutationProblems();
    }

    private static IResult Read(HttpRequest request, ProjectConfigService configService,
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
            value.Statuses.Select(option => new SettingsOptionResponse(option.Key, option.Name)).ToList(),
            value.Tracks.Select(option => new SettingsOptionResponse(option.Key, option.Name)).ToList(),
            value.Milestones.Select(option =>
                new SettingsMilestoneResponse(option.Key, option.Name, option.Priority)).ToList(),
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

    private static RouteHandlerBuilder WithSettingsReadProblems(this RouteHandlerBuilder builder) => builder
        .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
        .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
        .Produces<ApiProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json");

    private static RouteHandlerBuilder WithSettingsMutationProblems(this RouteHandlerBuilder builder) => builder
        .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
        .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
        .Produces<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
        .Produces<ApiProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")
        .Produces<ApiProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json");
}
