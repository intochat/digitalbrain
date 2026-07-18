import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:ino_flutter/persona/persona_state.dart';
import 'package:ino_flutter/state/ino_bloc.dart';
import 'package:ino_flutter/state/persona_bloc.dart';
import 'package:ino_flutter/ui/components/chat_bubble.dart';
import 'package:ino_flutter/ui/ino_runtime.dart';
import 'package:rfw/formats.dart' show parseLibraryFile;
import 'package:rfw/rfw.dart';

/// Live chat surface: renders InoBloc.state.messages as a vertical thread.
///
/// User turns → speech-bubble. Assistant turns → either a bubble (plain text)
/// or an RFW-rendered card (when the message carries an rfwDescription /
/// rfwData payload). RemoteWidget event callbacks are forwarded to InoBloc as
/// RfwEventEmitted so card taps (flight.selected, hotel.selected, …) advance
/// the underlying TripPlanner grain.
///
/// `?q=…` deep-links auto-send on first frame so Telegram, gRPC E2E tests and
/// the GoRouter root redirect all land here with the prompt already running.
class ChatScreen extends StatefulWidget {
  const ChatScreen({super.key});

  @override
  State<ChatScreen> createState() => _ChatScreenState();
}

class _ChatScreenState extends State<ChatScreen> {
  final TextEditingController _input = TextEditingController();
  final ScrollController _scroll = ScrollController();

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final q = Uri.base.queryParameters['q'];
      if (q != null && q.isNotEmpty) {
        context.read<PersonaBloc>().add(
              PersonaEmotionChanged(PersonaEmotion.thinking),
            );
        context.read<InoBloc>().add(SendMessage(q));
      }
    });
  }

  @override
  void dispose() {
    _input.dispose();
    _scroll.dispose();
    super.dispose();
  }

  void _send() {
    final text = _input.text.trim();
    if (text.isEmpty) return;
    _input.clear();
    context.read<PersonaBloc>().add(
          PersonaEmotionChanged(PersonaEmotion.thinking),
        );
    context.read<InoBloc>().add(SendMessage(text));
  }

  void _scrollToBottom() {
    if (!_scroll.hasClients) return;
    _scroll.animateTo(
      _scroll.position.maxScrollExtent,
      duration: const Duration(milliseconds: 220),
      curve: Curves.easeOut,
    );
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        title: const Text('ino'),
        leading: IconButton(
          icon: const Icon(Icons.psychology_alt_outlined),
          tooltip: 'Brain view',
          onPressed: () => context.go('/brain'),
        ),
      ),
      body: BlocConsumer<InoBloc, InoBlocState>(
        listenWhen: (a, b) => a.messages.length != b.messages.length,
        listener: (_, __) =>
            WidgetsBinding.instance.addPostFrameCallback((_) => _scrollToBottom()),
        builder: (context, state) {
          return Column(
            children: [
              Expanded(
                child: state.messages.isEmpty
                    ? _EmptyHint(scheme: scheme)
                    : ListView.builder(
                        controller: _scroll,
                        padding: const EdgeInsets.symmetric(vertical: 12),
                        itemCount: state.messages.length,
                        itemBuilder: (context, i) =>
                            _MessageView(message: state.messages[i]),
                      ),
              ),
              _Composer(
                controller: _input,
                onSend: _send,
                isLoading: state.isLoading,
              ),
            ],
          );
        },
      ),
    );
  }
}

class _EmptyHint extends StatelessWidget {
  const _EmptyHint({required this.scheme});
  final ColorScheme scheme;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Text(
          'Talk to ino. Try: "plan a trip to Bali next month".',
          textAlign: TextAlign.center,
          style: TextStyle(color: scheme.onSurface.withValues(alpha: 0.55)),
        ),
      ),
    );
  }
}

class _MessageView extends StatelessWidget {
  const _MessageView({required this.message});
  final ChatMessage message;

  @override
  Widget build(BuildContext context) {
    if (message.isUser || !message.hasRfw) {
      return chatBubbleVisual(
        context: context,
        text: message.text,
        isUser: message.isUser,
      );
    }
    return _RfwCard(message: message);
  }
}

