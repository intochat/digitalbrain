import 'package:flutter/material.dart';

import '../brain_theme.dart';

BoxDecoration brainPanelDecoration({Color color = BrainPalette.surfaceRaised}) =>
    BoxDecoration(
      color: color,
      borderRadius: BorderRadius.circular(14),
      border: Border.all(color: BrainPalette.line),
    );

final class BrainMetricCard extends StatelessWidget {
  const BrainMetricCard({
    super.key,
    required this.label,
    required this.value,
    this.accent = BrainPalette.textPrimary,
  });

  final String label;
  final String value;
  final Color accent;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 190,
      padding: const EdgeInsets.all(16),
      decoration: brainPanelDecoration(),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: BrainType.meta),
          const SizedBox(height: 9),
          Text(
            value,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: BrainType.metric.copyWith(color: accent),
          ),
        ],
      ),
    );
  }
}

final class BrainConnectionNotice extends StatelessWidget {
  const BrainConnectionNotice({super.key, required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: BrainPalette.signal.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: BrainPalette.signal.withValues(alpha: 0.25)),
      ),
      child: Text(message, style: BrainType.bodyMuted),
    );
  }
}

final class BrainInspectorField extends StatelessWidget {
  const BrainInspectorField({
    super.key,
    required this.label,
    required this.value,
  });

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 7),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: BrainType.meta),
          const SizedBox(height: 2),
          SelectableText(value, style: BrainType.metaStrong),
        ],
      ),
    );
  }
}
