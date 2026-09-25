import 'dart:ui' as ui;

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:digitalbrain_ui/src/components/graph/graph_camera.dart';
import 'package:digitalbrain_ui/src/components/graph/graph_painter.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';

const _trace = [
  GraphEdge(
    id: 'trace:visit:1',
    sourceId: 'root',
    targetId: 'neuron/a',
    label: '1',
  ),
  GraphEdge(
    id: 'trace:visit:2',
    sourceId: 'root',
    targetId: 'neuron/b',
    label: '2',
  ),
  GraphEdge(
    id: 'trace:visit:3',
    sourceId: 'root',
    targetId: 'neuron/a',
    label: '3',
  ),
];

const _nodes = [
  GraphNode(id: 'root', label: 'Root'),
  GraphNode(id: 'neuron/a', label: 'A'),
  GraphNode(id: 'neuron/b', label: 'B'),
];

const _edges = [
  GraphEdge(id: 'root|neuron/a|call', sourceId: 'root', targetId: 'neuron/a'),
  GraphEdge(id: 'root|neuron/b|call', sourceId: 'root', targetId: 'neuron/b'),
];

const _boundaryKey = Key('trace_boundary');

Future<List<int>> _pixels(WidgetTester tester) async =>
    (await tester.runAsync(() async {
      final boundary = tester.renderObject<RenderRepaintBoundary>(
        find.byKey(_boundaryKey),
      );
      final image = await boundary.toImage();
      final data = await image.toByteData(format: ui.ImageByteFormat.rawRgba);
      image.dispose();
      return data!.buffer.asUint8List().toList();
    }))!;

int _changedPixels(List<int> before, List<int> after) {
  var changed = 0;
  for (var offset = 0; offset < before.length; offset += 4) {
    if (before[offset] != after[offset] ||
        before[offset + 1] != after[offset + 1] ||
        before[offset + 2] != after[offset + 2] ||
        before[offset + 3] != after[offset + 3]) {
      changed++;
    }
  }
  return changed;
}

void main() {
  testWidgets('UiGraph projects every numbered trace step into its painter', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: SizedBox(
          width: 400,
          height: 300,
          child: UiGraph(nodes: _nodes, edges: _edges, tracePath: _trace),
        ),
      ),
    );
    final painter = tester
        .widgetList<CustomPaint>(find.byType(CustomPaint))
        .map((widget) => widget.painter)
        .whereType<GraphPainter>()
        .single;
    final steps = {
      for (final edge in painter.traceEdges)
        '${edge.edge.label}:${edge.edge.targetId}',
    };
    expect(steps, {'1:neuron/a', '2:neuron/b', '3:neuron/a'});
    expect(painter.edges, hasLength(2));
  });

  testWidgets('UiGraph paints the numbered trace over the observed edges', (
    tester,
  ) async {
    Widget view({required List<GraphEdge> trace}) => MaterialApp(
      home: Center(
        child: RepaintBoundary(
          key: _boundaryKey,
          child: SizedBox(
            width: 400,
            height: 300,
            child: UiGraph(nodes: _nodes, edges: _edges, tracePath: trace),
          ),
        ),
      ),
    );
    await tester.pumpWidget(view(trace: const []));
    final quiet = await _pixels(tester);
    await tester.pumpWidget(view(trace: _trace));
    final traced = await _pixels(tester);
    expect(_changedPixels(quiet, traced), greaterThan(100));
  });

  testWidgets('LumenBrainGraph paints the trace without new synapses', (
    tester,
  ) async {
    final snapshot = BrainSnapshot(
      rootId: 'root',
      observedAt: DateTime.utc(2026, 9, 25),
      nodes: const [
        BrainNeuron(
          id: 'root',
          type: 'neuron',
          name: 'root',
          label: 'Root',
          module: '',
        ),
        BrainNeuron(
          id: 'neuron/a',
          type: 'neuron',
          name: 'a',
          label: 'A',
          module: '',
        ),
        BrainNeuron(
          id: 'neuron/b',
          type: 'neuron',
          name: 'b',
          label: 'B',
          module: '',
        ),
      ],
      synapses: const [
        BrainSynapse(
          id: 'root|neuron/a|call',
          sourceId: 'root',
          targetId: 'neuron/a',
          signalType: '',
          kind: 'Observed call',
        ),
        BrainSynapse(
          id: 'root|neuron/b|call',
          sourceId: 'root',
          targetId: 'neuron/b',
          signalType: '',
          kind: 'Observed call',
        ),
      ],
    );
    Widget view({required List<GraphEdge> trace}) => MaterialApp(
      home: RepaintBoundary(
        key: _boundaryKey,
        child: UiThemeScope(
          child: Scaffold(
            body: SizedBox(
              width: 500,
              height: 400,
              child: LumenBrainGraph(
                snapshot: snapshot,
                tracePath: trace,
                onNeuron: (_) {},
                onSynapse: (_) {},
              ),
            ),
          ),
        ),
      ),
    );
    await tester.pumpWidget(view(trace: const []));
    await tester.pumpAndSettle();
    final quiet = await _pixels(tester);
    await tester.pumpWidget(view(trace: _trace));
    await tester.pumpAndSettle();
    final traced = await _pixels(tester);
    expect(_changedPixels(quiet, traced), greaterThan(100));

    final lumen = tester.widget<LumenBrainGraph>(
      find.byType(LumenBrainGraph),
    );
    expect(lumen.tracePath.map((edge) => edge.label), ['1', '2', '3']);
    expect(lumen.snapshot.synapses, hasLength(2));
  });

  testWidgets('SpatialGraph paints the numbered trace at projected midpoints', (
    tester,
  ) async {
    const scene = _FixedProjectionScene({
      'root': Offset(80, 150),
      'neuron/a': Offset(200, 90),
      'neuron/b': Offset(320, 210),
    });
    Widget view({required List<GraphEdge> trace}) => MaterialApp(
      home: RepaintBoundary(
        key: _boundaryKey,
        child: SizedBox(
          width: 400,
          height: 300,
          child: SpatialGraph(
            nodes: _nodes,
            edges: _edges,
            tracePath: trace,
            playing: false,
            sceneFactory: () => scene,
          ),
        ),
      ),
    );
    await tester.pumpWidget(view(trace: const []));
    final quiet = await _pixels(tester);
    await tester.pumpWidget(view(trace: _trace));
    final traced = await _pixels(tester);
    expect(_changedPixels(quiet, traced), greaterThan(100));
    expect(find.byKey(const Key('spatial_connections')), findsOneWidget);
  });
}

final class _FixedProjectionScene implements GraphScene {
  const _FixedProjectionScene(this.projections);
  final Map<String, Offset> projections;

  @override
  Future<void> load(
    List<GraphNode> nodes,
    List<GraphEdge> edges,
    Map<String, GraphPoint> layout,
  ) async {}

  @override
  void applyCamera(GraphCameraState state) {}

  @override
  String? pick(Offset local) => null;

  @override
  Offset? project(String nodeId) => projections[nodeId];

  @override
  Widget build(BuildContext context) => const SizedBox.expand();

  @override
  void dispose() {}
}
