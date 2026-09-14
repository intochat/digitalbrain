using System.Globalization;

namespace DigitalBrain.Core;

// {slot}-{timestamp}: unique per start, so a slot restarting after a crash never reuses its previous
// incarnation's ClusterId and stalls membership on that incarnation's dead row (R5.3, spike S2).
public static class ClusterIdMinting
{
    public static string Mint(string slot, DateTimeOffset now)
        => slot + "-" + now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
}
