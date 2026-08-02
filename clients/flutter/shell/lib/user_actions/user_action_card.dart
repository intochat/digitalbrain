import 'package:flutter/material.dart';

import '../brain_theme.dart';

final class UserActionCardModel {
  const UserActionCardModel({
    required this.moduleId,
    required this.displayText,
    required this.actionUrl,
    required this.taskId,
    this.continuationState = 'waiting',
  });

  final String moduleId;
  final String displayText;
  final Uri actionUrl;
  final String taskId;
  final String continuationState;
}

final class UserActionCard extends StatelessWidget {
  const UserActionCard({
    super.key,
    required this.model,
    this.onAuthorize,
  });

  final UserActionCardModel model;
  final VoidCallback? onAuthorize;

  @override
  Widget build(BuildContext context) {
    return Container(
      key: Key('user_action_${model.moduleId}_${model.taskId}'),
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceSunken,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: BrainPalette.lineStrong),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(model.moduleId, style: BrainType.metaStrong),
          const SizedBox(height: 8),
          Text(model.displayText, style: BrainType.body),
          const SizedBox(height: 8),
          Text('Task ${model.taskId} · ${model.continuationState}', style: BrainType.meta),
          const SizedBox(height: 12),
          FilledButton(
            key: Key('user_action_authorize_${model.moduleId}'),
            onPressed: onAuthorize,
            child: const Text('Connect / Authorize'),
          ),
        ],
      ),
    );
  }
}
