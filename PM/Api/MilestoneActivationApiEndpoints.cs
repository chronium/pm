using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PM.Application;
using PM.Project;

namespace PM.Api;

public sealed record ActivationRequirementRequest(
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string Source);
public sealed record CreateActivationTriggerRequest(
    [property: JsonRequired] string Key,
    [property: JsonRequired] string Title,
    [property: JsonRequired] IReadOnlyList<ActivationRequirementRequest> Requirements);
public sealed record RenameActivationTriggerRequest([property: JsonRequired] string Title);
public sealed record SetActivationRequirementsRequest(
    [property: JsonRequired] IReadOnlyList<ActivationRequirementRequest> Requirements);
public sealed record OverrideActivationTriggerRequest([property: JsonRequired] string Reason);
public sealed record ActivateActivationTriggerRequest;
public sealed record RedefineActivationTriggerRequest(
    [property: JsonRequired] IReadOnlyList<ActivationRequirementRequest> Requirements,
    [property: JsonRequired] string PreviewRevision,
    bool AllowDeactivation = false);
public sealed record ReconcileActivationRequest(bool DryRun = false);
public sealed record SetMilestoneRequiredTriggersPreviewRequest(
    [property: JsonRequired] IReadOnlyList<string> TriggerKeys);
public sealed record SetMilestoneRequiredTriggersRequest(
    [property: JsonRequired] IReadOnlyList<string> TriggerKeys,
    [property: JsonRequired] string PreviewRevision,
    bool AllowDeactivation = false);
public sealed record MilestoneDeliveryPreviewRequest(string? Reason = null);
public sealed record DeliverMilestoneRequest(
    string? Reason,
    [property: JsonRequired] string PreviewRevision,
    bool AllowExceptional = false);

public sealed record ActivationRequirementReferenceResponse(string Kind, string Source);
public sealed record ActivationRequirementResponse(
    string Kind,
    string Source,
    bool IsSatisfied,
    bool WasWaivedAtActivation);
public sealed record ActivationProvenanceResponse(
    DateTimeOffset At,
    string Mode,
    string? Reason,
    IReadOnlyList<ActivationRequirementReferenceResponse> WaivedRequirements);
public sealed record ActivationTriggerResponse(
    string Key,
    string Title,
    bool IsActive,
    ActivationProvenanceResponse? Activation,
    int SatisfiedRequirementCount,
    int RequirementCount,
    bool RequirementsSatisfied,
    bool IsLatchedDespiteUnmetRequirements,
    IReadOnlyList<ActivationRequirementResponse> Requirements,
    IReadOnlyList<string> ConsumingMilestones);
public sealed record MilestoneDeliveryResponse(
    DateTimeOffset At,
    string Mode,
    string? Reason,
    IReadOnlyList<string> AcceptedTaskIds,
    bool IsValid);
public sealed record ActivationMilestoneResponse(
    string Key,
    string Title,
    string Description,
    string Priority,
    string Lifecycle,
    int AssignedTaskCount,
    int DoneTaskCount,
    IReadOnlyList<string> RequiredActivationTriggers,
    IReadOnlyList<string> UnmetActivationTriggers,
    MilestoneDeliveryResponse? Delivery);
public sealed record ActivationIssueResponse(string Severity, string Code, string Message);
public sealed record ActivationSwitchboardResponse(
    IReadOnlyList<ActivationTriggerResponse> ActivationTriggers,
    IReadOnlyList<ActivationMilestoneResponse> Milestones,
    IReadOnlyList<ActivationIssueResponse> Issues,
    string Revision);
public sealed record ActivationMutationResponse(
    bool Changed,
    ActivationSwitchboardResponse Switchboard,
    ActivationMutationImpactResponse? Impact = null);
public sealed record ActivationMutationImpactResponse(
    IReadOnlyList<string> AffectedMilestones,
    IReadOnlyList<string> TaskIdsLosingEligibility,
    IReadOnlyList<string> AutomaticallyActivatedTriggers,
    ReleaseTransitionResponse? ReleaseTransition = null);
