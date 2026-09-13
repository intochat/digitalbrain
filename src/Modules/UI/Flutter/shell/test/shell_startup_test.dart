import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/main.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;

import 'support/memory_workspace.dart';

class NoRequests extends http.BaseClient {
  int calls = 0;
  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    calls++;
    throw StateError('Unexpected startup request: ${request.url}');
  }
}

void main() {
  testWidgets('the shell starts on the project directory without any request', (
    tester,
  ) async {
    final transport = NoRequests();
    await tester.pumpWidget(
      buildShell(
        chat: 'main',
        workspaceStore: WorkspaceStore(
          persistence: MemoryWorkspacePersistence(),
        ),
        edge: DigitalBrainUiClient(
          baseUri: Uri.parse('http://localhost'),
          httpClient: transport,
        ),
      ),
    );
    await tester.pump();
    expect(find.text('Your projects'), findsWidgets);
    expect(find.byKey(const Key('workspace_graph')), findsNothing);
    expect(transport.calls, 0);
  });
}
