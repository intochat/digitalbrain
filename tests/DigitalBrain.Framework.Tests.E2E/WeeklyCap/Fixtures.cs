using DigitalBrain.Rules;

namespace DigitalBrain.Framework.Tests.E2E.WeeklyCap;

[State("audit")]
public sealed record AuditState(int Calls = 0)
{
    public AuditState() : this(0) { }
}

[State("broken")]
public sealed record BrokenState(int Seen = 0)
{
    public BrokenState() : this(0) { }
}

// Extra gates the facts use beside the design sample.
public static class Fixtures
{
    [Synapse(typeof(Charging), "audit")]
    public static Check Audit(Charging s, AuditState a) => Check.Allow(a with { Calls = a.Calls + 1 });

    [Synapse(typeof(ChargingRisky), "broken")]
    public static Check Broken(ChargingRisky s, BrokenState b) => throw new InvalidOperationException("boom");
}
