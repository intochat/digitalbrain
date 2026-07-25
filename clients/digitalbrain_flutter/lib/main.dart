import 'package:flutter/material.dart';

import 'src/edge_client.dart';
import 'src/host_environment.dart';
import 'src/shell_chrome.dart';
import 'src/shell_surface.dart';

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
