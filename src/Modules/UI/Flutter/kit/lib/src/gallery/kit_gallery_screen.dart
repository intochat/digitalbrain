import 'package:flutter/material.dart';

import '../components/button/kit_button.dart';
import '../components/card/kit_card.dart';
import '../components/chart/kit_chart.dart';
import '../models/kit_part.dart';
import '../theme/kit_theme.dart';

/// Offline gallery of kit components (no backend).
final class KitGalleryScreen extends StatelessWidget {
  const KitGalleryScreen({super.key, this.onButtonPressed});

  final ValueChanged<KitButtonPart>? onButtonPressed;

  static final _demoChart = KitChartPart(
    title: 'Weekly throughput',
    points: const [
      KitChartPoint(label: 'Mon', value: 42),
      KitChartPoint(label: 'Tue', value: 68),
      KitChartPoint(label: 'Wed', value: 51),
      KitChartPoint(label: 'Thu', value: 89),
      KitChartPoint(label: 'Fri', value: 74),
    ],
  );

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      key: const Key('kit_gallery_screen'),
      color: KitPalette.surface,
      child: Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 1100),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(32, 28, 32, 48),
            children: [
              Text('UI Kit', style: KitType.heading),
              const SizedBox(height: 8),
              Text(
                'Components shared by surfaces and chat CustomMessage bubbles.',
                style: KitType.bodyMuted,
              ),
              const SizedBox(height: 28),
              Text('Button', style: KitType.title),
              const SizedBox(height: 12),
              KitButton(
                part: const KitButtonPart(
                  buttonId: 'show-time',
                  label: 'Show current time',
                  action: 'show-time',
                  offerCommandId: 'demo',
                ),
                onPressed: onButtonPressed,
              ),
              const SizedBox(height: 28),
              Text('Chart', style: KitType.title),
              const SizedBox(height: 12),
              KitChart(part: _demoChart),
              const SizedBox(height: 28),
              Text('Card', style: KitType.title),
              const SizedBox(height: 12),
              const KitCard(
                part: KitCardPart(
                  title: 'Sales summary',
                  body: 'Last week closed above plan.',
                  fields: [
                    (label: 'Revenue', value: '\$128k'),
                    (label: 'Delta', value: '+12%'),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
