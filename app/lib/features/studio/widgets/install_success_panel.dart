import 'package:flutter/material.dart';

import '../feature_studio_models.dart';

const Key featureStudioInstallSuccessPanelKey = Key(
  'feature-studio-install-success-panel',
);
const Key featureStudioReturnRunNowButtonKey = Key(
  'feature-studio-return-run-now-button',
);

class InstallSuccessPanel extends StatelessWidget {
  const InstallSuccessPanel({
    super.key,
    required this.success,
    required this.onReturnToChat,
    required this.onRunNow,
  });

  final FeatureStudioInstallSuccess success;
  final VoidCallback onReturnToChat;
  final VoidCallback? onRunNow;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      key: featureStudioInstallSuccessPanelKey,
      margin: EdgeInsets.zero,
      child: Semantics(
        liveRegion: true,
        container: true,
        label: 'Feature installed.',
        child: Padding(
          padding: const EdgeInsets.all(20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  Icon(Icons.check_circle, color: theme.colorScheme.tertiary),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      'Feature installed',
                      style: theme.textTheme.headlineSmall,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 14),
              Text('Installation', style: theme.textTheme.titleSmall),
              SelectableText(success.installationId),
              const SizedBox(height: 10),
              Text('Version identity', style: theme.textTheme.titleSmall),
              SelectableText(
                success.version.digest,
                style: theme.textTheme.bodySmall?.copyWith(
                  fontFamily: 'monospace',
                ),
              ),
              const SizedBox(height: 10),
              Text(
                success.rollbackAvailable
                    ? 'Rollback available'
                    : 'Rollback is not available for this installation.',
              ),
              const SizedBox(height: 18),
              Text('Original request', style: theme.textTheme.titleMedium),
              const SizedBox(height: 5),
              Text(success.originalRequest.text),
              const SizedBox(height: 18),
              Wrap(
                alignment: WrapAlignment.end,
                spacing: 10,
                runSpacing: 8,
                children: [
                  OutlinedButton(
                    onPressed: onReturnToChat,
                    child: const Text('Return to Chat'),
                  ),
                  FilledButton.icon(
                    key: featureStudioReturnRunNowButtonKey,
                    onPressed: onRunNow,
                    icon: const Icon(Icons.play_arrow),
                    label: const Text('Return to Chat · Run now'),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}
