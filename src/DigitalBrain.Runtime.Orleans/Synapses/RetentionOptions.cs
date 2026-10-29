namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>Retention of signal history, bound from <c>IntoChatOS:Signalling:Retention</c> (04 D-11).</summary>
public sealed class RetentionOptions
{
    /// <summary>History younger than this is kept. Default 7 days.</summary>
    public TimeSpan HistoryWindowAge { get; set; } = TimeSpan.FromDays(7);

    /// <summary>At least this many newest signals per publisher are kept. Default 10 000.</summary>
    public int HistoryWindowSignals { get; set; } = 10_000;

    /// <summary>A cursor not reported for this long stops pinning the floor. Default 7 days.</summary>
    public TimeSpan StaleCursorAfter { get; set; } = TimeSpan.FromDays(7);
}
