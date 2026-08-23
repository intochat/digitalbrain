import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';

import '../activity_screen.dart';
import '../behaviors/behavior_workspace.dart';
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
    this.onSend,
    this.onStream,
    this.onStreamVoice,
    this.onAttachmentTap,
    this.onOpenSignIn,
    this.onActivateButton,
    this.onReadChart,
    this.onReadImageBytes,
    this.userActions = const [],
    this.statusMessage,
  });

  final String chatName;
  final Stream<ChatTurnEvent>? turns;
  final SendMessage? onSend;
  final StreamMessage? onStream;
  final StreamVoice? onStreamVoice;
  final VoidCallback? onAttachmentTap;
  final OpenUrl? onOpenSignIn;
  final ActivateChatButton? onActivateButton;
  final ReadChart? onReadChart;
  final ReadImageBytes? onReadImageBytes;
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
    _session = WorkspaceSession(chatName: widget.chatName, turns: widget.turns)
      ..addListener(_onSession);
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
  }

  void _selectDestination(int index) {
    if (_destination != index) {
      setState(() => _destination = index);
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
    // Product tabs stay in an IndexedStack so chat state survives
    // switches. Kit/Windowing mount only while selected (offline demos with
    // periodic clocks would otherwise block widget tests via IndexedStack).
    if (_destination <= behaviorsDestinationIndex) {
      return IndexedStack(
        index: _destination,
        children: [
          BrainChatScreen(
            chatName: widget.chatName,
            turns: _session.projectedTurns,
            onSend: widget.onSend,
            onStream: widget.onStream,
            onStreamVoice: widget.onStreamVoice,
            onAttachmentTap: widget.onAttachmentTap,
            onOpenSignIn: widget.onOpenSignIn,
            onActivateButton: widget.onActivateButton,
            onReadChart: widget.onReadChart,
            onReadImageBytes: widget.onReadImageBytes,
          ),
          ActivityScreen(
            turns: _session.projectedTurns,
            userActions: widget.userActions,
            onOpenUserAction: widget.onOpenSignIn,
          ),
          BehaviorWorkspace(
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
