using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Scripting.Startup;

internal static class JournalWatch
{
    public static async IAsyncEnumerable<SignalDelivery> OutgoingAsync(
        Func<JournalKind, long, CancellationToken, Task<JournalRead>> read,
        Func<JournalKind, long, CancellationToken, IAsyncEnumerable<JournalRead>> watch,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var initial = await read(JournalKind.Outgoing, 0, cancellationToken);
        foreach (var delivery in initial.Delta)
        {
            yield return delivery;
        }

        await foreach (var page in watch(JournalKind.Outgoing, initial.ResumeSequence, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            foreach (var delivery in page.Delta)
            {
                yield return delivery;
            }
        }
    }
}
