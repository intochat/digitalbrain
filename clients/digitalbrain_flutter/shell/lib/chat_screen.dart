import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter_chat_ui/flutter_chat_ui.dart';
import 'package:flyer_chat_text_message/flyer_chat_text_message.dart';
import 'package:flyer_chat_text_stream_message/flyer_chat_text_stream_message.dart';
import 'package:provider/provider.dart';
import 'package:uuid/uuid.dart';

import 'activity_screen.dart';
import 'brain_screen.dart';
import 'brain_theme.dart';

typedef SendMessage = Future<void> Function(String text);
typedef StreamMessage = Stream<ChatDelta> Function(String text);

const _ownerUserId = 'owner';
const _assistantUserId = 'assistant';

final class BrainChatApp extends StatelessWidget {
  const BrainChatApp({
    super.key,
    required this.chatName,
    this.turns,
    this.topology,
    this.onSend,
    this.onStream,
    this.statusMessage,
  });

  final String chatName;
  final Stream<ChatTurnEvent>? turns;
  final Stream<BrainTopologySnapshot>? topology;
  final SendMessage? onSend;
  final StreamMessage? onStream;
  final String? statusMessage;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'DigitalBrain',
      debugShowCheckedModeBanner: false,
      theme: BrainTheme.dark(),
      home: _BrainWorkspace(
        chatName: chatName,
        turns: turns,
        topology: topology,
        onSend: onSend,
        onStream: onStream,
        statusMessage: statusMessage,
      ),
    );
  }
}

final class _BrainWorkspace extends StatefulWidget {
  const _BrainWorkspace({
    required this.chatName,
    this.turns,
    this.topology,
    this.onSend,
    this.onStream,
    this.statusMessage,
  });

  final String chatName;
  final Stream<ChatTurnEvent>? turns;
  final Stream<BrainTopologySnapshot>? topology;
  final SendMessage? onSend;
  final StreamMessage? onStream;
  final String? statusMessage;

  @override
  State<_BrainWorkspace> createState() => _BrainWorkspaceState();
}

final class _BrainWorkspaceState extends State<_BrainWorkspace> {
  static const _compactBreakpoint = 720.0;

  final _turns = <ChatTurnEvent>[];
  final _seen = <int>{};
  StreamSubscription<ChatTurnEvent>? _subscription;
  StreamSubscription<BrainTopologySnapshot>? _topologySubscription;
  BrainTopologySnapshot? _topology;
  int _destination = 0;
  String? _turnFailure;
  String? _topologyFailure;

  @override
  void initState() {
    super.initState();
    _listen(widget.turns);
    _listenTopology(widget.topology);
  }

  @override
  void didUpdateWidget(covariant _BrainWorkspace oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.chatName != widget.chatName) {
      _turns.clear();
      _seen.clear();
    }
    if (!identical(oldWidget.turns, widget.turns)) {
      unawaited(_subscription?.cancel());
      _listen(widget.turns);
    }
    if (!identical(oldWidget.topology, widget.topology)) {
      unawaited(_topologySubscription?.cancel());
      _listenTopology(widget.topology);
    }
  }

  void _listenTopology(Stream<BrainTopologySnapshot>? topology) {
    _topologySubscription = topology?.listen(
      (snapshot) {
        if (mounted) {
          setState(() {
            _topology = snapshot;
            _topologyFailure = null;
          });
        }
      },
      onError: (Object error) {
        if (mounted) {
          setState(() {
            _topology = null;
            _topologyFailure = '$error';
          });
        }
      },
    );
  }

  void _listen(Stream<ChatTurnEvent>? turns) {
    _turnFailure = null;
    _subscription = turns?.listen(
      (turn) {
        if (!mounted || !_seen.add(turn.sequence)) {
          return;
        }
        setState(() {
          _turns.add(turn);
          _turns.sort((a, b) => a.sequence.compareTo(b.sequence));
        });
      },
      onError: (Object error) {
        if (mounted) {
          setState(() => _turnFailure = '$error');
        }
      },
    );
  }

  String? get _statusMessage =>
      widget.statusMessage ?? _turnFailure ?? _topologyFailure;

  void _selectDestination(int index) {
    if (_destination != index) {
      setState(() => _destination = index);
    }
  }

  @override
  void dispose() {
    unawaited(_subscription?.cancel());
    unawaited(_topologySubscription?.cancel());
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final projectedTurns = List<ChatTurnEvent>.unmodifiable(_turns);
    final pages = [
      BrainChatScreen(
        chatName: widget.chatName,
        turns: projectedTurns,
        onSend: widget.onSend,
        onStream: widget.onStream,
      ),
      ActivityScreen(turns: projectedTurns),
      BrainScreen(
        chatName: widget.chatName,
        turns: projectedTurns,
        topology: _topology,
        statusMessage: _statusMessage,
      ),
    ];

    return LayoutBuilder(
      builder: (context, constraints) {
        final compact = constraints.maxWidth < _compactBreakpoint;
        final content = Column(
          children: [
            _StatusBar(
              chatName: widget.chatName,
              section: _sectionName(_destination),
              message: _statusMessage,
            ),
            Expanded(
              child: IndexedStack(index: _destination, children: pages),
            ),
          ],
        );

        return Scaffold(
          body: compact
              ? content
              : Row(
                  children: [
                    _WorkspaceRail(
                      selectedIndex: _destination,
                      onSelected: _selectDestination,
                    ),
                    const VerticalDivider(width: 1, thickness: 1),
                    Expanded(child: content),
                  ],
                ),
          bottomNavigationBar: compact
              ? _WorkspaceNavigationBar(
                  selectedIndex: _destination,
                  onSelected: _selectDestination,
                )
              : null,
        );
      },
    );
  }
}

