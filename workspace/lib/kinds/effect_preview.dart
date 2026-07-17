import 'package:flutter/material.dart';

import '../theme/brain_theme.dart';

class EffectPreview extends StatelessWidget {
  const EffectPreview({required this.data, super.key});

  final Map<String, dynamic> data;

  static const int _digestVisibleLength = 20;

  @override
  Widget build(BuildContext context) {
    final summary = data['summary']?.toString() ?? '';
    final digest = data['payloadDigest']?.toString() ?? '';
    final truncatedDigest = digest.length > _digestVisibleLength
        ? '${digest.substring(0, _digestVisibleLength)}…'
        : digest;

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(summary),
          const SizedBox(height: 4),
          Text(
            truncatedDigest,
            style: BrainTheme.mono(
              const TextStyle(fontSize: 12, color: BrainColors.inkFaint),
            ),
          ),
        ],
      ),
    );
  }
}
