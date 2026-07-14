import 'dart:ui' as ui;

import 'package:digital_brain_sdk_flutter/digital_brain_sdk_flutter.dart'
    as perf;
import 'package:flutter/material.dart';

import 'glow_icon_spec.dart';
import 'glow_painter.dart';

class GlowIcon extends StatelessWidget {
  const GlowIcon({required this.spec, super.key});

  final GlowIconSpec spec;

  static final Map<GlowIconSpec, ui.Image> _cache = {};
  static const int _kCacheLimit = 256;

  @override
  Widget build(BuildContext context) {
    final cached = _cache[spec];
    if (cached != null) {
      return RawImage(
        image: cached,
        width: spec.size,
        height: spec.size,
        fit: BoxFit.contain,
      );
    }
    final tier =
        perf.PerfTierScope.maybeOf(context)?.current ?? perf.PerfTier.smooth;
    return SizedBox(
      width: spec.size,
      height: spec.size,
      child: CustomPaint(
        painter: GlowPainter(
          spec,
          dotCount: perf.glowPainterDotCount(tier),
          blurSigma: perf.glowPainterBlurSigma(tier),
        ),
      ),
    );
  }

  static Future<void> prewarm(Iterable<GlowIconSpec> specs) async {
    final smoothDotCount = perf.glowPainterDotCount(perf.PerfTier.smooth);
    final smoothBlurSigma = perf.glowPainterBlurSigma(perf.PerfTier.smooth);
    for (final spec in specs) {
      if (_cache.containsKey(spec)) continue;
      final recorder = ui.PictureRecorder();
      final canvas = Canvas(recorder);
      GlowPainter(
        spec,
        dotCount: smoothDotCount,
        blurSigma: smoothBlurSigma,
      ).paint(canvas, Size(spec.size, spec.size));
      final pic = recorder.endRecording();
      final image = await pic.toImage(spec.size.toInt(), spec.size.toInt());
      _cache[spec] = image;
      if (_cache.length > _kCacheLimit) _cache.remove(_cache.keys.first);
    }
  }
}
