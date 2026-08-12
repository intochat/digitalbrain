import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';

import '../activity_screen.dart';
import '../behaviors/behavior_workspace.dart';
import '../brain_screen.dart';
import '../user_actions/user_action_card.dart';
import '../windowing/windowing_screen.dart';
import 'brain_chat_screen.dart';
import 'chat_contracts.dart';
import 'workspace_chrome.dart';
import 'workspace_session.dart';

final class BrainWorkspace extends StatefulWidget {
  const BrainWorkspace({
    super.key,
    required this.chatName,
    this.turns,
    this.authorizations,
    this.graphChanges,
    this.onLoadTopology,
    this.onSend,
    this.onStream,
    this.onStreamVoice,
    this.onOpenSignIn,
    this.onActivateButton,
    this.behaviorClient,
    this.userActions = const [],
    this.statusMessage,
  });

  final String chatName;
  final Stream<ChatTurnEvent>? turns;
  final Stream<AuthorizationEvent>? authorizations;
  final Stream<GraphChangeEvent>? graphChanges;
  final LoadTopology? onLoadTopology;
  final SendMessage? onSend;
  final StreamMessage? onStream;
  final StreamVoice? onStreamVoice;
  final OpenUrl? onOpenSignIn;
  final ActivateChatButton? onActivateButton;
  final BehaviorClient? behaviorClient;
  final List<UserActionCardModel> userActions;
  final String? statusMessage;

  @override
  State<BrainWorkspace> createState() => _BrainWorkspaceState();
}

final class _BrainWorkspaceState extends State<BrainWorkspace> {
  static const _compactBreakpoint = 720.0;

  late final WorkspaceSession _session;
  int _destination = 0;

  @override
  void initState() {
    super.initState();
    _session = WorkspaceSession(
      chatName: widget.chatName,
      turns: widget.turns,
      authorizations: widget.authorizations,
      graphChanges: widget.graphChanges,
      onLoadTopology: widget.onLoadTopology,
    )..addListener(_onSession);
  }

  void _onSession() {
    if (mounted) {
      setState(() {});
    }
  }

  @override
  void didUpdateWidget(covariant BrainWorkspace oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.chatName != widget.chatName) {
      _session.updateChatName(widget.chatName);
    }
    if (!identical(oldWidget.turns, widget.turns)) {
      _session.listenTurns(widget.turns);
    }
    if (!identical(oldWidget.authorizations, widget.authorizations)) {
      _session.listenAuthorizations(widget.authorizations);
    }
    if (!identical(oldWidget.graphChanges, widget.graphChanges)) {
      _session.listenGraphChanges(widget.graphChanges);
    }
    if (!identical(oldWidget.onLoadTopology, widget.onLoadTopology)) {
      _session.onLoadTopology = widget.onLoadTopology;
      unawaited(_session.refreshTopology());
    }
  }

  void _selectDestination(int index) {
    if (_destination != index) {
      setState(() => _destination = index);
    }
    if (index == brainDestinationIndex) {
      unawaited(_session.refreshTopology());
    }
  }

  @override
  void dispose() {
    _session
      ..removeListener(_onSession)
      ..dispose();
    super.dispose();
  }

  Widget _destinationPage() {
    // Product tabs stay in an IndexedStack so chat/topology state survives
    // switches. Kit/Windowing mount only while selected (offline demos with
    // periodic clocks would otherwise block widget tests via IndexedStack).
    if (_destination <= behaviorsDestinationIndex) {
      return IndexedStack(
        index: _destination,
        children: [
          BrainChatScreen(
            chatName: widget.chatName,
            turns: _session.projectedTurns,
            signInCards: _session.signInCards,
            onSend: widget.onSend,
            onStream: widget.onStream,
            onStreamVoice: widget.onStreamVoice,
            onOpenSignIn: widget.onOpenSignIn,
            onActivateButton: widget.onActivateButton,
          ),
          ActivityScreen(
            turns: _session.projectedTurns,
            userActions: widget.userActions,
            onOpenUserAction: widget.onOpenSignIn,
          ),
          BrainScreen(
            chatName: widget.chatName,
            turns: _session.projectedTurns,
            topology: _session.topology,
            graphChange: _session.graphChange,
            statusMessage: _session.statusMessage(widget.statusMessage),
          ),
          BehaviorWorkspace(
            client: widget.behaviorClient,
            userActions: widget.userActions,
            onOpenUserAction: widget.onOpenSignIn,
          ),
        ],
      );
    }
    if (_destination == kitDestinationIndex) {
      return KitGalleryScreen(
        onButtonPressed: widget.onActivateButton == null
            ? null
            : (part) {
                final offer = part.offerCommandId;
                if (offer == null) {
                  return;
                }
                unawaited(
                  widget.onActivateButton!(
                    offerCommandId: offer,
                    buttonId: part.buttonId,
                    action: part.action,
                  ),
                );
              },
      );
    }
    return const WindowingScreen();
  }

  @override
  Widget build(BuildContext context) {
    final status = _session.statusMessage(widget.statusMessage);
    return LayoutBuilder(
      builder: (context, constraints) {
        final compact = constraints.maxWidth < _compactBreakpoint;
        final content = Column(
          children: [
            WorkspaceStatusBar(
              chatName: widget.chatName,
              section: workspaceSectionName(_destination),
              message: status,
            ),
            Expanded(child: _destinationPage()),
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
