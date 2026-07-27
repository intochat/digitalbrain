import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'brain_theme.dart';

typedef SendMessage = Future<void> Function(String text);

final class BrainChatApp extends StatelessWidget {
  const BrainChatApp({
    super.key,
    required this.chatName,
    this.turns,
    this.onSend,
    this.statusMessage,
  });

  final String chatName;
  final Stream<ChatTurnEvent>? turns;
  final SendMessage? onSend;
  final String? statusMessage;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'DigitalBrain',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        useMaterial3: true,
        brightness: Brightness.dark,
        scaffoldBackgroundColor: BrainPalette.surface,
        colorScheme: ColorScheme.fromSeed(
          seedColor: BrainPalette.signal,
          brightness: Brightness.dark,
          surface: BrainPalette.surface,
        ),
      ),
      home: BrainChatScreen(
        chatName: chatName,
        turns: turns,
        onSend: onSend,
        statusMessage: statusMessage,
      ),
    );
  }
}

final class BrainChatScreen extends StatefulWidget {
  const BrainChatScreen({
    super.key,
    required this.chatName,
    this.turns,
    this.onSend,
    this.statusMessage,
  });

  final String chatName;
  final Stream<ChatTurnEvent>? turns;
  final SendMessage? onSend;
  final String? statusMessage;

  @override
  State<BrainChatScreen> createState() => _BrainChatScreenState();
}

final class _BrainChatScreenState extends State<BrainChatScreen> {
  final _turns = <ChatTurnEvent>[];
  final _seen = <int>{};
  final _composer = TextEditingController();
  final _composerFocus = FocusNode();
  final _scroll = ScrollController();

  StreamSubscription<ChatTurnEvent>? _subscription;
  bool _awaitingBrain = false;
  String? _failure;

  @override
  void initState() {
    super.initState();
    _listen(widget.turns);
  }

  @override
  void didUpdateWidget(covariant BrainChatScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!identical(oldWidget.turns, widget.turns)) {
      _subscription?.cancel();
      _listen(widget.turns);
    }
  }

  void _listen(Stream<ChatTurnEvent>? turns) {
    _subscription = turns?.listen((turn) {
      if (!mounted || !_seen.add(turn.sequence)) {
        return;
      }
      setState(() {
        _turns.add(turn);
        _turns.sort((a, b) => a.sequence.compareTo(b.sequence));
        if (!turn.fromUser) {
          _awaitingBrain = false;
        }
      });
      _scrollToLatest();
    });
  }

  void _scrollToLatest() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!_scroll.hasClients) {
        return;
      }
      _scroll.animateTo(
        _scroll.position.maxScrollExtent,
        duration: const Duration(milliseconds: 220),
        curve: Curves.easeOutCubic,
      );
    });
  }

  Future<void> _send() async {
    final text = _composer.text.trim();
    final send = widget.onSend;
    if (text.isEmpty || send == null) {
      return;
    }

    _composer.clear();
    setState(() {
      _awaitingBrain = true;
      _failure = null;
    });

    try {
      await send(text);
    } on Object catch (error) {
      if (mounted) {
        setState(() {
          _awaitingBrain = false;
          _failure = '$error';
        });
      }
    }
    _composerFocus.requestFocus();
  }

  @override
  void dispose() {
    _subscription?.cancel();
    _composer.dispose();
    _composerFocus.dispose();
    _scroll.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Column(
        children: [
          _StatusBar(chatName: widget.chatName, message: widget.statusMessage),
          Expanded(
            child: _turns.isEmpty && !_awaitingBrain
                ? const _EmptyJournal()
                : _Journal(
                    turns: _turns,
                    awaitingBrain: _awaitingBrain,
                    controller: _scroll,
                  ),
          ),
          if (_failure != null) _FailureNotice(message: _failure!),
          _Composer(
            controller: _composer,
            focusNode: _composerFocus,
            enabled: widget.onSend != null,
            onSubmit: _send,
          ),
        ],
      ),
    );
  }
}

final class _StatusBar extends StatelessWidget {
  const _StatusBar({required this.chatName, this.message});

  final String chatName;
  final String? message;

  @override
  Widget build(BuildContext context) {
    final offline = message != null && message!.isNotEmpty;

    return Container(
      height: 44,
      padding: const EdgeInsets.symmetric(horizontal: 20),
      decoration: const BoxDecoration(
        color: BrainPalette.surfaceRaised,
        border: Border(bottom: BorderSide(color: BrainPalette.line)),
      ),
      child: Row(
        children: [
          const Text('DigitalBrain', style: BrainType.title),
          const SizedBox(width: 12),
          Text('chat:$chatName', style: BrainType.meta),
          const Spacer(),
          Container(
            width: 6,
            height: 6,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: offline ? BrainPalette.signal : BrainPalette.owner,
            ),
          ),
          const SizedBox(width: 8),
          Text(offline ? 'not connected' : 'connected', style: BrainType.meta),
        ],
      ),
    );
  }
}

