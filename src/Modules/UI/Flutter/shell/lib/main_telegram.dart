import 'package:flutter/material.dart';

import 'auth/telegram_session_gate.dart';
import 'main.dart' show buildShell;
import 'telegram_bridge.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(
    TelegramSessionGate(
      initData: initializeTelegram(),
      baseUri: Uri.base.replace(path: '/', query: '', fragment: ''),
      builder: (client, status) => buildShell(
        chat: 'main',
        edge: client,
        statusMessage: status,
        allowLinkTelegram: false,
      ),
    ),
  );
}
