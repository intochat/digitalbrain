using Brain.Abstractions.Context;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Policy;
using Brain.Abstractions.Wiring;
using Brain.Core.Endpoints;
using Brain.Core.Graph;
using Brain.Core.Modules;

namespace Brain.Core.Wiring;

internal sealed class WiringActivationGrain(
    ModuleSet modules,
    IWorkspacePolicyEvaluator policy,
    IEndpointResolver endpoints,
    GraphShardDirectory directory,
    Action<NeuronRoleId>? beforeStage = null,
    Action<NeuronRoleId>? beforePromote = null)
{
    private readonly ModuleSet _modules = modules ?? throw new ArgumentNullException(nameof(modules));
    private readonly IWorkspacePolicyEvaluator _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    private readonly IEndpointResolver _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
    private readonly GraphShardDirectory _directory = directory ?? throw new ArgumentNullException(nameof(directory));
    private readonly Action<NeuronRoleId>? _beforeStage = beforeStage;
    private readonly Action<NeuronRoleId>? _beforePromote = beforePromote;
    private readonly Dictionary<WiringActivationKey, ActivationState> _activations = [];

    internal BrainActivityId CurrentId { get; private set; }

    internal Task<WiringActivation> StartApplyAsync(WiringVersion version, ActivityContext context)
    {
        var state = GetOrStart(version, context);
        return Task.FromResult(state.View());
    }

    internal async Task StageOneShardAsync(BrainActivityId activation)
    {
        var state = _activations.Values.SingleOrDefault(candidate => candidate.Id == activation)
            ?? throw new KeyNotFoundException("No wiring activation exists for the requested id.");
        await StageNextAsync(state);
    }

    internal async Task<WiringActivation> ApplyAsync(WiringVersion version, ActivityContext context)
    {
        var state = GetOrStart(version, context);
        if (state.Status == WiringActivationStatus.Active)
        {
            return state.View();
        }

        state.Status = WiringActivationStatus.Staging;
        while (state.StagedShards.Count < state.RouteGroups.Count)
        {
            await StageNextAsync(state);
        }

        while (state.PromotedShards.Count < state.RouteGroups.Count)
        {
            await PromoteNextAsync(state);
        }

        _directory.Activate(state.Id);
        state.Status = WiringActivationStatus.Active;
        return state.View();
    }

    internal Task<WiringActivationStatus> StatusAsync(BrainActivityId activation)
    {
        var state = _activations.Values.SingleOrDefault(candidate => candidate.Id == activation)
            ?? throw new KeyNotFoundException("No wiring activation exists for the requested id.");
        return Task.FromResult(state.Status);
    }

    private ActivationState GetOrStart(WiringVersion version, ActivityContext context)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(context);
        var key = new WiringActivationKey(version.Wiring, version.Version, context.Workspace, context.Principal);
        if (_activations.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var caller = new WorkspaceContext(context.Workspace, context.Principal, isServicePrincipal: false);
        var applicability = new WiringApplicabilityEvaluator().Evaluate(version, caller, _modules);
        if (applicability.Readiness != WiringReadiness.Ready)
        {
            throw new InvalidOperationException(applicability.Explanation);
        }

        var operation = _modules.OperationIndex[version.Operation.Value];
        var groups = new Dictionary<GraphShardId, RouteGroup>();
        foreach (var route in version.Routes)
        {
            var sourceRole = DeclaredRole(operation.Owner, route.SourceRole);
            var targetRole = DeclaredRole(route.TargetRole);
            var source = _endpoints.Resolve(sourceRole, caller);
            var target = _endpoints.Resolve(targetRole, caller);
            var decision = _policy.AuthorizeGraphChange(context, new GraphChangeRequest(
                GraphChangeKind.Install, operation.Owner, route.EventContract, route.TargetRole));
            if (decision != PolicyDecision.Allowed)
            {
                throw new InvalidOperationException($"Workspace policy did not allow wiring application: {decision}.");
            }

            var reshape = route.Reshape is null
                ? null
                : new ReshapeDescriptor(route.Reshape.InputContract, route.Reshape.OutputContract, route.Reshape.Owner);
            var request = new SynapseChangeRequest(source, route.EventContract, target, "wiring", route.Slot, reshape, context);
            var shard = new GraphShardResolver().Resolve(source);
            if (!groups.TryGetValue(shard, out var group))
            {
                group = new RouteGroup(source);
                groups.Add(shard, group);
            }

            group.Requests.Add(request);
        }

        var started = new ActivationState(BrainActivityId.New(), version, groups.Values.ToList());
        _activations.Add(key, started);
        CurrentId = started.Id;
        return started;
    }

    private async Task StageNextAsync(ActivationState state)
    {
        var group = state.RouteGroups.FirstOrDefault(candidate => !state.StagedShards.Contains(candidate.Source));
        if (group is null)
        {
            return;
        }

        try
        {
            _beforeStage?.Invoke(group.Source.Role);
            var shard = _directory.Open(group.Source, _modules, _policy);
            foreach (var request in group.Requests)
            {
                await shard.StageAsync(request, state.Id);
            }

            state.StagedShards.Add(group.Source);
        }
        catch
        {
            state.Status = WiringActivationStatus.Failed;
            throw;
        }
    }

    private async Task PromoteNextAsync(ActivationState state)
    {
        var group = state.RouteGroups.FirstOrDefault(candidate => !state.PromotedShards.Contains(candidate.Source));
        if (group is null)
        {
            return;
        }

        try
        {
            _beforePromote?.Invoke(group.Source.Role);
            var shard = _directory.Open(group.Source, _modules, _policy);
            await shard.PromoteAsync(state.Id);
            state.PromotedShards.Add(group.Source);
        }
        catch
        {
            state.Status = WiringActivationStatus.Failed;
            throw;
        }
    }

    private NeuronRoleDescriptor DeclaredRole(ModuleId owner, NeuronRoleId role)
        => _modules.Modules.Single(module => module.Id == owner).Roles.Single(candidate => candidate.Id == role);

    private NeuronRoleDescriptor DeclaredRole(NeuronRoleId role)
        => _modules.Modules.SelectMany(static module => module.Roles).Single(candidate => candidate.Id == role);

    private readonly record struct WiringActivationKey(WiringId Wiring, int Version, WorkspaceId Workspace, PrincipalId Principal);

    private sealed class RouteGroup(EndpointAddress source)
    {
        internal EndpointAddress Source { get; } = source;
        internal List<SynapseChangeRequest> Requests { get; } = [];
    }

    private sealed class ActivationState(BrainActivityId id, WiringVersion version, IReadOnlyList<RouteGroup> routeGroups)
    {
        internal BrainActivityId Id { get; } = id;
        internal WiringVersion Version { get; } = version;
        internal IReadOnlyList<RouteGroup> RouteGroups { get; } = routeGroups;
        internal HashSet<EndpointAddress> StagedShards { get; } = [];
        internal HashSet<EndpointAddress> PromotedShards { get; } = [];
        internal WiringActivationStatus Status { get; set; } = WiringActivationStatus.Staging;

        internal WiringActivation View() => new(Id, Version.Wiring, Version.Version, Status, StagedShards.Count, RouteGroups.Count);
    }
}
