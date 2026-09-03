import 'dart:math' as math;

import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const hub = GraphNode(id: 'brain', label: 'BRAIN', kind: GraphNodeKind.hub);
  const a = GraphNode(id: 'chat', label: 'chat');
  const b = GraphNode(id: 'excel', label: 'excel');
  const c = GraphNode(id: 'recall', label: 'recall');

  test('hub sits at the origin', () {
    final layout = layoutGraph([hub, a, b]);
    expect(layout['brain'], const GraphPoint(0, 0, 0));
  });

  test('placement is independent of list order', () {
    final one = layoutGraph([hub, a, b, c]);
    final two = layoutGraph([c, b, hub, a]);
    for (final id in ['brain', 'chat', 'excel', 'recall']) {
      expect(one[id], two[id], reason: 'node $id moved when the list reordered');
    }
  });

  test('shell nodes land on the requested radius', () {
    final layout = layoutGraph([hub, a, b, c], radius: 2);
    for (final id in ['chat', 'excel', 'recall']) {
      final p = layout[id]!;
      expect(math.sqrt(p.x * p.x + p.y * p.y + p.z * p.z), closeTo(2, 1e-9));
    }
  });

  test('an explicit position always wins', () {
    const pinned = GraphNode(
      id: 'pinned',
      label: 'pinned',
      position: GraphPoint(0.25, -0.5, 0.75),
    );
    final layout = layoutGraph([hub, pinned]);
    expect(layout['pinned'], const GraphPoint(0.25, -0.5, 0.75));
  });

  test('a single shell node still gets a finite position', () {
    final layout = layoutGraph([hub, a]);
    final p = layout['chat']!;
    expect(p.x.isFinite && p.y.isFinite && p.z.isFinite, isTrue);
  });
}
