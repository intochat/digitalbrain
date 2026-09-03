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

public static class Brain
{
    public static async Task<IDigitalBrain> CreateAsync(
        string[] args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        var context = new ActivationContext(args);
        await ActivationService.Default.ActivateAsync(context, cancellationToken)
            .ConfigureAwait(false);
        return context.Brain
            ?? throw new InvalidOperationException("Activation finished without a brain.");
    }

    internal static async Task<HostedBrain> StartLocalSiloAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
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

    internal sealed class HostedBrain(IHost host) : IDigitalBrain
    {
        private readonly IDigitalBrain _inner =
            DigitalBrainClient.Connect(host.Services.GetRequiredService<IGrainFactory>(), DigitalBrainNames.DefaultOwner);

        public IHost Host => host;

        public OwnerId Owner => _inner.Owner;

        public Task ActivateAsync(CancellationToken cancellationToken = default)
            => _inner.ActivateAsync(cancellationToken);

        public NeuronReference<TNeuron> Get<TNeuron>(string name = "default") where TNeuron : INeuron
            => _inner.Get<TNeuron>(name);

        public TEntity GetEntity<TEntity>(string name = "default") where TEntity : class, IEntity
            => _inner.GetEntity<TEntity>(name);

        public TNeuron GetGrainProxy<TNeuron>(string name = "default") where TNeuron : class, INeuron
            => _inner.GetGrainProxy<TNeuron>(name);

        public Task<DeliveryOutcome> SendAsync<TNeuron>(string name, Signal signal, CancellationToken cancellationToken = default)
            where TNeuron : INeuron
            => _inner.SendAsync<TNeuron>(name, signal, cancellationToken);

        public Task<IReadOnlyList<Synapse>> GetSynapsesAsync(CancellationToken cancellationToken = default)
            => _inner.GetSynapsesAsync(cancellationToken);

        public Task<JournalRead> ReadJournalAsync(JournalKind kind, long afterSequence = 0, CancellationToken cancellationToken = default)
            => _inner.ReadJournalAsync(kind, afterSequence, cancellationToken);

        public IAsyncEnumerable<JournalRead> WatchJournalAsync(JournalKind kind, long afterSequence = 0, CancellationToken cancellationToken = default)
            => _inner.WatchJournalAsync(kind, afterSequence, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync().ConfigureAwait(false);
            host.Dispose();
        }
    }
}
