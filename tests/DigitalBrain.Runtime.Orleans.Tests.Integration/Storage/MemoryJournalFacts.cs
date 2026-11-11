using DigitalBrain.Kernel;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;

internal sealed class MemoryJournalTarget : IJournalTarget
{
    private readonly MemoryJournal _journal;

    public MemoryJournalTarget()
    {
        _journal = new MemoryJournal(new BrainId("b1"), new ManualTime());
    }

    public IBrainJournal Journal => _journal;
    public IJournalView View => _journal;

    public Task Reopen()
    {
        var copy = new MemoryJournal(new BrainId("b1"), new ManualTime());
        copy.RestoreSnapshot(_journal);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class MemoryJournalFacts : JournalConformance
{
    internal override Task<IJournalTarget> CreateTargetAsync()
        => Task.FromResult<IJournalTarget>(new MemoryJournalTarget());
}
