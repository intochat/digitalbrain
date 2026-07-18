import 'package:flutter/material.dart';
import '../glass/glass_material.dart';

class AdaptiveDialog extends StatelessWidget {
  const AdaptiveDialog({
    required this.child,
    required this.onDismiss,
    this.widthFraction = 0.5,
    this.heightFraction = 0.6,
    this.cornerRadius = 28,
    super.key,
  });

  final Widget child;
  final VoidCallback onDismiss;
  final double widthFraction;
  final double heightFraction;
  final double cornerRadius;

  @override
  Widget build(BuildContext context) {
    final size = MediaQuery.sizeOf(context);
    final w = size.width * widthFraction;
    final h = size.height * heightFraction;

    final body = SizedBox(width: w, height: h, child: child);

    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTap: onDismiss,
      child: ColoredBox(
        color: Colors.black.withValues(alpha: 0.3),
        child: Center(
          child: GestureDetector(
            onTap: () {},
            child: GlassMaterial(
              cornerRadius: cornerRadius,
              tintOpacity: 0.10,
              blurSigma: 24,
              child: body,
            ),
          ),
        ),
      ),
    );
  }
}
