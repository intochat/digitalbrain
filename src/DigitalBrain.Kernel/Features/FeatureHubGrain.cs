using System.Diagnostics;
using DigitalBrain.Kernel.Contracts;
using Orleans.Runtime;

namespace DigitalBrain.Kernel.Features;

[GrainType("digitalbrain.v3.feature-hub")]
public sealed class FeatureHubGrain(
    [PersistentState("feature-hub")] IPersistentState<FeatureHubState> persistentState,
    IGrainFactory grainFactory) : Grain, IFeatureHubGrain
{
    private static readonly ActivitySource ActivitySource = new("DigitalBrain.Features.Hub");

    public async Task RegisterAsync(FeatureInstallationRegistration registration)
    {
        using var activity = Start("register");
        ArgumentNullException.ThrowIfNull(registration);
        var ownerId = ParseKey();
        var next = Domain(() => FeatureHubTransitions.Register(State, registration));
        var installation = grainFactory.GetGrain<IFeatureInstallationGrain>(
            FeatureGrainIds.Installation(ownerId, registration.InstallationId));
        await installation.InitializeAsync(registration.Release);
        await WriteAsync(next);
    }

    public async Task<FeatureFanOutResult> PublishAsync(FeatureInput input)
    {
        using var activity = Start("publish", input);
        var ownerId = ParseKey();
        var begun = Domain(() => FeatureHubTransitions.BeginFanOut(State, input));
        if (!ReferenceEquals(begun, persistentState.State))
        {
            await WriteAsync(begun);
        }

        var batch = State.FanOuts.Single(candidate =>
            string.Equals(candidate.Input.InputId, input.InputId, StringComparison.Ordinal));
        var tasks = batch.Deliveries
            .Where(delivery => !delivery.Delivered)
            .Select(delivery => DeliverAsync(ownerId, delivery.InstallationId, batch.Input))
            .ToArray();
        var delivered = (await Task.WhenAll(tasks))
            .Where(result => result.Delivered)
            .Select(result => result.InstallationId)
            .ToHashSet();
        var completed = FeatureHubTransitions.RecordDeliveries(State, input.InputId, delivered);
        if (!ReferenceEquals(completed, persistentState.State))
        {
            await WriteAsync(completed);
        }
        return FanOutResult(State.FanOuts.Single(candidate =>
            string.Equals(candidate.Input.InputId, input.InputId, StringComparison.Ordinal)));
    }

    public Task<FeatureHubSnapshot> ReadAsync()
    {
        using var activity = Start("read");
        return Task.FromResult(new FeatureHubSnapshot(
            State.Installations.ToArray(),
            State.FanOuts.Select(FanOutResult).ToArray(),
            State.Revision));
    }

    private FeatureHubState State =>
        persistentState.RecordExists && persistentState.State is not null
            ? persistentState.State
            : FeatureHubState.Empty;

    private BrainOwnerId ParseKey() => FeatureGrainIds.ParseHub(this.GetPrimaryKeyString());

    private async Task WriteAsync(FeatureHubState next)
    {
        await PersistedStateReconciliation.WriteWithRollbackAsync(
            persistentState,
            next,
            FeatureStateEquality.Same);
    }

    private async Task<(FeatureInstallationId InstallationId, bool Delivered)> DeliverAsync(
        BrainOwnerId ownerId,
        FeatureInstallationId installationId,
        FeatureInput input)
    {
        try
        {
            var grain = grainFactory.GetGrain<IFeatureInstallationGrain>(
                FeatureGrainIds.Installation(ownerId, installationId));
            var status = await grain.AppendAsync(input);
            return (installationId, status is FeatureAppendStatus.Accepted or FeatureAppendStatus.Duplicate);
        }
        catch
        {
            return (installationId, false);
        }
    }

    private Activity? Start(string operation, FeatureInput? input = null)
    {
        var activity = ActivitySource.StartActivity(operation);
        activity?.SetTag("feature.grain_key", this.GetPrimaryKeyString());
        activity?.SetTag("feature.input_id", input?.InputId);
        activity?.SetTag("feature.correlation_id", input?.CorrelationId);
        activity?.SetTag("feature.trace_id", input?.TraceId);
        return activity;
    }

    private static FeatureFanOutResult FanOutResult(FeatureFanOutState batch) => new(
        batch.Input.InputId,
        batch.Deliveries.Count(delivery => delivery.Delivered),
        batch.Deliveries.Count(delivery => !delivery.Delivered));

    private static T Domain<T>(Func<T> transition)
    {
        try
        {
            return transition();
        }
        catch (FeatureConcurrencyException exception)
        {
            throw new InvalidOperationException(exception.Message);
        }
        catch (FeatureLimitExceededException exception)
        {
            throw new InvalidOperationException(exception.Message);
        }
    }
}
