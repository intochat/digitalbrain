import 'dart:async';

import 'package:digitalbrain_flutter_shell/workspace/csharp/csharp_detail.dart';
import 'package:digitalbrain_flutter_shell/workspace/csharp/csharp_manager.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Map<String, dynamic> fileView({
  String id = 'timer',
  String name = 'Timer status',
  Object status = 1,
  String source = 'Console.WriteLine("tick");',
  String? logs,
}) => {
  'description': {
    'id': id,
    'name': name,
    'purpose': 'Show ticks',
    'createdAt': '2026-09-27T10:00:00Z',
  },
  'file': {
    'id': id,
    'source': source,
    'settings': <String, String>{},
    'status': status,
    'exitCode': null,
    'startedAt': '2026-09-27T10:01:00Z',
  },
  'logs': ?logs,
};

Widget host(Widget child) => MaterialApp(home: Scaffold(body: child));

CSharpDetailView detailView(
  Map<String, dynamic> view, {
  bool allowActivation = true,
  Future<void> Function(Map<String, Object?> body)? onSave,
  Future<void> Function()? onStart,
  Future<void> Function()? onStop,
}) => CSharpDetailView(
  view: view,
  allowActivation: allowActivation,
  enabled: true,
  stale: false,
  onSave: onSave ?? (_) async {},
  onStart: onStart ?? () async {},
  onStop: onStop ?? () async {},
  onDelete: () async {},
  onRefresh: () async {},
  onAsk: (_) {},
);

