import 'package:flutter/widgets.dart';

import 'glass_material.dart';

class LiquidGlassSurface extends StatefulWidget {
  const LiquidGlassSurface({
    required this.child,
    this.morphFrom,
    this.cornerRadius = 28,
    this.tintOpacity = 0.04,
    this.collapsing = false,
    this.onCollapsed,
    super.key,
  });

  final Widget child;
  final Offset? morphFrom;
  final double cornerRadius;
  final double tintOpacity;
  final bool collapsing;
  final VoidCallback? onCollapsed;

  @override
  State<LiquidGlassSurface> createState() => _LiquidGlassSurfaceState();
}

class _LiquidGlassSurfaceState extends State<LiquidGlassSurface>
    with SingleTickerProviderStateMixin {
  late final AnimationController _ctrl;

  static const _kCurve = Curves.easeOutCubic;
  static const _kSeedRadius = 7.0;
  static const _kMorphDuration = Duration(milliseconds: 800);

  bool get _shouldMorph {
    if (widget.morphFrom == null) return false;
    final mq = MediaQuery.maybeOf(context);
    if (mq == null) return true;
    return !mq.disableAnimations;
  }

  @override
  void initState() {
    super.initState();
    _ctrl = AnimationController(vsync: this, duration: _kMorphDuration);
    WidgetsBinding.instance.addPostFrameCallback(_kickOff);
  }

  @override
  void didUpdateWidget(LiquidGlassSurface old) {
    super.didUpdateWidget(old);
    if (widget.collapsing && !old.collapsing) {
      if (_shouldMorph) {
        _ctrl.reverse().whenComplete(() {
          if (mounted) widget.onCollapsed?.call();
        });
      } else {
        _ctrl.value = 0;
        widget.onCollapsed?.call();
      }
      return;
    }
    // Re-trigger the morph when morphFrom changes (e.g. user closed and
    // re-opened the surface from a different origin point).
    if (widget.morphFrom != old.morphFrom) {
      WidgetsBinding.instance.addPostFrameCallback(_kickOff);
    }
  }

  void _kickOff(Duration _) {
    if (!mounted) return;
    if (_shouldMorph) {
      _ctrl.forward(from: 0);
    } else {
      _ctrl.value = 1.0;
    }
  }

  @override
  void dispose() {
    _ctrl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        return AnimatedBuilder(
          animation: _ctrl,
          builder: (context, _) {
            final t = _kCurve.transform(_ctrl.value);
            final fullW = constraints.maxWidth;
            final fullH = constraints.maxHeight;

            final origin = widget.morphFrom ?? Offset(fullW / 2, fullH / 2);
            final clampedOrigin = Offset(
              origin.dx.clamp(0.0, fullW),
              origin.dy.clamp(0.0, fullH),
            );

            final w = _lerp(_kSeedRadius * 2, fullW, t);
            final h = _lerp(_kSeedRadius * 2, fullH, t);
            final left = _lerp(clampedOrigin.dx - _kSeedRadius, 0, t);
            final top = _lerp(clampedOrigin.dy - _kSeedRadius, 0, t);
            final radius = _lerp(_kSeedRadius, widget.cornerRadius, t);

            // Child fades in over the latter half (450..800 ms / 800 ms = 0.5625..1.0).
            final childOpacity = ((_ctrl.value - 0.5625) / (1.0 - 0.5625))
                .clamp(0.0, 1.0);

            return Stack(
              children: [
                Positioned(
                  left: left,
                  top: top,
                  width: w,
                  height: h,
                  child: GlassMaterial(
                    cornerRadius: radius,
                    tintOpacity: widget.tintOpacity,
                    blurSigma: 22 * t,
                    child: Opacity(opacity: childOpacity, child: widget.child),
                  ),
                ),
              ],
            );
          },
        );
      },
    );
  }

  static double _lerp(double a, double b, double t) => a + (b - a) * t;
}
