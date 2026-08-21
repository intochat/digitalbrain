import 'package:flutter/material.dart';

import '../brain_theme.dart';
import '../user_actions/user_action_card.dart';

/// An intentionally static preview of the future behavior composer.
///
/// Recipes do not call a `/behaviors` API or claim to execute. The live chat
/// and voice surfaces remain the product paths; this tab shows the composition
/// language those modules will expose when behavior execution is introduced.
final class BehaviorWorkspace extends StatelessWidget {
  const BehaviorWorkspace({
    super.key,
    this.userActions = const [],
    this.onOpenUserAction,
  });

  final List<UserActionCardModel> userActions;
  final ValueChanged<Uri>? onOpenUserAction;

  @override
  Widget build(BuildContext context) {
    return ListView(
      key: const Key('behavior_workspace'),
      padding: const EdgeInsets.all(24),
      children: const [
        Text('Behavior recipes', style: BrainType.heading),
        SizedBox(height: 8),
        Text(
          'Prepared examples of future module composition. They are not running automations.',
          style: BrainType.bodyMuted,
        ),
        SizedBox(height: 24),
        _RecipeCard(
          trigger: 'When there is a new event in',
          source: 'Google Calendar',
          sourceNeuron: 'ICalendar',
          action: 'send me a summary to',
          target: '@intochat',
          targetNeuron: 'IChat',
        ),
        SizedBox(height: 16),
        _RecipeCard(
          trigger: 'When a message arrives in',
          source: 'Chat',
          sourceNeuron: 'IChat',
          action: 'ask',
          target: 'OpenAI · IGpt56',
          targetNeuron: 'planned',
        ),
        SizedBox(height: 24),
        Text('Planned composition', style: BrainType.cardTitle),
        SizedBox(height: 8),
        Text(
          'Modules will contribute typed neurons such as ICalendar, IChat, and IGpt56. A later composer can join them into a recipe without hiding which provider or capability is used.',
          style: BrainType.bodyMuted,
        ),
      ],
    );
  }
}

final class _RecipeCard extends StatelessWidget {
  const _RecipeCard({
    required this.trigger,
    required this.source,
    required this.sourceNeuron,
    required this.action,
    required this.target,
    required this.targetNeuron,
  });

  final String trigger;
  final String source;
  final String sourceNeuron;
  final String action;
  final String target;
  final String targetNeuron;

  @override
  Widget build(BuildContext context) {
    return Card(
      color: BrainPalette.surfaceRaised,
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Wrap(
          crossAxisAlignment: WrapCrossAlignment.center,
          spacing: 8,
          runSpacing: 8,
          children: [
            Text(trigger, style: BrainType.body),
            _NeuronLabel(label: source, neuron: sourceNeuron),
            Text(action, style: BrainType.body),
            _NeuronLabel(label: target, neuron: targetNeuron),
          ],
        ),
      ),
    );
  }
}

final class _NeuronLabel extends StatelessWidget {
  const _NeuronLabel({required this.label, required this.neuron});

  final String label;
  final String neuron;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: BoxDecoration(
        color: BrainPalette.surfaceSunken,
        border: Border.all(color: BrainPalette.line),
        borderRadius: BorderRadius.circular(6),
      ),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 5),
        child: Text.rich(
          TextSpan(
            children: [
              TextSpan(text: label, style: BrainType.metaStrong),
              TextSpan(text: ' · $neuron', style: BrainType.meta),
            ],
          ),
        ),
      ),
    );
  }
}