/// Per-message RFW host. Each card builds its own Runtime + DynamicContent so
/// libraries with the same import name across messages don't clobber one
/// another. The runtime stays alive for the lifetime of the bubble; tap
/// callbacks (event 'flight.selected' { … }) round-trip back to InoBloc as
/// RfwEventEmitted with the originating correlation_id.
class _RfwCard extends StatefulWidget {
  const _RfwCard({required this.message});
  final ChatMessage message;

  @override
  State<_RfwCard> createState() => _RfwCardState();
}

class _RfwCardState extends State<_RfwCard> {
  static const LibraryName _rootLib = LibraryName(<String>['ino', 'message']);

  late final InoRuntime _ino;
  late final DynamicContent _data;
  Object? _parseError;

  @override
  void initState() {
    super.initState();
    _ino = createInoRuntime();
    try {
      final dsl = utf8.decode(widget.message.rfwDescription!);
      _ino.runtime.update(_rootLib, parseLibraryFile(dsl));
      final json = jsonDecode(utf8.decode(widget.message.rfwData!));
      _data = DynamicContent(json is Map<String, Object?>
          ? json
          : <String, Object?>{'value': json});
    } catch (e) {
      _data = DynamicContent();
      _parseError = e;
    }
  }

  void _onRfwEvent(String name, DynamicMap args) {
    final correlationId = widget.message.correlationId;
    if (correlationId == null || correlationId.isEmpty) return;
    final stringArgs = <String, String>{
      for (final e in args.entries) e.key: e.value?.toString() ?? '',
    };
    context.read<InoBloc>().add(RfwEventEmitted(
          correlationId: correlationId,
          eventName: name,
          args: stringArgs,
        ));
  }

  @override
  Widget build(BuildContext context) {
    if (_parseError != null) {
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: 4, horizontal: 12),
        child: Text(
          'Card render failed: $_parseError',
          style: const TextStyle(color: Colors.redAccent, fontSize: 12),
        ),
      );
    }
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4, horizontal: 12),
      child: Align(
        alignment: Alignment.centerLeft,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 520),
          child: RemoteWidget(
            runtime: _ino.runtime,
            data: _data,
            widget: const FullyQualifiedWidgetName(_rootLib, 'root'),
            onEvent: _onRfwEvent,
          ),
        ),
      ),
    );
  }
}

class _Composer extends StatelessWidget {
  const _Composer({
    required this.controller,
    required this.onSend,
    required this.isLoading,
  });

  final TextEditingController controller;
  final VoidCallback onSend;
  final bool isLoading;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return SafeArea(
      top: false,
      child: Container(
        margin: const EdgeInsets.fromLTRB(12, 0, 12, 12),
        padding: const EdgeInsets.fromLTRB(8, 6, 6, 6),
        decoration: BoxDecoration(
          color: Colors.black.withAlpha(170),
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: Colors.white.withAlpha(30)),
        ),
        child: Row(
          children: [
            const SizedBox(width: 8),
            Expanded(
              child: Shortcuts(
                shortcuts: const {
                  SingleActivator(LogicalKeyboardKey.enter):
                      _SubmitIntent(),
                },
                child: Actions(
                  actions: <Type, Action<Intent>>{
                    _SubmitIntent: CallbackAction<_SubmitIntent>(
                      onInvoke: (_) {
                        onSend();
                        return null;
                      },
                    ),
                  },
                  child: TextField(
                    controller: controller,
                    enabled: !isLoading,
                    style: const TextStyle(color: Colors.white),
                    decoration: InputDecoration(
                      hintText:
                          isLoading ? 'ino is thinking...' : 'Talk to ino...',
                      hintStyle:
                          TextStyle(color: Colors.white.withAlpha(120)),
                      border: InputBorder.none,
                      contentPadding:
                          const EdgeInsets.symmetric(horizontal: 12),
                    ),
                    onSubmitted: (_) => onSend(),
                  ),
                ),
              ),
            ),
            IconButton(
              onPressed: isLoading ? null : onSend,
              icon: Icon(Icons.arrow_upward_rounded, color: scheme.primary),
            ),
          ],
        ),
      ),
    );
  }
}

class _SubmitIntent extends Intent {
  const _SubmitIntent();
}
