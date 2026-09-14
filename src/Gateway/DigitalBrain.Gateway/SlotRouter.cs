using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace DigitalBrain.Gateway;

// One route, two clusters. A switch rebuilds the route with the other cluster's id and hands YARP a new
// snapshot: requests already routed finish against the destination they were routed to, and new ones go to
// the new slot (spike S4). YARP rebuilds its whole route table per update, so switches are debounced.
internal sealed class SlotRouter
{
    private const string RouteId = "active-slot";
    private const string DestinationId = "slot";

    private readonly GatewayOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<SlotRouter> _logger;
    private readonly ClusterConfig[] _clusters;
    private readonly Lock _gate = new();
    private string _active;
    private DateTimeOffset _switchedAt = DateTimeOffset.MinValue;

    public SlotRouter(GatewayOptions options, TimeProvider clock, ILogger<SlotRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _clock = clock;
        _logger = logger;
        _clusters = [.. options.Slots.Select(static slot => new ClusterConfig
        {
            ClusterId = ClusterFor(slot.Key),
            Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
            {
                [DestinationId] = new DestinationConfig { Address = slot.Value.AbsoluteUri },
            },
        })];
        _active = options.Active;
        Provider = new InMemoryConfigProvider([RouteFor(_active)], _clusters);
    }

    public InMemoryConfigProvider Provider { get; }

    public string Active => Volatile.Read(ref _active);

    public TimeSpan MinSwitchInterval => _options.MinSwitchInterval;

    public bool Knows(string slot) => _options.Slots.ContainsKey(slot);

    // Used once at startup, before the server accepts anything, so it needs no debounce and no lock.
    public void Adopt(string slot)
    {
        if (!Knows(slot) || string.Equals(_active, slot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Volatile.Write(ref _active, slot);
        Provider.Update([RouteFor(slot)], _clusters);
        _logger.LogInformation("The lease row names slot {Slot}; the gateway starts out routing to it.", slot);
    }

    // Nothing here waits: a gateway that sleeps inside a request is a gateway that stops answering. An
    // early re-switch is reported back with the time left, and the caller decides what to do with it.
    public SwitchResult Switch(string slot)
    {
        if (!Knows(slot))
        {
            return new SwitchResult(SwitchVerdict.Unknown, TimeSpan.Zero);
        }

        lock (_gate)
        {
            if (string.Equals(_active, slot, StringComparison.OrdinalIgnoreCase))
            {
                return new SwitchResult(SwitchVerdict.Switched, TimeSpan.Zero);
            }

            var since = _clock.GetUtcNow() - _switchedAt;
            if (since < _options.MinSwitchInterval)
            {
                // YARP rebuilds its whole route table per update and documents one update per 15s as the
                // ceiling (spike S4).
                return new SwitchResult(SwitchVerdict.TooSoon, _options.MinSwitchInterval - since);
            }

            Volatile.Write(ref _active, slot);
            _switchedAt = _clock.GetUtcNow();
            Provider.Update([RouteFor(slot)], _clusters);
            _logger.LogInformation("The active slot is now {Slot}.", slot);
            return new SwitchResult(SwitchVerdict.Switched, TimeSpan.Zero);
        }
    }

    private static RouteConfig RouteFor(string slot) => new()
    {
        RouteId = RouteId,
        ClusterId = ClusterFor(slot),
        Match = new RouteMatch { Path = "{**catch-all}" },
    };

    private static string ClusterFor(string slot) => "slot-" + slot;
}

internal enum SwitchVerdict
{
    Switched = 0,
    Unknown = 1,
    TooSoon = 2,
}

internal sealed record SwitchResult(SwitchVerdict Verdict, TimeSpan RetryAfter);
