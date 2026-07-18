using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Visualization;

// One sample window's worth of Flutter rendering health pushed up by
// digital_brain_sdk_flutter at the SDK's sample period (default 1 s).
// No `Path` field — the project's prior slice locked the rule that we
// avoid `Path` so it doesn't collide with System.IO.Path.
[GenerateSerializer]
public sealed record FlutterPerfSample([property: Id(1)] string ClientId,
    [property: Id(2)] string SampleWindowId,
    [property: Id(3)] int FrameCount,
    [property: Id(4)] double P50FrameMs,
    [property: Id(5)] double P95FrameMs,
    [property: Id(6)] double JankPct,
    [property: Id(7)] int WidgetCount,
    [property: Id(8)] int GlowPainterCount,
    [property: Id(9)] int RebuildsPerSecond,
    [property: Id(10)] string Platform
) : Synapse;
