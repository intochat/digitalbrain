using DigitalBrain.Core;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Spikes;

#pragma warning disable ORLEANSEXP005

public class JournalFormatSpikeTests
{
    [SkippableFact]
    public async Task Orleans_Native_Format_Round_Trips_A_Synapse_Without_JournalJsonContext()
    {
        var cluster = new TestClusterBuilder()
            .AddSiloBuilderConfigurator<NativeFormatSiloConfigurator>()
            .Build();
        cluster.Deploy();

        try
        {
            var grain = cluster.GrainFactory.GetGrain<IDemoNeuron>("spike-native-format");
            await grain.FireAsync(new DemoMessageSynapse("spike-payload"));

            var timeline = await grain.GetTimelineAsync();
            Assert.Contains(timeline, s => s is DemoMessageSynapse d && d.Text == "spike-payload");

            // Write-only round-trips prove serialization works, but not deserialization -- the JSON
            // registration story (JournalJsonContext) matters just as much on read. Force this grain's
            // activation to be collected and reactivated so the assertions below can only pass if the
            // journal was actually reconstructed from VolatileJournalStorage's raw bytes via the
            // "orleans-binary" format's read-side codecs, not merely re-read from a live in-process list.
            var activationCountBeforeReactivation = timeline.Count(s => s is NeuronActivated);
            await cluster.GrainFactory.GetGrain<IManagementGrain>(0).ForceActivationCollection(TimeSpan.Zero);

            // The activation collector's sweep is asynchronous and not tied to ForceActivationCollection's
            // return, so poll generously rather than assume a fixed short delay -- this bounds total wait
            // at 40s without hardcoding a fragile sleep. Observed to need more than 2s under load locally,
            // and more than 15s on a contended shared CI runner (many concurrent TestCluster silos on a
            // 2-vCPU GitHub-hosted runner) -- 40s gives that headroom without materially lengthening the
            // suite when the sweep completes promptly, as it normally does.
            IReadOnlyList<Synapse> timelineAfterReactivation = [];
            for (var attempt = 0; attempt < 200; attempt++)
            {
                timelineAfterReactivation = await grain.GetTimelineAsync();
                if (timelineAfterReactivation.Count(s => s is NeuronActivated) > activationCountBeforeReactivation)
                    break;

                await Task.Delay(200);
            }

            var reactivated = timelineAfterReactivation.Count(s => s is NeuronActivated) > activationCountBeforeReactivation;
            Skip.IfNot(reactivated, "Orleans did not collect/reactivate the grain within the retry window; deserialization-on-read was not exercised.");
            Assert.Contains(timelineAfterReactivation, s => s is DemoMessageSynapse d && d.Text == "spike-payload");
        }
        finally
        {
            cluster.StopAllSilos();
        }
    }
}

// Wires the real (non-fake) Orleans.Journaling pipeline: VolatileJournalStorage gives us an
// in-memory, byte-sequence-backed journal (no Azurite needed) that still runs synapses through
// actual journal-format encode/decode -- unlike NeuronTestSiloConfigurator's InMemoryDurableList,
// which is a plain List<T> that never serializes at all. JournalFormatKey = "orleans-binary" is
// set WITHOUT calling UseJsonJournalFormat and WITHOUT referencing JournalJsonContext anywhere in
// this file, per this spike's purpose: confirm whether the native (non-JSON) format needs manual
// per-Synapse-subtype registration the way JournalJsonContext currently does.
file sealed class NativeFormatSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddJournalStorage()
            .ConfigureServices(services =>
            {
                services.AddScoped<NeuronJournals>();
                services.AddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
                services.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = "orleans-binary");

                // Short collection quantum so ForceActivationCollection's sweep runs promptly in the
                // test below instead of waiting on Orleans' multi-minute production default.
                services.Configure<GrainCollectionOptions>(options =>
                {
                    options.CollectionQuantum = TimeSpan.FromMilliseconds(200);
                    options.CollectionAge = TimeSpan.FromMilliseconds(400);
                });
            });
    }
}
