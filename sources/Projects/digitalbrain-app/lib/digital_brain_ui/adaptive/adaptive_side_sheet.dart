import 'package:flutter/material.dart';
import '../glass/glass_material.dart';

/// Material 3 doesn't ship a SideSheet. This renders a right-docked panel with
/// a left-edge scrim that swipes shut on compact/medium.
class AdaptiveSideSheet extends StatelessWidget {
  const AdaptiveSideSheet({
    required this.child,
    required this.onDismiss,
    this.widthFraction = 0.48,
    super.key,
  });

  final Widget child;
  final VoidCallback onDismiss;
  final double widthFraction;

  @override
  Widget build(BuildContext context) {
    final mq = MediaQuery.sizeOf(context);
    final w = mq.width * widthFraction;
    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: onDismiss,
      child: ColoredBox(
        color: Colors.black.withValues(alpha: 0.3),
        child: Align(
          alignment: Alignment.centerRight,
          child: SizedBox(
            width: w,
            height: mq.height,
            child: GestureDetector(
              onTap: () {},
              onHorizontalDragEnd: (d) {
                if (d.primaryVelocity != null && d.primaryVelocity! > 200) {
                  onDismiss();
                }
              },
              child: GlassMaterial(
                borderRadius: const BorderRadius.horizontal(left: Radius.circular(24)),
                tintOpacity: 0.10,
                blurSigma: 24,
                child: child,
              ),
            ),
          ),
        ),
      ),
    );
  }
}

