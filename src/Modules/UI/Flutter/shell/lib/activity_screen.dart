import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import 'brain_theme.dart';
import 'user_actions/user_action_card.dart';

final class ActivityScreen extends StatelessWidget {
  const ActivityScreen({
    super.key,
    required this.turns,
    this.userActions = const [],
    this.onOpenUserAction,
  });

  final List<ChatTurnEvent> turns;
  final List<UserActionCardModel> userActions;
  final ValueChanged<Uri>? onOpenUserAction;

  @override
  Widget build(BuildContext context) {
    final empty = turns.isEmpty && userActions.isEmpty;
    return ColoredBox(
      key: const Key('activity_screen'),
      color: BrainPalette.surface,
      child: empty
          ? const _EmptyActivity()
          : Align(
              alignment: Alignment.topCenter,
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 980),
                child: ListView(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 32,
                    vertical: 32,
                  ),
                  children: [
                    const _ActivityHeader(),
                    if (userActions.isNotEmpty) ...[
                      const SizedBox(height: 18),
                      for (final action in userActions)
                        UserActionCard(
                          model: action,
                          onAuthorize: onOpenUserAction == null
                              ? null
                              : () => onOpenUserAction!(action.actionUrl),
                        ),
                    ],
                    const SizedBox(height: 24),
                    for (final turn in turns.reversed)
                      _ActivityEntry(turn: turn),
                  ],
                ),
              ),
            ),
    );
  }
}

final class _ActivityHeader extends StatelessWidget {
  const _ActivityHeader();

  @override
  Widget build(BuildContext context) {
    return const Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('Activity', style: BrainType.heading),
        SizedBox(height: 8),
        Text(
          'Durable conversation facts, newest first. Message content stays in Chat.',
          style: BrainType.bodyMuted,
        ),
      ],
    );
  }
}

final class _EmptyActivity extends StatelessWidget {
  const _EmptyActivity();

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(
            Icons.timeline_rounded,
            size: 34,
            color: BrainPalette.textMuted,
          ),
          const SizedBox(height: 16),
          const Text('No activity yet.', style: BrainType.empty),
          const SizedBox(height: 7),
          Text(
            'Journal facts appear after the conversation begins.',
            style: BrainType.body.copyWith(color: BrainPalette.textMuted),
          ),
        ],
      ),
    );
  }
}

final class _ActivityEntry extends StatelessWidget {
  const _ActivityEntry({required this.turn});

  final ChatTurnEvent turn;

  @override
  Widget build(BuildContext context) {
    final color = turn.fromUser ? BrainPalette.owner : BrainPalette.signal;

    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: BrainPalette.line),
      ),
      child: Row(
        children: [
          Container(
            width: 34,
            height: 34,
            decoration: BoxDecoration(
              color: color.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(10),
            ),
            child: Icon(
              turn.fromUser
                  ? Icons.north_east_rounded
                  : Icons.south_west_rounded,
              color: color,
              size: 17,
            ),
          ),
          const SizedBox(width: 16),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  turn.signal,
                  style: BrainType.metaStrong.copyWith(color: color),
                ),
                const SizedBox(height: 6),
                Wrap(
                  spacing: 18,
                  runSpacing: 5,
                  children: [
                    Text(
                      'sequence ${turn.sequence.toString().padLeft(3, '0')}',
                      style: BrainType.meta,
                    ),
                    Text('command ${turn.commandId}', style: BrainType.meta),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
