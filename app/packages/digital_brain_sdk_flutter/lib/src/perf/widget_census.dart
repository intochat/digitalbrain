import 'package:flutter/widgets.dart';

class WidgetCensusSnapshot {
  const WidgetCensusSnapshot({
    required this.widgetCount,
    required this.glowPainterCount,
  });
  static const empty = WidgetCensusSnapshot(
    widgetCount: 0,
    glowPainterCount: 0,
  );
  final int widgetCount;
  final int glowPainterCount;
}

class WidgetCensus {
  static Type? glowIconType;


  static WidgetCensusSnapshot capture(WidgetsBinding binding) {
    final root = binding.rootElement;
    if (root == null) return WidgetCensusSnapshot.empty;
    var widgetCount = 0;
    var glowPainterCount = 0;
    final stack = <Element>[root];
    while (stack.isNotEmpty) {
      final e = stack.removeLast();
      widgetCount++;


      final type = e.widget.runtimeType;
      if (glowIconType != null) {
        if (type == glowIconType) {
          glowPainterCount++;
        }
      } else if (type.toString() == 'GlowIcon') {
        glowPainterCount++;
      }
      e.visitChildren(stack.add);
    }
    return WidgetCensusSnapshot(
      widgetCount: widgetCount,
      glowPainterCount: glowPainterCount,
    );
  }
}