void main() {
  test('status parses both the numeric and the string enum form', () {
    expect(csharpStatus({'status': 0}), 'Stopped');
    expect(csharpStatus({'status': 1}), 'Running');
    expect(csharpStatus({'status': 2}), 'Restarting');
    expect(csharpStatus({'status': 3}), 'Exited');
    expect(csharpStatus({'status': 'Exited'}), 'Exited');
    expect(csharpStatus({'status': 9}), 'Unknown');
    expect(
      csharpStatusLabel({'status': 3, 'exitCode': 1}),
      'Exited with code 1',
    );
  });

  testWidgets('saving sends the edited source, name and purpose', (
    tester,
  ) async {
    Map<String, Object?>? saved;
    await tester.pumpWidget(
      host(detailView(fileView(), onSave: (body) async => saved = body)),
    );
    final save = find.widgetWithText(FilledButton, 'Save');
    expect(tester.widget<FilledButton>(save).onPressed, isNull);

    await tester.enterText(
      find.byKey(const ValueKey('csharp-source')),
      'Console.WriteLine("tock");',
    );
    await tester.pump();
    expect(find.textContaining('Unsaved changes'), findsOneWidget);
    await tester.tap(save);
    await tester.pump();

    expect(saved, {
      'source': 'Console.WriteLine("tock");',
      'name': 'Timer status',
      'purpose': 'Show ticks',
    });
  });

  testWidgets('server updates replace untouched fields but keep edits', (
    tester,
  ) async {
    await tester.pumpWidget(host(detailView(fileView(source: 'one'))));
    await tester.pumpWidget(host(detailView(fileView(source: 'two'))));
    TextField field(String key) =>
        tester.widget<TextField>(find.byKey(ValueKey(key)));
    expect(field('csharp-source').controller!.text, 'two');

    await tester.enterText(find.byKey(const ValueKey('csharp-source')), 'mine');
    await tester.pumpWidget(host(detailView(fileView(source: 'three'))));
    expect(field('csharp-source').controller!.text, 'mine');
  });

  testWidgets('start is disabled when the host does not allow activation', (
    tester,
  ) async {
    await tester.pumpWidget(
      host(detailView(fileView(status: 0), allowActivation: false)),
    );
    expect(
      tester
          .widget<OutlinedButton>(find.widgetWithText(OutlinedButton, 'Start'))
          .onPressed,
      isNull,
    );
    expect(
      tester
          .widget<OutlinedButton>(find.widgetWithText(OutlinedButton, 'Stop'))
          .onPressed,
      isNull,
    );
    expect(find.textContaining('disabled by host settings'), findsOneWidget);
  });

  testWidgets('shows exit code and read-only logs', (tester) async {
    await tester.pumpWidget(
      host(
        detailView({
          ...fileView(logs: 'error CS1002: ; expected'),
          'file': {...fileView()['file'] as Map, 'status': 3, 'exitCode': 1},
        }),
      ),
    );
    expect(find.text('Exited with code 1'), findsOneWidget);
    expect(find.text('error CS1002: ; expected'), findsOneWidget);
    expect(find.byTooltip('Refresh logs'), findsOneWidget);
  });

  testWidgets('stop posts to the stop route and shows the new status', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1100, 800);
    tester.view.devicePixelRatio = 1;
    var status = 1;
    final calls = <String>[];
    await tester.pumpWidget(
      host(
        CSharpManager(
          initialId: 'timer',
          onAsk: (_, _) {},
          onSelected: (_) {},
          request: (method, path, {body}) async {
            calls.add('$method $path');
            if (method == 'POST' && path == 'timer/stop') status = 0;
            if (path.isEmpty) {
              return {
                'items': [fileView(status: status)],
                'allowActivation': true,
              };
            }
            return fileView(status: status);
          },
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.textContaining('Running · since'), findsOneWidget);

    await tester.tap(find.widgetWithText(OutlinedButton, 'Stop'));
    await tester.pumpAndSettle();

    expect(calls, contains('POST timer/stop'));
    expect(find.text('Stopped.'), findsOneWidget);
    expect(find.widgetWithText(OutlinedButton, 'Start'), findsOneWidget);
    await tester.pumpWidget(const SizedBox.shrink());
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });

  testWidgets('a poll started before a command cannot restore obsolete state', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1100, 800);
    tester.view.devicePixelRatio = 1;
    final oldPoll = Completer<Map<String, dynamic>>();
    var delay = false, stopped = false;
    await tester.pumpWidget(
      host(
        CSharpManager(
          initialId: 'timer',
          onAsk: (_, _) {},
          onSelected: (_) {},
          request: (method, path, {body}) async {
            if (method == 'POST') {
              stopped = true;
              return fileView(status: 0);
            }
            if (path.isEmpty) {
              return {
                'items': [fileView(status: stopped ? 0 : 1)],
                'allowActivation': true,
              };
            }
            if (delay) {
              delay = false;
              return oldPoll.future;
            }
            return fileView(status: stopped ? 0 : 1);
          },
        ),
      ),
    );
    await tester.pumpAndSettle();
    final obsolete = fileView(status: 1);
    delay = true;
    await tester.tap(find.byTooltip('Refresh C# files'));
    await tester.pump();
    await tester.tap(find.widgetWithText(OutlinedButton, 'Stop'));
    await tester.pump();
    oldPoll.complete(obsolete);
    await tester.pumpAndSettle();
    expect(find.widgetWithText(OutlinedButton, 'Start'), findsOneWidget);
    expect(find.widgetWithText(OutlinedButton, 'Restart'), findsNothing);
    await tester.pumpWidget(const SizedBox.shrink());
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });

  testWidgets(
    'narrow screens navigate back and stale selections cannot replace the detail',
    (tester) async {
      tester.view.physicalSize = const Size(360, 800);
      tester.view.devicePixelRatio = 1;
      final pending = Completer<Map<String, dynamic>>();
      final second = fileView(id: 'second', name: 'Second app');
      await tester.pumpWidget(
        host(
          CSharpManager(
            request: (method, path, {body}) async => path.isEmpty
                ? {
                    'items': [fileView(), second],
                    'allowActivation': true,
                  }
                : path == 'timer'
                ? pending.future
                : second,
            onAsk: (_, _) {},
            onSelected: (_) {},
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Timer status'));
      await tester.pump();
      expect(find.byTooltip('All C# files'), findsOneWidget);
      await tester.tap(find.byTooltip('All C# files'));
      await tester.pump();
      await tester.tap(find.text('Second app'));
      await tester.pump();
      pending.complete(fileView());
      await tester.pumpAndSettle();
      expect(find.text('Second app'), findsWidgets);
      expect(find.text('Timer status'), findsNothing);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox.shrink());
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    },
  );

  testWidgets('disables mutations after the connection is lost', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1100, 800);
    tester.view.devicePixelRatio = 1;
    var disconnected = false;
    await tester.pumpWidget(
      host(
        CSharpManager(
          request: (method, path, {body}) async {
            if (disconnected) throw StateError('offline');
            return path.isEmpty
                ? {
                    'items': [fileView()],
                    'allowActivation': true,
                  }
                : fileView();
          },
          onAsk: (_, _) {},
          onSelected: (_) {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('C# files'), findsOneWidget);
    await tester.tap(find.text('Timer status').first);
    await tester.pumpAndSettle();
    disconnected = true;
    await tester.tap(find.byTooltip('Refresh C# files'));
    await tester.pumpAndSettle();
    expect(find.textContaining('Connection lost'), findsOneWidget);
    expect(
      tester
          .widget<OutlinedButton>(find.widgetWithText(OutlinedButton, 'Stop'))
          .onPressed,
      isNull,
    );
    await tester.pumpWidget(const SizedBox.shrink());
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });

  testWidgets('delete asks first, then removes the file and clears selection', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1100, 800);
    tester.view.devicePixelRatio = 1;
    var deleted = false;
    String? lastSelection = 'timer';
    await tester.pumpWidget(
      host(
        CSharpManager(
          initialId: 'timer',
          request: (method, path, {body}) async {
            if (method == 'DELETE' && path == 'timer') {
              deleted = true;
              return fileView(status: 0);
            }
            return path.isEmpty
                ? {
                    'items': [if (!deleted) fileView()],
                    'allowActivation': true,
                  }
                : fileView();
          },
          onAsk: (_, _) {},
          onSelected: (id) => lastSelection = id,
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(TextButton, 'Delete'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(TextButton, 'Cancel'));
    await tester.pumpAndSettle();
    expect(deleted, isFalse);

    await tester.tap(find.widgetWithText(TextButton, 'Delete'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Delete'));
    await tester.pumpAndSettle();
    expect(deleted, isTrue);
    expect(lastSelection, isNull);
    expect(find.text('Select a C# file to edit or run.'), findsOneWidget);
    await tester.pumpWidget(const SizedBox.shrink());
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });

  testWidgets('creating a file saves a starter source under the chosen ID', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1100, 800);
    tester.view.devicePixelRatio = 1;
    final writes = <String, Map<String, Object?>?>{};
    await tester.pumpWidget(
      host(
        CSharpManager(
          request: (method, path, {body}) async {
            if (method == 'PUT') writes[path] = body;
            return path.isEmpty
                ? {'items': <Object>[], 'allowActivation': true}
                : fileView(id: 'hello', name: 'Hello');
          },
          onAsk: (_, _) {},
          onSelected: (_) {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.textContaining('No C# files yet'), findsOneWidget);
    await tester.tap(find.text('New C# file'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const ValueKey('csharp-new-id')),
      'bad id!',
    );
    await tester.tap(find.text('Create'));
    await tester.pumpAndSettle();
    expect(writes, isEmpty);
    await tester.enterText(
      find.byKey(const ValueKey('csharp-new-id')),
      'hello',
    );
    await tester.enterText(
      find.byKey(const ValueKey('csharp-new-name')),
      'Hello',
    );
    await tester.tap(find.text('Create'));
    await tester.pumpAndSettle();
    expect(writes.keys, ['hello']);
    expect(writes['hello']!['name'], 'Hello');
    expect(writes['hello']!['source'], isNotEmpty);
    await tester.pumpWidget(const SizedBox.shrink());
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
}
