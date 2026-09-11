import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flyer_chat_text_message/flyer_chat_text_message.dart';

import '../brain_theme.dart';
import '../chat/chat_contracts.dart';

/// Product chat chrome (flutter_chat_ui + flyer text bubbles) with fixture turns.
/// Same packages as [BrainChatScreen], offline for the Ui gallery.
final class ShellChatDemo extends StatefulWidget {
  const ShellChatDemo({super.key, this.height = 360});

  final double height;

  @override
  State<ShellChatDemo> createState() => _ShellChatDemoState();
}

final class _ShellChatDemoState extends State<ShellChatDemo> {
  static const _owner = User(id: ownerUserId, name: 'you');
  static const _assistant = User(id: assistantUserId, name: 'brain');

  late final InMemoryChatController _controller = InMemoryChatController(
    messages: [
      TextMessage(
        id: 'ui-1',
        authorId: ownerUserId,
        createdAt: DateTime.utc(2026, 8, 1, 10, 0),
        text: 'Show me synapse throughput for the last hour.',
      ),
      TextMessage(
        id: 'ui-2',
        authorId: assistantUserId,
        createdAt: DateTime.utc(2026, 8, 1, 10, 0, 12),
        text: 'p50 is 12ms across 186 synapses/min. Want a chart window opened on the desktop?',
      ),
      TextMessage(
        id: 'ui-3',
        authorId: ownerUserId,
        createdAt: DateTime.utc(2026, 8, 1, 10, 1),
        text: 'Yes — spawn a metrics chart.',
      ),
      TextMessage(
        id: 'ui-4',
        authorId: assistantUserId,
        createdAt: DateTime.utc(2026, 8, 1, 10, 1, 8),
        text: 'Done. Check the Windowing tab for the floating chart panel.',
      ),
    ],
  );

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _onSend(String text) async {
    final trimmed = text.trim();
    if (trimmed.isEmpty) return;
    await _controller.insertMessage(
      TextMessage(
        id: 'ui-local-${DateTime.now().microsecondsSinceEpoch}',
        authorId: ownerUserId,
        createdAt: DateTime.now().toUtc(),
        text: trimmed,
      ),
    );
    await _controller.insertMessage(
      TextMessage(
        id: 'ui-echo-${DateTime.now().microsecondsSinceEpoch}',
        authorId: assistantUserId,
        createdAt: DateTime.now().toUtc(),
        text: 'Ui demo reply — no edge. You said: $trimmed',
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      key: const Key('ui_chat_demo'),
      height: widget.height,
      decoration: BoxDecoration(
        color: BrainPalette.surface,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: BrainPalette.line),
      ),
      clipBehavior: Clip.antiAlias,
      child: UiChat(
        chatController: _controller,
        currentUserId: ownerUserId,
        resolveUser: (id) async => switch (id) {
          ownerUserId => _owner,
          assistantUserId => _assistant,
          _ => null,
        },
        onMessageSend: _onSend,
        builders: Builders(
          textMessageBuilder:
              (
                context,
                message,
                index, {
                required bool isSentByMe,
                MessageGroupStatus? groupStatus,
              }) => FlyerChatTextMessage(
                message: message,
                index: index,
                showTime: false,
                showStatus: false,
              ),
        ),
      ),
    );
  }
}
