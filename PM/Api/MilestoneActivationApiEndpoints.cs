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
    IReadOnlyList<string> AutomaticallyActivatedTriggers);
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
        api.MapGet("/activation", (HttpRequest request) => Read(request, resolver, validationService, revisions))
            .WithName("GetMilestoneActivation")
            .WithSummary("Get the milestone activation switchboard")
            .Produces<ActivationSwitchboardResponse>()
            .WithRevisionedReadMetadata()
            .WithActivationProblems();

        MapJsonWithoutKey<CreateActivationTriggerRequest, ActivationTriggerMutationResult>(api, "/activation/triggers", HttpMethods.Post,
            "CreateActivationTrigger", "Create an activation trigger", resolver, validationService, revisions,
            (input, _) => ParseRequirements(input.Requirements, requirements =>
                triggerService.AddTrigger(input.Key, input.Title, requirements)), MutationImpact);
        MapJson<RenameActivationTriggerRequest, ActivationTriggerMutationResult>(api, "/activation/triggers/{key}/title", HttpMethods.Put,
            "RenameActivationTrigger", "Rename an activation trigger", resolver, validationService, revisions,
            (input, key, _) => triggerService.RenameTrigger(key, input.Title), MutationImpact);
        MapJson<SetActivationRequirementsRequest, ActivationTriggerMutationResult>(api, "/activation/triggers/{key}/requirements", HttpMethods.Put,
            "SetActivationTriggerRequirements", "Replace inactive trigger requirements", resolver, validationService, revisions,
            (input, key, _) => ParseRequirements(input.Requirements, requirements =>
                triggerService.SetRequirements(key, requirements)), MutationImpact);
        MapDelete(api, "/activation/triggers/{key}", "DeleteActivationTrigger", "Delete an activation trigger",
            resolver, validationService, revisions, key => triggerService.RemoveTrigger(key), MutationImpact);

        MapPreview<SetActivationRequirementsRequest, ActivationTriggerRedefinitionPreview,
            ActivationTriggerRedefinitionPreviewResponse>(api,
            "/activation/triggers/{key}/redefinition-preview", "PreviewActivationTriggerRedefinition",
            "Preview redefining an active trigger", resolver, validationService, revisions,
            (input, key) => ParseRequirements(input.Requirements, requirements =>
                triggerService.PreviewRedefinition(key, requirements)), ToResponse);
        MapJson<RedefineActivationTriggerRequest, ActivationTriggerRedefinitionResult>(api, "/activation/triggers/{key}/redefinition", HttpMethods.Put,
            "RedefineActivationTrigger", "Redefine an active trigger", resolver, validationService, revisions,
            (input, key, _) => ParseRequirements(input.Requirements, requirements =>
                triggerService.RedefineTrigger(key, requirements, input.PreviewRevision, input.AllowDeactivation)),
            result => new ActivationMutationImpactResponse(result.AffectedMilestones, [], []));

        MapJson<ActivateActivationTriggerRequest, ResolvedActivationTrigger>(api,
            "/activation/triggers/{key}/activate", HttpMethods.Post, "ActivateManualTrigger",
            "Activate a manual-only trigger", resolver, validationService, revisions,
            (_, key, _) => triggerService.ActivateTrigger(key, null), TriggerImpact);
        MapJson<OverrideActivationTriggerRequest, ResolvedActivationTrigger>(api, "/activation/triggers/{key}/override", HttpMethods.Post,
            "OverrideActivationTrigger", "Override unmet activation requirements", resolver, validationService, revisions,
            (input, key, _) => triggerService.ActivateTrigger(key, input.Reason), TriggerImpact);
        MapDelete(api, "/activation/triggers/{key}/activation", "ResetActivationTrigger",
            "Reset a latched activation trigger", resolver, validationService, revisions,
            key => triggerService.ResetTrigger(key), TriggerImpact);

        MapJsonWithoutKey<ReconcileActivationRequest, ActivationReconciliationResult>(api, "/activation/reconcile", HttpMethods.Post,
            "ReconcileActivationTriggers", "Latch satisfied activation triggers", resolver, validationService, revisions,
            (input, _) => triggerService.Reconcile(input.DryRun),
            result => new ActivationMutationImpactResponse(
                result.ActivationImpact.MilestoneChanges.Select(change => change.MilestoneKey).ToList(),
                [],
                result.ActivationImpact.ActivatedTriggers.Select(trigger => trigger.Key).ToList()),
            result => result.ActivationImpact.ActivatedTriggers.Count > 0 && !result.DryRun);

        MapPreview<SetMilestoneRequiredTriggersPreviewRequest, MilestoneRequiredTriggersPreview,
            MilestoneRequiredTriggersPreviewResponse>(api,
            "/activation/milestones/{key}/required-triggers-preview", "PreviewMilestoneRequiredTriggers",
            "Preview replacing a milestone's required triggers", resolver, validationService, revisions,
            (input, key) => triggerService.PreviewMilestoneRequiredTriggers(key, input.TriggerKeys), ToResponse);
        MapJson<SetMilestoneRequiredTriggersRequest, ActivationTriggerMutationResult>(api,
            "/activation/milestones/{key}/required-triggers", HttpMethods.Put,
            "SetMilestoneRequiredTriggers", "Replace a milestone's required triggers",
            resolver, validationService, revisions,
            (input, key, _) => triggerService.SetMilestoneRequiredTriggers(
                key, input.TriggerKeys, input.PreviewRevision, input.AllowDeactivation), MutationImpact);

        MapPreview<MilestoneDeliveryPreviewRequest, MilestoneDeliveryPreview,
            MilestoneDeliveryPreviewResponse>(api,
            "/activation/milestones/{key}/delivery-preview", "PreviewMilestoneDelivery",
            "Preview milestone delivery", resolver, validationService, revisions,
            (input, key) => deliveryService.PreviewDelivery(key, input.Reason), ToResponse);
        MapJson<DeliverMilestoneRequest, LifecycleMutationResult<ResolvedMilestone>>(api, "/activation/milestones/{key}/delivery", HttpMethods.Put,
            "DeliverMilestone", "Deliver a milestone", resolver, validationService, revisions,
            (input, key, _) => deliveryService.DeliverMilestone(
                key, input.Reason, input.PreviewRevision, input.AllowExceptional),
            result => new ActivationMutationImpactResponse(
                result.ActivationImpact.MilestoneChanges.Select(change => change.MilestoneKey).ToList(),
                [],
                result.ActivationImpact.ActivatedTriggers.Select(trigger => trigger.Key).ToList()));
        MapDelete(api, "/activation/milestones/{key}/delivery", "ReopenMilestone",
            "Reopen a delivered milestone", resolver, validationService, revisions,
            key => deliveryService.ReopenMilestone(key),
            milestone => new ActivationMutationImpactResponse([milestone.Key], [], []));
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
        MilestoneActivationResolver resolver, MilestoneActivationValidationService validationService,
        ResourceRevisionService revisions, Func<string, AppResult<T>> mutate,
        Func<T, ActivationMutationImpactResponse?>? impact = null)
    {
        api.MapDelete(pattern, (HttpRequest request, string key) =>
            ExecuteMutation(request, resolver, validationService, revisions, () => mutate(key), impact))
            .WithName(name).WithSummary(summary).Produces<ActivationMutationResponse>()
            .WithClientHeaderMetadata().WithRevisionedMutationMetadata().WithActivationProblems();
    }

    private static void MapJson<TRequest, TResult>(RouteGroupBuilder api, string pattern, string method,
        string name, string summary, MilestoneActivationResolver resolver,
        MilestoneActivationValidationService validationService, ResourceRevisionService revisions,
        Func<TRequest, string, HttpRequest, AppResult<TResult>> mutate,
        Func<TResult, ActivationMutationImpactResponse?>? impact = null,
        Func<TResult, bool>? changed = null)
        where TRequest : class
    {
        api.MapMethods(pattern, [method], async (HttpRequest request, string key, CancellationToken cancellationToken) =>
            {
                var (input, error) = await ApiJsonRequest.Read<TRequest>(request, cancellationToken);
                if (error != null) return error;
                return ExecuteMutation(request, resolver, validationService, revisions,
                    () => mutate(input!, key, request), impact, changed);
            })
            .WithName(name).WithSummary(summary).Accepts<TRequest>("application/json")
            .Produces<ActivationMutationResponse>().WithClientHeaderMetadata()
            .WithRevisionedMutationMetadata().WithActivationProblems();
    }

    private static void MapJsonWithoutKey<TRequest, TResult>(RouteGroupBuilder api, string pattern, string method,
        string name, string summary, MilestoneActivationResolver resolver,
        MilestoneActivationValidationService validationService, ResourceRevisionService revisions,
        Func<TRequest, HttpRequest, AppResult<TResult>> mutate,
        Func<TResult, ActivationMutationImpactResponse?>? impact = null,
        Func<TResult, bool>? changed = null)
        where TRequest : class
    {
        api.MapMethods(pattern, [method], async (HttpRequest request, CancellationToken cancellationToken) =>
            {
                var (input, error) = await ApiJsonRequest.Read<TRequest>(request, cancellationToken);
                if (error != null) return error;
                return ExecuteMutation(request, resolver, validationService, revisions,
                    () => mutate(input!, request), impact, changed);
            })
            .WithName(name).WithSummary(summary).Accepts<TRequest>("application/json")
            .Produces<ActivationMutationResponse>().WithClientHeaderMetadata()
            .WithRevisionedMutationMetadata().WithActivationProblems();
    }

    private static void MapPreview<TRequest, TPreview, TResponse>(RouteGroupBuilder api, string pattern,
        string name, string summary, MilestoneActivationResolver resolver,
        MilestoneActivationValidationService validationService, ResourceRevisionService revisions,
        Func<TRequest, string, AppResult<TPreview>> preview,
        Func<TPreview, TResponse> map)
        where TRequest : class
    {
        api.MapPost(pattern, async (HttpRequest request, string key, CancellationToken cancellationToken) =>
            {
                var (input, error) = await ApiJsonRequest.Read<TRequest>(request, cancellationToken);
                if (error != null) return error;
                var precondition = CheckPrecondition(request, resolver, revisions);
                if (precondition != null) return precondition;
                var result = preview(input!, key);
                if (!result.Success)
                    return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
                var current = GetResponse(resolver, validationService, revisions, request);
                if (current.Error != null) return current.Error;
                ApiPreconditions.SetETag(request.HttpContext.Response, current.Value!.Revision);
                return Results.Ok(map(result.Payload!));
            })
            .WithName(name).WithSummary(summary).Accepts<TRequest>("application/json")
            .Produces<TResponse>().WithClientHeaderMetadata().WithRevisionedMutationMetadata()
            .WithActivationProblems();
    }

    private static IResult ExecuteMutation<TResult>(HttpRequest request,
        MilestoneActivationResolver resolver, MilestoneActivationValidationService validationService,
        ResourceRevisionService revisions, Func<AppResult<TResult>> mutate,
        Func<TResult, ActivationMutationImpactResponse?>? impact = null,
        Func<TResult, bool>? changed = null)
    {
        var precondition = CheckPrecondition(request, resolver, revisions);
        if (precondition != null) return precondition;
        var result = mutate();
        if (!result.Success) return ApiResults.Failure(result.ErrorCode, result.Message, request.Path);
        var response = GetResponse(resolver, validationService, revisions, request);
        if (response.Error != null) return response.Error;
        ApiPreconditions.SetETag(request.HttpContext.Response, response.Value!.Revision);
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

    private static RouteHandlerBuilder WithActivationProblems(this RouteHandlerBuilder builder) => builder
        .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
        .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
        .Produces<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
        .Produces<ApiProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")
        .Produces<ApiProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json");
}
