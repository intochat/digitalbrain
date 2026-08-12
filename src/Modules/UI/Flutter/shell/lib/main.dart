import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import 'chat_screen.dart';
import 'open_url_io.dart' if (dart.library.html) 'open_url_web.dart' as open_url;

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  final chat = DigitalBrainHostEnv.resolveChat();

  DigitalBrainUiClient? client;
  BehaviorClient? behavior;
  String? status;
  try {
    client = DigitalBrainUiClient.fromEnvironment();
    final me = await client.ensureSession();
    // Same cookie jar as the UI session — behavior host is not anonymous.
    behavior = BehaviorClient.sharingSession(client);
    debugPrint(
      'DigitalBrain session: ${me.username} principal=${me.principalId}',
    );
  } on Object catch (error) {
    status = error.toString();
    debugPrint('DigitalBrain session failed: $error');
  }

  final edge = client;

  runApp(
    BrainChatApp(
      chatName: chat,
      statusMessage: status,
      turns: edge?.watchChatTurns(chatName: chat),
      authorizations: edge?.watchAuthorizations(),
      graphChanges: edge?.watchGraphChanges(),
      onLoadTopology: edge?.readBrainTopology,
      onStream: edge == null
          ? null
          : (text) => edge.streamMessage(chatName: chat, text: text),
      onActivateButton: edge == null
          ? null
          : ({
              required offerCommandId,
              required buttonId,
              required action,
            }) => edge.activateChatButton(
              chatName: chat,
              offerCommandId: offerCommandId,
              buttonId: buttonId,
              action: action,
            ),
      onOpenSignIn: openExternalUrl,
      behaviorClient: behavior,
    ),
  );
}

Future<void> openExternalUrl(Uri url) => open_url.openExternalUrl(url);
