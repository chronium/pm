using PM.Application;
using PM.Project;

namespace PM.Tests;

internal sealed record TestMilestoneActivationServiceSet(
    MilestoneActivationResolver Resolver,
    MilestoneActivationValidationService Validator,
    ActivationTriggerService Triggers,
    MilestoneDeliveryService Deliveries);

internal static class TestMilestoneActivationServices
{
    public static TestMilestoneActivationServiceSet Create(ProjectRoot projectRoot)
    {
        var resolver = new MilestoneActivationResolver(projectRoot);
        var validator = new MilestoneActivationValidationService(
            projectRoot, new MilestoneActivationGraphService(), resolver);
        var persistence = new ProjectConfigPersistence(projectRoot);
        var automaticActivations = new AutomaticActivationService(resolver, TimeProvider.System);
        return new TestMilestoneActivationServiceSet(
            resolver,
            validator,
            new ActivationTriggerService(
                projectRoot, resolver, validator, automaticActivations, TimeProvider.System, persistence),
            new MilestoneDeliveryService(
                projectRoot, resolver, validator, automaticActivations, TimeProvider.System, persistence));
    }
}
