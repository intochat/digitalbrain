import 'package:flutter/material.dart';
import 'package:lottie/lottie.dart';

/// The orbiting "Ino session" marker pinned to the top-left of every panel.
/// A small, perpetual loop of `orbit.lottie` that reads as "this widget is a
/// live neuron with an active session" — the same way an Ino session shows it
/// is thinking. Neuron-agnostic: every floating panel carries one.
class OrbitSessionBadge extends StatelessWidget {
  const OrbitSessionBadge({super.key, this.size = 20});

  final double size;

  // dotLottie (.lottie) is a zip archive; pick the bundled animation JSON.
  static Future<LottieComposition?> _decodeDotLottie(List<int> bytes) {
    return LottieComposition.decodeZip(
      bytes,
      filePicker: (files) {
        for (final file in files) {
          if (file.name.startsWith('animations/') &&
              file.name.endsWith('.json')) {
            return file;
          }
        }
        for (final file in files) {
          if (file.name.endsWith('.json')) return file;
        }
        return null;
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: size,
      height: size,
      child: Lottie.asset(
        'assets/lottie/orbit.lottie',
        decoder: _decodeDotLottie,
        repeat: true,
        fit: BoxFit.contain,
        // A blank box while the 13MB archive decodes; never a layout error box.
        errorBuilder: (_, _, _) => const SizedBox.shrink(),
      ),
    );
  }
}
