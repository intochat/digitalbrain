using System.Text.Json.Serialization;

namespace DigitalBrain.Capabilities.Storage;

/// <summary>Whether a storage's space is known.</summary>
public enum QuotaState
{
    /// <summary>Used and total are known.</summary>
    Known,

    /// <summary>There is no limit; only used may be known.</summary>
    Unlimited,

    /// <summary>The storage does not say; render "unknown", never 0.</summary>
    Unknown,
}

/// <summary>
/// The space of a storage. Tri-state so that an unknown quota can never render as 0: <see cref="Unknown"/> has null
/// <see cref="Used"/> and <see cref="Total"/>.
/// </summary>
public sealed record Quota
{
    /// <summary>Creates a quota; the numbers must fit the state.</summary>
    /// <param name="State">Known, unlimited or unknown.</param>
    /// <param name="Used">Bytes used: required when known, optional when unlimited, null when unknown.</param>
    /// <param name="Total">Bytes available in total: required when known, null otherwise.</param>
    /// <param name="Pooled">True when the space is shared with other services (for example a cloud account pool).</param>
    [JsonConstructor]
    public Quota(QuotaState State, long? Used, long? Total, bool Pooled = false)
    {
        switch (State)
        {
            case QuotaState.Known:
                if (Used is not { } used || Total is not { } total)
                {
                    throw new ArgumentOutOfRangeException(nameof(Used), "A known quota needs used and total.");
                }
                ArgumentOutOfRangeException.ThrowIfNegative(used, nameof(Used));
                ArgumentOutOfRangeException.ThrowIfNegative(total, nameof(Total));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(used, total, nameof(Used));
                break;
            case QuotaState.Unlimited:
                if (Total is not null)
                {
                    throw new ArgumentOutOfRangeException(nameof(Total), "An unlimited quota has no total.");
                }
                if (Used is { } unlimitedUsed)
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(unlimitedUsed, nameof(Used));
                }
                break;
            case QuotaState.Unknown:
                if (Used is not null || Total is not null)
                {
                    throw new ArgumentOutOfRangeException(nameof(Used), "An unknown quota has no numbers.");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(State));
        }
        this.State = State;
        this.Used = Used;
        this.Total = Total;
        this.Pooled = Pooled;
    }

    /// <summary>Known, unlimited or unknown.</summary>
    public QuotaState State { get; }

    /// <summary>Bytes used, when known.</summary>
    public long? Used { get; }

    /// <summary>Bytes available in total, when known.</summary>
    public long? Total { get; }

    /// <summary>True when the space is shared with other services.</summary>
    public bool Pooled { get; }

    /// <summary>The storage does not report its space.</summary>
    public static Quota Unknown { get; } = new(QuotaState.Unknown, null, null);

    /// <summary>A known quota.</summary>
    public static Quota Known(long used, long total, bool pooled = false) => new(QuotaState.Known, used, total, pooled);

    /// <summary>An unlimited quota, with the bytes used when known.</summary>
    public static Quota Unlimited(long? used) => new(QuotaState.Unlimited, used, null);
}
