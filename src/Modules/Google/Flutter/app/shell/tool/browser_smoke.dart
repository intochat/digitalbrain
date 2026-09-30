// Native harness for the opt-in Playwright CDP smoke test. Run on Windows with
// --dart-define=BROWSER_SMOKE_ATTACHMENT=<absolute path>, then pass the JSON's
// port/sessionId to DIGITALBRAIN_PLAYWRIGHT_CDP_PORT / _SESSION in dotnet test.
import 'dart:convert';
import 'dart:io';

import 'package:digitalbrain_ui/src/components/browser/embedded_browser_windows.dart';
import 'package:flutter/material.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  const output = String.fromEnvironment('BROWSER_SMOKE_ATTACHMENT');
  if (output.isEmpty)
    throw ArgumentError('BROWSER_SMOKE_ATTACHMENT is required.');
  runApp(
    MaterialApp(
      home: Scaffold(
        appBar: AppBar(
          title: const Text('Customer Researcher browser smoke test'),
        ),
        body: EmbeddedBrowser(
          onConnect: (port, sessionId) async {
            await File(output).parent.create(recursive: true);
            await File(
              output,
            ).writeAsString(jsonEncode({'port': port, 'sessionId': sessionId}));
          },
          onDisconnect: (_) async {},
        ),
      ),
    ),
  );
}
