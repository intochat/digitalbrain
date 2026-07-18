import 'dart:async';
import 'dart:io' show Platform;

import 'package:flutter/foundation.dart' show kIsWeb, kReleaseMode;
import 'package:flutter/scheduler.dart';
import 'package:flutter/widgets.dart';

import 'perf_sample.dart';
import 'perf_stream.dart';
import 'widget_census.dart';

class PerfProbe extends StatefulWidget {
  const PerfProbe({
    required this.stream,
    required this.child,
    this.samplePeriod = const Duration(seconds: 1),
    this.censusEveryNFlushes = 5,
    super.key,
  });
  final PerfStream stream;
  final Widget child;
  final Duration samplePeriod;

  // Element-tree DFS dominates per-sample cost; subsample to keep the UI
  // isolate quiet. At samplePeriod=1s, default 5 → census at 0.2 Hz.
  final int censusEveryNFlushes;

  @override
  State<PerfProbe> createState() => _PerfProbeState();
}

class _PerfProbeState extends State<PerfProbe> {
  final _frameTimes = <Duration>[];
  Timer? _ticker;
  int _rebuildAccumulator = 0;
  int _flushCounter = 0;
  WidgetCensusSnapshot _lastCensus = WidgetCensusSnapshot.empty;

  @override
  void initState() {
    super.initState();
    // Skip probe work in release. Tier downgrade still flows independently
    // via PerfStream.bootstrap's watchHints, owned by main.dart.
    if (kReleaseMode) return;
    SchedulerBinding.instance.addTimingsCallback(_onTimings);
    _ticker = Timer.periodic(widget.samplePeriod, (_) => _flush());
  }

  @override
  void dispose() {
    if (!kReleaseMode) {
      SchedulerBinding.instance.removeTimingsCallback(_onTimings);
      _ticker?.cancel();
    }
    super.dispose();
  }

  void _onTimings(List<FrameTiming> timings) {
    for (final t in timings) {
      _frameTimes.add(t.totalSpan);
      _rebuildAccumulator++;
    }
  }

  void _flush() {
    if (_frameTimes.isEmpty) return;
    final sortedMs = _frameTimes.map((d) => d.inMicroseconds / 1000.0).toList()
      ..sort();
    final p50 = sortedMs[sortedMs.length ~/ 2];
    final p95Idx = ((sortedMs.length - 1) * 0.95).floor();
    final p95 = sortedMs[p95Idx];
    final jankCount = sortedMs.where((m) => m > 16.0).length;
    final jankPct = jankCount / sortedMs.length;
    _flushCounter++;
    // First flush captures so the published widgetCount is meaningful from
    // sample 1; subsequent captures every Nth flush.
    final isFirstFlush = _lastCensus.widgetCount == 0;
    if (isFirstFlush || _flushCounter % widget.censusEveryNFlushes == 0) {
      _lastCensus = WidgetCensus.capture(WidgetsBinding.instance);
    }
    final platform = kIsWeb
        ? 'web'
        : (Platform.isWindows ? 'windows' : 'other');
    final sample = PerfSample(
      clientId: widget.stream.clientId,
      sampleWindowId: DateTime.now().microsecondsSinceEpoch.toString(),
      frameCount: sortedMs.length,
      p50FrameMs: p50,
      p95FrameMs: p95,
      jankPct: jankPct,
      widgetCount: _lastCensus.widgetCount,
      glowPainterCount: _lastCensus.glowPainterCount,
      rebuildsPerSecond:
          (_rebuildAccumulator * 1000) ~/ widget.samplePeriod.inMilliseconds,
      platform: platform,
      timestamp: DateTime.now().toUtc(),
    );
    widget.stream.push(sample);
    _frameTimes.clear();
    _rebuildAccumulator = 0;
  }

  @override
  Widget build(BuildContext context) => widget.child;
}
