import 'package:flutter/material.dart';

import '../blocks/block_action.dart';
import '../theme/brain_theme.dart';
import 'connection_health.dart';
import 'conversation_view.dart';
import 'decision_card.dart';
import 'effect_preview.dart';
import 'grant_prompt.dart';

class KindView extends StatelessWidget {
  const KindView(this.viewKind, this.data, {super.key, this.onAction});

  final String viewKind;
  final Map<String, dynamic> data;
  final void Function(BlockAction)? onAction;

  @override
  Widget build(BuildContext context) {
    try {
      switch (viewKind) {
        case 'decisionCard':
          return DecisionCard(data: data, onAction: onAction);
        case 'connectionHealth':
          return ConnectionHealth(data: data, onAction: onAction);
        case 'conversation':
          return ConversationView(data: data);
        case 'grantPrompt':
          return GrantPrompt(data: data, onAction: onAction);
        case 'effectPreview':
          return EffectPreview(data: data);
        default:
          return _fallback(viewKind);
      }
    } catch (_) {
      return _fallback(viewKind);
    }
  }

  Widget _fallback(String kind) {
    return Container(
      margin: const EdgeInsets.symmetric(vertical: 4),
      padding: const EdgeInsets.all(8),
      decoration: BoxDecoration(
        border: Border.all(color: BrainColors.hairlineStrong),
      ),
      child: Text('unsupported kind: $kind'),
    );
  }
}
