import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';

import 'brain_workspace.dart';
import 'chat_contracts.dart';

export 'chat_contracts.dart';

final class BrainChatApp extends StatelessWidget {
  const BrainChatApp({
    super.key,
    required this.chatName,
    this.turns,
    this.onSend,
    this.onSendVoice,
    this.onAttachmentTap,
    this.onOpenSignIn,
    this.kernelBaseUri,
    this.onCancelTurn,
    this.onReadChart,
    this.onReadImageBytes,
    this.onReadSpreadsheet,
    this.onReadGraph,
    this.onReadTable,
    this.onUpdateTableView,
    this.onReadSurface,
    this.onReadBrain,
    this.onWatchBrain,
    this.onWatchActivities,
    this.surfaceEvents,
    this.client,
    this.onSetBrainSubscription,
    this.graphSceneFactory,
    this.statusMessage,
  });

  final String chatName;
  final Stream<ChatStreamEvent>? turns;
  final SendMessage? onSend;
  final SendVoice? onSendVoice;
  final VoidCallback? onAttachmentTap;
  final OpenUrl? onOpenSignIn;
  final Uri? kernelBaseUri;
  final CancelChatTurn? onCancelTurn;
  final ReadChart? onReadChart;
  final ReadImageBytes? onReadImageBytes;
  final ReadSpreadsheet? onReadSpreadsheet;
  final ReadGraph? onReadGraph;
  final ReadTable? onReadTable;
  final UpdateTableView? onUpdateTableView;
  final ReadSurface? onReadSurface;
  final ReadBrain? onReadBrain;
  final WatchBrain? onWatchBrain;
  final WatchExecutionActivities? onWatchActivities;
  final Stream<SurfaceStreamEvent>? surfaceEvents;
  final DigitalBrainUiClient? client;
  final SetBrainSubscription? onSetBrainSubscription;
  final GraphSceneFactory? graphSceneFactory;
  final String? statusMessage;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'DigitalBrain',
      debugShowCheckedModeBanner: false,
      theme: UiTheme.light(),
      builder: (context, child) => UiThemeScope(child: child!),
      home: BrainWorkspace(
        chatName: chatName,
        turns: turns,
        onSend: onSend,
        onSendVoice: onSendVoice,
        onAttachmentTap: onAttachmentTap,
        onOpenSignIn: onOpenSignIn,
        kernelBaseUri: kernelBaseUri,
        onCancelTurn: onCancelTurn,
        onReadChart: onReadChart,
        onReadImageBytes: onReadImageBytes,
        onReadSpreadsheet: onReadSpreadsheet,
        onReadGraph: onReadGraph,
        onReadTable: onReadTable,
        onUpdateTableView: onUpdateTableView,
        onReadSurface: onReadSurface,
        onReadBrain: onReadBrain,
        onWatchBrain: onWatchBrain,
        onWatchActivities: onWatchActivities,
        surfaceEvents: surfaceEvents,
        client: client,
        onSetBrainSubscription: onSetBrainSubscription,
        graphSceneFactory: graphSceneFactory,
        statusMessage: statusMessage,
      ),
    );
  }
}
