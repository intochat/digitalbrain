using System.Text;

namespace DigitalBrain.Kernel.Visualization;

internal static class FlutterPerfProjection
{
    public static string ResolveTier(double p95FrameMs, FlutterPerfOptions opts)
    {
        if (p95FrameMs <= opts.P95SmoothMs) return "smooth";
        if (p95FrameMs <= opts.P95StrainedMs) return "strained";
        return "red";
    }

    public static (double P50, double P95, double Jank) Aggregate(
        IReadOnlyList<SampleSnapshot> window)
    {
        if (window.Count == 0) return (0, 0, 0);

        long totalFrames = 0;
        double weightedP50 = 0;
        double weightedJank = 0;
        double worstP95 = 0;

        foreach (var s in window)
        {
            totalFrames += s.FrameCount;
            weightedP50 += s.P50FrameMs * s.FrameCount;
            weightedJank += s.JankPct * s.FrameCount;
            if (s.P95FrameMs > worstP95) worstP95 = s.P95FrameMs;
        }

        if (totalFrames == 0) return (0, worstP95, 0);
        return (weightedP50 / totalFrames, worstP95, weightedJank / totalFrames);
    }

    public static string Signature(FlutterPerfCardPayload payload)
    {
        var sb = new StringBuilder();
        sb.Append(payload.Summary.Tier).Append('|')
          .Append(payload.Summary.ClientCount).Append('|')
          .Append(payload.Summary.AggregateP95Ms.ToString("F1")).Append('|')
          .Append(payload.Summary.AggregateJankPct.ToString("F3")).Append(';');
        foreach (var c in payload.Clients)
        {
            sb.Append(c.ClientId).Append(':')
              .Append(c.Tier).Append(':')
              .Append(c.P95FrameMs.ToString("F1")).Append(':')
              .Append(c.JankPct.ToString("F3")).Append(';');
        }
        return sb.ToString();
    }
}

internal sealed record SampleSnapshot(
    int FrameCount, double P50FrameMs, double P95FrameMs, double JankPct);

public sealed record FlutterPerfCardSummary(
    string Tier, int ClientCount, double AggregateP95Ms, double AggregateJankPct);

public sealed record ClientPerfRow(
    string ClientId, string Platform, string Tier,
    double P50FrameMs, double P95FrameMs, double JankPct,
    int WidgetCount, int GlowPainterCount, int RebuildsPerSecond);

public sealed record FlutterPerfCardPayload(
    FlutterPerfCardSummary Summary,
    IReadOnlyList<ClientPerfRow> Clients);
