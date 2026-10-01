using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Testing.E2E.Agent;
using Microsoft.Playwright;

namespace DigitalBrain.Testing.E2E;

// A fact on the shared host: xUnit injects the assembly fixture, the lease gives the fact its own
// WorkspaceId and lifetime, and the scripted model starts from a clean script. A fact that needs
// another composition does not inherit this; it boots its own host through ReferenceBrain.
public abstract class BrainFact(ReferenceBrainFixture host) : IAsyncLifetime
{
    private E2EBrain? _brain;

    protected ReferenceBrainFixture Host { get; } = host;
    protected E2EBrain Brain => _brain ?? throw new InvalidOperationException("The lease is taken in InitializeAsync.");
    protected ScriptedModelServer Model => Host.Model;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        _brain = await Host.LeaseAsync(ct);
        Host.Model.Reset();
        await ResetShellStateAsync(_brain, ct);
    }

    // The shell snapshot (project list, selection) is one deployment-global document; starting
    // every fact from an empty shell keeps browser facts as isolated as on a private host.
    private static async Task ResetShellStateAsync(E2EBrain brain, CancellationToken ct)
    {
        var current = await brain.HttpClient.GetFromJsonAsync<JsonElement>("/shell/state", ct);
        var revision = current.GetProperty("revision").GetInt32();
        if (revision == 0) { return; }
        using var reset = await brain.HttpClient.PutAsJsonAsync("/shell/state", new
        {
            expectedRevision = revision,
            operationId = Guid.NewGuid().ToString(),
            snapshot = new { version = 1, projects = Array.Empty<object>(), selectedProjectId = (string?)null, settings = new { } },
        }, ct);
        reset.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        if (_brain is not null) { await _brain.DisposeAsync(); }
    }

    // A fresh browser context against the shared web shell; it closes with the fact's lease.
    protected async Task<IPage> OpenPageAsync(CancellationToken cancellationToken)
        => (await Brain.OpenBrowserAsync(cancellationToken)).Page;
}
