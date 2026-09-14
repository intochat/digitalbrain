using DigitalBrain.Core;

namespace DigitalBrain.Testing;

// The lease a fact drives by hand: Holder decides whether this silo is live or standby, so a fact can
// pretend the other slot owns it and watch the fence close. Interleave models the table's 412.
public sealed class InMemoryActiveSlotLease(string slot, string? holder = null) : IActiveSlotLease
{
    private readonly Lock _gate = new();
    private string? _holder = holder ?? slot;
    private long _generation;
    private int _refreshes;

    public string Slot { get; } = slot;

    public string? Holder
    {
        get
        {
            lock (_gate)
            {
                return _holder;
            }
        }

        set
        {
            lock (_gate)
            {
                _holder = value;
                _generation++;
            }
        }
    }

    // Runs between the read and the write of TryAcquireAsync so a fact can make the swap lose its race.
    public Action? Interleave { get; set; }

    public bool HoldsLease
    {
        get
        {
            lock (_gate)
            {
                return string.Equals(_holder, Slot, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    public long Generation
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
    }

    public int Refreshes => Volatile.Read(ref _refreshes);

    public Task<bool> TryAcquireAsync(string slot, CancellationToken cancellationToken = default)
    {
        long observed;
        lock (_gate)
        {
            observed = _generation;
        }

        Interleave?.Invoke();
        lock (_gate)
        {
            if (observed != _generation)
            {
                return Task.FromResult(false);
            }

            _holder = slot;
            _generation++;
            return Task.FromResult(true);
        }
    }

    public Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (string.Equals(_holder, Slot, StringComparison.OrdinalIgnoreCase))
            {
                _holder = null;
                _generation++;
            }
        }

        return Task.CompletedTask;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _refreshes);
        return Task.CompletedTask;
    }
}
