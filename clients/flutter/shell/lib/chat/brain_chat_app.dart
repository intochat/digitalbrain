import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';
import 'brain_workspace.dart';
import 'chat_contracts.dart';

export 'chat_contracts.dart';

final class BrainChatApp extends StatelessWidget {
  const BrainChatApp({
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
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'DigitalBrain',
      debugShowCheckedModeBanner: false,
      theme: BrainTheme.dark(),
      home: BrainWorkspace(
        chatName: chatName,
        turns: turns,
        onLoadTopology: onLoadTopology,
        onSend: onSend,
        onStream: onStream,
        statusMessage: statusMessage,
      ),
    );
  }
}
