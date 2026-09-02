using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Client;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Hosting;
using Orleans.Journaling;

namespace DigitalBrainConsole;

// Host composition for the console proof: a local single-node silo with in-memory persistence,
// so `dotnet run` needs no external dependency. Named Brain, not DigitalBrain: this lives in
// DigitalBrainConsole and composes types from the DigitalBrain.Core namespace (Neuron,
// DigitalBrainRuntime, ModuleManifest) unqualified — a type here named DigitalBrain would read
// as if it belonged to that namespace instead of naming the module.
public static class Brain
{
    public static async Task<IDigitalBrain> CreateAsync(
        string[] args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.UseOrleans(silo =>
        {
            silo.UseLocalhostClustering();
            silo.Services.AddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
            DigitalBrainRuntime.Add(silo, new ModuleManifest([]));
            silo.AddMemoryGrainStorage(DigitalBrainNames.DefaultGrainStorage);
            silo.UseInMemoryReminderService();
        });

        var host = builder.Build();
        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        return new HostedBrain(host);
    }

    private sealed class HostedBrain(IHost host) : IDigitalBrain
    {
        private readonly IDigitalBrain _inner =
            DigitalBrainClient.Connect(host.Services.GetRequiredService<IGrainFactory>(), DigitalBrainNames.DefaultOwner);

        public OwnerId Owner => _inner.Owner;

        public Task ActivateAsync(CancellationToken cancellationToken = default)
            => _inner.ActivateAsync(cancellationToken);

        public NeuronReference<TNeuron> Get<TNeuron>(string name = "default") where TNeuron : INeuron
            => _inner.Get<TNeuron>(name);

        public TEntity GetEntity<TEntity>(string name = "default") where TEntity : class, IEntity
            => _inner.GetEntity<TEntity>(name);

        public TNeuron GetGrainProxy<TNeuron>(string name = "default") where TNeuron : class, INeuron
            => _inner.GetGrainProxy<TNeuron>(name);

        public Task FireAsync<TNeuron>(string name, Signal signal, CancellationToken cancellationToken = default)
            where TNeuron : INeuron
            => _inner.FireAsync<TNeuron>(name, signal, cancellationToken);

        public Task<IReadOnlyList<Synapse>> GetSynapsesAsync(NeuronId subject, CancellationToken cancellationToken = default)
            => _inner.GetSynapsesAsync(subject, cancellationToken);

        public Task<JournalRead> ReadJournalAsync(NeuronId subject, JournalKind kind, long afterSequence = 0, CancellationToken cancellationToken = default)
            => _inner.ReadJournalAsync(subject, kind, afterSequence, cancellationToken);

        public IAsyncEnumerable<JournalRead> WatchJournalAsync(NeuronId subject, JournalKind kind, long afterSequence = 0, CancellationToken cancellationToken = default)
            => _inner.WatchJournalAsync(subject, kind, afterSequence, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync().ConfigureAwait(false);
            host.Dispose();
        }
    }
}
