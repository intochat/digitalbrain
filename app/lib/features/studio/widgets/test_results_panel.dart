import 'package:flutter/material.dart';

import '../feature_studio_controller.dart';

const Key featureStudioVerifyButtonKey = Key('feature-studio-verify-button');
const Key featureStudioTestResultsKey = Key('feature-studio-test-results');

class TestResultsPanel extends StatelessWidget {
  const TestResultsPanel({super.key, required this.controller});

  final FeatureStudioController controller;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final verification = controller.verification;
    return Card(
      key: featureStudioTestResultsKey,
      margin: EdgeInsets.zero,
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Test results', style: theme.textTheme.headlineSmall),
            const SizedBox(height: 4),
            Text(
              'Verify the current saved Behavior and Code together.',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 16),
            if (controller.verificationPhase ==
                FeatureStudioVerificationPhase.verifying) ...[
              Semantics(
                liveRegion: true,
                container: true,
                label: 'Verification is running.',
                child: const Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    LinearProgressIndicator(),
                    SizedBox(height: 10),
                    Text('Running tests…'),
                  ],
                ),
              ),
            ] else if (controller.verificationPhase ==
                    FeatureStudioVerificationPhase.passed &&
                verification != null) ...[
              Semantics(
                liveRegion: true,
                label:
                    'Verification passed. ${verification.passed} of ${verification.total} tests passed.',
                child: Row(
                  children: [
                    Icon(Icons.check_circle, color: theme.colorScheme.tertiary),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Text(
                        '${verification.passed} of ${verification.total} tests passed',
                        style: theme.textTheme.titleMedium,
                      ),
                    ),
                  ],
                ),
              ),
            ] else if (controller.verificationPhase ==
                FeatureStudioVerificationPhase.stale) ...[
              Semantics(
                liveRegion: true,
                label: 'These Test results are out of date.',
                child: const Text('These Test results are out of date.'),
              ),
            ] else if (controller.verificationPhase ==
                FeatureStudioVerificationPhase.failedTests) ...[
              Semantics(
                liveRegion: true,
                label: 'Verification did not pass.',
                child: Text(
                  'Verification did not pass. Review Behavior and Code, then try again.',
                  style: TextStyle(color: theme.colorScheme.error),
                ),
              ),
            ] else if (controller.verificationPhase ==
                FeatureStudioVerificationPhase.retryableFailure) ...[
              Semantics(
                liveRegion: true,
                label: 'Verification is temporarily unavailable. Try again.',
                child: const Text('Verification is temporarily unavailable.'),
              ),
              const SizedBox(height: 8),
              OutlinedButton(
                onPressed: controller.retryVerification,
                child: const Text('Try again'),
              ),
            ] else if (controller.verificationPhase ==
                FeatureStudioVerificationPhase.failed) ...[
              Semantics(
                liveRegion: true,
                label: 'Verification could not be completed.',
                child: Text(
                  'Verification could not be completed.',
                  style: TextStyle(color: theme.colorScheme.error),
                ),
              ),
            ] else
              const Text('No current Test results.'),
            const SizedBox(height: 18),
            Semantics(
              button: true,
              label: controller.canVerify
                  ? 'Verify saved Draft'
                  : 'Verify unavailable until the Draft is saved and valid',
              child: FilledButton.icon(
                key: featureStudioVerifyButtonKey,
                onPressed: controller.canVerify ? controller.verify : null,
                icon: const Icon(Icons.play_arrow),
                label: const Text('Verify'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
