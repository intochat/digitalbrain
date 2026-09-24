import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/packages_screen.dart';

void main() {
  testWidgets('installs a shared behavior in one tap, runs it and forks it', (
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
      contains('POST /workspaces/workspace-bob/packages/alice/researcher'),
    );
    expect(
      find.byKey(const ValueKey('uninstall-alice/researcher')),
      findsOneWidget,
    );

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
      contains(
        'POST /workspaces/workspace-bob/packages/alice/researcher/upgrade',
      ),
    );
    expect(
      find.byKey(const ValueKey('upgrade-alice/researcher')),
      findsNothing,
    );
  });
}

class _FakePackagesServer {
  final calls = <String>[];
  String? installedRevision;
  var _reads = 0;
  String _input = '';

  Future<dynamic> request(String method, String path, [Object? body]) async {
    calls.add('$method $path');
    const app = '/workspaces/workspace-bob/packages/alice/researcher';
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
        },
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
