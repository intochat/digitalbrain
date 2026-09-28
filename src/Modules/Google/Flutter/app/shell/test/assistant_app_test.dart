import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

import 'application_fixture.dart';

import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:flutter/material.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_islands.dart';
import 'package:digitalbrain_flutter_shell/workspace/app_launcher.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter_test/flutter_test.dart';

import 'workspace_remote_controller_test.dart' show MemoryPersistence;

void main() {
  testWidgets('main workspace mounts only the neuron-declared application', (
    tester,
  ) async {
    final store = WorkspaceStore(persistence: MemoryWorkspacePersistence());
    final requests = <String>[];
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://test'),
      httpClient: MockClient((request) async {
        requests.add(request.url.path);
        return http.Response(
          jsonEncode(
            applicationResponse(request) ??
                {'revision': 0, 'windows': [], 'apps': []},
          ),
          200,
        );
      }),
    );
    await tester.pumpWidget(
      WorkspaceApp(store: store, programmingClient: client),
    );
    await tester.pumpAndSettle();
    expect(find.byType(ApplicationSurface), findsOneWidget);
    expect(find.byType(TextField), findsOneWidget);
    expect(
      requests.any((path) => path.endsWith('/applications/assistant/start')),
      isTrue,
    );
    expect(requests.any((path) => path.contains('/agent')), isFalse);
    await tester.pumpWidget(const SizedBox());
    store.dispose();
  });

  testWidgets('each Assistant launch opens a fresh floating neuron surface', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1600, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final store = WorkspaceStore(persistence: MemoryWorkspacePersistence());
    var opened = 0;
    Map<String, dynamic>? legacy;
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://test'),
      httpClient: MockClient((request) async {
        if (request.url.path.endsWith('/start')) {
          legacy =
              (jsonDecode(request.body) as Map)['legacy']
                  as Map<String, dynamic>?;
        }
        if (request.url.path.endsWith('/open')) {
          final id = 'window-${++opened}';
          return http.Response(
            jsonEncode({
              'id': id,
              'title': 'Assistant $opened',
              'kind': 'surface',
              'surface': {'kind': 'surface', 'name': id},
            }),
            200,
          );
        }
        return http.Response(
          jsonEncode(
            applicationResponse(request) ?? {'revision': 0, 'windows': []},
          ),
          200,
        );
      }),
    );
    await tester.pumpWidget(
      WorkspaceApp(store: store, programmingClient: client),
    );
    await tester.pumpAndSettle();
    expect(legacy?['id'], store.currentProject.id);
    for (var i = 0; i < 2; i++) {
      tester
          .widget<WorkspaceIslands>(find.byType(WorkspaceIslands))
          .onLaunch('assistant');
      await tester.pumpAndSettle();
    }
    expect(opened, 2);
    expect(store.currentProject.artifacts.map((a) => a.id), [
      'window-1',
      'window-2',
    ]);
    expect(store.currentProject.presentation.windowModes, {
      'window-1': 'floating',
      'window-2': 'floating',
    });
    expect(find.byType(TextField), findsNWidgets(3));
    await tester.pumpWidget(const SizedBox());
    store.dispose();
  });

  test(
    'saved standalone assistant windows migrate to the main application',
    () async {
      final persistence = MemoryPersistence();
      final original = WorkspaceStore(persistence: persistence);
      original.addArtifact(
        WorkspaceArtifact(
          id: 'app-assistant',
          title: 'Assistant',
          kind: 'app',
          data: {'app': 'assistant'},
        ),
      );
      original.currentConversation.draft = 'Keep my draft';
      original.currentConversation.messages.add({
        'id': 'saved',
        'role': 'user',
        'text': 'Keep my history',
      });
      await original.save();
      original.dispose();

      final restored = WorkspaceStore(persistence: persistence);
      await restored.load();
      expect(restored.currentProject.artifacts, isEmpty);
      expect(restored.currentProject.presentation.chatCollapsed, isFalse);
      expect(restored.currentConversation.draft, 'Keep my draft');
      expect(
        restored.currentConversation.messages.single['text'],
        'Keep my history',
      );
      expect(() => restored.launchLocalApp('assistant'), throwsArgumentError);
      restored.dispose();
    },
  );

  test('the main assistant is offered in the launcher', () {
    expect(assistantLauncherEntry.launchKey, 'assistant');
    expect(assistantLauncherEntry.title, 'Assistant');
  });
}
