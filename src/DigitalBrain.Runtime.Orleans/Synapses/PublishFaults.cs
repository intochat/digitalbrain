namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// Test-only seam (04-12, 04-15): registered as a singleton, no-op by default; only facts set it, through
/// <c>RuntimeTestHooks</c>. No topology class touches it.
/// </summary>
internal sealed class PublishFaults
{
    private string? _failNext;

    /// <summary>While set, <see cref="SignalPublisher.Publish"/> throws after the journal append and before any stream item is sent.</summary>
    public bool FailAfterAppend { get; set; }

    /// <summary>Makes exactly the next <see cref="SignalPublisher.Publish"/> of <paramref name="publisher"/> throw after its journal append (a kill between write and publish).</summary>
    public void FailNextAfterAppend(NeuronId publisher) => Volatile.Write(ref _failNext, publisher.ToString());

    /// <summary>Sends every reference twice, as an at-least-once stream may.</summary>
    public bool DuplicateEach { get; set; }

    internal bool ShouldFail(NeuronId publisher)
    {
        if (FailAfterAppend) { return true; }
        var armed = Volatile.Read(ref _failNext);
        return armed is not null && armed == publisher.ToString() && Interlocked.CompareExchange(ref _failNext, null, armed) == armed;
    }
}
