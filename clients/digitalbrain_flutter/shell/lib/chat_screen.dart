import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'activity_screen.dart';
import 'brain_screen.dart';
import 'brain_theme.dart';

typedef SendMessage = Future<void> Function(String text);

final class BrainChatApp extends StatelessWidget {
  const BrainChatApp({
    super.key,
    required this.chatName,
    this.turns,
    this.topology,
    this.onSend,
    this.statusMessage,
  });

  final String chatName;
  final Stream<ChatTurnEvent>? turns;
  final Stream<BrainTopologySnapshot>? topology;
  final SendMessage? onSend;
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
    this.statusMessage,
  });

  final String chatName;
  final Stream<ChatTurnEvent>? turns;
  final Stream<BrainTopologySnapshot>? topology;
  final SendMessage? onSend;
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
  });

  final String chatName;
  final List<ChatTurnEvent> turns;
  final SendMessage? onSend;

  @override
  State<BrainChatScreen> createState() => _BrainChatScreenState();
}

final class _BrainChatScreenState extends State<BrainChatScreen> {
  final _composer = TextEditingController();
  final _composerFocus = FocusNode();
  final _scroll = ScrollController();

  bool _awaitingBrain = false;
  int _awaitingAfterSequence = 0;
  String? _failure;

  @override
  void didUpdateWidget(covariant BrainChatScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.turns.length != oldWidget.turns.length) {
      if (_awaitingBrain &&
          widget.turns.any(
            (turn) => !turn.fromUser && turn.sequence > _awaitingAfterSequence,
          )) {
        setState(() => _awaitingBrain = false);
      }
      _scrollToLatest();
    }
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
      _awaitingAfterSequence = widget.turns.isEmpty
          ? 0
          : widget.turns.last.sequence;
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
    _composer.dispose();
    _composerFocus.dispose();
    _scroll.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      color: BrainPalette.surface,
      child: Column(
        children: [
          Expanded(
            child: widget.turns.isEmpty && !_awaitingBrain
                ? const _EmptyJournal()
                : _Journal(
                    turns: widget.turns,
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

final class _EmptyJournal extends StatelessWidget {
  const _EmptyJournal();

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 54,
            height: 54,
            decoration: BoxDecoration(
              color: BrainPalette.surfaceRaised,
              borderRadius: BorderRadius.circular(18),
              border: Border.all(color: BrainPalette.line),
            ),
            child: const Icon(
              Icons.chat_bubble_outline_rounded,
              color: BrainPalette.textMuted,
            ),
          ),
          const SizedBox(height: 18),
          const Text('Nothing yet.', style: BrainType.empty),
          const SizedBox(height: 7),
          Text(
            'Ask your brain to do something.',
            style: BrainType.body.copyWith(color: BrainPalette.textMuted),
          ),
        ],
      ),
    );
  }
}

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
        constraints: const BoxConstraints(maxWidth: 820),
        child: ListView.builder(
          key: const Key('chat_journal'),
          controller: controller,
          padding: const EdgeInsets.symmetric(vertical: 34, horizontal: 28),
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
      padding: const EdgeInsets.only(bottom: 28),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 48,
            child: Padding(
              padding: const EdgeInsets.only(top: 3),
              child: Text(
                turn.sequence.toString().padLeft(3, '0'),
                style: BrainType.meta,
              ),
            ),
          ),
          Container(
            width: 3,
            height: 22,
            decoration: BoxDecoration(
              color: voice,
              borderRadius: BorderRadius.circular(2),
            ),
          ),
          const SizedBox(width: 18),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  turn.fromUser ? 'you' : 'brain',
                  style: BrainType.metaStrong.copyWith(color: voice),
                ),
                const SizedBox(height: 7),
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
      padding: const EdgeInsets.only(bottom: 28),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const SizedBox(width: 48),
          Container(
            width: 3,
            height: 22,
            decoration: BoxDecoration(
              color: BrainPalette.signal,
              borderRadius: BorderRadius.circular(2),
            ),
          ),
          const SizedBox(width: 18),
          Text(
            'thinking',
            style: BrainType.metaStrong.copyWith(color: BrainPalette.signal),
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
      padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 11),
      color: BrainPalette.signal.withValues(alpha: 0.08),
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
      padding: const EdgeInsets.symmetric(horizontal: 28, vertical: 16),
      child: Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 820),
          child: Container(
            decoration: BoxDecoration(
              color: BrainPalette.surfaceSunken,
              borderRadius: BorderRadius.circular(14),
              border: Border.all(color: BrainPalette.lineStrong),
            ),
            padding: const EdgeInsets.only(left: 16, right: 8),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Expanded(
                  child: Shortcuts(
                    shortcuts: const {
                      SingleActivator(LogicalKeyboardKey.enter):
                          _SubmitIntent(),
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
                          contentPadding: const EdgeInsets.symmetric(
                            vertical: 13,
                          ),
                          hintText: 'Ask your brain…',
                          hintStyle: BrainType.body.copyWith(
                            color: BrainPalette.textMuted,
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                IconButton(
                  key: const Key('chat_send'),
                  onPressed: enabled ? () => unawaited(onSubmit()) : null,
                  tooltip: 'Send',
                  color: BrainPalette.signal,
                  disabledColor: BrainPalette.textFaint,
                  icon: const Icon(Icons.arrow_upward_rounded),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

final class _SubmitIntent extends Intent {
  const _SubmitIntent();
}
