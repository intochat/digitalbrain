import 'package:flutter/material.dart';

import '../blocks/block_action.dart';
import '../theme/brain_theme.dart';

class ConnectionHealth extends StatelessWidget {
  const ConnectionHealth({required this.data, super.key, this.onAction});

  final Map<String, dynamic> data;
  final void Function(BlockAction)? onAction;

  static const Map<String, Color> _healthColors = {
    'healthy': BrainColors.green,
    'missingAppCredentials': BrainColors.inkMuted,
    'notConfigured': BrainColors.inkMuted,
    'notAuthorized': BrainColors.amber,
    'tokenExpired': BrainColors.amber,
    'providerError': BrainColors.orange,
    'networkError': BrainColors.orange,
  };

  static const Map<String, String> _fixLabels = {
    'connect': 'Connect',
    'reauthorize': 'Reauthorize',
    'retry': 'Retry',
  };

  @override
  Widget build(BuildContext context) {
    final provider = data['provider']?.toString() ?? '';
    final health = data['health']?.toString() ?? '';
    final fix = data['fix']?.toString() ?? 'none';
    final color = _healthColors[health] ?? BrainColors.inkMuted;
    final fixLabel = _fixLabels[fix];

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(
        children: [
          if (provider.isNotEmpty) ...[
            Text(provider),
            const SizedBox(width: 8),
          ],
          Chip(
            label: Text(health),
            backgroundColor: color,
            labelStyle: const TextStyle(color: BrainColors.ground),
          ),
          if (fixLabel != null) ...[
            const SizedBox(width: 8),
            OutlinedButton(
              style: OutlinedButton.styleFrom(
                foregroundColor: color,
                side: BorderSide(color: color),
              ),
              onPressed: () {
                final fixContract = data['fixContract']?.toString();
                if (fixContract == null || fixContract.isEmpty) return;
                onAction?.call(
                  BlockAction(
                    label: fixLabel,
                    contract: fixContract,
                    inputJson: data['fixInput']?.toString() ?? '{}',
                  ),
                );
              },
              child: Text(fixLabel),
            ),
          ],
        ],
      ),
    );
  }
}