String _sectionName(int index) => switch (index) {
  0 => 'Chat',
  1 => 'Activity',
  _ => 'Brain',
};

final class _WorkspaceRail extends StatelessWidget {
  const _WorkspaceRail({required this.selectedIndex, required this.onSelected});

  final int selectedIndex;
  final ValueChanged<int> onSelected;

  @override
  Widget build(BuildContext context) {
    return NavigationRail(
      backgroundColor: BrainPalette.navigation,
      minWidth: 88,
      groupAlignment: -0.78,
      labelType: NavigationRailLabelType.all,
      selectedIndex: selectedIndex,
      onDestinationSelected: onSelected,
      leading: const Padding(
        padding: EdgeInsets.only(top: 10, bottom: 28),
        child: _BrainMark(),
      ),
      destinations: _destinations(),
    );
  }
}

final class _WorkspaceNavigationBar extends StatelessWidget {
  const _WorkspaceNavigationBar({
    required this.selectedIndex,
    required this.onSelected,
  });

  final int selectedIndex;
  final ValueChanged<int> onSelected;

  @override
  Widget build(BuildContext context) {
    return NavigationBar(
      selectedIndex: selectedIndex,
      onDestinationSelected: onSelected,
      destinations: const [
        NavigationDestination(
          icon: Icon(Icons.forum_outlined, key: Key('destination_chat')),
          selectedIcon: Icon(Icons.forum, key: Key('destination_chat')),
          label: 'Chat',
        ),
        NavigationDestination(
          icon: Icon(Icons.timeline_outlined, key: Key('destination_activity')),
          selectedIcon: Icon(Icons.timeline, key: Key('destination_activity')),
          label: 'Activity',
        ),
        NavigationDestination(
          icon: Icon(Icons.hub_outlined, key: Key('destination_brain')),
          selectedIcon: Icon(Icons.hub, key: Key('destination_brain')),
          label: 'Brain',
        ),
      ],
    );
  }
}

List<NavigationRailDestination> _destinations() => const [
  NavigationRailDestination(
    icon: Icon(Icons.forum_outlined, key: Key('destination_chat')),
    selectedIcon: Icon(Icons.forum, key: Key('destination_chat')),
    label: Text('Chat'),
  ),
  NavigationRailDestination(
    icon: Icon(Icons.timeline_outlined, key: Key('destination_activity')),
    selectedIcon: Icon(Icons.timeline, key: Key('destination_activity')),
    label: Text('Activity'),
  ),
  NavigationRailDestination(
    icon: Icon(Icons.hub_outlined, key: Key('destination_brain')),
    selectedIcon: Icon(Icons.hub, key: Key('destination_brain')),
    label: Text('Brain'),
  ),
];

