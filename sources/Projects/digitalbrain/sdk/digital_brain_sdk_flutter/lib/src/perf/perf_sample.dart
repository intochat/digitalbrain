class PerfSample {
  PerfSample({
    required this.clientId,
    required this.sampleWindowId,
    required this.frameCount,
    required this.p50FrameMs,
    required this.p95FrameMs,
    required this.jankPct,
    required this.widgetCount,
    required this.glowPainterCount,
    required this.rebuildsPerSecond,
    required this.platform,
    required this.timestamp,
  });
  final String clientId;
  final String sampleWindowId;
  final int frameCount;
  final double p50FrameMs;
  final double p95FrameMs;
  final double jankPct;
  final int widgetCount;
  final int glowPainterCount;
  final int rebuildsPerSecond;
  final String platform;
  final DateTime timestamp;
}
