import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:digitalbrain_flutter_shell/workspace/neuron_workspace_persistence.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

import 'workspace_remote_controller_test.dart' show MemoryPersistence;
import 'first_run_test.dart' show FirstRunClient;

void main() {
  testWidgets(
    'empty server starts one default workspace without device preferences',
    (tester) async {
      final api = _StartupClient();
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://kernel'),
        httpClient: api,
      )..defaultBrainId = 'default';
      // No preferences plugin is registered: production startup must never read it.
      await tester.pumpWidget(WorkspaceApp(programmingClient: client));
      await tester.pumpAndSettle();
      expect(find.text('Retry loading'), findsNothing);
      expect(api.snapshot?['projects'], hasLength(1));
      expect(api.snapshot!['projects'][0]['id'], 'default');
      expect(api.snapshot!['projects'][0]['title'], 'Personal');
      expect(api.imports, 0);
      await tester.pumpWidget(const SizedBox());
      await api.events.close();
      client.close();
    },
  );
  test(
    'explicit server recovery bypasses malformed legacy without changing it',
    () async {
      final server = _ShellServer();
      final original = WorkspaceStore(
        persistence: NeuronWorkspacePersistence(client: server.client),
      );
      await original.load();
      original.currentConversation.draft = 'Server draft';
      await original.save();
      final legacy = _Legacy('{malformed');
      final recovering = WorkspaceStore(
        persistence: NeuronWorkspacePersistence(
          client: server.client,
          legacy: legacy,
        ),
      );
      await recovering.load();
      expect(recovering.loadFailed, isTrue);
      await recovering.reload(useServerVersion: true);
      expect(recovering.loadFailed, isFalse);
      expect(recovering.currentConversation.draft, 'Server draft');
      expect(legacy.value, '{malformed');
      expect(legacy.writes, 0);
      recovering.dispose();
      original.dispose();
    },
  );
  test(
    'retry cannot rebase a stale snapshot after another client writes',
    () async {
      final server = _ShellServer();
      final store = WorkspaceStore(
        persistence: NeuronWorkspacePersistence(client: server.client),
      );
      await store.load();
      server.failAfterCommit = true;
      store.currentConversation.draft = 'Unacknowledged';
      await store.save();
      final other = WorkspaceStore(
        persistence: NeuronWorkspacePersistence(client: server.client),
      );
      await other.load();
      other.currentConversation.draft = 'Newer server draft';
      await other.save();
      store.currentConversation.draft = 'Still unsaved here';
      await store.save();
      expect(store.persistenceError, contains('another session'));
      expect(
        server.snapshot!['projects'][0]['conversations'][0]['draft'],
        'Newer server draft',
      );
      expect(store.currentConversation.draft, 'Still unsaved here');
      store.dispose();
      other.dispose();
    },
  );
  testWidgets('failed initial load offers retry without creating a workspace', (
    tester,
  ) async {
    final store = WorkspaceStore(
      seedProject: false,
      persistence: _FailedRead(),
    );
    await tester.pumpWidget(WorkspaceApp(store: store));
    await tester.pumpAndSettle();
    expect(find.text('Retry loading'), findsOneWidget);
    expect(store.projects, isEmpty);
    await tester.pumpWidget(const SizedBox());
    store.dispose();
  });

  test('fresh clients recover the complete server workspace', () async {
    final server = _ShellServer();
    final first = WorkspaceStore(
      persistence: NeuronWorkspacePersistence(client: server.client),
    );
    await first.load();
    first.currentProject.title = 'Research';
    first.currentConversation.draft = 'Preserve this draft';
    first.settings.displayName = 'Alice';
    await first.save();
    final fresh = WorkspaceStore(
      seedProject: false,
      persistence: NeuronWorkspacePersistence(client: server.client),
    );
    await fresh.load();
    expect(fresh.currentProject.title, 'Research');
    expect(fresh.currentConversation.draft, 'Preserve this draft');
    expect(fresh.settings.displayName, 'Alice');
    first.dispose();
    fresh.dispose();
  });

  test(
    'conflicting clients retain drafts without overwriting newer state',
    () async {
      final server = _ShellServer();
      final first = WorkspaceStore(
        persistence: NeuronWorkspacePersistence(client: server.client),
      );
      await first.load();
      await first.save();
      final second = WorkspaceStore(
        persistence: NeuronWorkspacePersistence(client: server.client),
      );
      await second.load();
      first.currentConversation.draft = 'Server winner';
      await first.save();
      second.currentConversation.draft = 'Unsaved competing draft';
      await second.save();
      await second.save();
      expect(second.currentConversation.draft, 'Unsaved competing draft');
      expect(second.persistenceError, contains('another session'));
      expect(
        server.snapshot!['projects'][0]['conversations'][0]['draft'],
        'Server winner',
      );
      first.dispose();
      second.dispose();
    },
  );

  test('legacy import is verified and its source is never written', () async {
    final server = _ShellServer();
    final legacyStore = WorkspaceStore(persistence: MemoryPersistence());
    legacyStore.currentConversation.draft = 'Old draft';
    final raw = jsonEncode(legacyStore.toJson());
    final legacy = _Legacy(raw);
    final persistence = NeuronWorkspacePersistence(
      client: server.client,
      legacy: legacy,
      legacyKey: 'account-scope',
    );
    final imported = jsonDecode((await persistence.read())!);
    expect(imported['projects'][0]['conversations'][0]['draft'], 'Old draft');
    expect(legacy.writes, 0);
    final revision = server.revision;
    await NeuronWorkspacePersistence(
      client: server.client,
      legacy: legacy,
      legacyKey: 'account-scope',
    ).read();
    expect(server.revision, revision);
    expect(legacy.value, raw);
    legacyStore.dispose();
  });

  test('ambiguous successful write retries its original operation before latest state', () async {
    final server = _ShellServer();
    final store = WorkspaceStore(
      persistence: NeuronWorkspacePersistence(client: server.client),
    );
    await store.load();
    server.failAfterCommit = true;
    store.currentConversation.draft = 'First';
    await store.save();
    expect(store.persistenceError, isNotNull);
    store.currentConversation.draft = 'Latest';
    await store.save();
    expect(store.persistenceError, isNull);
    expect(
      server.snapshot!['projects'][0]['conversations'][0]['draft'],
      'Latest',
    );
    expect(server.revision, 2);
    store.dispose();
  });

  test('a failed load never overwrites the unread durable state', () async {
    final persistence = _FailedRead();
    final store = WorkspaceStore(persistence: persistence);
    await store.load();
    store.currentConversation.draft = 'Unsaved draft';
    await store.save();
    expect(persistence.writes, 0);
    expect(store.persistenceError, isNotNull);
    expect(store.currentConversation.draft, 'Unsaved draft');
    store.dispose();
  });

  test(
    'HTTP errors expose revision conflicts to persistence callers',
    () async {
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://kernel'),
        httpClient: MockClient((_) async => http.Response('{}', 409)),
      );
      await expectLater(
        client.jsonRequest('PUT', '/shell/state', {}),
        throwsA(
          predicate((Object e) {
            // Keep the test compiling before the typed exception exists.
            try {
              return (e as dynamic).statusCode == 409;
            } catch (_) {
              return false;
            }
          }),
        ),
      );
    },
  );

  test(
    'malformed saved JSON remains recoverable after a save attempt',
    () async {
      final persistence = MemoryPersistence()..value = '{broken';
      final store = WorkspaceStore(persistence: persistence);
      await store.load();
      await store.save();
      expect(persistence.value, '{broken');
      expect(store.persistenceError, isNotNull);
      store.dispose();
    },
  );
}

