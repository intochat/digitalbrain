import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
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
    this.signInCards = const [],
    this.onSend,
    this.onStream,
    this.onOpenSignIn,
    this.onActivateButton,
  });

  final String chatName;
  final List<ChatTurnEvent> turns;
  final List<SignInCardProjection> signInCards;
  final SendMessage? onSend;
  final StreamMessage? onStream;
  final OpenUrl? onOpenSignIn;
  final ActivateChatButton? onActivateButton;

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
        ...KitMessageFactory.messagesForTurn(
          sequence: turn.sequence,
          fromUser: turn.fromUser,
          text: turn.text,
          createdAt: turn.timestamp,
          parts: turn.kitParts,
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

  Future<void> _onKitButton(KitButtonPart part) async {
    final action = part.action;
    final openUrl = Uri.tryParse(action);
    if (openUrl != null &&
        (openUrl.isScheme('https') || openUrl.isScheme('http')) &&
        widget.onOpenSignIn != null) {
      try {
        await widget.onOpenSignIn!(openUrl);
      } on Object catch (error) {
        if (mounted) {
          setState(() => _failure = '$error');
        }
      }
      return;
    }

    final activate = widget.onActivateButton;
    if (activate == null) {
      return;
    }
    final offer = part.offerCommandId;
    if (offer == null || offer.isEmpty) {
      return;
    }
    try {
      await activate(
        offerCommandId: offer,
        buttonId: part.buttonId,
        action: part.action,
      );
    } on Object catch (error) {
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
          if (widget.signInCards.isNotEmpty)
            SignInCardRail(
              cards: widget.signInCards,
              onOpenSignIn: widget.onOpenSignIn,
            ),
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
                  // Flyer Chat: CustomMessage + customMessageBuilder
                  // https://pub.dev/packages/flutter_chat_ui
                  customMessageBuilder:
                      (
                        context,
                        message,
                        index, {
                        required bool isSentByMe,
                        MessageGroupStatus? groupStatus,
                      }) => KitChatBuilders.customMessageBuilder(
                        context,
                        message,
                        index,
                        isSentByMe: isSentByMe,
                        groupStatus: groupStatus,
                        onButtonPressed: widget.onActivateButton == null
                            ? null
                            : _onKitButton,
                      ),
                ),
              ),
            ),
          ),
          if (_failure != null)
            Padding(
              padding: const EdgeInsets.all(12),
              child: Text(_failure!, style: BrainType.bodyMuted),
            ),
        ],
      ),
    );
  }

  bool _sameJournal(List<ChatTurnEvent> a, List<ChatTurnEvent> b) {
    if (identical(a, b)) {
      return true;
    }
    if (a.length != b.length) {
      return false;
    }
    for (var i = 0; i < a.length; i++) {
      if (a[i].sequence != b[i].sequence ||
          a[i].text != b[i].text ||
          a[i].buttons.length != b[i].buttons.length ||
          a[i].charts.length != b[i].charts.length) {
        return false;
      }
    }
    return true;
  }
}
final class SignInCardRail extends StatelessWidget {
  const SignInCardRail({
    super.key,
    required this.cards,
    this.onOpenSignIn,
  });

  final List<SignInCardProjection> cards;
  final OpenUrl? onOpenSignIn;

  @override
  Widget build(BuildContext context) {
    return Column(
      key: const Key('sign_in_card_rail'),
      mainAxisSize: MainAxisSize.min,
      children: [
        for (final card in cards)
          SignInCard(
            key: Key('sign_in_card_${card.state}'),
            card: card,
            onOpenSignIn: onOpenSignIn,
          ),
      ],
    );
  }
}

final class SignInCard extends StatelessWidget {
  const SignInCard({
    super.key,
    required this.card,
    this.onOpenSignIn,
  });

  final SignInCardProjection card;
  final OpenUrl? onOpenSignIn;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: BrainPalette.surfaceRaised,
      child: Container(
        width: double.infinity,
        margin: const EdgeInsets.fromLTRB(16, 12, 16, 0),
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
        decoration: BoxDecoration(
          border: Border.all(color: BrainPalette.line),
          borderRadius: BorderRadius.circular(10),
          color: BrainPalette.surfaceSunken,
        ),
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    card.serverDisplayName,
                    style: BrainType.metaStrong.copyWith(
                      color: BrainPalette.textPrimary,
                    ),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    'Sign in to continue this chat turn.',
                    style: BrainType.meta.copyWith(
                      color: BrainPalette.textMuted,
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 12),
            FilledButton(
              key: Key('sign_in_open_${card.state}'),
              onPressed: onOpenSignIn == null
                  ? null
                  : () => unawaited(onOpenSignIn!(card.signInUrl)),
              style: FilledButton.styleFrom(
                backgroundColor: BrainPalette.signal.withValues(alpha: 0.16),
                foregroundColor: BrainPalette.signal,
              ),
              child: Text('Sign in via ${card.serverDisplayName}'),
            ),
          ],
        ),
      ),
    );
  }
}