public sealed record ReleaseTransitionResponse(
    DateTimeOffset At,
    string Kind,
    string FromVersion,
    string ToVersion,
    string? Source,
    string? Reason);
public sealed record ActivationMilestoneImpactResponse(
    string MilestoneKey,
    string Before,
    string After,
    IReadOnlyList<string> CurrentlyEligibleTaskIds,
    IReadOnlyList<string> TaskIdsLosingEligibility);
public sealed record ActivationTriggerRedefinitionPreviewResponse(
    string TriggerKey,
    string PreviewRevision,
    bool WillReactivateAutomatically,
    bool RequiresConfirmation,
    IReadOnlyList<ActivationMilestoneImpactResponse> Milestones,
    IReadOnlyList<string> CurrentlyEligibleTaskIds,
    IReadOnlyList<string> TaskIdsLosingEligibility);
public sealed record MilestoneRequiredTriggersPreviewResponse(
    string MilestoneKey,
    string PreviewRevision,
    IReadOnlyList<string> CurrentTriggerKeys,
    IReadOnlyList<string> ProposedTriggerKeys,
    string Before,
    string After,
    IReadOnlyList<string> CurrentlyEligibleTaskIds,
    IReadOnlyList<string> TaskIdsLosingEligibility,
    bool RequiresConfirmation);
public sealed record MilestoneDeliveryPreviewResponse(
    string MilestoneKey,
    string Title,
    string PreviewRevision,
    string Mode,
    int AssignedTaskCount,
    int DoneTaskCount,
    IReadOnlyList<string> UnfinishedTaskIds,
    bool RequiresConfirmation);

internal sealed record ActivationApiReadTarget(
    MilestoneActivationResolver Resolver,
    MilestoneActivationValidationService Validation,
    ResourceRevisionService Revisions);

internal sealed record ActivationApiWriteTarget(
    MilestoneActivationResolver Resolver,
    MilestoneActivationValidationService Validation,
    ActivationTriggerService ActivationTriggers,
    MilestoneDeliveryService MilestoneDeliveries,
    ResourceRevisionService Revisions,
    Func<LinkedProjectMutationTracker?> BeginMutation);

internal delegate (ActivationApiReadTarget? Target, IResult? Error) ActivationApiReadTargetResolver(
    HttpRequest request);

internal delegate (ActivationApiWriteTarget? Target, IResult? Error) ActivationApiWriteTargetResolver(
    HttpRequest request);

public static class MilestoneActivationApiEndpoints
{
    public static void MapMilestoneActivationApi(
        this RouteGroupBuilder api,
        MilestoneActivationResolver resolver,
        MilestoneActivationValidationService validationService,
        ActivationTriggerService triggerService,
        MilestoneDeliveryService deliveryService,
        ResourceRevisionService revisions)
    {
        MapMilestoneActivationApi(
            api,
            _ => (new ActivationApiReadTarget(resolver, validationService, revisions), null),
            _ => (new ActivationApiWriteTarget(
                resolver,
                validationService,
                triggerService,
                deliveryService,
                revisions,
                () => null), null),
            static name => name,
            false);
    }