class _Legacy extends MemoryPersistence {
  _Legacy(String raw) {
    value = raw;
  }
  int writes = 0;
  @override
  Future<void> write(String value) async {
    writes++;
    throw StateError('Read only');
  }
}

class _StartupClient extends FirstRunClient {
  Map<String, dynamic>? snapshot;
  int revision = 0, imports = 0;
  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    if (!request.url.path.startsWith('/shell/')) return super.send(request);
    if (request.url.path == '/shell/import') imports++;
    if (request.method != 'GET') {
      final command =
          jsonDecode(await request.finalize().bytesToString()) as Map;
      snapshot = Map<String, dynamic>.from(command['snapshot']);
      revision++;
    }
    return http.StreamedResponse(
      Stream.value(
        utf8.encode(jsonEncode({'revision': revision, 'snapshot': snapshot})),
      ),
      200,
    );
  }
}

class _ShellServer {
  int revision = 0;
  Map<String, dynamic>? snapshot;
  final operations = <String>{};
  bool failAfterCommit = false;
  late final client = DigitalBrainUiClient(
    baseUri: Uri.parse('http://kernel'),
    httpClient: MockClient((request) async {
      if (request.method != 'GET') {
        final command = jsonDecode(request.body) as Map;
        final id = command['operationId'] as String;
        if (!operations.contains(id)) {
          if (command['expectedRevision'] != revision) {
            return http.Response('{}', 409);
          }
          snapshot = Map<String, dynamic>.from(command['snapshot']);
          operations.add(id);
          revision++;
          if (failAfterCommit) {
            failAfterCommit = false;
            throw http.ClientException('Connection lost after commit');
          }
        }
      }
      return http.Response(
        jsonEncode({'revision': revision, 'snapshot': snapshot}),
        200,
      );
    }),
  );
}

class _FailedRead implements WorkspacePersistence {
  int writes = 0;
  @override
  Future<String?> read() async => throw StateError('Unavailable');
  @override
  Future<void> write(String value) async {
    writes++;
  }
}
