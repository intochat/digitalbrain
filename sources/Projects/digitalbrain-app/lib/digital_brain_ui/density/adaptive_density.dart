import 'package:flutter/material.dart';

import '../breakpoints/input_mode.dart';
import '../breakpoints/window_size.dart';
import '../breakpoints/window_size_scope.dart';

/// Spacing tokens used by every adaptive surface and overlay.
/// Tighter on compact+touch, looser on large+pointer.
class AdaptiveSpacing {
  const AdaptiveSpacing._({
    required this.xs,
    required this.s,
    required this.m,
    required this.l,
    required this.xl,
  });

  final double xs;
  final double s;
  final double m;
  final double l;
  final double xl;

  static AdaptiveSpacing of(BuildContext context) {
    final size = WindowSizeContext.of(context);
    final mode = InputModeContext.of(context);
    final tight = size == WindowSize.compact || mode == InputMode.touch;
    return tight
        ? const AdaptiveSpacing._(xs: 4, s: 8, m: 12, l: 16, xl: 24)
        : const AdaptiveSpacing._(xs: 6, s: 10, m: 16, l: 24, xl: 40);
  }
}

/// VisualDensity per (WindowSize, InputMode). Replaces the blanket
/// `VisualDensity.adaptivePlatformDensity` in buildDigitalBrainTheme.
VisualDensity adaptiveVisualDensity(BuildContext context) {
  final size = WindowSizeContext.of(context);
  final mode = InputModeContext.of(context);
  if (mode == InputMode.touch) return VisualDensity.comfortable;
  if (size == WindowSize.compact) return VisualDensity.standard;
  return VisualDensity.compact;
}
