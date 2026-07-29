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
    this.onLoadTopology,
    this.onSend,
    this.onStream,
    this.statusMessage,
  });

  final String chatName;
  final Stream<ChatTurnEvent>? turns;
  final LoadTopology? onLoadTopology;
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
  BrainTopologySnapshot? _topology;
  int _destination = 0;
  int _topologyLoadEpoch = 0;
  String? _turnFailure;
  String? _topologyFailure;

  @override
  void initState() {
    super.initState();
    _listen(widget.turns);
    unawaited(_refreshTopology());
  }

  @override
  void didUpdateWidget(covariant BrainWorkspace oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.chatName != widget.chatName) {
      _turns.clear();
      _seen.clear();
      _projectedTurns = const [];
      unawaited(_refreshTopology());
    }
    if (!identical(oldWidget.turns, widget.turns)) {
      unawaited(_subscription?.cancel());
      _listen(widget.turns);
    }
    if (!identical(oldWidget.onLoadTopology, widget.onLoadTopology)) {
      unawaited(_refreshTopology());
    }
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
        unawaited(_refreshTopology());
      },
      onError: (Object error) {
        if (mounted) {
          setState(() => _turnFailure = '$error');
        }
      },
    );
  }

  Future<void> _refreshTopology() async {
    final load = widget.onLoadTopology;
    if (load == null) {
      return;
    }

    final epoch = ++_topologyLoadEpoch;
    try {
      final snapshot = await load();
      if (!mounted || epoch != _topologyLoadEpoch) {
        return;
      }
      setState(() {
        _topology = snapshot;
        _topologyFailure = null;
      });
    } on Object catch (error) {
      if (!mounted || epoch != _topologyLoadEpoch) {
        return;
      }
      setState(() {
        _topology = null;
        _topologyFailure = '$error';
      });
    }
  }

  String? get _statusMessage =>
      widget.statusMessage ?? _turnFailure ?? _topologyFailure;

  void _selectDestination(int index) {
    if (_destination != index) {
      setState(() => _destination = index);
    }
    if (index == brainDestinationIndex) {
      unawaited(_refreshTopology());
    }
  }

  @override
  void dispose() {
    unawaited(_subscription?.cancel());
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
