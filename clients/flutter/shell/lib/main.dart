import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import 'chat_screen.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();

  final chat = DigitalBrainHostEnv.resolveChat();

  DigitalBrainUiClient? client;
  String? status;
  try {
    client = DigitalBrainUiClient.fromEnvironment();
  } on Object catch (error) {
    status = error.toString();
  }

  final edge = client;

  runApp(
    BrainChatApp(
      chatName: chat,
      statusMessage: status,
      turns: edge?.watchChatTurns(chatName: chat),
      onLoadTopology: edge?.readBrainTopology,
      onStream: edge == null
          ? null
          : (text) => edge.streamMessage(chatName: chat, text: text),
    ),
  );
}
