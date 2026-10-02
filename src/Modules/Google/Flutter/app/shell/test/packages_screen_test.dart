import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/packages_screen.dart';

void main() {
  testWidgets('a refused uninstall can explicitly leave storage behind', (
    tester,
  ) async {
    final server = _FakePackagesServer()
      ..installedRevision = 'published-revision';
    var pending = false;
    var abandoned = false;
    await tester.pumpWidget(
      MaterialApp(
        home: PackagesScreen(
          workspaceId: 'workspace-bob',
          onClose: () {},
          request: (method, path, [body]) async {
            if (method == 'DELETE') {
              pending = true;
              throw UiRequestException(
                method,
                path,
                409,
                '{"detail":"Restore Postgres to remove storage."}',
              );
            }
            if (path.endsWith('/abandon-storage')) {
              expect(method, 'POST');
              expect((body as Map)['operationId'], isNotEmpty);
              abandoned = true;
              pending = false;
              server.installedRevision = null;
              return <String, dynamic>{};
            }
            final result = await server.request(method, path, body);
            if (result is Map && result['app'] is Map) {
              (result['app'] as Map)['uninstallPending'] = pending;
            }
            return result;
          },
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('uninstall-alice/researcher')));
    await tester.pumpAndSettle();
    expect(find.text('Restore Postgres to remove storage.'), findsOneWidget);
    expect(find.text('Uninstall pending'), findsOneWidget);
    await tester.tap(
      find.byKey(const ValueKey('abandon-storage-alice/researcher')),
    );
    await tester.pumpAndSettle();
    expect(
      find.text(
        'Existing storage will remain and must be cleaned up separately.',
      ),
      findsOneWidget,
    );
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
    expect(abandoned, isFalse);
    await tester.tap(
      find.byKey(const ValueKey('abandon-storage-alice/researcher')),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('confirm-abandon-storage')));
    await tester.pumpAndSettle();
    expect(abandoned, isTrue);
    expect(
      find.byKey(const ValueKey('install-alice/researcher')),
      findsOneWidget,
    );
  });

  testWidgets('install refusals are displayed verbatim', (tester) async {
    final server = _FakePackagesServer();
    const refusal = 'Cannot install this app. Missing modules: Postgres.';
    await tester.pumpWidget(
      MaterialApp(
        home: PackagesScreen(
          workspaceId: 'workspace-bob',
          request: (method, path, [body]) async {
            if (method == 'POST') {
              throw UiRequestException(
                method,
                path,
                409,
                '{"detail":"$refusal"}',
              );
            }
            return server.request(method, path, body);
          },
          onClose: () {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('install-alice/researcher')));
    await tester.pumpAndSettle();
    expect(find.text(refusal), findsOneWidget);
    expect(server.installedRevision, isNull);
  });

  testWidgets('installs a shared C# app in one tap, runs it and forks it', (
    tester,
  ) async {
    final server = _FakePackagesServer();
    await tester.pumpWidget(
      MaterialApp(
        home: PackagesScreen(
          workspaceId: 'workspace-bob',
          request: server.request,
          onClose: () {},
          pollInterval: const Duration(milliseconds: 1),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Internet researcher'), findsOneWidget);
    expect(find.text('Forked from alice/origin'), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('install-alice/researcher')));
    await tester.pumpAndSettle();
    expect(
      server.calls,
      contains('POST /brains/workspace-bob/packages/alice/researcher'),
    );
    expect(
      find.byKey(const ValueKey('uninstall-alice/researcher')),
      findsOneWidget,
    );
    expect(find.text('C# app: Running'), findsOneWidget);

    await tester.enterText(
      find.byKey(const ValueKey('input-alice/researcher')),
      'What is Orleans?',
    );
    await tester.tap(
      find.byKey(const ValueKey('run-alice/researcher-research')),
    );
    await tester.pumpAndSettle();
    expect(find.text('Research (plain): What is Orleans?'), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('fork-alice/researcher')));
    await tester.pumpAndSettle();
    expect(server.calls, contains('POST /packages/alice/researcher/fork'));
    expect(
      find.text('Forked alice/researcher into your packages.'),
      findsOneWidget,
    );
  });

  testWidgets('keeps the marketplace visible when one install cannot be read', (
    tester,
  ) async {
    Future<dynamic> request(String method, String path, [Object? body]) async {
      if (path == '/packages') {
        return [
          for (final name in ['researcher', 'broken'])
            {
              'package': {'owner': 'alice', 'name': name},
              'title': 'Package $name',
              'revision': 'published-revision',
            },
        ];
      }
      if (path.endsWith('/broken')) throw Exception('unavailable');
      return {
        'app': {'status': 0},
      };
    }

    await tester.pumpWidget(
      MaterialApp(
        home: PackagesScreen(
          workspaceId: 'workspace-bob',
          request: request,
          onClose: () {},
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Package researcher'), findsOneWidget);
    expect(find.text('Package broken'), findsOneWidget);
    expect(find.text('Could not read 1 installed package(s).'), findsOneWidget);
  });

  testWidgets('offers an update when the publisher has moved on', (
    tester,
  ) async {
    final server = _FakePackagesServer()..installedRevision = 'older-revision';
    await tester.pumpWidget(
      MaterialApp(
        home: PackagesScreen(
          workspaceId: 'workspace-bob',
          request: server.request,
          onClose: () {},
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const ValueKey('upgrade-alice/researcher')));
    await tester.pumpAndSettle();

    expect(
      server.calls,
      contains('POST /brains/workspace-bob/packages/alice/researcher/upgrade'),
    );
    expect(
      find.byKey(const ValueKey('upgrade-alice/researcher')),
      findsNothing,
    );
  });
  testWidgets('reopens a draft where the person left off', (tester) async {
    final draftId = 'a' * 32;
    final calls = <String>[];
    Future<dynamic> request(String method, String path, [Object? body]) async {
      calls.add('$method $path');
      if (path == '/packages') return <Object>[];
      if (path == '/packages/drafts') {
        return [
          {
            'id': draftId,
            'title': 'Shouter',
            'status': 1,
            'updatedAt': '2026-09-30T10:00:00Z',
          },
        ];
      }
      if (path == '/packages/drafts/$draftId') {
        return {
          'draft': {
            'request': 'Shout back whatever I say.',
            'title': 'Shouter',
            'runtime': 'prompt',
            'spec': '## Scenario: It shouts',
            'status': 1,
            'attempts': <Object>[],
          },
        };
      }
      throw StateError('Unexpected $method $path');
    }

    await tester.pumpWidget(
      MaterialApp(
        home: PackagesScreen(
          workspaceId: 'workspace-bob',
          request: request,
          onClose: () {},
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Shouter'), findsOneWidget);
    expect(find.text('Scenarios written'), findsOneWidget);

    await tester.tap(find.byKey(ValueKey('draft-$draftId')));
    await tester.pumpAndSettle();

    expect(calls, contains('GET /packages/drafts/$draftId'));
    expect(find.text('Scenario: It shouts'), findsOneWidget);
    expect(find.text('Shout back whatever I say.'), findsOneWidget);
  });
}

class _FakePackagesServer {
  final calls = <String>[];
  String? installedRevision;
  var _reads = 0;
  String _input = '';

  Future<dynamic> request(String method, String path, [Object? body]) async {
    calls.add('$method $path');
    const app = '/brains/workspace-bob/packages/alice/researcher';
    if (method == 'GET' && path == '/packages/drafts') return <Object>[];
    if (method == 'GET' && path == '/packages') {
      return [
        {
          'package': {'owner': 'alice', 'name': 'researcher'},
          'title': 'Internet researcher',
          'description': 'Answers a question with a short research brief.',
          'revision': 'published-revision',
          'forkedFrom': {
            'package': {'owner': 'alice', 'name': 'origin'},
            'revision': 'origin-revision',
          },
        },
      ];
    }
    if (path == app && method == 'POST') {
      installedRevision = 'published-revision';
    }
    if (path == '$app/upgrade') installedRevision = 'published-revision';
    if (path == app && method == 'DELETE') installedRevision = null;
    if (path == app || path == '$app/upgrade') {
      return {
        'app': {
          'status': installedRevision == null ? 0 : 1,
          'revision': installedRevision == null
              ? null
              : {
                  'package': {'owner': 'alice', 'name': 'researcher'},
                  'revision': installedRevision,
                },
          'operations': [
            {'name': 'research', 'description': 'Research a question.'},
          ],
          'csharpFiles': installedRevision == null
              ? <String>[]
              : ['researcher'],
        },
        'files': installedRevision == null
            ? <Object>[]
            : [
                {
                  'id': 'researcher',
                  'source': 'Console.WriteLine("research");',
                  'settings': <String, String>{},
                  'status': 1,
                  'exitCode': null,
                  'startedAt': '2026-09-27T10:00:00Z',
                },
              ],
      };
    }
    if (path == '$app/invocations') {
      _input = (body as Map)['input'] as String;
      return {'id': 'invocation-1', 'status': 0};
    }
    if (path == '$app/invocations/invocation-1') {
      return ++_reads < 2
          ? {'id': 'invocation-1', 'status': 0}
          : {
              'id': 'invocation-1',
              'status': 1,
              'output': 'Research (plain): $_input',
            };
    }
    if (path == '/packages/alice/researcher/fork') {
      return {
        'id': {'owner': 'bob', 'name': 'researcher'},
      };
    }
    throw StateError('Unexpected $method $path');
  }
}