    internal static void MapMilestoneActivationApi(
        this RouteGroupBuilder api,
        ActivationApiReadTargetResolver resolveRead,
        ActivationApiWriteTargetResolver resolveWrite,
        Func<string, string> operationName,
        bool linkedProject)
    {
        api.MapGet("/activation", (HttpRequest request) =>
            {
                var resolved = resolveRead(request);
                return resolved.Error ?? Read(
                    request, resolved.Target!.Resolver, resolved.Target.Validation, resolved.Target.Revisions);
            })
            .WithName(operationName("GetMilestoneActivation"))
            .WithSummary("Get the milestone activation switchboard")
            .Produces<ActivationSwitchboardResponse>()
            .WithRevisionedReadMetadata()
            .WithActivationProblems(false);

        MapJsonWithoutKey<CreateActivationTriggerRequest, ActivationTriggerMutationResult>(api, "/activation/triggers", HttpMethods.Post,
            operationName("CreateActivationTrigger"), "Create an activation trigger", resolveWrite,
            (target, input, _) => ParseRequirements(input.Requirements, requirements =>
                target.ActivationTriggers.AddTrigger(input.Key, input.Title, requirements)), MutationImpact,
            linkedProject: linkedProject);
        MapJson<RenameActivationTriggerRequest, ActivationTriggerMutationResult>(api, "/activation/triggers/{key}/title", HttpMethods.Put,
            operationName("RenameActivationTrigger"), "Rename an activation trigger", resolveWrite,
            (target, input, key, _) => target.ActivationTriggers.RenameTrigger(key, input.Title), MutationImpact,
            linkedProject: linkedProject);
        MapJson<SetActivationRequirementsRequest, ActivationTriggerMutationResult>(api, "/activation/triggers/{key}/requirements", HttpMethods.Put,
            operationName("SetActivationTriggerRequirements"), "Replace inactive trigger requirements", resolveWrite,
            (target, input, key, _) => ParseRequirements(input.Requirements, requirements =>
                target.ActivationTriggers.SetRequirements(key, requirements)), MutationImpact,
            linkedProject: linkedProject);
        MapDelete(api, "/activation/triggers/{key}", operationName("DeleteActivationTrigger"),
            "Delete an activation trigger", resolveWrite,
            (target, key) => target.ActivationTriggers.RemoveTrigger(key), MutationImpact, linkedProject);

        MapPreview<SetActivationRequirementsRequest, ActivationTriggerRedefinitionPreview,
            ActivationTriggerRedefinitionPreviewResponse>(api,
            "/activation/triggers/{key}/redefinition-preview", operationName("PreviewActivationTriggerRedefinition"),
            "Preview redefining an active trigger", resolveWrite,
            (target, input, key) => ParseRequirements(input.Requirements, requirements =>
                target.ActivationTriggers.PreviewRedefinition(key, requirements)), ToResponse, linkedProject);
        MapJson<RedefineActivationTriggerRequest, ActivationTriggerRedefinitionResult>(api, "/activation/triggers/{key}/redefinition", HttpMethods.Put,
            operationName("RedefineActivationTrigger"), "Redefine an active trigger", resolveWrite,
            (target, input, key, _) => ParseRequirements(input.Requirements, requirements =>
                target.ActivationTriggers.RedefineTrigger(
                    key, requirements, input.PreviewRevision, input.AllowDeactivation)),
            result => new ActivationMutationImpactResponse(result.AffectedMilestones, [], []),
            linkedProject: linkedProject);

        MapJson<ActivateActivationTriggerRequest, ResolvedActivationTrigger>(api,
            "/activation/triggers/{key}/activate", HttpMethods.Post, operationName("ActivateManualTrigger"),
            "Activate a manual-only trigger", resolveWrite,
            (target, _, key, _) => target.ActivationTriggers.ActivateTrigger(key, null), TriggerImpact,
            linkedProject: linkedProject);
        MapJson<OverrideActivationTriggerRequest, ResolvedActivationTrigger>(api, "/activation/triggers/{key}/override", HttpMethods.Post,
            operationName("OverrideActivationTrigger"), "Override unmet activation requirements", resolveWrite,
            (target, input, key, _) => target.ActivationTriggers.ActivateTrigger(key, input.Reason), TriggerImpact,
            linkedProject: linkedProject);
        MapDelete(api, "/activation/triggers/{key}/activation", operationName("ResetActivationTrigger"),
            "Reset a latched activation trigger", resolveWrite,
            (target, key) => target.ActivationTriggers.ResetTrigger(key), TriggerImpact, linkedProject);

        MapJsonWithoutKey<ReconcileActivationRequest, ActivationReconciliationResult>(api, "/activation/reconcile", HttpMethods.Post,
            operationName("ReconcileActivationTriggers"), "Latch satisfied activation triggers", resolveWrite,
            (target, input, _) => target.ActivationTriggers.Reconcile(input.DryRun),
            result => new ActivationMutationImpactResponse(
                result.ActivationImpact.MilestoneChanges.Select(change => change.MilestoneKey).ToList(),
                [],
                result.ActivationImpact.ActivatedTriggers.Select(trigger => trigger.Key).ToList()),
            result => result.ActivationImpact.ActivatedTriggers.Count > 0 && !result.DryRun,
            linkedProject);

        MapPreview<SetMilestoneRequiredTriggersPreviewRequest, MilestoneRequiredTriggersPreview,
            MilestoneRequiredTriggersPreviewResponse>(api,
            "/activation/milestones/{key}/required-triggers-preview", operationName("PreviewMilestoneRequiredTriggers"),
            "Preview replacing a milestone's required triggers", resolveWrite,
            (target, input, key) => target.ActivationTriggers.PreviewMilestoneRequiredTriggers(
                key, input.TriggerKeys), ToResponse, linkedProject);
        MapJson<SetMilestoneRequiredTriggersRequest, ActivationTriggerMutationResult>(api,
            "/activation/milestones/{key}/required-triggers", HttpMethods.Put,
            operationName("SetMilestoneRequiredTriggers"), "Replace a milestone's required triggers",
            resolveWrite,
            (target, input, key, _) => target.ActivationTriggers.SetMilestoneRequiredTriggers(
                key, input.TriggerKeys, input.PreviewRevision, input.AllowDeactivation), MutationImpact,
            linkedProject: linkedProject);

        MapPreview<MilestoneDeliveryPreviewRequest, MilestoneDeliveryPreview,
            MilestoneDeliveryPreviewResponse>(api,
            "/activation/milestones/{key}/delivery-preview", operationName("PreviewMilestoneDelivery"),
            "Preview milestone delivery", resolveWrite,
            (target, input, key) => target.MilestoneDeliveries.PreviewDelivery(key, input.Reason),
            ToResponse, linkedProject);
        MapJson<DeliverMilestoneRequest, LifecycleMutationResult<ResolvedMilestone>>(api, "/activation/milestones/{key}/delivery", HttpMethods.Put,
            operationName("DeliverMilestone"), "Deliver a milestone", resolveWrite,
            (target, input, key, _) => target.MilestoneDeliveries.DeliverMilestone(
                key, input.Reason, input.PreviewRevision, input.AllowExceptional),
            result => new ActivationMutationImpactResponse(
                result.ActivationImpact.MilestoneChanges.Select(change => change.MilestoneKey).ToList(),
                [],
                result.ActivationImpact.ActivatedTriggers.Select(trigger => trigger.Key).ToList(),
                result.ReleaseTransition == null
                    ? null
                    : new ReleaseTransitionResponse(
                        result.ReleaseTransition.At,
                        result.ReleaseTransition.Kind,
                        result.ReleaseTransition.FromVersion,
                        result.ReleaseTransition.ToVersion,
                        result.ReleaseTransition.Source,
                        result.ReleaseTransition.Reason)),
            linkedProject: linkedProject);
        MapDelete(api, "/activation/milestones/{key}/delivery", operationName("ReopenMilestone"),
            "Reopen a delivered milestone", resolveWrite,
            (target, key) => target.MilestoneDeliveries.ReopenMilestone(key),
            milestone => new ActivationMutationImpactResponse([milestone.Key], [], []), linkedProject);
    }

