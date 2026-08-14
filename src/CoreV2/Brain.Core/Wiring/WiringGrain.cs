using Brain.Abstractions.Context;
using Brain.Abstractions.Policy;
using Brain.Abstractions.Wiring;
using Brain.Core.Modules;

namespace Brain.Core.Wiring;

// The publish grain owns only immutable declarations. It does not retain any
// resolved runtime address or mutable graph history.
internal sealed class WiringGrain(ModuleSet modules, IWorkspacePolicyEvaluator policy)
{
    private readonly ModuleSet _modules = modules ?? throw new ArgumentNullException(nameof(modules));
    private readonly IWorkspacePolicyEvaluator _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    private readonly Dictionary<Brain.Abstractions.Identity.WiringId, List<WiringVersion>> _history = [];

    internal Task<WiringVersion> PublishAsync(WiringProposal proposal, ActivityContext context)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(context);
        var version = proposal.Version;
        if (version.CauseActivity != context.Activity)
        {
            throw new InvalidOperationException("A published wiring must retain its originating activity.");
        }

        var applicability = new WiringApplicabilityEvaluator().Evaluate(version, new WorkspaceContext(context.Workspace, context.Principal, isServicePrincipal: false), _modules);
        if (applicability.Readiness != WiringReadiness.Ready)
        {
            throw new InvalidOperationException(applicability.Explanation);
        }

        var operation = _modules.OperationIndex[version.Operation.Value];
        foreach (var route in version.Routes)
        {
            var decision = _policy.AuthorizeGraphChange(context, new GraphChangeRequest(
                GraphChangeKind.Install, operation.Owner, route.EventContract, route.TargetRole));
            if (decision != PolicyDecision.Allowed)
            {
                throw new InvalidOperationException($"Workspace policy did not allow wiring publication: {decision}.");
            }
        }

        if (!_history.TryGetValue(version.Wiring, out var versions))
        {
            if (version.Version != 1 || version.ParentVersion is not null)
            {
                throw new InvalidOperationException("The first wiring version must be version one without a parent.");
            }

            _history.Add(version.Wiring, [version]);
            return Task.FromResult(version);
        }

        var previous = versions[^1];
        if (version.Version != previous.Version + 1 || version.ParentVersion != previous.Version)
        {
            throw new InvalidOperationException("Wiring versions are immutable and append-only.");
        }

        versions.Add(version);
        return Task.FromResult(version);
    }

    internal Task<IReadOnlyList<WiringVersion>> HistoryAsync(Brain.Abstractions.Identity.WiringId wiring)
        => Task.FromResult<IReadOnlyList<WiringVersion>>(
            _history.TryGetValue(wiring, out var versions) ? versions.AsReadOnly() : []);
}
