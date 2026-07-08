using DigitalBrain.Core;
using DigitalBrain.Kernel.Foundry;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace DigitalBrain.Kernel;

/// Poll trigger (reminder-driven): projects reactions (When containing "poll" or "Poll"), uses ICapabilityBroker for
/// sanctioned HTTP/RSS fetch, simple cursor/dedup via journals, emits per new item as trigger.poll.* signals.
[GrainType("poll-trigger.v1")]
public class PollTriggerNeuron(ILogger<PollTriggerNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IRemindable
{
    protected override bool ShouldSubscribeToTimeline => true;

    private List<RegisterReaction> _polls = new();
    private HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase); // simple dedup, enhanced from journals on replay
    private Dictionary<string, IGrainReminder> _reminders = new(StringComparer.OrdinalIgnoreCase);

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        EnsurePolls();
        foreach (var p in _polls) await RegisterPollReminder(p, cancellationToken);
    }

    public override async Task OnNextAsync(Synapse item, StreamSequenceToken? token = null)
    {
        await RecordBroadcastReceivedAsync(item);
        EnsurePolls();
        await DispatchSynapse(item);
    }

    protected void EnsurePolls()
    {
        var removes = OutgoingJournal.Concat(IncomingJournal).OfType<RemoveReaction>().Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _polls = OutgoingJournal.Concat(IncomingJournal)
            .OfType<RegisterReaction>()
            .Where(r => !removes.Contains(r.Id) && IsPollReaction(r))
            .ToList();
    }

    private static bool IsPollReaction(RegisterReaction r) =>
        !string.IsNullOrWhiteSpace(r.When) && r.When.Contains("poll", StringComparison.OrdinalIgnoreCase);

    private async Task RegisterPollReminder(RegisterReaction p, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_reminders.ContainsKey(p.Id))
        {
            var rem = await this.RegisterOrUpdateReminder(p.Id, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5));
            _reminders[p.Id] = rem;
        }
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        Logger.LogInformation("Poll reminder {Name} ticked", reminderName);
        EnsurePolls();
        var reaction = _polls.FirstOrDefault(r => r.Id == reminderName);
        if (reaction is null) return;

        var broker = ServiceProvider.GetService<ICapabilityBroker>();
        string content = string.Empty;
        string source = reaction.Target ?? reaction.When;
        try
        {
            if (broker != null && !string.IsNullOrWhiteSpace(source) && (source.StartsWith("http", StringComparison.OrdinalIgnoreCase)))
            {
                content = await broker.HttpGetAsync(source);
            }
            else
            {
                content = $"<poll-source>{source}</poll-source>";
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Poll fetch failed for {Id}", reminderName);
            content = "fetch-error";
        }

        // Simple dedup key (hash snippet); real would parse RSS items by guid/link.
        var dedup = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content.Length > 200 ? content[..200] : content)))[..16];
        if (_seen.Add(dedup))
        {
            await FireAsync(new Signal("trigger.poll." + reminderName, new Dictionary<string, object?>
            {
                ["source"] = source,
                ["item"] = content.Length > 280 ? content[..280] + "..." : content,
                ["dedup"] = dedup
            }));
        }
    }

    protected override async Task DispatchSynapse(Synapse synapse, CancellationToken cancellationToken = default)
    {
        await base.DispatchSynapse(synapse, cancellationToken);
        switch (synapse)
        {
            case RegisterReaction rr when IsPollReaction(rr):
                _polls.Add(rr);
                await RegisterPollReminder(rr, cancellationToken);
                break;
            case RemoveReaction rm:
                _polls.RemoveAll(r => r.Id == rm.Id);
                if (_reminders.Remove(rm.Id, out var rem)) { try { await this.UnregisterReminder(rem); } catch { } }
                break;
        }
    }
}
