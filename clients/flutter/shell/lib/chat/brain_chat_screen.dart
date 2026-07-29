import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter_chat_ui/flutter_chat_ui.dart';
import 'package:flyer_chat_text_message/flyer_chat_text_message.dart';
import 'package:flyer_chat_text_stream_message/flyer_chat_text_stream_message.dart';
import 'package:provider/provider.dart';
import 'package:uuid/uuid.dart';

import '../brain_theme.dart';
import 'chat_contracts.dart';
import 'stream_state_store.dart';

final class BrainChatScreen extends StatefulWidget {
  const BrainChatScreen({
    super.key,
    required this.chatName,
    required this.turns,
    this.onSend,
    this.onStream,
  });

  final String chatName;
  final List<ChatTurnEvent> turns;
  final SendMessage? onSend;
  final StreamMessage? onStream;

  @override
  State<BrainChatScreen> createState() => _BrainChatScreenState();
}

final class _BrainChatScreenState extends State<BrainChatScreen> {
  static const _owner = User(id: ownerUserId, name: 'you');
  static const _assistant = User(id: assistantUserId, name: 'brain');
  static const _uuid = Uuid();

  final _controller = InMemoryChatController();
  final _streamStates = StreamStateStore();
  final _appliedSequences = <int>{};
  String? _pendingUserMessageId;
  String? _pendingUserText;
  String? _activeStreamId;
  String? _failure;

  @override
  void initState() {
    super.initState();
    unawaited(_syncJournal(widget.turns));
  }

  @override
  void didUpdateWidget(covariant BrainChatScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!_sameJournal(oldWidget.turns, widget.turns)) {
      unawaited(_syncJournal(widget.turns));
    }
  }

  Future<void> _syncJournal(List<ChatTurnEvent> turns) async {
    final sequences = {for (final turn in turns) turn.sequence};
    if (sequences.length == _appliedSequences.length &&
        sequences.every(_appliedSequences.contains)) {
      return;
    }

    // Empty journal snapshots must not clobber optimistic/stream bubbles.
    // initState syncs [] and can finish after insertMessage without this guard.
    if (sequences.isEmpty &&
        (_pendingUserMessageId != null || _activeStreamId != null)) {
      return;
    }

    if (_pendingUserText != null &&
        turns.any((turn) => turn.fromUser && turn.text == _pendingUserText)) {
      _pendingUserMessageId = null;
      _pendingUserText = null;
    }

    final journalHasAssistant = turns.any((turn) => !turn.fromUser);
    if (_activeStreamId != null && journalHasAssistant) {
      _streamStates.forget(_activeStreamId!);
      _activeStreamId = null;
    }

    final messages = <Message>[
      for (final turn in turns)
        TextMessage(
          id: 'turn_${turn.sequence}',
          authorId: turn.fromUser ? ownerUserId : assistantUserId,
          createdAt: turn.timestamp.toUtc(),
          text: turn.text,
        ),
    ];

    if (_pendingUserMessageId != null && _pendingUserText != null) {
      messages.add(
        TextMessage(
          id: _pendingUserMessageId!,
          authorId: ownerUserId,
          createdAt: DateTime.now().toUtc(),
          text: _pendingUserText!,
        ),
      );
    }

    if (_activeStreamId != null) {
      messages.add(
        TextStreamMessage(
          id: _activeStreamId!,
          authorId: assistantUserId,
          createdAt: DateTime.now().toUtc(),
          streamId: _activeStreamId!,
        ),
      );
    }

    _appliedSequences
      ..clear()
      ..addAll(sequences);

    await _controller.setMessages(messages, animated: false);
    if (mounted) {
      setState(() {});
    }
  }

  Future<void> _handleSend(String text) async {
    final trimmed = text.trim();
    if (trimmed.isEmpty) {
      return;
    }

    setState(() => _failure = null);

    final localId = _uuid.v4();
    _pendingUserMessageId = localId;
    _pendingUserText = trimmed;
    await _controller.insertMessage(
      TextMessage(
        id: localId,
        authorId: ownerUserId,
        createdAt: DateTime.now().toUtc(),
        text: trimmed,
      ),
    );

    final stream = widget.onStream;
    if (stream != null) {
      await _drainStream(stream(trimmed));
      return;
    }

    final send = widget.onSend;
    if (send == null) {
      return;
    }

    try {
      await send(trimmed);
    } on Object catch (error) {
      if (mounted) {
        setState(() => _failure = '$error');
      }
    }
  }

  Future<void> _drainStream(Stream<ChatDelta> deltas) async {
    final streamId = _uuid.v4();
    _activeStreamId = streamId;
    final streamMessage = TextStreamMessage(
      id: streamId,
      authorId: assistantUserId,
      createdAt: DateTime.now().toUtc(),
      streamId: streamId,
    );
    await _controller.insertMessage(streamMessage);
    _streamStates.start(streamId);

    final buffer = StringBuffer();
    try {
      await for (final delta in deltas) {
        buffer.write(delta.text);
        _streamStates.streaming(streamId, buffer.toString());
      }
      _streamStates.complete(streamId, buffer.toString());
    } on Object catch (error) {
      _streamStates.error(streamId, '$error');
      if (mounted) {
        setState(() => _failure = '$error');
      }
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    _streamStates.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final canSend = widget.onSend != null || widget.onStream != null;

    return ColoredBox(
      color: BrainPalette.surface,
      child: Column(
        children: [
          Expanded(
            child: ChangeNotifierProvider.value(
              value: _streamStates,
              child: Chat(
                key: const Key('chat_surface'),
                chatController: _controller,
                currentUserId: ownerUserId,
                resolveUser: (id) async => switch (id) {
                  ownerUserId => _owner,
                  assistantUserId => _assistant,
                  _ => null,
                },
                theme: BrainChatTheme.dark(),
                onMessageSend: canSend ? _handleSend : null,
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
                  textStreamMessageBuilder:
                      (
                        context,
                        message,
                        index, {
                        required bool isSentByMe,
                        MessageGroupStatus? groupStatus,
                      }) {
                        final streamState = context
                            .watch<StreamStateStore>()
                            .stateFor(message.streamId);
                        return FlyerChatTextStreamMessage(
                          message: message,
                          index: index,
                          streamState: streamState,
                          showTime: false,
                          showStatus: false,
                        );
                      },
                ),
              ),
            ),
          ),
          if (_failure != null) FailureNotice(message: _failure!),
        ],
      ),
    );
  }

  static bool _sameJournal(List<ChatTurnEvent> left, List<ChatTurnEvent> right) {
    if (identical(left, right)) {
      return true;
    }
    if (left.length != right.length) {
      return false;
    }
    for (var index = 0; index < left.length; index++) {
      if (left[index].sequence != right[index].sequence) {
        return false;
      }
    }
    return true;
  }
}

final class FailureNotice extends StatelessWidget {
  const FailureNotice({super.key, required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 11),
      color: BrainPalette.signal.withValues(alpha: 0.08),
      child: Text(
        message,
        style: BrainType.meta.copyWith(color: BrainPalette.signal),
      ),
    );
  }
}
