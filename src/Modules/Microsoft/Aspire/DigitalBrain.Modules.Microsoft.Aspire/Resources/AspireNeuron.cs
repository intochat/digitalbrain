using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Orleans.Runtime;

namespace DigitalBrain.Microsoft.Aspire;

[GrainType("microsoft.aspire")]
internal sealed class AspireNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AspireState> store,
    AspireBridge bridge)
    : Neuron<AspireState>(store), IAspire, IAspireResourceReporter
{
    public Task<IReadOnlyList<AspireResource>> ListResources(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AspireResource>>([.. Snapshot.Resources.Values.OrderBy(resource => resource.Name, StringComparer.Ordinal)]);

    public Task StartResource(string name, CancellationToken cancellationToken = default) => bridge.ExecuteAsync(name, "start", cancellationToken);

    public Task StopResource(string name, CancellationToken cancellationToken = default) => bridge.ExecuteAsync(name, "stop", cancellationToken);

    public Task RestartResource(string name, CancellationToken cancellationToken = default) => bridge.ExecuteAsync(name, "restart", cancellationToken);

    public async Task Report(AspireResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var previous = Snapshot.Resources.GetValueOrDefault(resource.Name);
        if (previous is not null && previous.State == resource.State && previous.Health == resource.Health
            && previous.Urls.SequenceEqual(resource.Urls, StringComparer.Ordinal)) { return; }
        var resources = new Dictionary<string, AspireResource>(Snapshot.Resources, StringComparer.Ordinal) { [resource.Name] = resource };
        await Save(Snapshot with { Resources = resources }, new ResourceStateChanged(resource.Name, resource.State, resource.Health, previous?.State));
    }

    // The AppHost syncs every resource after (re)connecting; one call keeps that burst to a
    // single span instead of one per resource during an otherwise idle minute.
    public async Task ReportMany(AspireResource[] resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        foreach (var resource in resources) { await Report(resource); }
    }
}
