import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import 'chat_screen.dart';
import 'open_url_io.dart' if (dart.library.html) 'open_url_web.dart' as open_url;

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
      onOpenSignIn: openExternalUrl,
      behaviorClient: behaviorClient,
    ),
  );
}

Future<void> openExternalUrl(Uri url) => open_url.openExternalUrl(url);