final class _BrainMark extends StatelessWidget {
  const _BrainMark();

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 36,
      height: 36,
      decoration: BoxDecoration(
        color: BrainPalette.signal.withValues(alpha: 0.14),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: BrainPalette.signal.withValues(alpha: 0.4)),
      ),
      child: const Icon(
        Icons.graphic_eq_rounded,
        color: BrainPalette.signal,
        size: 20,
      ),
    );
  }
}

final class _StatusBar extends StatelessWidget {
  const _StatusBar({
    required this.chatName,
    required this.section,
    this.message,
  });

  final String chatName;
  final String section;
  final String? message;

  @override
  Widget build(BuildContext context) {
    final offline = message != null && message!.isNotEmpty;

    return Container(
      height: 58,
      padding: const EdgeInsets.symmetric(horizontal: 24),
      decoration: const BoxDecoration(
        color: BrainPalette.surfaceRaised,
        border: Border(bottom: BorderSide(color: BrainPalette.line)),
      ),
      child: Row(
        children: [
          const Text('DigitalBrain', style: BrainType.title),
          const SizedBox(width: 10),
          Container(width: 1, height: 16, color: BrainPalette.lineStrong),
          const SizedBox(width: 10),
          Text(section, style: BrainType.metaStrong),
          const Spacer(),
          Text('chat:$chatName', style: BrainType.meta),
          const SizedBox(width: 14),
          Container(
            width: 7,
            height: 7,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: offline ? BrainPalette.signal : BrainPalette.success,
              boxShadow: [
                BoxShadow(
                  color: (offline ? BrainPalette.signal : BrainPalette.success)
                      .withValues(alpha: 0.35),
                  blurRadius: 8,
                ),
              ],
            ),
          ),
          const SizedBox(width: 8),
          Text(offline ? 'not connected' : 'connected', style: BrainType.meta),
        ],
      ),
    );
  }
}

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
  static const _owner = User(id: _ownerUserId, name: 'you');
  static const _assistant = User(id: _assistantUserId, name: 'brain');
  static const _uuid = Uuid();

  final _controller = InMemoryChatController();
  final _streamStates = _StreamStateStore();
  final _seenSequences = <int>{};
  String? _activeStreamId;
  String? _failure;

  @override
  void initState() {
    super.initState();
    _syncJournal(widget.turns);
  }

  @override
  void didUpdateWidget(covariant BrainChatScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!identical(oldWidget.turns, widget.turns) ||
        oldWidget.turns.length != widget.turns.length) {
      unawaited(_syncJournal(widget.turns));
    }
  }

  Future<void> _syncJournal(List<ChatTurnEvent> turns) async {
    final messages = <Message>[];
    for (final turn in turns) {
      _seenSequences.add(turn.sequence);
      messages.add(
        TextMessage(
          id: 'turn_${turn.sequence}',
          authorId: turn.fromUser ? _ownerUserId : _assistantUserId,
          createdAt: turn.timestamp.toUtc(),
          text: turn.text,
        ),
      );
    }

    if (_activeStreamId != null &&
        turns.any((turn) => !turn.fromUser && turn.sequence > 0)) {
      _streamStates.forget(_activeStreamId!);
      _activeStreamId = null;
    }

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
    await _controller.insertMessage(
      TextMessage(
        id: localId,
        authorId: _ownerUserId,
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
      authorId: _assistantUserId,
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
                currentUserId: _ownerUserId,
                resolveUser: (id) async => switch (id) {
                  _ownerUserId => _owner,
                  _assistantUserId => _assistant,
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
                            .watch<_StreamStateStore>()
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
          if (_failure != null) _FailureNotice(message: _failure!),
        ],
      ),
    );
  }
}

final class _StreamStateStore extends ChangeNotifier {
  final _states = <String, StreamState>{};

  StreamState stateFor(String streamId) =>
      _states[streamId] ?? StreamStateLoading();

  void start(String streamId) {
    _states[streamId] = StreamStateLoading();
    notifyListeners();
  }

  void streaming(String streamId, String text) {
    _states[streamId] = StreamStateStreaming(text);
    notifyListeners();
  }

  void complete(String streamId, String text) {
    _states[streamId] = StreamStateCompleted(text);
    notifyListeners();
  }

  void error(String streamId, String message) {
    _states[streamId] = StreamStateError(message);
    notifyListeners();
  }

  void forget(String streamId) {
    _states.remove(streamId);
    notifyListeners();
  }
}

final class _FailureNotice extends StatelessWidget {
  const _FailureNotice({required this.message});

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
