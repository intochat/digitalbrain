import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../activity_screen.dart';
import '../brain_screen.dart';
import 'brain_chat_screen.dart';
import 'chat_contracts.dart';
import 'workspace_chrome.dart';

final class BrainWorkspace extends StatefulWidget {
  const BrainWorkspace({
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
  State<BrainWorkspace> createState() => _BrainWorkspaceState();
}

final class _BrainWorkspaceState extends State<BrainWorkspace> {
  static const _compactBreakpoint = 720.0;

  final _turns = <ChatTurnEvent>[];
  final _seen = <int>{};
  List<ChatTurnEvent> _projectedTurns = const [];
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
  void didUpdateWidget(covariant BrainWorkspace oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.chatName != widget.chatName) {
      _turns.clear();
      _seen.clear();
      _projectedTurns = const [];
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
          _projectedTurns = List<ChatTurnEvent>.unmodifiable(_turns);
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
    final pages = [
      BrainChatScreen(
        chatName: widget.chatName,
        turns: _projectedTurns,
        onSend: widget.onSend,
        onStream: widget.onStream,
      ),
      ActivityScreen(turns: _projectedTurns),
      BrainScreen(
        chatName: widget.chatName,
        turns: _projectedTurns,
        topology: _topology,
        statusMessage: _statusMessage,
      ),
    ];

    return LayoutBuilder(
      builder: (context, constraints) {
        final compact = constraints.maxWidth < _compactBreakpoint;
        final content = Column(
          children: [
            WorkspaceStatusBar(
              chatName: widget.chatName,
              section: workspaceSectionName(_destination),
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
                    WorkspaceRail(
                      selectedIndex: _destination,
                      onSelected: _selectDestination,
                    ),
                    const VerticalDivider(width: 1, thickness: 1),
                    Expanded(child: content),
                  ],
                ),
          bottomNavigationBar: compact
              ? WorkspaceNavigationBar(
                  selectedIndex: _destination,
                  onSelected: _selectDestination,
                )
              : null,
        );
      },
    );
  }
}
