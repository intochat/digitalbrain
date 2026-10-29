using DigitalBrain.Invocation;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Signalling;
using DigitalBrain.Runtime.Orleans.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Core.Internal;

namespace DigitalBrain.Runtime.Orleans.Testing;

/// <summary>
/// Runtime controls for facts (04-12, 04-13, 04-15). Not a production entry: internal, reached only by the runtime's own
/// test assemblies, and it never references the test-kit package.
/// </summary>
internal static class RuntimeTestHooks
{
    /// <summary>Runs a checkpoint of <paramref name="brain"/>'s journal now, trimming each publisher at its retention floor.</summary>
    public static async Task ForceCheckpoint(IServiceProvider services, string brain, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        var log = await services.GetRequiredService<BrainLogs>().For(brain).ConfigureAwait(false);
        await log.Checkpoint(ct).ConfigureAwait(false);
    }

    /// <summary>Moves the clock runtime services read for SessionTtl forward by <paramref name="by"/> (04-13).</summary>
    public static void Advance(IServiceProvider services, TimeSpan by)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.GetRequiredService<RuntimeClock>().Advance(by);
    }

    /// <summary>A typed proxy, from outside any neuron, to the hosted neuron <paramref name="id"/>.</summary>
    public static T Get<T>(IServiceProvider services, NeuronId id) where T : class => NeuronHosting.Get<T>(services, id);

    /// <summary>Deactivates the host activation of <paramref name="id"/> once it is idle; state and synapses survive.</summary>
    public static async Task Deactivate(IServiceProvider services, NeuronId id)
    {
        ArgumentNullException.ThrowIfNull(services);
        var types = services.GetRequiredService<NeuronTypeRegistry>();
        var sink = new OrleansInvocationSink(services.GetRequiredService<IGrainFactory>(), types, null);
        var key = id.ToString();
        var host = (await sink.Host(id, new RuntimeInvocation(string.Empty, string.Empty, key, string.Empty, []), CancellationToken.None).ConfigureAwait(false)).Host;
        await host.Cast<IGrainManagementExtension>().DeactivateOnIdle().ConfigureAwait(false);
    }

    /// <summary>Makes exactly the next publish of <paramref name="publisher"/> throw after its journal append: a kill between write and publish.</summary>
    public static void FailNextPublishAfterAppend(IServiceProvider services, NeuronId publisher)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.GetRequiredService<PublishFaults>().FailNextAfterAppend(publisher);
    }

    /// <summary>Sends every signal reference twice from now on.</summary>
    public static void DuplicateEach(IServiceProvider services, bool on)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.GetRequiredService<PublishFaults>().DuplicateEach = on;
    }
}
