import 'package:flutter/material.dart';

import '../blocks/block_action.dart';
import '../theme/brain_theme.dart';

class GrantPrompt extends StatelessWidget {
  const GrantPrompt({required this.data, super.key, this.onAction});

  final Map<String, dynamic> data;
  final void Function(BlockAction)? onAction;

  @override
  Widget build(BuildContext context) {
    final rawReasons = data['reasons'];
    final reasons = rawReasons is List ? rawReasons : const <dynamic>[];

    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          ...reasons.map((entry) {
            final reason = entry is Map<String, dynamic>
                ? entry
                : <String, dynamic>{};
            final scope = reason['scope']?.toString() ?? '';
            final why = reason['reason']?.toString() ?? '';
            return Padding(
              padding: const EdgeInsets.symmetric(vertical: 2),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    scope,
                    style: const TextStyle(fontWeight: FontWeight.w600),
                  ),
                  const SizedBox(width: 8),
                  Flexible(
                    child: Text(
                      why,
                      style: const TextStyle(color: BrainColors.inkMuted),
                    ),
                  ),
                ],
              ),
            );
          }),
          const SizedBox(height: 12),
          Row(
            children: [
              FilledButton(
                onPressed: () => onAction?.call(
                  BlockAction(
                    label: 'Grant',
                    contract:
                        data['grantContract']?.toString() ??
                        'effect.approve.v1',
                    inputJson: data['grantInput']?.toString() ?? '{}',
                  ),
                ),
                child: const Text('Grant'),
              ),
              const SizedBox(width: 8),
              OutlinedButton(
                onPressed: () => onAction?.call(
                  BlockAction(
                    label: 'Cancel',
                    contract:
                        data['cancelContract']?.toString() ??
                        'effect.decline.v1',
                    inputJson: data['cancelInput']?.toString() ?? '{}',
                  ),
                ),
                child: const Text('Cancel'),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
