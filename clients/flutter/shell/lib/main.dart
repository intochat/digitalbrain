import 'dart:io';

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

  final behaviorClient = edge == null
      ? null
      : BehaviorClient(baseUri: edge.baseUri);

  runApp(
    BrainChatApp(
      chatName: chat,
      statusMessage: status,
      turns: edge?.watchChatTurns(chatName: chat),
      authorizations: edge?.watchAuthorizations(),
      onLoadTopology: edge?.readBrainTopology,
      onStream: edge == null
          ? null
          : (text) => edge.streamMessage(chatName: chat, text: text),
      onOpenSignIn: openSystemBrowser,
      behaviorClient: behaviorClient,
    ),
  );
}

Future<void> openSystemBrowser(Uri url) async {
  if (Platform.isWindows) {
    await Process.start('cmd', ['/c', 'start', '', url.toString()]);
    return;
  }

  if (Platform.isMacOS) {
    await Process.start('open', [url.toString()]);
    return;
  }

  await Process.start('xdg-open', [url.toString()]);
}
