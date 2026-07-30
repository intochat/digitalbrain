using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.TestingTests.Harness;

public sealed class ReenteringJournalWatcher : Neuron, IReenteringJournalWatcher
{
    private string? _subjectName;
    private int _reentries;

    public Task Arm(string subjectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);
        _subjectName = subjectName;
        _reentries = 0;
        return Task.CompletedTask;
    }

    public Task<int> Reentries() => Task.FromResult(_reentries);

    public async Task ObserveAsync(JournalKind kind, JournalRead read)
    {
        ArgumentNullException.ThrowIfNull(read);

        if (_subjectName is null)
        {
            return;
        }

        var subject = GrainFactory.GetGrain<IGreeter>(
            NeuronId.For<IGreeter>(Id.Owner, _subjectName).ToGrainId());
        _ = await subject.ReadJournal(kind, afterSequence: 0);
        Interlocked.Increment(ref _reentries);
    }
}
