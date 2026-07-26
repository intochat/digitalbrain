using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Flutter;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Ui;

internal sealed class OwnerSessionJournal
{
    private readonly ISessionNeuron _session;

    private readonly OwnerId _owner;

    private OwnerSessionJournal(ISessionNeuron session, OwnerId owner)
    {
        _session = session;
        _owner = owner;
    }

    public static OwnerSessionJournal Open(IGrainFactory grains, OwnerId owner)
    {
        ArgumentNullException.ThrowIfNull(grains);

        return new OwnerSessionJournal(
            grains.GetGrain<ISessionNeuron>(ISessionNeuron.ForOwner(owner).ToGrainId()),
            owner);
    }

    public Task<JournalRead> ReadShellOutgoingAsync(string shellName, long afterSequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        return _session.ReadNeuronJournal(
            NeuronId.For<IShell>(_owner, shellName),
            JournalKind.Outgoing,
            afterSequence);
    }
}

internal static class UiEdgeServices
{
    public static IServiceCollection AddUiEdgeServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(static services =>
            OwnerSessionJournal.Open(
                services.GetRequiredService<IGrainFactory>(),
                services.GetRequiredService<IDigitalBrain>().Owner));

        return services;
    }
}
