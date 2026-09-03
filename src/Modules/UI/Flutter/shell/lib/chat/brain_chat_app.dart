import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';
import '../user_actions/user_action_card.dart';
import 'brain_workspace.dart';
import '../behaviors/behavior_workspace.dart';
import 'chat_contracts.dart';

export 'chat_contracts.dart';

final class BrainChatApp extends StatelessWidget {
  const BrainChatApp({
    super.key,
    required this.chatName,
    this.turns,
    this.onSend,
    this.onStream,
    this.onStreamVoice,
    this.onAttachmentTap,
    this.onOpenSignIn,
    this.kernelBaseUri,
    this.onCancelTurn,
    this.onActivateButton,
    this.onReadChart,
    this.onReadImageBytes,
    this.onReadSpreadsheet,
    this.onReadGraph,
    this.onLoadBehaviors,
    this.onLoadBehaviorSteps,
    this.onSaveBehavior,
    this.onTestBehavior,
    this.onActivateBehavior,
    this.onRunBehaviorFake,
    this.onGenerateBehavior,
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
  final Uri? kernelBaseUri;
  final CancelChatTurn? onCancelTurn;
  final ActivateChatButton? onActivateButton;
  final ReadChart? onReadChart;
  final ReadImageBytes? onReadImageBytes;
  final ReadSpreadsheet? onReadSpreadsheet;
  final ReadGraph? onReadGraph;
  final LoadBehaviors? onLoadBehaviors;
  final LoadBehaviorSteps? onLoadBehaviorSteps;
  final SaveBehavior? onSaveBehavior;
  final TestBehavior? onTestBehavior;
  final ActivateBehavior? onActivateBehavior;
  final RunBehaviorFake? onRunBehaviorFake;
  final GenerateBehavior? onGenerateBehavior;
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
        onSend: onSend,
        onStream: onStream,
        onStreamVoice: onStreamVoice,
        onAttachmentTap: onAttachmentTap,
        onOpenSignIn: onOpenSignIn,
        kernelBaseUri: kernelBaseUri,
        onCancelTurn: onCancelTurn,
        onActivateButton: onActivateButton,
        onReadChart: onReadChart,
        onReadImageBytes: onReadImageBytes,
        onReadSpreadsheet: onReadSpreadsheet,
        onReadGraph: onReadGraph,
        onLoadBehaviors: onLoadBehaviors,
        onLoadBehaviorSteps: onLoadBehaviorSteps,
        onSaveBehavior: onSaveBehavior,
        onTestBehavior: onTestBehavior,
        onActivateBehavior: onActivateBehavior,
        onRunBehaviorFake: onRunBehaviorFake,
        onGenerateBehavior: onGenerateBehavior,
        userActions: userActions,
        statusMessage: statusMessage,
      ),
    );
  }
}
