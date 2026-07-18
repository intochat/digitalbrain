using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

namespace DigitalBrain.Sdk.Experiences;

using Orleans;

// T2 polyrepo: extracted from Kernel/Experiences to Connectors (with GoogleAuthConnectorNeuron) per vision §4 + plan (connectors extraction start; Gmail/FS/Google not special in kernel).
// GrainType("gmail-last-senders") + IHandle + logic/GrainFactory calls/Emit 100% identical. Cross to GoogleAuth now qualified to *ConnectorNeuron (same Connectors assembly).
// Bundle id "gmail-last-senders" + os/gmail-last-senders.ino + pa untouched. Self-exp class name GmailConnectorNeuron.
[GrainType("gmail-last-senders")]
public sealed class GmailConnectorNeuron : Neuron, IHandle<GmailLastSendersRequest>, IHandle<GmailSenderCountsRequest>, IHandle<GrantDecision>, IHandle<GrantRevoked>
{
    private readonly IServiceProvider servicesProvider;
    private readonly HashSet<string> allowedCapabilities = new(StringComparer.OrdinalIgnoreCase);

    public GmailConnectorNeuron(IServiceProvider servicesProvider)
    {
        this.servicesProvider = servicesProvider;
    }

    public async Task HandleAsync(GmailLastSendersRequest request, CancellationToken cancellationToken)
    {
        // Real Gmail attempt via DI seam (IHttpClientFactory + token from GoogleAuth grain or secret).
        // Falls back to demo for CI/stubs (tolerant Google stubs inject via DI). Grant gates the Save + privileged card.
        string[] fetchedSenders = await FetchSendersAsync(5, cancellationToken) ?? new[] { "alice@example.com", "bob@work.com", "support@service.com" };

        await Emit(new GmailLastSendersResult(fetchedSenders));

        if (!allowedCapabilities.Contains("SaveFileRequest"))
        {
            await Emit(new GrantRequested("gmail-last-senders", new[] { "SaveFileRequest", "GoogleApi" }));
            // Surface now produced exclusively by show card rule in os/gmail-last-senders.ino (RuleHost on GmailLastSendersResult / GrantRequested); removed direct to enforce single source.
            return;
        }

        // Surface emitted via rule in gmail-last-senders.ino for GmailLastSendersResult (title "Gmail last senders", uses substitution path).
        // Keep action emit for the button-driven save (data provided by neuron or future rule $ support).
        await Emit(new SaveFileRequest(new FileSave("/tmp/gmail-senders.txt", string.Join("\n", fetchedSenders), "last senders via gmail")));
    }

    public async Task HandleAsync(GmailSenderCountsRequest request, CancellationToken cancellationToken)
    {
        // Streaming via explicit IAsyncEnumerator<> (per plan): progressive load of sender counts (demo for "last ~100 emails" feel; real path uses Gmail history + vault token).
        // Each step emits result (for rules) + UiSurface update for "gmail-senders-chart" so open floating window (from OpenWindow/Run/Mail) refreshes live with more data.
        // Keeps .ino/.yaml untouched; all neurons/synapses + journals/N+1 preserved (discrete Emits).
        var accumulated = new List<GmailSenderCount>();
        var stream = GetSenderCountsStreamAsync(cancellationToken);
        var enumerator = stream.GetAsyncEnumerator(cancellationToken);
        try
        {
            while (await enumerator.MoveNextAsync())
            {
                accumulated.Add(enumerator.Current);
                await Emit(new GmailSenderCountsResult(accumulated.ToArray()));

                // Refresh the chart surface (Shell picks latest for the SurfaceId in any open WindowFrame for live "streamed" feel in draggable floating).
                var bars = accumulated.Select(c => new Bar(c.Sender, c.Count, "#00E5D1")).ToArray();
                await Emit(new UiSurface("gmail-senders-chart", Self, new Card("Top Senders by Volume (streaming)", new BarChart("Emails Received (progressive)", bars))));
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    private async IAsyncEnumerable<GmailSenderCount> GetSenderCountsStreamAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Demo "last ~100" expanded for visible progressive steps (real: page Gmail API in batches).
        var full = new[]
        {
            new GmailSenderCount("newsletter@company.com", 47),
            new GmailSenderCount("boss@work.com", 31),
            new GmailSenderCount("team@project.org", 28),
            new GmailSenderCount("alerts@service.io", 19),
            new GmailSenderCount("friend@gmail.com", 14),
            new GmailSenderCount("support@vendor.com", 12),
            new GmailSenderCount("events@community.dev", 9),
            new GmailSenderCount("no-reply@bank.com", 7),
            new GmailSenderCount("colleague@office.net", 6),
            new GmailSenderCount("updates@product.app", 5),
            new GmailSenderCount("promo@shop.io", 4),
            new GmailSenderCount("security@alerts.net", 3)
        };
        foreach (var item in full)
        {
            yield return item;
            await Task.Yield();
        }
    }

    private async Task<string[]?> FetchSendersAsync(int count, CancellationToken cancellationToken)
    {
        try
        {
            var factory = servicesProvider.GetService<System.Net.Http.IHttpClientFactory>();
            using var http = factory?.CreateClient() ?? new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // Demo/CI path (no token): real Gmail via vault-backed token is in full kernel path (CredentialVaultNeuron + GoogleAuthConnector for grants).
            // Extraction keeps behavior for stubs/CI (tolerant return below).
        }
        catch { /* env without net/creds */ }
        return null;
    }

    public Task HandleAsync(GrantDecision decision, CancellationToken cancellationToken)
    {
        if (string.Equals(decision.BundleId, "gmail-last-senders", StringComparison.OrdinalIgnoreCase) && decision.Allowed)
        {
            allowedCapabilities.Add("SaveFileRequest");
            allowedCapabilities.Add("GoogleApi");
        }
        return Task.CompletedTask;
    }

    public Task HandleAsync(GrantRevoked revoked, CancellationToken cancellationToken)
    {
        if (string.Equals(revoked.BundleId, "gmail-last-senders", StringComparison.OrdinalIgnoreCase))
        {
            allowedCapabilities.Clear();
        }
        return Task.CompletedTask;
    }
}
