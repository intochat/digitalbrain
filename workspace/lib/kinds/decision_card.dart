import 'package:flutter/material.dart';

import '../blocks/block_action.dart';
import '../theme/brain_theme.dart';

class DecisionCard extends StatelessWidget {
  const DecisionCard({required this.data, super.key, this.onAction});

  final Map<String, dynamic> data;
  final void Function(BlockAction)? onAction;

  @override
  Widget build(BuildContext context) {
    final title = data['title']?.toString() ?? '';
    final summary = data['summary']?.toString() ?? '';

    return Card(
      shape: const RoundedRectangleBorder(
        side: BorderSide(color: BrainColors.amber),
        borderRadius: BorderRadius.all(Radius.circular(12)),
      ),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title, style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
            Text(summary),
            const SizedBox(height: 16),
            Row(
              children: [
                FilledButton(
                  onPressed: () => onAction?.call(
                    BlockAction(
                      label: 'Approve',
                      contract:
                          data['approveContract']?.toString() ??
                          'effect.approve.v1',
                      inputJson: data['approveInput']?.toString() ?? '{}',
                    ),
                  ),
                  child: const Text('Approve'),
                ),
                const SizedBox(width: 8),
                OutlinedButton(
                  onPressed: () => onAction?.call(
                    BlockAction(
                      label: 'Decline',
                      contract:
                          data['declineContract']?.toString() ??
                          'effect.decline.v1',
                      inputJson: data['declineInput']?.toString() ?? '{}',
                    ),
                  ),
                  child: const Text('Decline'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
