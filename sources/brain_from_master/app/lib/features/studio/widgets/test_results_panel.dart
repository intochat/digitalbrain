import 'package:flutter/material.dart';

import '../feature_studio_controller.dart';
import '../feature_studio_models.dart';

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
            _VerificationStatus(controller: controller),
            if (verification != null) ...[
              const SizedBox(height: 18),
              _VerificationEvidence(verification: verification),
            ],
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

class _VerificationStatus extends StatelessWidget {
  const _VerificationStatus({required this.controller});

  final FeatureStudioController controller;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final verification = controller.verification;
    return switch (controller.verificationPhase) {
      FeatureStudioVerificationPhase.verifying => Semantics(
        liveRegion: true,
        container: true,
        label: 'Verification is running.',
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            LinearProgressIndicator(),
            SizedBox(height: 10),
            Text('Running tests…'),
          ],
        ),
      ),
      FeatureStudioVerificationPhase.passed when verification != null => Semantics(
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
      FeatureStudioVerificationPhase.stale => Semantics(
        liveRegion: true,
        label: 'These Test results are out of date.',
        child: Text('These Test results are out of date.'),
      ),
      FeatureStudioVerificationPhase.failedTests => Semantics(
        liveRegion: true,
        label: 'Verification did not pass.',
        child: Text(
          'Verification did not pass. Review the safe failures below.',
          style: TextStyle(color: theme.colorScheme.error),
        ),
      ),
      FeatureStudioVerificationPhase.retryableFailure => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Semantics(
            liveRegion: true,
            label: 'Verification is temporarily unavailable. Try again.',
            child: Text('Verification is temporarily unavailable.'),
          ),
          const SizedBox(height: 8),
          OutlinedButton(
            onPressed: controller.retryVerification,
            child: const Text('Try again'),
          ),
        ],
      ),
      FeatureStudioVerificationPhase.failed => Semantics(
        liveRegion: true,
        label: 'Verification could not be completed.',
        child: Text(
          'Verification could not be completed.',
          style: TextStyle(color: theme.colorScheme.error),
        ),
      ),
      _ => const Text('No current Test results.'),
    };
  }
}

class _VerificationEvidence extends StatelessWidget {
  const _VerificationEvidence({required this.verification});

  final FeatureStudioVerification verification;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          '${verification.passed} passed · ${verification.failed} failed · ${verification.skipped} skipped',
          style: theme.textTheme.titleMedium,
        ),
        const SizedBox(height: 14),
        Text('Current source digest', style: theme.textTheme.titleSmall),
        const SizedBox(height: 3),
        SelectableText(
          verification.sourceReference.isEmpty
              ? 'Not provided'
              : verification.sourceReference,
        ),
        if (verification.releaseDigest case final digest?) ...[
          const SizedBox(height: 10),
          Text('Verified Version identity', style: theme.textTheme.titleSmall),
          const SizedBox(height: 3),
          SelectableText(
            digest,
            style: theme.textTheme.bodySmall?.copyWith(fontFamily: 'monospace'),
          ),
        ],
        if (verification.scenarios.isNotEmpty) ...[
          const SizedBox(height: 18),
          Text('Scenarios', style: theme.textTheme.titleMedium),
          const SizedBox(height: 6),
          for (final scenario in verification.scenarios)
            _ScenarioResult(scenario: scenario),
        ],
        if (verification.artifacts.isNotEmpty) ...[
          const SizedBox(height: 18),
          Text('Artifacts', style: theme.textTheme.titleMedium),
          const SizedBox(height: 6),
          for (final artifact in verification.artifacts)
            _VerificationArtifact(artifact: artifact),
        ],
      ],
    );
  }
}

class _ScenarioResult extends StatelessWidget {
  const _ScenarioResult({required this.scenario});

  final FeatureStudioVerificationScenario scenario;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final (icon, color, outcome) = switch (scenario.outcome) {
      FeatureStudioScenarioOutcome.passed => (
        Icons.check_circle_outline,
        theme.colorScheme.tertiary,
        'Passed',
      ),
      FeatureStudioScenarioOutcome.failed => (
        Icons.error_outline,
        theme.colorScheme.error,
        'Failed',
      ),
      FeatureStudioScenarioOutcome.skipped => (
        Icons.remove_circle_outline,
        theme.colorScheme.onSurfaceVariant,
        'Skipped',
      ),
    };
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, color: color, size: 20),
          const SizedBox(width: 9),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(scenario.name, style: theme.textTheme.titleSmall),
                Text('$outcome · ${scenario.durationMilliseconds} ms'),
                if (scenario.safeFailure case final failure?) ...[
                  const SizedBox(height: 4),
                  Text(
                    failure,
                    style: TextStyle(color: theme.colorScheme.error),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _VerificationArtifact extends StatelessWidget {
  const _VerificationArtifact({required this.artifact});

  final FeatureStudioVerificationArtifact artifact;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.all(10),
      decoration: BoxDecoration(
        border: Border.all(color: theme.colorScheme.outlineVariant),
        borderRadius: BorderRadius.circular(10),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(artifact.name, style: theme.textTheme.titleSmall),
          Text('${artifact.mediaType} · ${artifact.sizeBytes} bytes'),
          const SizedBox(height: 4),
          SelectableText(
            artifact.digest,
            style: theme.textTheme.bodySmall?.copyWith(fontFamily: 'monospace'),
          ),
        ],
      ),
    );
  }
}