    internal static IResult Read(
        HttpRequest request,
        MilestoneActivationResolver resolver,
        MilestoneActivationValidationService validationService,
        ResourceRevisionService revisions)
    {
        var response = GetResponse(resolver, validationService, revisions, request);
        if (response.Error != null) return response.Error;
        var conditional = ApiPreconditions.EvaluateIfNoneMatch(request, response.Value!.Revision);
        if (conditional != null) return conditional;
        ApiPreconditions.SetETag(request.HttpContext.Response, response.Value.Revision);
        return Results.Ok(response.Value);
    }

    private static void MapDelete<T>(RouteGroupBuilder api, string pattern, string name, string summary,
        ActivationApiWriteTargetResolver resolveWrite,
        Func<ActivationApiWriteTarget, string, AppResult<T>> mutate,
        Func<T, ActivationMutationImpactResponse?>? impact = null,
        bool linkedProject = false)
    {
        api.MapDelete(pattern, (HttpRequest request, string key) =>
            {
                var resolved = resolveWrite(request);
                if (resolved.Error != null) return resolved.Error;
                return ExecuteMutation(request, resolved.Target!, () => mutate(resolved.Target!, key), impact);
            })
            .WithName(name).WithSummary(summary).Produces<ActivationMutationResponse>()
            .WithClientHeaderMetadata().WithRevisionedMutationMetadata().WithActivationProblems(linkedProject);
    }

