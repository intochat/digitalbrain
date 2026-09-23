import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter_shell/workspace/behaviors/behavior_manager.dart';
import 'package:digitalbrain_flutter_shell/workspace/behaviors/behavior_detail.dart';

import 'dart:async';

Map<String, dynamic> item({bool ready = true}) => {
  'description': {
    'id': 'timer',
    'revision': 1,
    'name': 'Timer status',
    'purpose': 'Show ticks',
    'triggers': ['Timer tick'],
    'effects': ['Update text'],
  },
  'program': {
    'revision': 2,
    'state': 'Running',
    'ready': ready,
    'desiredState': 'Running',
    'deployments': [
      {
        'revision': 1,
        'artifact': {'id': 'active'},
        'configurationJson': '{}',
        'createdAt': '2026-09-22',
      },
    ],
    'activeDeploymentRevision': 1,
  },
  'draftRevision': 1,
};
void main() {
  testWidgets('configuration alone can deploy and follows later versions', (
    tester,
  ) async {
    Map<String, Object?>? sent;
    final data = {
      ...item(),
      'draft': {'revision': 1, 'source': '', 'tests': ''},
      'check': {
        'revision': 1,
        'status': 'Passed',
        'artifact': {'id': 'active'},
      },
      'canDeploy': true,
      'allowActivation': true,
    };
    Widget screen() => MaterialApp(
      home: Scaffold(
        body: BehaviorDetailView(
          detail: data,
          enabled: true,
          stale: false,
          onDescribe: () {},
          onAsk: (_) {},
          onCommand: (_, body) async {
            sent = body;
          },
        ),
      ),
    );
    await tester.pumpWidget(screen());
    await tester.pumpAndSettle();
    expect(find.text('Deploy changes'), findsNothing);
    await tester.ensureVisible(find.text('Configuration'));
    await tester.tap(find.text('Configuration'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byType(TextField),
      '{"Behavior__Interval":"10"}',
    );
    await tester.pump();
    await tester.tap(find.text('Deploy changes'));
    await tester.pumpAndSettle();
    expect(sent?['configurationJson'], '{"Behavior__Interval":"10"}');
    final deployment =
        ((data['program'] as Map)['deployments'] as List).last as Map;
    deployment['configurationJson'] = '{"Behavior__Interval":"10"}';
    await tester.pumpWidget(screen());
    await tester.pumpAndSettle();
    expect(find.text('Deploy changes'), findsNothing);
    deployment['configurationJson'] = '{"Behavior__Interval":"20"}';
    await tester.pumpWidget(screen());
    await tester.pumpAndSettle();
    expect(
      tester.widget<TextField>(find.byType(TextField)).controller!.text,
      '{"Behavior__Interval":"20"}',
    );
  });

  testWidgets('a pre-command poll cannot restore obsolete state', (
    tester,
  ) async {
    final oldPoll = Completer<Map<String, dynamic>>();
    var delay = false, stopped = false;
    Map<String, dynamic> snapshot() => {
      ...item(),
      'program': {
        ...item()['program'] as Map,
        'revision': stopped ? 3 : 2,
        'state': stopped ? 'Stopped' : 'Running',
      },
      'draft': {'revision': 1, 'source': '', 'tests': ''},
      'allowActivation': true,
      'canDeploy': false,
    };
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: BehaviorManager(
            initialId: 'timer',
            onAsk: (_, _) {},
            onSelected: (_) {},
            request: (path, {body}) async {
              if (body != null) {
                stopped = true;
                return {};
              }
              if (path.isEmpty) {
                return {
                  'items': [item()],
                };
              }
              if (delay) {
                delay = false;
                return oldPoll.future;
              }
              return snapshot();
            },
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    final obsolete = snapshot();
    delay = true;
    await tester.tap(find.byTooltip('Refresh automations'));
    await tester.pump();
    await tester.tap(find.text('Stop'));
    await tester.pump();
    oldPoll.complete(obsolete);
    await tester.pumpAndSettle();
    expect(find.text('Start'), findsOneWidget);
    expect(find.text('Stop'), findsNothing);
    await tester.pumpWidget(const SizedBox.shrink());
  });

  testWidgets(
    'narrow screens navigate back and stale selections cannot replace the detail',
    (tester) async {
      tester.view.physicalSize = const Size(360, 800);
      tester.view.devicePixelRatio = 1;
      final pending = Completer<Map<String, dynamic>>();
      final second = {
        ...item(),
        'description': {
          ...item()['description'] as Map,
          'id': 'second',
          'name': 'Second behavior',
        },
      };
      Map<String, dynamic> detail(Map<String, dynamic> entry) => {
        ...entry,
        'draft': {'revision': 1, 'source': '', 'tests': ''},
        'logs': {'entries': []},
        'canDeploy': false,
        'allowActivation': true,
      };
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: BehaviorManager(
              request: (path, {body}) async => path.isEmpty
                  ? {
                      'items': [item(), second],
                    }
                  : path.startsWith('timer/')
                  ? pending.future
                  : detail(second),
              onAsk: (_, _) {},
              onSelected: (_) {},
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Timer status'));
      await tester.pump();
      // The narrow back control is a compact accessible icon button.
      expect(find.byTooltip('All automations'), findsOneWidget);
      await tester.tap(find.byTooltip('All automations'));
      await tester.pump();
      await tester.tap(find.text('Second behavior'));
      await tester.pump();
      pending.complete(detail(item()));
      await tester.pumpAndSettle();
      expect(find.text('Second behavior'), findsWidgets);
      expect(find.text('Timer status'), findsNothing);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox.shrink());
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    },
  );

  testWidgets('deploy sends exact checked artifact and expected revision', (
    tester,
  ) async {
    Map<String, Object?>? sent;
    final artifact = {
      'id': 'checked',
      'sourceHash': 'hash',
      'environmentHash': 'env',
    };
    final data = {
      ...item(),
      'draft': {'revision': 5, 'source': 'source', 'tests': ''},
      'check': {'revision': 5, 'status': 'Passed', 'artifact': artifact},
      'canDeploy': true,
      'allowActivation': true,
    };
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: BehaviorDetailView(
            detail: data,
            enabled: true,
            stale: false,
            onDescribe: () {},
            onAsk: (_) {},
            onCommand: (path, body) async {
              expect(path, 'deploy');
              sent = body;
            },
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Deploy changes'));
    await tester.pumpAndSettle();
    expect(sent?['artifact'], artifact);
    expect(sent?['expectedRevision'], 2);
    expect(sent?['operationId'], isNotEmpty);
    await tester.pumpWidget(const SizedBox.shrink());
  });
  testWidgets(
    'shows truthful readiness and disables mutations after disconnect',
    (tester) async {
      tester.view.physicalSize = const Size(1100, 800);
      tester.view.devicePixelRatio = 1;
      var disconnected = false;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: BehaviorManager(
              request: (path, {body}) async {
                if (disconnected) throw StateError('offline');
                if (path.isEmpty) {
                  return {
                    'items': [item(ready: false)],
                    'allowActivation': true,
                  };
                }
                return {
                  ...item(ready: false),
                  'draft': {
                    'revision': 1,
                    'source': '',
                    'tests': '',
                    'moduleIds': [],
                  },
                  'logs': {'entries': []},
                  'allowActivation': true,
                  'canDeploy': false,
                };
              },
              onAsk: (_, _) {},
              onSelected: (_) {},
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Automations'), findsOneWidget);
      await tester.tap(find.text('Timer status').first);
      await tester.pumpAndSettle();
      expect(find.textContaining('Waiting for readiness'), findsWidgets);
      expect(find.text('Deploy changes'), findsNothing);
      disconnected = true;
      await tester.tap(find.byTooltip('Refresh automations'));
      await tester.pumpAndSettle();
      expect(find.textContaining('Connection lost'), findsOneWidget);
      final stop = tester.widget<OutlinedButton>(
        find.widgetWithText(OutlinedButton, 'Stop'),
      );
      expect(stop.onPressed, isNull);
      await tester.pumpWidget(const SizedBox.shrink());
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    },
  );

  testWidgets('customer copy names automations, not behaviors', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: BehaviorManager(
            onAsk: (_, _) {},
            onSelected: (_) {},
            request: (path, {body}) async => path.isEmpty
                ? {'items': <Map<String, dynamic>>[]}
                : <String, dynamic>{},
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Automations'), findsOneWidget);
    expect(find.text('New automation'), findsOneWidget);
    expect(find.byTooltip('Refresh automations'), findsOneWidget);
    expect(find.textContaining('Create your first automation'), findsOneWidget);
    expect(find.textContaining('behavior'), findsNothing);
    await tester.pumpWidget(const SizedBox.shrink());
  });
}
