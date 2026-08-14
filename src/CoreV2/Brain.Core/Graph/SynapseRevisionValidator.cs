using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Policy;
using Brain.Core.Endpoints;
using Brain.Core.Modules;

namespace Brain.Core.Graph;

internal sealed class GraphValidationException(string message) : InvalidOperationException(message);

internal sealed class GraphPolicyException(PolicyDecision decision)
    : InvalidOperationException($"Graph policy decision '{decision}' did not allow the requested change.")
{
    internal PolicyDecision Decision { get; } = decision;
}

internal sealed record SynapseChangeRequest(
    EndpointAddress Source,
    ContractId Contract,
    EndpointAddress Target,
    string Scope,
    WiringSlotId WiringSlot,
    ReshapeDescriptor? Reshape,
    ActivityContext Provenance)
{
    internal void EnsureWellFormed()
    {
        ArgumentNullException.ThrowIfNull(Source);
        ArgumentNullException.ThrowIfNull(Target);
        ArgumentNullException.ThrowIfNull(Provenance);
        if (Source.Workspace.IsEmpty || Target.Workspace.IsEmpty || Provenance.Workspace.IsEmpty
            || Source.Workspace != Target.Workspace || Source.Workspace != Provenance.Workspace
            || string.IsNullOrWhiteSpace(Source.Module.Value) || string.IsNullOrWhiteSpace(Source.Role.Value)
            || string.IsNullOrWhiteSpace(Target.Module.Value) || string.IsNullOrWhiteSpace(Target.Role.Value)
            || string.IsNullOrWhiteSpace(Source.ScopeToken) || string.IsNullOrWhiteSpace(Target.ScopeToken)
            || string.IsNullOrWhiteSpace(Contract.Value) || string.IsNullOrWhiteSpace(Scope)
            || string.IsNullOrWhiteSpace(WiringSlot.Value))
        {
            throw new GraphValidationException("A graph change requires valid workspace-local endpoints and stable route dimensions.");
        }
    }
}

internal sealed class SynapseRevisionValidator(ModuleSet modules, IWorkspacePolicyEvaluator policy)
{
    private readonly ModuleSet _modules = modules ?? throw new ArgumentNullException(nameof(modules));
    private readonly IWorkspacePolicyEvaluator _policy = policy ?? throw new ArgumentNullException(nameof(policy));

    internal void ValidateInstallOrReplace(SynapseChangeRequest request, GraphChangeKind kind)
    {
        request.EnsureWellFormed();
        Authorize(request, kind);
        var @event = EventProducedBySource(request);
        ValidateEndpointOwnership(request.Source, request.Provenance);
        ValidateEndpointOwnership(request.Target, request.Provenance);

        if (request.Source.Module != request.Target.Module && @event.Visibility != EventVisibility.Published)
        {
            throw new GraphValidationException("Cross-module graph delivery requires a published event.");
        }

        var acceptedContract = request.Reshape is null
            ? request.Contract
            : ValidateReshape(request);
        if (!_modules.ModuleIndex[request.Target.Module.Value].ConsumedEvents.Contains(acceptedContract))
        {
            throw new GraphValidationException("The target module does not accept the graph delivery contract.");
        }
    }

    internal void ValidateRetire(SynapseRevision existing, ActivityContext provenance)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(provenance);
        if (provenance.Workspace != existing.Source.Workspace)
        {
            throw new GraphValidationException("Retirement provenance must belong to the synapse workspace.");
        }

        var decision = _policy.AuthorizeGraphChange(
            provenance,
            new GraphChangeRequest(GraphChangeKind.Retire, existing.Source.Module, existing.Contract, existing.Target.Role));
        if (decision != PolicyDecision.Allowed)
        {
            throw new GraphPolicyException(decision);
        }
    }

    private void Authorize(SynapseChangeRequest request, GraphChangeKind kind)
    {
        var decision = _policy.AuthorizeGraphChange(
            request.Provenance,
            new GraphChangeRequest(kind, request.Source.Module, request.Contract, request.Target.Role));
        if (decision != PolicyDecision.Allowed)
        {
            throw new GraphPolicyException(decision);
        }
    }

    private EventDescriptor EventProducedBySource(SynapseChangeRequest request)
    {
        if (!_modules.EventIndex.TryGetValue(request.Contract.Value, out var @event)
            || @event.Owner != request.Source.Module)
        {
            throw new GraphValidationException("The source module has not declared the emitted event contract.");
        }

        return @event;
    }

    private ContractId ValidateReshape(SynapseChangeRequest request)
    {
        var reshape = request.Reshape!;
        if (string.IsNullOrWhiteSpace(reshape.InputEvent.Value)
            || string.IsNullOrWhiteSpace(reshape.OutputEvent.Value)
            || string.IsNullOrWhiteSpace(reshape.Owner.Value)
            || reshape.InputEvent != request.Contract
            || !_modules.ModuleIndex.TryGetValue(reshape.Owner.Value, out var owner)
            || !owner.Reshapes.Contains(reshape)
            || !_modules.EventIndex.TryGetValue(reshape.OutputEvent.Value, out var output)
            || output.Owner != reshape.Owner)
        {
            throw new GraphValidationException("The requested reshape is not an installed pure reshape declaration.");
        }

        return reshape.OutputEvent;
    }

    private void ValidateEndpointOwnership(EndpointAddress endpoint, ActivityContext provenance)
    {
        if (!_modules.ModuleIndex.TryGetValue(endpoint.Module.Value, out var module))
        {
            throw new GraphValidationException("The endpoint module does not own its declared role.");
        }

        var role = module.Roles.SingleOrDefault(candidate => candidate.Id == endpoint.Role && candidate.Owner == endpoint.Module);
        if (role is null)
        {
            throw new GraphValidationException("The endpoint module does not own its declared role.");
        }

        var expectedScopeToken = role.Scope == NeuronScope.Workspace
            ? "workspace"
            : provenance.Principal.Value;
        if (!string.Equals(endpoint.ScopeToken, expectedScopeToken, StringComparison.Ordinal))
        {
            throw new GraphValidationException("The endpoint scope token does not match its declared role scope and provenance.");
        }
    }
}
