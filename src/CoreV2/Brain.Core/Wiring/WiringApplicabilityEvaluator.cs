using Brain.Abstractions.Context;
using Brain.Abstractions.Events;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Wiring;
using Brain.Core.Modules;

namespace Brain.Core.Wiring;

internal sealed class WiringApplicabilityEvaluator
{
    internal WiringApplicability Evaluate(WiringVersion version, WorkspaceContext caller, ModuleSet modules)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(modules);

        if (!modules.OperationIndex.TryGetValue(version.Operation.Value, out var operation)
            || operation.Version != version.OperationMajor)
        {
            return Unavailable("The declared operation is not installed at its requested major version.");
        }

        foreach (var route in version.Routes)
        {
            if (!RouteIsInstalledAndPublic(route, operation.Owner, modules))
            {
                return Unavailable("A declared role, public event contract, reshape, or target acceptance is unavailable.");
            }
        }

        if (version.RequiredCapabilities.Any(capability => !modules.CapabilityIndex.ContainsKey(capability.Value)))
        {
            return new WiringApplicability(WiringReadiness.NeedsSetup, "A declared capability is not installed.");
        }

        if (version.PolicyPrerequisites.Any(prerequisite => prerequisite.Kind == WiringPrerequisiteKind.PrincipalAuthorization))
        {
            return new WiringApplicability(WiringReadiness.NeedsAuthorization, "A declared principal authorization prerequisite remains unsatisfied.");
        }

        if (version.PolicyPrerequisites.Any(prerequisite => prerequisite.Kind == WiringPrerequisiteKind.WorkspaceConfiguration))
        {
            return new WiringApplicability(WiringReadiness.NeedsSetup, "A declared workspace configuration prerequisite remains unsatisfied.");
        }

        return new WiringApplicability(WiringReadiness.Ready, "All declared framework prerequisites are available.");
    }

    private static bool RouteIsInstalledAndPublic(WiringRoute route, Brain.Abstractions.Identity.ModuleId sourceModule, ModuleSet modules)
    {
        var source = modules.Modules.SingleOrDefault(module => module.Id == sourceModule);
        var target = modules.Modules.SingleOrDefault(module => module.Roles.Any(role => role.Id == route.TargetRole));
        if (source is null || target is null
            || !source.Roles.Any(role => role.Id == route.SourceRole)
            || !modules.EventIndex.TryGetValue(route.EventContract.Value, out var @event)
            || @event.Owner != sourceModule || @event.Visibility != EventVisibility.Published)
        {
            return false;
        }

        var accepted = route.Reshape?.OutputContract ?? route.EventContract;
        if (!target.ConsumedEvents.Contains(accepted))
        {
            return false;
        }

        return route.Reshape is null || modules.ModuleIndex.TryGetValue(route.Reshape.Owner.Value, out var reshapeOwner)
            && reshapeOwner.Reshapes.Contains(new ReshapeDescriptor(route.Reshape.InputContract, route.Reshape.OutputContract, route.Reshape.Owner))
            && route.Reshape.InputContract == route.EventContract;
    }

    private static WiringApplicability Unavailable(string explanation)
        => new(WiringReadiness.Unavailable, explanation);
}
