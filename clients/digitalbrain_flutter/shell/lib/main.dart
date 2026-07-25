import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import 'shell_chrome.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();

  final shell = DigitalBrainHostEnv.resolveShell();
  final surface = ShellSurfaceController();

  DigitalBrainUiEdgeClient? client;
  String? status;
  try {
    client = DigitalBrainUiEdgeClient.fromEnvironment();
  } on Object catch (error) {
    status = error.toString();
  }

  runApp(
    ShellSurfaceApp(
      controller: surface,
      shellName: shell,
      statusMessage: status,
      events: client?.watchShellEvents(shellName: shell),
    ),
  );
}
