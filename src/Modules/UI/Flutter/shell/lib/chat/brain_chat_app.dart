import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';
import '../user_actions/user_action_card.dart';
import 'brain_workspace.dart';
import 'chat_contracts.dart';

export 'chat_contracts.dart';

final class BrainChatApp extends StatelessWidget {
  const BrainChatApp({
    super.key,
    required this.chatName,
    this.turns,
    this.authorizations,
    this.onLoadTopology,
    this.onSend,
    this.onStream,
    this.onOpenSignIn,
    this.onActivateButton,
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
  final ActivateChatButton? onActivateButton;
  final BehaviorClient? behaviorClient;
  final List<UserActionCardModel> userActions;
  final String? statusMessage;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'DigitalBrain',
      debugShowCheckedModeBanner: false,
      theme: BrainTheme.dark(),
      home: BrainWorkspace(
        chatName: chatName,
        turns: turns,
        authorizations: authorizations,
        onLoadTopology: onLoadTopology,
        onSend: onSend,
        onStream: onStream,
        onOpenSignIn: onOpenSignIn,
        onActivateButton: onActivateButton,
        behaviorClient: behaviorClient,
        userActions: userActions,
        statusMessage: statusMessage,
      ),
    );
  }
}
