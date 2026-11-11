using DigitalBrain.Kernel;
using DigitalBrain.Runtime.Orleans.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Runtime.Orleans.Synapses;

internal sealed class JournalCursorLookup(
    IServiceProvider services,
    IOptions<RetentionOptions> options,
    TimeProvider time) : ISynapseCursorLookup
{
    public ValueTask<long?> LowestLive(string brainKey, NeuronId publisher)
    {
        var logs = services.GetService<BrainLogs>();
        if (logs is null || logs.TryOpen(brainKey) is not { } log)
        {
            return ValueTask.FromResult<long?>(null);
        }

        var cutoff = time.GetUtcNow() - options.Value.StaleCursorAfter;
        var lowest = log.Subscriptions.LowestLive(publisher, cutoff);
        if (lowest is null) { return ValueTask.FromResult<long?>(null); }
        var sequence = log.SequenceAt(publisher, lowest.Value);
        return ValueTask.FromResult<long?>(sequence);
    }
}
