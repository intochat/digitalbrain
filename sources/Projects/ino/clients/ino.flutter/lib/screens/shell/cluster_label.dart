import 'package:flutter/material.dart';

import 'shell_theme.dart';

class ClusterLabel extends StatelessWidget {
  const ClusterLabel({
    required this.label,
    required this.count,
    required this.position,
    required this.opacity,
    super.key,
  });

  final String label;
  final int count;
  final Offset position;
  final double opacity;

  @override
  Widget build(BuildContext context) {
    return Positioned(
      left: position.dx,
      top: position.dy,
      child: FractionalTranslation(
        translation: const Offset(-0.5, -0.5),
        child: IgnorePointer(
          child: Opacity(
            opacity: opacity.clamp(0.0, 1.0),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  label,
                  style: const TextStyle(
                    fontSize: 10,
                    letterSpacing: 1.6,
                    color: InoShellTheme.muted,
                    fontWeight: FontWeight.w500,
                  ),
                ),
                Text(
                  '$count neurons',
                  style: const TextStyle(
                    fontFamily: 'JetBrains Mono',
                    fontSize: 11,
                    color: InoShellTheme.text,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