    private static void MapJson<TRequest, TResult>(RouteGroupBuilder api, string pattern, string method,
        string name, string summary, ActivationApiWriteTargetResolver resolveWrite,
        Func<ActivationApiWriteTarget, TRequest, string, HttpRequest, AppResult<TResult>> mutate,
        Func<TResult, ActivationMutationImpactResponse?>? impact = null,
        Func<TResult, bool>? changed = null,
        bool linkedProject = false)
        where TRequest : class
    {
        api.MapMethods(pattern, [method], async (HttpRequest request, string key, CancellationToken cancellationToken) =>
            {
                var resolved = resolveWrite(request);
                if (resolved.Error != null) return resolved.Error;
                var (input, error) = await ApiJsonRequest.Read<TRequest>(request, cancellationToken);
                if (error != null) return error;
                return ExecuteMutation(request, resolved.Target!,
                    () => mutate(resolved.Target!, input!, key, request), impact, changed);
            })
            .WithName(name).WithSummary(summary).Accepts<TRequest>("application/json")
            .Produces<ActivationMutationResponse>().WithClientHeaderMetadata()
            .WithRevisionedMutationMetadata().WithActivationProblems(linkedProject);
    }

    private static void MapJsonWithoutKey<TRequest, TResult>(RouteGroupBuilder api, string pattern, string method,
        string name, string summary, ActivationApiWriteTargetResolver resolveWrite,
        Func<ActivationApiWriteTarget, TRequest, HttpRequest, AppResult<TResult>> mutate,
        Func<TResult, ActivationMutationImpactResponse?>? impact = null,
        Func<TResult, bool>? changed = null,
        bool linkedProject = false)
        where TRequest : class
    {
        api.MapMethods(pattern, [method], async (HttpRequest request, CancellationToken cancellationToken) =>
            {
                var resolved = resolveWrite(request);
                if (resolved.Error != null) return resolved.Error;
                var (input, error) = await ApiJsonRequest.Read<TRequest>(request, cancellationToken);
                if (error != null) return error;
                return ExecuteMutation(request, resolved.Target!,
                    () => mutate(resolved.Target!, input!, request), impact, changed);
            })
            .WithName(name).WithSummary(summary).Accepts<TRequest>("application/json")
            .Produces<ActivationMutationResponse>().WithClientHeaderMetadata()
            .WithRevisionedMutationMetadata().WithActivationProblems(linkedProject);
    }

