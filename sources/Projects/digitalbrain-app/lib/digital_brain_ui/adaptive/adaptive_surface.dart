import 'package:flutter/widgets.dart';

import '../breakpoints/window_size.dart';
import '../breakpoints/window_size_scope.dart';
import '../glass/liquid_glass_surface.dart';
import 'adaptive_dialog.dart';
import 'adaptive_sheet.dart';
import 'adaptive_side_sheet.dart';

/// Describes the conceptual "weight" of a surface — how much screen space and
/// visual prominence it should command at each window size.
enum SurfaceWeight {
  /// Lightweight overlays: tooltips, pickers, short confirmations.
  light,

  /// Mid-weight content: settings panels, detail views, form editors.
  medium,

  /// Heavy content: immersive editors, full-context views, multi-step flows.
  heavy,
}

/// Routes an overlay to the correct container widget by consulting the
/// `(SurfaceWeight × WindowSize)` dispatch table.
///
/// Place this widget where you would normally show a modal; it picks
/// [AdaptiveSheet], [AdaptiveDialog], [AdaptiveSideSheet], or
/// [_FullScreenRoute] automatically so that each weight reads correctly
/// on every breakpoint.
///
/// [morphFrom] anchors the Liquid Glass entrance — pass the screen-space
/// origin (e.g. the tapped neuron's projected center) and the wrapped child
/// expands from that point. Pass null to skip the morph.
class AdaptiveSurface extends StatelessWidget {
  const AdaptiveSurface({
    required this.weight,
    required this.child,
    required this.onDismiss,
    this.morphFrom,
    this.onDismissEffect,
    super.key,
  });

  final SurfaceWeight weight;
  final Widget child;
  final VoidCallback onDismiss;

  /// Screen-space origin forwarded to [LiquidGlassSurface].
  final Offset? morphFrom;

  /// Called with the dismiss origin before [onDismiss]. Use to fire a
  /// brain-side CollapseWave effect. Falls back to screen center when
  /// [morphFrom] is null.
  final void Function(Offset origin)? onDismissEffect;

  VoidCallback _wrapDismiss(BuildContext context) {
    return () {
      if (onDismissEffect != null) {
        final size = MediaQuery.sizeOf(context);
        onDismissEffect!(morphFrom ?? Offset(size.width / 2, size.height / 2));
      }
      onDismiss();
    };
  }

  @override
  Widget build(BuildContext context) {
    final size = WindowSizeContext.of(context);
    return _dispatch(context, size);
  }

  Widget _dispatch(BuildContext context, WindowSize size) {
    final body = morphFrom == null
        ? child
        : LiquidGlassSurface(morphFrom: morphFrom, child: child);

    final dismiss = _wrapDismiss(context);

    return switch ((weight, size)) {
      // Light: bottom sheet on compact; centered dialog everywhere else.
      (SurfaceWeight.light, WindowSize.compact) => AdaptiveSheet(
        onDismiss: dismiss,
        child: body,
      ),
      (SurfaceWeight.light, _) => AdaptiveDialog(
        onDismiss: dismiss,
        widthFraction: 0.42,
        heightFraction: 0.50,
        child: body,
      ),

      // Medium: full-bleed sheet on compact; wide dialog through expanded;
      // side-sheet on large+.
      (SurfaceWeight.medium, WindowSize.compact) => AdaptiveSheet(
        onDismiss: dismiss,
        fullBleed: true,
        child: body,
      ),
      (SurfaceWeight.medium, WindowSize.medium) => AdaptiveDialog(
        onDismiss: dismiss,
        widthFraction: 0.74,
        heightFraction: 0.66,
        child: body,
      ),
      (SurfaceWeight.medium, WindowSize.expanded) => AdaptiveDialog(
        onDismiss: dismiss,
        widthFraction: 0.60,
        heightFraction: 0.66,
        child: body,
      ),
      (SurfaceWeight.medium, WindowSize.large) => AdaptiveSideSheet(
        onDismiss: dismiss,
        widthFraction: 0.48,
        child: body,
      ),
      (SurfaceWeight.medium, WindowSize.xLarge) => AdaptiveSideSheet(
        onDismiss: dismiss,
        widthFraction: 0.40,
        child: body,
      ),

      // Heavy: full-screen route on compact+medium; 90% dialog on expanded;
      // wide side-sheet on large+.
      (SurfaceWeight.heavy, WindowSize.compact) => _FullScreenRoute(
        onDismiss: dismiss,
        child: body,
      ),
      (SurfaceWeight.heavy, WindowSize.medium) => _FullScreenRoute(
        onDismiss: dismiss,
        child: body,
      ),
      (SurfaceWeight.heavy, WindowSize.expanded) => AdaptiveDialog(
        onDismiss: dismiss,
        widthFraction: 0.90,
        heightFraction: 0.90,
        child: body,
      ),
      (SurfaceWeight.heavy, WindowSize.large) => AdaptiveSideSheet(
        onDismiss: dismiss,
        widthFraction: 0.60,
        child: body,
      ),
      (SurfaceWeight.heavy, WindowSize.xLarge) => AdaptiveSideSheet(
        onDismiss: dismiss,
        widthFraction: 0.55,
        child: body,
      ),
    };
  }
}

class _FullScreenRoute extends StatelessWidget {
  const _FullScreenRoute({required this.child, required this.onDismiss});
  final Widget child;
  final VoidCallback onDismiss;

  @override
  Widget build(BuildContext context) {
    final mq = MediaQuery.sizeOf(context);
    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (_, _) => onDismiss(),
      child: SizedBox(width: mq.width, height: mq.height, child: child),
    );
  }
}
