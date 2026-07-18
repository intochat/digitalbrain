import 'package:flutter/cupertino.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

/// Bottom sheet that adapts to the platform.
/// On iOS / macOS → [CupertinoPopupSurface] (native vibrancy material).
/// Everywhere else → Material-style container with a grab-handle pill.
///
/// [fullBleed] removes the top scrim gap; pass `true` for medium@compact weight.
/// Scrim-tap calls [onDismiss]; tap inside the sheet body does NOT dismiss.
class AdaptiveSheet extends StatelessWidget {
  const AdaptiveSheet({
    required this.child,
    required this.onDismiss,
    this.fullBleed = false,
    super.key,
  });

  final Widget child;
  final VoidCallback onDismiss;
  final bool fullBleed;

  bool get _isCupertino =>
      defaultTargetPlatform == TargetPlatform.iOS ||
      defaultTargetPlatform == TargetPlatform.macOS;

  @override
  Widget build(BuildContext context) {
    if (_isCupertino) {
      return _CupertinoInlineSheet(
        onDismiss: onDismiss,
        fullBleed: fullBleed,
        child: child,
      );
    }

    final mq = MediaQuery.sizeOf(context);
    final topInset = fullBleed ? 0.0 : 48.0;

    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: onDismiss,
      child: ColoredBox(
        color: Colors.black54,
        child: GestureDetector(
          onTap: () {},
          onVerticalDragEnd: (d) {
            // Flick-down dismiss: positive primaryVelocity = downward fling.
            if (d.primaryVelocity != null && d.primaryVelocity! > 300) {
              onDismiss();
            }
          },
          child: Align(
            alignment: Alignment.bottomCenter,
            child: SizedBox(
              width: mq.width,
              height: mq.height - topInset,
              child: ClipRRect(
                borderRadius: const BorderRadius.vertical(
                  top: Radius.circular(20),
                ),
                child: Material(
                  color: Theme.of(context).colorScheme.surfaceContainerHigh,
                  child: Column(
                    children: [
                      const SizedBox(height: 8),
                      Container(
                        width: 36,
                        height: 4,
                        decoration: BoxDecoration(
                          color: Colors.white24,
                          borderRadius: BorderRadius.circular(2),
                        ),
                      ),
                      Expanded(child: child),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _CupertinoInlineSheet extends StatelessWidget {
  const _CupertinoInlineSheet({
    required this.child,
    required this.onDismiss,
    required this.fullBleed,
  });

  final Widget child;
  final VoidCallback onDismiss;
  final bool fullBleed;

  @override
  Widget build(BuildContext context) {
    final mq = MediaQuery.sizeOf(context);
    final topInset = fullBleed ? 0.0 : 56.0;

    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: onDismiss,
      child: ColoredBox(
        color: const Color(0x66000000),
        child: GestureDetector(
          onTap: () {},
          child: Align(
            alignment: Alignment.bottomCenter,
            child: SizedBox(
              width: mq.width,
              height: mq.height - topInset,
              child: CupertinoPopupSurface(child: child),
            ),
          ),
        ),
      ),
    );
  }
}