    private static void MapPreview<TRequest, TPreview, TResponse>(RouteGroupBuilder api, string pattern,
        string name, string summary, ActivationApiWriteTargetResolver resolveWrite,
        Func<ActivationApiWriteTarget, TRequest, string, AppResult<TPreview>> preview,
        Func<TPreview, TResponse> map,
        bool linkedProject)
        where TRequest : class
    {
        api.MapPost(pattern, async (HttpRequest request, string key, CancellationToken cancellationToken) =>
            {
                var resolved = resolveWrite(request);
                if (resolved.Error != null) return resolved.Error;
                var (input, error) = await ApiJsonRequest.Read<TRequest>(request, cancellationToken);
                if (error != null) return error;
                var target = resolved.Target!;
                var precondition = CheckPrecondition(request, target.Resolver, target.Revisions);
                if (precondition != null) return precondition;
                var result = preview(target, input!, key);
                if (!result.Success)
                    return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                var current = GetResponse(target.Resolver, target.Validation, target.Revisions, request);
                if (current.Error != null) return current.Error;
                ApiPreconditions.SetETag(request.HttpContext.Response, current.Value!.Revision);
                return Results.Ok(map(result.Payload!));
            })
            .WithName(name).WithSummary(summary).Accepts<TRequest>("application/json")
            .Produces<TResponse>().WithClientHeaderMetadata().WithRevisionedMutationMetadata()
            .WithActivationProblems(linkedProject);
    }

    private static IResult ExecuteMutation<TResult>(HttpRequest request, ActivationApiWriteTarget target,
        Func<AppResult<TResult>> mutate,
        Func<TResult, ActivationMutationImpactResponse?>? impact = null,
        Func<TResult, bool>? changed = null)
    {
        var precondition = CheckPrecondition(request, target.Resolver, target.Revisions);
        if (precondition != null) return precondition;
        using var tracker = target.BeginMutation();
        var result = mutate();
        if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
        var response = GetResponse(target.Resolver, target.Validation, target.Revisions, request);
        if (response.Error != null) return response.Error;
        ApiPreconditions.SetETag(request.HttpContext.Response, response.Value!.Revision);
        if (tracker != null) ProjectMutationApiHeaders.Set(request.HttpContext.Response, tracker.Receipt);
        return Results.Ok(new ActivationMutationResponse(
            changed?.Invoke(result.Payload!) ?? true,
            response.Value,
            impact?.Invoke(result.Payload!)));
    }

    private static IResult? CheckPrecondition(HttpRequest request,
        MilestoneActivationResolver resolver, ResourceRevisionService revisions)
    {
        var snapshot = resolver.ResolveCurrentProject();
        if (!snapshot.Success) return ApiResults.Failure(snapshot.ErrorCode, snapshot.Message, request.Path);
        var revision = revisions.GetMilestoneActivationRevision(snapshot.Payload!);
        return revision.Success
            ? ApiPreconditions.RequireIfMatch(request, revision.Payload!)
            : ApiResults.Failure(revision.ErrorCode, revision.Message, request.Path);
    }

    private static (ActivationSwitchboardResponse? Value, IResult? Error) GetResponse(
        MilestoneActivationResolver resolver, MilestoneActivationValidationService validationService,
        ResourceRevisionService revisions, HttpRequest request)
    {
        var snapshot = resolver.ResolveCurrentProject();
        if (!snapshot.Success)
            return (null, ApiResults.Failure(snapshot.ErrorCode, snapshot.Message, request.Path));
        var resolved = snapshot.Payload!;
        var revision = revisions.GetMilestoneActivationRevision(resolved);
        if (!revision.Success)
            return (null, ApiResults.Failure(revision.ErrorCode, revision.Message, request.Path));
        var validation = validationService.ValidateCurrentProject();
        var issues = validation.Success
            ? validation.Payload!.Select(issue => new ActivationIssueResponse(
                issue.Severity, issue.Code, issue.Message)).ToList()
            : [];
        return (new ActivationSwitchboardResponse(
            resolved.ActivationTriggers.Select(ToResponse).ToList(),
            resolved.Milestones.Select(ToResponse).ToList(),
            issues,
            revision.Payload!), null);
    }

