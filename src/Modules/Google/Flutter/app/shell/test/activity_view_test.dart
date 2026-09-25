import 'dart:async';
import 'dart:convert';
import 'dart:ui' as ui;

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/activity/activity_controller.dart';
import 'package:digitalbrain_flutter_shell/workspace/activity/activity_view.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart' as graph;
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:digitalbrain_ui/src/components/graph/graph_camera.dart';
import 'package:digitalbrain_ui/src/components/graph/graph_scene.dart';

final class FakeGraphScene implements GraphScene, AnimatedGraphScene {
  List<graph.GraphPulse> shownPulses = const [];
  Offset? projectedNode;
  Map<String, Offset>? projections;
  int advanceCalls = 0;
  bool cleared = false;
  bool failLoad = false;
  @override
  Future<void> load(
    List<graph.GraphNode> nodes,
    List<graph.GraphEdge> edges,
    Map<String, graph.GraphPoint> layout,
  ) async {
    if (failLoad) throw StateError('Renderer initialization failed');
  }

  @override
  void applyCamera(GraphCameraState state) {}
  @override
  String? pick(Offset local) => null;
  @override
  Offset? project(String nodeId) => projections?[nodeId] ?? projectedNode;
  @override
  Widget build(BuildContext context) => const SizedBox.expand();
  @override
  void dispose() {}
  @override
  void setPulses(List<graph.GraphPulse> pulses) => shownPulses = pulses;
  @override
  void advance(double seconds) => advanceCalls++;
  @override
  void clearPulses() {
    cleared = true;
    shownPulses = const [];
  }
}

ActivityRecord item(int sequence, String kind) => ActivityRecord(
  id: 'event-$sequence',
  operationId: 'operation',
  sequence: sequence,
  at: DateTime.utc(2026, 9, 25, 12),
  kind: kind,
  type: 'Read',
  status: 'started',
  sourceId: 'caller',
  targetId: 'target',
);