final class _EmptyJournal extends StatelessWidget {
  const _EmptyJournal();

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Text('Nothing yet.', style: BrainType.empty),
          const SizedBox(height: 8),
          Text(
            'Ask your brain to do something.',
            style: BrainType.body.copyWith(color: BrainPalette.textMuted),
          ),
        ],
      ),
    );
  }
}

/// The signature: every turn keeps its real journal sequence in a monospace
/// rail. Order here is durable kernel truth, not scroll position.
final class _Journal extends StatelessWidget {
  const _Journal({
    required this.turns,
    required this.awaitingBrain,
    required this.controller,
  });

  final List<ChatTurnEvent> turns;
  final bool awaitingBrain;
  final ScrollController controller;

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: Alignment.topCenter,
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 760),
        child: ListView.builder(
          key: const Key('chat_journal'),
          controller: controller,
          padding: const EdgeInsets.symmetric(vertical: 28, horizontal: 20),
          itemCount: turns.length + (awaitingBrain ? 1 : 0),
          itemBuilder: (context, index) {
            if (index == turns.length) {
              return const _AwaitingTurn();
            }
            return _JournalTurn(turn: turns[index]);
          },
        ),
      ),
    );
  }
}

final class _JournalTurn extends StatelessWidget {
  const _JournalTurn({required this.turn});

  final ChatTurnEvent turn;

  @override
  Widget build(BuildContext context) {
    final voice = turn.fromUser ? BrainPalette.owner : BrainPalette.signal;

    return Padding(
      key: Key('turn_${turn.sequence}'),
      padding: const EdgeInsets.only(bottom: 26),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 44,
            child: Padding(
              padding: const EdgeInsets.only(top: 3),
              child: Text(
                turn.sequence.toString().padLeft(3, '0'),
                style: BrainType.meta,
              ),
            ),
          ),
          Container(width: 2, height: 20, color: voice),
          const SizedBox(width: 16),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  turn.fromUser ? 'you' : 'brain',
                  style: BrainType.meta.copyWith(color: voice),
                ),
                const SizedBox(height: 6),
                SelectableText(turn.text, style: BrainType.body),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

final class _AwaitingTurn extends StatelessWidget {
  const _AwaitingTurn();

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 26),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const SizedBox(width: 44),
          Container(width: 2, height: 20, color: BrainPalette.signal),
          const SizedBox(width: 16),
          Text(
            'thinking',
            style: BrainType.meta.copyWith(color: BrainPalette.signal),
          ),
        ],
      ),
    );
  }
}

final class _FailureNotice extends StatelessWidget {
  const _FailureNotice({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
      color: BrainPalette.surfaceRaised,
      child: Text(
        message,
        style: BrainType.meta.copyWith(color: BrainPalette.signal),
      ),
    );
  }
}

final class _Composer extends StatelessWidget {
  const _Composer({
    required this.controller,
    required this.focusNode,
    required this.enabled,
    required this.onSubmit,
  });

  final TextEditingController controller;
  final FocusNode focusNode;
  final bool enabled;
  final Future<void> Function() onSubmit;

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: const BoxDecoration(
        color: BrainPalette.surfaceRaised,
        border: Border(top: BorderSide(color: BrainPalette.line)),
      ),
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 14),
      child: Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 760),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Expanded(
                child: Shortcuts(
                  shortcuts: const {
                    SingleActivator(LogicalKeyboardKey.enter): _SubmitIntent(),
                  },
                  child: Actions(
                    actions: {
                      _SubmitIntent: CallbackAction<_SubmitIntent>(
                        onInvoke: (_) {
                          unawaited(onSubmit());
                          return null;
                        },
                      ),
                    },
                    child: TextField(
                      key: const Key('chat_composer'),
                      controller: controller,
                      focusNode: focusNode,
                      enabled: enabled,
                      autofocus: true,
                      maxLines: 4,
                      minLines: 1,
                      style: BrainType.body,
                      cursorColor: BrainPalette.signal,
                      decoration: InputDecoration(
                        isDense: true,
                        border: InputBorder.none,
                        hintText: 'Ask your brain…',
                        hintStyle: BrainType.body
                            .copyWith(color: BrainPalette.textMuted),
                      ),
                    ),
                  ),
                ),
              ),
              const SizedBox(width: 12),
              TextButton(
                key: const Key('chat_send'),
                onPressed: enabled ? () => unawaited(onSubmit()) : null,
                style: TextButton.styleFrom(
                  foregroundColor: BrainPalette.signal,
                  padding:
                      const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                ),
                child: const Text('Send', style: BrainType.meta),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

final class _SubmitIntent extends Intent {
  const _SubmitIntent();
}
