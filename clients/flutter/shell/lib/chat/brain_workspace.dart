import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../activity_screen.dart';
import '../behaviors/behavior_workspace.dart';
import '../brain_screen.dart';
import '../user_actions/user_action_card.dart';
import 'brain_chat_screen.dart';
import 'chat_contracts.dart';
import 'workspace_chrome.dart';

final class BrainWorkspace extends StatefulWidget {
  const BrainWorkspace({
    super.key,
    required this.chatName,
    this.turns,
    this.authorizations,
    this.onLoadTopology,
    this.onSend,
    this.onStream,
    this.onOpenSignIn,
    this.behaviorClient,
    this.userActions = const [],
    this.statusMessage,
  });

  final String chatName;
  final Stream<ChatTurnEvent>? turns;
  final Stream<AuthorizationEvent>? authorizations;
  final LoadTopology? onLoadTopology;
  final SendMessage? onSend;
  final StreamMessage? onStream;
  final OpenUrl? onOpenSignIn;
  final BehaviorClient? behaviorClient;
  final List<UserActionCardModel> userActions;
  final String? statusMessage;

  @override
  State<BrainWorkspace> createState() => _BrainWorkspaceState();
}

final class _BrainWorkspaceState extends State<BrainWorkspace> {
  static const _compactBreakpoint = 720.0;

  final _turns = <ChatTurnEvent>[];
  final _seen = <int>{};
  final _authorizationEvents = <AuthorizationEvent>[];
  final _seenAuthorizations = <int>{};
  List<ChatTurnEvent> _projectedTurns = const [];
  List<SignInCardProjection> _signInCards = const [];
  StreamSubscription<ChatTurnEvent>? _subscription;
  StreamSubscription<AuthorizationEvent>? _authorizationSubscription;
  BrainTopologySnapshot? _topology;
  int _destination = 0;
  int _topologyLoadEpoch = 0;
  String? _turnFailure;
  String? _topologyFailure;

  @override
  void initState() {
    super.initState();
    _listen(widget.turns);
    _listenAuthorizations(widget.authorizations);
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
    if (!identical(oldWidget.authorizations, widget.authorizations)) {
      unawaited(_authorizationSubscription?.cancel());
      _listenAuthorizations(widget.authorizations);
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

  void _listenAuthorizations(Stream<AuthorizationEvent>? authorizations) {
    _authorizationSubscription = authorizations?.listen(
      (event) {
        if (!mounted || !_seenAuthorizations.add(event.sequence)) {
          return;
        }
        setState(() {
          _authorizationEvents.add(event);
          _signInCards = SignInCardProjection.project(_authorizationEvents);
        });
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
    unawaited(_authorizationSubscription?.cancel());
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final pages = [
      BrainChatScreen(
        chatName: widget.chatName,
        turns: _projectedTurns,
        signInCards: _signInCards,
        onSend: widget.onSend,
        onStream: widget.onStream,
        onOpenSignIn: widget.onOpenSignIn,
      ),
      ActivityScreen(
        turns: _projectedTurns,
        userActions: widget.userActions,
        onOpenUserAction: widget.onOpenSignIn,
      ),
      BrainScreen(
        chatName: widget.chatName,
        turns: _projectedTurns,
        topology: _topology,
        statusMessage: _statusMessage,
      ),
      BehaviorWorkspace(
        client: widget.behaviorClient,
        userActions: widget.userActions,
        onOpenUserAction: widget.onOpenSignIn,
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
