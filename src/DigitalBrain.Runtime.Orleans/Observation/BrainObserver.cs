using System.Reflection;
using System.Runtime.CompilerServices;
using DigitalBrain.Contracts;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Orleans;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>The stamped caller an <see cref="IBrainObserver"/> reads. The host registers it; a runtime-only silo has none.</summary>
internal interface IObservedCaller
{
    /// <summary>The brain of the current call.</summary>
    string BrainId { get; }

    /// <summary>The person of the current call.</summary>
    string PersonId { get; }

    /// <summary>The brains <paramref name="personId"/> is a member of.</summary>
    Task<string[]> BrainsOf(string personId, CancellationToken cancellationToken);
}

/// <summary>Reads the instance directory and type tables through their grains.</summary>
internal sealed class ClusterBrainObserver(IGrainFactory grains, IServiceProvider services) : IBrainObserver
{
    public IAsyncEnumerable<BrainInstance> Instances<T>(CancellationToken cancellationToken = default) where T : class
        => Instances(TypeOf(typeof(T)), cancellationToken);

    public IAsyncEnumerable<BrainInstance> Instances(string? type, CancellationToken cancellationToken = default)
        => InstancesIn(Caller().BrainId, type, cancellationToken);

    public async IAsyncEnumerable<BrainInstance> InstancesIn(string brain, string? type, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brain);
        string[] types = type is null
            ? await grains.GetGrain<IInstanceTypes>(brain).List().WaitAsync(cancellationToken).ConfigureAwait(false)
            : [type];
        foreach (var each in types)
        {
            foreach (var instance in await grains.GetGrain<IInstanceDirectory>(InstanceDirectoryKey.Of(brain, each)).List().WaitAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return instance;
            }
        }
    }

    public Task<BrainType[]> Types(CancellationToken cancellationToken = default)
        => grains.GetGrain<ICompiledTypeTable>(TypeTableKey.NoBrain).List().WaitAsync(cancellationToken);

    public async Task<string[]> Brains(CancellationToken cancellationToken = default)
    {
        var caller = Caller();
        var members = await caller.BrainsOf(caller.PersonId, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        return members.Contains(caller.BrainId, StringComparer.Ordinal) ? members : [caller.BrainId, .. members];
    }

    public Task<ObservedSynapse[]> Synapses(CancellationToken cancellationToken = default)
        => services.GetService<BrainLogs>() is { } logs
            ? SynapseObservation.List(logs, Caller().BrainId, cancellationToken)
            : Task.FromResult<ObservedSynapse[]>([]);

    public async Task<System.Text.Json.JsonElement?> State(NeuronId id, CancellationToken cancellationToken = default)
    {
        var host = grains.GetGrain<Hosting.INeuronHost>(id.ToString());
        return await host.InspectState(cancellationToken).ConfigureAwait(false);
    }

    public IAsyncEnumerable<SignalDelivery> Deliveries(CancellationToken cancellationToken = default)
        => services.GetService<DeliveryHub>() is { } hub
            ? hub.Listen(Caller().BrainId, cancellationToken)
            : EmptyDeliveries(cancellationToken);

    private static async IAsyncEnumerable<SignalDelivery> EmptyDeliveries([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }

    private IObservedCaller Caller()
        => services.GetService<IObservedCaller>()
            ?? throw new InvalidOperationException("No caller is stamped and no IObservedCaller is registered.");

    private string TypeOf(Type contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (contract.GetCustomAttribute<DefaultNeuronTypeAttribute>(inherit: false)?.Type is { } plain) { return plain; }
        try { return grains.GetGrain(contract, "_").GetGrainId().Type.ToString()!; }
        catch (ArgumentException error)
        {
            throw new ArgumentException($"{contract.Name} has no single grain type: {error.Message}", nameof(contract), error);
        }
    }
}

/// <summary>The instance directory grain contract for a brain and neuron type.</summary>
[Alias("kernel.instance-directory")]
public interface IInstanceDirectory : IGrainWithStringKey
{
    /// <summary>Registers an instance key.</summary>
    Task Register(string key, string? creator);

    /// <summary>Retires an instance key.</summary>
    Task Retire(string key);

    /// <summary>Lists registered instances.</summary>
    Task<BrainInstance[]> List();
}

/// <summary>The instance types grain contract for a brain.</summary>
[Alias("kernel.instance-types")]
public interface IInstanceTypes : IGrainWithStringKey
{
    /// <summary>Records a neuron type in the brain.</summary>
    Task Add(string type);

    /// <summary>Lists neuron types in the brain.</summary>
    Task<string[]> List();
}

/// <summary>Key formatting for <see cref="IInstanceDirectory"/>.</summary>
public static class InstanceDirectoryKey
{
    /// <summary>Returns the instance directory key for <paramref name="brain"/> and <paramref name="type"/>.</summary>
    public static string Of(string brain, string type) => brain + "/" + type;
}

/// <summary>Constants for <see cref="ICompiledTypeTable"/>.</summary>
public static class TypeTableKey
{
    /// <summary>The cluster-wide compiled type table key.</summary>
    public const string NoBrain = "/";
}

/// <summary>The compiled type table grain contract.</summary>
[Alias("kernel.compiled-type-table")]
public interface ICompiledTypeTable : IGrainWithStringKey
{
    /// <summary>Lists compiled types.</summary>
    Task<BrainType[]> List();

    /// <summary>Returns the host grain type for a neuron type, or null.</summary>
    Task<string?> HostType(string type);
}
