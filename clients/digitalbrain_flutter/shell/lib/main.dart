import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import 'chat_screen.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();

  final chat = DigitalBrainHostEnv.resolveChat();

  DigitalBrainUiEdgeClient? client;
  String? status;
  try {
    client = DigitalBrainUiEdgeClient.fromEnvironment();
  } on Object catch (error) {
    status = error.toString();
  }

  final edge = client;

  runApp(
    BrainChatApp(
      chatName: chat,
      statusMessage: status,
      turns: edge?.watchChatTurns(chatName: chat),
      topology: edge?.watchBrainTopology(),
      onStream: edge == null
          ? null
          : (text) => edge.streamMessage(chatName: chat, text: text),
    ),
  );
}