void main() {
  test('activity keeps only ten distinct interaction trails', () async {
    final controller = ActivityController(
      read: () async => ActivitySnapshot(
        events: [
          for (var i = 1; i <= 12; i++)
            ActivityRecord(
              id: 'event-$i',
              operationId: 'operation-$i',
              sequence: i,
              at: DateTime.now().toUtc(),
              kind: 'CallStarted',
              type: 'Read',
              status: 'started',
              sourceId: 'caller',
              targetId: 'target',
            ),
        ],
        nextSequence: 12,
        gap: false,
        observedAt: DateTime.now().toUtc(),
      ),
      watch: (_, _) => const Stream<ActivityUpdate>.empty(),
    );
    await controller.start();
    expect(controller.pulses.length, 10);
    expect(controller.pulses.first.signature, 'event-3');
    expect(controller.pulses.last.signature, 'event-12');
    controller.pause();
    final frozen = controller.visualNow;
    await Future<void>.delayed(const Duration(milliseconds: 2));
    expect(controller.visualNow, frozen);
    controller.dispose();
  });

  testWidgets('selection links activity list to path and details', (
    tester,
  ) async {
    final controller = ActivityController(
      read: () async => ActivitySnapshot(
        events: [item(1, 'CallStarted'), item(2, 'CallArrived')],
        nextSequence: 2,
        gap: false,
        observedAt: DateTime.utc(2026, 9, 25, 12),
      ),
      watch: (_, _) => const Stream<ActivityUpdate>.empty(),
    );
    await controller.start();
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(body: ActivityView(controller: controller)),
      ),
    );
    await tester.tap(find.byKey(const Key('activity_event_1')));
    await tester.pump();
    expect(controller.selectedOperationId, 'operation');
    expect(find.text('Read'), findsWidgets);
    expect(find.textContaining('caller'), findsWidgets);
    expect(find.textContaining('target'), findsWidgets);
    expect(find.byKey(const Key('ui_graph')), findsOneWidget);
    controller.dispose();
  });

  testWidgets(
    'Activity switches between three views without losing selection',
    (tester) async {
      final controller = ActivityController(
        read: () async => ActivitySnapshot(
          events: [item(1, 'CallStarted')],
          nextSequence: 1,
          gap: false,
          observedAt: DateTime.utc(2026, 9, 25, 12),
        ),
        watch: (_, _) => const Stream<ActivityUpdate>.empty(),
      );
      await controller.start();
      controller.selectActivity('event-1');
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: graph.UiThemeScope(
              child: ActivityView(
                controller: controller,
                spatialSceneFactory: FakeGraphScene.new,
              ),
            ),
          ),
        ),
      );
      expect(find.text('Lumen'), findsOneWidget);
      expect(find.text('Graph'), findsOneWidget);
      expect(find.text('3D'), findsOneWidget);
      await tester.tap(find.text('Lumen'));
      await tester.pump();
      expect(find.byKey(const Key('lumen_graph_canvas')), findsOneWidget);
      expect(controller.selectedOperationId, 'operation');
      await tester.tap(find.text('Graph'));
      await tester.pump();
      expect(find.byKey(const Key('ui_graph')), findsOneWidget);
      await tester.tap(find.text('3D'));
      await tester.pump();
      expect(find.byKey(const Key('spatial_graph')), findsOneWidget);
      expect(find.byTooltip('Fit 3D graph'), findsOneWidget);
      controller.dispose();
    },
  );

  testWidgets('opening 3D does not replay retained activity as new motion', (
    tester,
  ) async {
    final scene = FakeGraphScene();
    await tester.pumpWidget(
      MaterialApp(
        home: SizedBox(
          width: 400,
          height: 300,
          child: graph.SpatialGraph(
            nodes: const [graph.GraphNode(id: 'a', label: 'A')],
            edges: const [],
            pulses: [
              graph.GraphPulse(
                fromId: 'a',
                toId: 'a',
                signature: 'retained',
                at: DateTime.now().toUtc(),
                outcome: graph.GraphPulseOutcome.signal,
              ),
            ],
            sceneFactory: () => scene,
          ),
        ),
      ),
    );
    expect(scene.shownPulses, isEmpty);
  });

  testWidgets('3D icon label selects its neuron', (tester) async {
    final scene = FakeGraphScene()..projectedNode = const Offset(200, 150);
    String? selected;
    await tester.pumpWidget(
      MaterialApp(
        home: SizedBox(
          width: 400,
          height: 300,
          child: graph.SpatialGraph(
            nodes: const [graph.GraphNode(id: 'a', label: 'A')],
            edges: const [],
            sceneFactory: () => scene,
            onNodeTap: (node) => selected = node.id,
          ),
        ),
      ),
    );
    await tester.tapAt(const Offset(200, 150));
    await tester.pump();
    expect(selected, 'a');
  });

  testWidgets('3D observed route can be selected', (tester) async {
    final scene = FakeGraphScene()
      ..projections = {
        'a': const Offset(100, 150),
        'b': const Offset(300, 150),
      };
    String? selected;
    await tester.pumpWidget(
      MaterialApp(
        home: SizedBox(
          width: 400,
          height: 300,
          child: graph.SpatialGraph(
            nodes: const [
              graph.GraphNode(id: 'a', label: 'A'),
              graph.GraphNode(id: 'b', label: 'B'),
            ],
            edges: const [
              graph.GraphEdge(id: 'ab', sourceId: 'a', targetId: 'b'),
            ],
            sceneFactory: () => scene,
            onEdgeTap: (edge) => selected = edge.id,
          ),
        ),
      ),
    );
    await tester.tapAt(const Offset(200, 150));
    expect(selected, 'ab');
  });

  testWidgets('3D paints a visible trail between projected neurons', (
    tester,
  ) async {
    final scene = FakeGraphScene()
      ..projections = {
        'a': const Offset(100, 150),
        'b': const Offset(300, 150),
      };
    final start = DateTime.utc(2026, 9, 25, 12);
    const boundaryKey = Key('trail_image');
    Widget view(List<graph.GraphPulse> pulses) => MaterialApp(
      home: Center(
        child: RepaintBoundary(
          key: boundaryKey,
          child: SizedBox(
            width: 400,
            height: 300,
            child: graph.SpatialGraph(
              nodes: const [
                graph.GraphNode(id: 'a', label: 'A'),
                graph.GraphNode(id: 'b', label: 'B'),
              ],
              edges: const [],
              pulses: pulses,
              now: start.add(const Duration(seconds: 1)),
              playing: false,
              sceneFactory: () => scene,
            ),
          ),
        ),
      ),
    );
    Future<List<int>> pixels() async => (await tester.runAsync(() async {
      final boundary = tester.renderObject<RenderRepaintBoundary>(
        find.byKey(boundaryKey),
      );
      final image = await boundary.toImage();
      final data = await image.toByteData(format: ui.ImageByteFormat.rawRgba);
      image.dispose();
      return data!.buffer.asUint8List().toList();
    }))!;

    await tester.pumpWidget(view(const []));
    final quiet = await pixels();
    await tester.pumpWidget(
      view([
        graph.GraphPulse(fromId: 'a', toId: 'b', signature: 'call', at: start),
      ]),
    );
    final active = await pixels();
    var changed = 0;
    for (var y = 80; y < 160; y++) {
      for (var x = 140; x < 260; x++) {
        final offset = (y * 400 + x) * 4;
        if (quiet[offset] != active[offset] ||
            quiet[offset + 1] != active[offset + 1] ||
            quiet[offset + 2] != active[offset + 2]) {
          changed++;
        }
      }
    }
    expect(changed, greaterThan(100));
  });

  testWidgets('3D highlights a selected retained route without live pulses', (
    tester,
  ) async {
    final scene = FakeGraphScene()
      ..projections = {
        'a': const Offset(100, 150),
        'b': const Offset(300, 150),
      };
    await tester.pumpWidget(
      MaterialApp(
        home: SizedBox(
          width: 400,
          height: 300,
          child: graph.SpatialGraph(
            nodes: const [
              graph.GraphNode(id: 'a', label: 'A'),
              graph.GraphNode(id: 'b', label: 'B'),
            ],
            edges: const [
              graph.GraphEdge(id: 'ab', sourceId: 'a', targetId: 'b'),
            ],
            selectedEdgeId: 'ab',
            sceneFactory: () => scene,
          ),
        ),
      ),
    );
    expect(find.byKey(const Key('spatial_selected_route_ab')), findsOneWidget);
  });

  testWidgets('3D initialization failure offers the graph view', (
    tester,
  ) async {
    var fallback = false;
    await tester.pumpWidget(
      MaterialApp(
        home: SizedBox(
          width: 400,
          height: 300,
          child: graph.SpatialGraph(
            nodes: const [],
            edges: const [],
            sceneFactory: () => throw StateError('Renderer unavailable'),
            onFallback: () => fallback = true,
          ),
        ),
      ),
    );
    expect(find.text('3D renderer unavailable'), findsOneWidget);
    await tester.tap(find.text('Use Graph view'));
    expect(fallback, isTrue);
  });

  testWidgets('async 3D load failure offers the graph view', (tester) async {
    final scene = FakeGraphScene()..failLoad = true;
    await tester.pumpWidget(
      MaterialApp(
        home: SizedBox(
          width: 400,
          height: 300,
          child: graph.SpatialGraph(
            nodes: const [],
            edges: const [],
            sceneFactory: () => scene,
          ),
        ),
      ),
    );
    await tester.pump();
    expect(find.text('3D renderer unavailable'), findsOneWidget);
  });

  testWidgets('reduced motion keeps spatial signal animation off', (
    tester,
  ) async {
    final scene = FakeGraphScene();
    Widget view(List<graph.GraphPulse> pulses) => MaterialApp(
      home: MediaQuery(
        data: const MediaQueryData(disableAnimations: true),
        child: SizedBox(
          width: 400,
          height: 300,
          child: graph.SpatialGraph(
            nodes: const [graph.GraphNode(id: 'a', label: 'A')],
            edges: const [],
            pulses: pulses,
            sceneFactory: () => scene,
          ),
        ),
      ),
    );
    await tester.pumpWidget(view(const []));
    await tester.pumpWidget(
      view([
        graph.GraphPulse(
          fromId: 'a',
          toId: 'a',
          signature: 'new',
          at: DateTime.now().toUtc(),
          outcome: graph.GraphPulseOutcome.signal,
        ),
      ]),
    );
    expect(scene.shownPulses, isEmpty);
  });

  testWidgets('pausing 3D freezes an activity trail in flight', (tester) async {
    final scene = FakeGraphScene();
    Widget view(bool playing, List<graph.GraphPulse> pulses) => MaterialApp(
      home: SizedBox(
        width: 400,
        height: 300,
        child: graph.SpatialGraph(
          nodes: const [graph.GraphNode(id: 'a', label: 'A')],
          edges: const [],
          pulses: pulses,
          playing: playing,
          sceneFactory: () => scene,
        ),
      ),
    );
    await tester.pumpWidget(view(true, const []));
    final pulses = [
      graph.GraphPulse(
        fromId: 'a',
        toId: 'a',
        signature: 'new',
        at: DateTime.now().toUtc(),
        outcome: graph.GraphPulseOutcome.signal,
      ),
    ];
    await tester.pumpWidget(view(true, pulses));
    await tester.pump(const Duration(milliseconds: 30));
    expect(scene.advanceCalls, greaterThan(0));
    final beforePause = scene.advanceCalls;
    await tester.pumpWidget(view(false, pulses));
    await tester.pump(const Duration(milliseconds: 30));
    expect(scene.cleared, isFalse);
    expect(scene.shownPulses.map((pulse) => pulse.signature), ['new']);
    expect(scene.advanceCalls, beforePause);
  });

  testWidgets('Lumen shows a published signal at its source', (tester) async {
    final controller = ActivityController(
      read: () async => ActivitySnapshot(
        events: [
          ActivityRecord(
            id: 'signal-now',
            operationId: 'op',
            sequence: 1,
            at: DateTime.now().toUtc(),
            kind: 'SignalPublished',
            type: 'Changed',
            status: 'published',
            sourceId: 'caller',
          ),
        ],
        nextSequence: 1,
        gap: false,
        observedAt: DateTime.now().toUtc(),
      ),
      watch: (_, _) => const Stream<ActivityUpdate>.empty(),
    );
    await controller.start();
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: graph.UiThemeScope(child: ActivityView(controller: controller)),
        ),
      ),
    );
    await tester.tap(find.text('Lumen'));
    await tester.pump();
    expect(find.byKey(const Key('lumen_signal_caller')), findsOneWidget);
    controller.dispose();
  });

  test('pause keeps frame fixed while new observations arrive', () async {
    final updates = StreamController<ActivityUpdate>();
    var latest = 1;
    final controller = ActivityController(
      read: () async => ActivitySnapshot(
        events: [
          for (var i = 1; i <= latest; i++)
            item(i, i == 1 ? 'CallStarted' : 'CallCompleted'),
        ],
        nextSequence: latest,
        gap: false,
        observedAt: DateTime.utc(2026, 9, 25, 12),
      ),
      watch: (_, _) => updates.stream,
    );
    await controller.start();
    controller.pause();
    latest = 2;
    updates.add(ActivityItem(item(2, 'CallCompleted')));
    await Future<void>.delayed(Duration.zero);
    expect(controller.visibleCursor, 1);
    await controller.resumeLive();
    expect(controller.visibleCursor, 2);
    await updates.close();
    controller.dispose();
  });

  test('paused replay survives live eviction and can seek forward', () async {
    final updates = StreamController<ActivityUpdate>();
    final controller = ActivityController(
      read: () async => ActivitySnapshot(
        events: [item(1, 'CallStarted'), item(2, 'CallCompleted')],
        nextSequence: 2,
        gap: false,
        observedAt: DateTime.utc(2026, 9, 25),
      ),
      watch: (_, _) => updates.stream,
    );
    await controller.start();
    controller.pause();
    controller.seek(1);
    expect(controller.replayLastSequence, 2);
    controller.seek(2);
    expect(controller.visibleEvents.map((e) => e.sequence), [1, 2]);
    for (var i = 3; i <= 2003; i++) {
      updates.add(ActivityItem(item(i, 'CallCompleted')));
    }
    await Future<void>.delayed(const Duration(milliseconds: 10));
    expect(controller.replayFirstSequence, 1);
    expect(controller.visibleEvents.map((e) => e.sequence), [1, 2]);
    await updates.close();
    controller.dispose();
  });

  test('failure filter preserves the graph route', () async {
    final controller = ActivityController(
      read: () async => ActivitySnapshot(
        events: [item(1, 'CallStarted'), item(2, 'CallFailed')],
        nextSequence: 2,
        gap: false,
        observedAt: DateTime.utc(2026, 9, 25),
      ),
      watch: (_, _) => const Stream<ActivityUpdate>.empty(),
    );
    await controller.start();
    controller.setKindFilter('CallFailed');
    expect(controller.visibleEvents.length, 1);
    expect(controller.edges.length, 1);
    expect(controller.pulse?.outcome, graph.GraphPulseOutcome.failed);
    controller.dispose();
  });

  test('selection includes separate signal in the same correlation', () async {
    final controller = ActivityController(
      read: () async => ActivitySnapshot(
        events: [
          ActivityRecord(
            id: 'call',
            operationId: 'call-op',
            correlationId: 'intent',
            sequence: 1,
            at: DateTime.utc(2026, 9, 25),
            kind: 'CallStarted',
            type: 'Run',
            status: 'started',
            sourceId: 'caller',
            targetId: 'target',
          ),
          ActivityRecord(
            id: 'signal',
            operationId: 'signal-op',
            correlationId: 'intent',
            sequence: 2,
            at: DateTime.utc(2026, 9, 25),
            kind: 'SignalPublished',
            type: 'Changed',
            status: 'published',
            sourceId: 'target',
          ),
        ],
        nextSequence: 2,
        gap: false,
        observedAt: DateTime.utc(2026, 9, 25),
      ),
      watch: (_, _) => const Stream<ActivityUpdate>.empty(),
    );
    await controller.start();
    controller.selectActivity('call');
    expect(controller.selectedSteps.map((e) => e.id), ['call', 'signal']);
    controller.dispose();
  });

  test(
    'concurrent calls and publications remain separate visual events',
    () async {
      final controller = ActivityController(
        read: () async => ActivitySnapshot(
          events: [
            item(1, 'CallStarted'),
            ActivityRecord(
              id: 'publication',
              operationId: 'signal-op',
              sequence: 2,
              at: DateTime.utc(2026, 9, 25, 12),
              kind: 'SignalPublished',
              type: 'Changed',
              status: 'published',
              sourceId: 'target',
            ),
          ],
          nextSequence: 2,
          gap: false,
          observedAt: DateTime.utc(2026, 9, 25, 12),
        ),
        watch: (_, _) => const Stream<ActivityUpdate>.empty(),
      );
      await controller.start();
      expect(controller.pulses.map((pulse) => pulse.signature), [
        'event-1',
        'publication',
      ]);
      expect(controller.pulses.last.local, isTrue);
      expect(controller.edges, hasLength(1));
      controller.dispose();
    },
  );

  test(
    'one call operation has one visual path across start and arrival',
    () async {
      final controller = ActivityController(
        read: () async => ActivitySnapshot(
          events: [item(1, 'CallStarted'), item(2, 'CallArrived')],
          nextSequence: 2,
          gap: false,
          observedAt: DateTime.utc(2026, 9, 25),
        ),
        watch: (_, _) => const Stream<ActivityUpdate>.empty(),
      );
      await controller.start();
      expect(controller.pulses, hasLength(1));
      expect(controller.pulses.single.outcome, graph.GraphPulseOutcome.arrived);
      controller.dispose();
    },
  );

  test(
    'completion recolors the original route without restarting travel',
    () async {
      final start = DateTime.utc(2026, 9, 25, 12);
      final controller = ActivityController(
        read: () async => ActivitySnapshot(
          events: [
            ActivityRecord(
              id: 'started',
              operationId: 'operation',
              sequence: 1,
              at: start,
              kind: 'CallStarted',
              type: 'Run',
              status: 'started',
              sourceId: 'a',
              targetId: 'b',
            ),
            ActivityRecord(
              id: 'published',
              operationId: 'operation',
              sequence: 2,
              at: start.add(const Duration(milliseconds: 400)),
              kind: 'SignalPublished',
              type: 'Changed',
              status: 'published',
              sourceId: 'b',
            ),
            ActivityRecord(
              id: 'completed',
              operationId: 'operation',
              sequence: 3,
              at: start.add(const Duration(seconds: 2)),
              kind: 'CallCompleted',
              type: 'Run',
              status: 'completed',
              sourceId: 'a',
              targetId: 'b',
            ),
          ],
          nextSequence: 3,
          gap: false,
          observedAt: start,
        ),
        watch: (_, _) => const Stream<ActivityUpdate>.empty(),
      );
      await controller.start();
      expect(controller.pulses.map((pulse) => pulse.signature), [
        'published',
        'completed',
      ]);
      expect(controller.pulses.last.at, start);
      expect(
        controller.pulses.last.updatedAt,
        start.add(const Duration(seconds: 2)),
      );
      expect(controller.pulses.first.local, isTrue);
      controller.dispose();
    },
  );

  testWidgets('gap and unknown target are visible without invented edge', (
    tester,
  ) async {
    final controller = ActivityController(
      read: () async => ActivitySnapshot(
        events: [
          ActivityRecord(
            id: 'signal',
            operationId: 'signal-op',
            sequence: 3,
            at: DateTime.utc(2026, 9, 25),
            kind: 'SignalPublished',
            type: 'Changed',
            status: 'published',
            sourceId: 'caller',
          ),
        ],
        nextSequence: 3,
        gap: true,
        observedAt: DateTime.utc(2026, 9, 25),
      ),
      watch: (_, _) => const Stream<ActivityUpdate>.empty(),
    );
    await controller.start();
    await tester.pumpWidget(
      MaterialApp(
        home: MediaQuery(
          data: const MediaQueryData(disableAnimations: true),
          child: Scaffold(body: ActivityView(controller: controller)),
        ),
      ),
    );
    expect(find.textContaining('earlier activity was lost'), findsOneWidget);
    expect(controller.edges, isEmpty);
    expect(controller.pulse?.local, isTrue);
    await tester.tap(find.byKey(const Key('activity_event_3')));
    await tester.pump();
    expect(find.textContaining('Unknown target'), findsWidgets);
    controller.dispose();
  });

  testWidgets('activity screen reads the product snapshot', (tester) async {
    final paths = <String>[];
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://localhost:5000'),
      httpClient: MockClient((request) async {
        paths.add(request.url.path);
        if (request.url.path.endsWith('/events')) return http.Response('', 200);
        return http.Response(
          jsonEncode({
            'events': [
              {
                'id': 'one',
                'operationId': 'operation',
                'sequence': 1,
                'at': '2026-09-25T12:00:00Z',
                'kind': 4,
                'type': 'SignalSeen',
                'status': 'published',
                'sourceId': 'neuron',
              },
            ],
            'nextSequence': 1,
            'gap': false,
            'observedAt': '2026-09-25T12:00:00Z',
          }),
          200,
        );
      }),
    );
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: ActivityScreen(
            workspaceId: 'one',
            client: client,
            active: true,
          ),
        ),
      ),
    );
    await tester.pump();
    expect(find.text('SignalSeen'), findsOneWidget);
    expect(paths, contains('/workspaces/one/activity'));
    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pump(const Duration(seconds: 1));
    client.close();
  });
}
