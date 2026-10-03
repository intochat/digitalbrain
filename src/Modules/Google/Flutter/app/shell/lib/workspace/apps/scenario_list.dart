import 'package:flutter/material.dart';

import 'app_document.dart';
import 'app_verification_summary.dart';
import 'registry_token_sheet.dart';
import 'spec_vocabulary.dart';

class ScenarioList extends StatelessWidget {
  const ScenarioList({
    super.key,
    required this.scenarios,
    required this.all,
    this.run,
    this.current = true,
    this.vocabulary = const [],
  });
  final List<AppScenario> scenarios, all;
  final VerificationRun? run;
  final bool current;
  final List<SpecToken> vocabulary;
  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      for (final scenario in scenarios)
        Padding(
          padding: const EdgeInsets.symmetric(vertical: 12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Wrap(
                spacing: 12,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  Text(
                    'Scenario: ${scenario.name}',
                    key: ValueKey('scenario-${scenario.name}'),
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                  Text(
                    scenarioStatus(scenario, all, run, current),
                    style: Theme.of(context).textTheme.labelMedium,
                  ),
                ],
              ),
              const SizedBox(height: 8),
              SpecProse(scenario.body.trim(), vocabulary: vocabulary),
              if (scenario.isLive)
                const Text('Documents behavior; not part of automated checks.'),
              if (scenarioStatus(scenario, all, run, current) == 'Failed')
                Container(
                  key: ValueKey('scenario-message-${scenario.name}'),
                  margin: const EdgeInsets.only(top: 8),
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: Theme.of(context).colorScheme.errorContainer,
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: SelectableText(
                    run!.verdictFor(scenario)!.message,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.onErrorContainer,
                    ),
                  ),
                ),
            ],
          ),
        ),
    ],
  );
}
