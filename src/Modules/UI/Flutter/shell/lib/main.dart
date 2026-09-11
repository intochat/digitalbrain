import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import 'auth/brain_session_gate.dart';
import 'chat/agent_chat_app.dart';
import 'open_url_io.dart'
    if (dart.library.html) 'open_url_web.dart'
    as open_url;

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  final chat = DigitalBrainHostEnv.resolveChat();

  // The gate owns client construction now: it must hold the credentials the
  // kernel accepted before any stream opens.
  runApp(
    BrainSessionGate(
      builder: (client, status) =>
          buildShell(chat: chat, edge: client, statusMessage: status),
    ),
  );
}

@visibleForTesting
Widget buildShell({
  required String chat,
  required DigitalBrainUiClient? edge,
  String? statusMessage,
}) {
  return AgentChatApp(
    onRun: edge?.runAgent,
    statusMessage: statusMessage,
    onOpenUrl: openExternalUrl,
  );
}

Future<void> openExternalUrl(Uri url) => open_url.openExternalUrl(url);
