import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';

final class BehaviorScenariosView extends StatelessWidget {
  const BehaviorScenariosView({
    super.key,
    required this.document,
  });

  final BehaviorDocument document;

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      key: const Key('behavior_scenarios'),
      color: BrainPalette.surface,
      child: Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 1080),
          child: ListView(
            padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 28),
            children: [
              const Text('Scenarios', style: BrainType.heading),
              const SizedBox(height: 8),
              const Text(
                'Readable English behavior. Pass/fail evidence stays with admission results.',
                style: BrainType.bodyMuted,
              ),
              const SizedBox(height: 20),
              if (document.scenarios.isEmpty)
                const Text('No scenarios defined.', style: BrainType.bodyMuted)
              else
                for (final scenario in document.scenarios)
                  _ScenarioCard(scenario: scenario),
              const SizedBox(height: 20),
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(18),
                decoration: BoxDecoration(
                  color: BrainPalette.surfaceRaised,
                  borderRadius: BorderRadius.circular(14),
                  border: Border.all(color: BrainPalette.line),
                ),
                child: SelectableText(
                  document.featureText.isEmpty
                      ? 'No feature text yet.'
                      : document.featureText,
                  style: BrainType.body.copyWith(
                    fontFamily: BrainType.monoFamily,
                    fontFamilyFallback: BrainType.monoFallback,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

final class _ScenarioCard extends StatelessWidget {
  const _ScenarioCard({required this.scenario});

  final BehaviorScenario scenario;

  @override
  Widget build(BuildContext context) {
    final evidence = switch (scenario.passed) {
      true => ('pass', BrainPalette.success),
      false => ('fail', BrainPalette.signal),
      null => ('pending', BrainPalette.textMuted),
    };

    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: BrainPalette.line),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(scenario.title, style: BrainType.cardTitle),
              ),
              Text(
                evidence.$1,
                style: BrainType.metaStrong.copyWith(color: evidence.$2),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text(scenario.scenarioId, style: BrainType.meta),
          if (scenario.detail != null) ...[
            const SizedBox(height: 8),
            Text(scenario.detail!, style: BrainType.bodyMuted),
          ],
        ],
      ),
    );
  }
}