    private static AppResult<TResult> ParseRequirements<TResult>(
        IReadOnlyList<ActivationRequirementRequest> input,
        Func<IReadOnlyList<ActivationRequirement>, AppResult<TResult>> action)
    {
        var requirements = new List<ActivationRequirement>();
        foreach (var requirement in input ?? [])
        {
            if (!Enum.TryParse<ActivationRequirementKind>(requirement.Kind, true, out var kind))
                return AppResult<TResult>.Fail(
                    "invalid_activation_requirement_kind",
                    "Activation requirement kind must be task or milestone.");
            requirements.Add(new ActivationRequirement { Kind = kind, Source = requirement.Source });
        }
        return action(requirements);
    }

    private static ActivationTriggerResponse ToResponse(ResolvedActivationTrigger trigger) => new(
        trigger.Key, trigger.Title, trigger.IsActive,
        trigger.Activation == null ? null : new ActivationProvenanceResponse(
            trigger.Activation.At, Value(trigger.Activation.Mode), trigger.Activation.Reason,
            trigger.Activation.WaivedRequirements.Select(requirement =>
                new ActivationRequirementReferenceResponse(Value(requirement.Kind), requirement.Source)).ToList()),
        trigger.SatisfiedRequirementCount, trigger.RequirementCount, trigger.RequirementsSatisfied,
        trigger.IsLatchedDespiteUnmetRequirements,
        trigger.Requirements.Select(requirement => new ActivationRequirementResponse(
            Value(requirement.Kind), requirement.Source, requirement.IsSatisfied,
            requirement.WasWaivedAtActivation)).ToList(),
        trigger.ConsumingMilestones);

    private static ActivationMilestoneResponse ToResponse(ResolvedMilestone milestone) => new(
        milestone.Key, milestone.Title, milestone.Description, milestone.Priority, Value(milestone.Lifecycle),
        milestone.AssignedTaskCount, milestone.DoneTaskCount, milestone.RequiredActivationTriggers,
        milestone.UnmetActivationTriggers,
        milestone.Delivery == null ? null : new MilestoneDeliveryResponse(
            milestone.Delivery.At, Value(milestone.Delivery.Mode), milestone.Delivery.Reason,
            milestone.Delivery.AcceptedTaskIds, milestone.Delivery.IsValid));

    private static ActivationTriggerRedefinitionPreviewResponse ToResponse(
        ActivationTriggerRedefinitionPreview preview) => new(
        preview.TriggerKey, preview.Revision, preview.WillReactivateAutomatically,
        preview.RequiresConfirmation, preview.Milestones.Select(impact => new ActivationMilestoneImpactResponse(
            impact.MilestoneKey, Value(impact.Before), Value(impact.After), impact.CurrentlyEligibleTaskIds,
            impact.TaskIdsLosingEligibility)).ToList(), preview.CurrentlyEligibleTaskIds,
        preview.TaskIdsLosingEligibility);

    private static MilestoneRequiredTriggersPreviewResponse ToResponse(
        MilestoneRequiredTriggersPreview preview) => new(
        preview.MilestoneKey, preview.Revision, preview.CurrentTriggerKeys, preview.ProposedTriggerKeys,
        Value(preview.Before), Value(preview.After), preview.CurrentlyEligibleTaskIds,
        preview.TaskIdsLosingEligibility, preview.RequiresConfirmation);

    private static MilestoneDeliveryPreviewResponse ToResponse(MilestoneDeliveryPreview preview) => new(
        preview.MilestoneKey, preview.Title, preview.Revision, Value(preview.Mode), preview.AssignedTaskCount,
        preview.DoneTaskCount, preview.UnfinishedTaskIds, preview.RequiresConfirmation);

    private static ActivationMutationImpactResponse MutationImpact(ActivationTriggerMutationResult result) =>
        new(result.AffectedMilestones, [], []);

    private static ActivationMutationImpactResponse TriggerImpact(ResolvedActivationTrigger trigger) =>
        new(trigger.ConsumingMilestones, [], []);

    private static string Value<T>(T value) where T : struct, Enum =>
        string.Concat(value.ToString().Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? $"_{char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));

    private static RouteHandlerBuilder WithActivationProblems(
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
