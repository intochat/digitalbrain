import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';

import 'ui_chat.dart';

const _you = 'user';

// Renders a `uichat` neuron: messages come from its state, sending posts a submit event.
class UiNeuronChat extends StatefulWidget {
  const UiNeuronChat({
    super.key,
    required this.name,
    required this.state,
    required this.reload,
    this.onAction,
    this.enabled = true,
    this.refreshEvery = const Duration(seconds: 2),
  });

  final String name;
  final Map<String, dynamic> state;
  final Future<Map<String, dynamic>> Function() reload;
  final Future<void> Function(Map<String, dynamic>)? onAction;
  final bool enabled;
  final Duration refreshEvery;

  @override
  State<UiNeuronChat> createState() => _UiNeuronChatState();
}

class _UiNeuronChatState extends State<UiNeuronChat> {
  final _controller = InMemoryChatController();
  Timer? _refresh;
  int? _shownRevision;
  String? _error;

  @override
  void initState() {
    super.initState();
    _show(widget.state);
    _refresh = Timer.periodic(widget.refreshEvery, (_) => _pull());
  }

  @override
  void didUpdateWidget(UiNeuronChat old) {
    super.didUpdateWidget(old);
    if (old.state != widget.state) _show(widget.state);
  }

  Future<void> _pull() async {
    try {
      final state = await widget.reload();
      if (mounted) _show(state);
    } catch (_) {
      // The next tick retries; a transient refresh failure must not break the chat.
    }
  }

  void _show(Map<String, dynamic> state) {
    final revision = (state['revision'] as num?)?.toInt();
    if (revision != null && revision == _shownRevision) return;
    _shownRevision = revision;
    final messages = (state['messages'] as List? ?? []).whereType<Map>();
    _controller.setMessages([
      for (final message in messages)
        TextMessage(
          id: '${message['id']}',
          authorId: _isUser(message['role']) ? _you : 'assistant',
          text: '${message['text'] ?? ''}',
        ),
    ], animated: false);
  }

  static bool _isUser(Object? role) => role == 0 || role == 'User';

  Future<void> _send(String text) async {
    if (text.trim().isEmpty || !widget.enabled) return;
    setState(() => _error = null);
    try {
      await widget.onAction?.call({
        'kind': 'uichat',
        'name': widget.name,
        'value': text,
      });
      await _pull();
    } catch (error) {
      if (mounted) setState(() => _error = '$error');
    }
  }

  @override
  void dispose() {
    _refresh?.cancel();
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      if (_error != null)
        Padding(
          padding: const EdgeInsets.all(8),
          child: Text(
            _error!,
            style: TextStyle(color: Theme.of(context).colorScheme.error),
          ),
        ),
      Expanded(
        child: UiChat(
          workspaceTheme: true,
          chatController: _controller,
          currentUserId: _you,
          resolveUser: (id) async =>
              User(id: id, name: id == _you ? 'You' : 'Assistant'),
          onMessageSend: _send,
        ),
      ),
    ],
  );
}
