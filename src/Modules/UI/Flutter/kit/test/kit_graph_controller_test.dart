import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/widgets.dart' show Offset;
import 'package:flutter_test/flutter_test.dart';

const _nodes = [
  GraphNode(id: 'brain', label: 'BRAIN', kind: GraphNodeKind.hub),
  GraphNode(id: 'chat', label: 'chat'),
  GraphNode(id: 'excel', label: 'excel'),
  GraphNode(id: 'sheet', label: 'budget.xlsx'),
];

const _edges = [
  GraphEdge(id: 'e1', sourceId: 'brain', targetId: 'chat'),
  GraphEdge(id: 'e2', sourceId: 'brain', targetId: 'excel'),
  GraphEdge(id: 'e3', sourceId: 'excel', targetId: 'sheet'),
];

KitGraphController controller() =>
    KitGraphController(nodes: _nodes, edges: _edges);

void main() {
  test('starts with nothing selected and no history', () {
    final c = controller();
    expect(c.selected, isNull);
    expect(c.canGoBack, isFalse);
    expect(c.canGoForward, isFalse);
  });

  test('focus selects and notifies', () {
    final c = controller();
    var notified = 0;
    c.addListener(() => notified++);
    c.focus('excel');
    expect(c.selected, 'excel');
    expect(notified, 1);
  });

  test('back and forward walk the history', () {
    final c = controller()
      ..focus('chat')
      ..focus('excel')
      ..focus('sheet');
    expect(c.selected, 'sheet');

    c.back();
    expect(c.selected, 'excel');
    c.back();
    expect(c.selected, 'chat');
    expect(c.canGoBack, isFalse);

    c.forward();
    expect(c.selected, 'excel');
    expect(c.canGoForward, isTrue);
  });

  test('focusing after going back drops the forward entries', () {
    final c = controller()
      ..focus('chat')
      ..focus('excel')
      ..back();
    expect(c.canGoForward, isTrue);

    c.focus('sheet');
    expect(c.selected, 'sheet');
    expect(c.canGoForward, isFalse);
    c.back();
    expect(c.selected, 'chat');
  });

  test('re-focusing the current node does not grow the history', () {
    final c = controller()..focus('chat');
    var notified = 0;
    c.addListener(() => notified++);
    c.focus('chat');
    expect(notified, 0);
    expect(c.canGoBack, isFalse);
  });

  test('an unknown id is ignored', () {
    final c = controller()..focus('chat');
    c.focus('nope');
    expect(c.selected, 'chat');
  });

  test('neighbours are split by edge direction', () {
    final c = controller();
    final n = c.neighbours('excel');
    expect(n.incoming.map((e) => e.id), ['brain']);
    expect(n.outgoing.map((e) => e.id), ['sheet']);
  });

  test('breadcrumb runs from the hub to the selection', () {
    final c = controller()..focus('sheet');
    expect(c.breadcrumb.map((e) => e.id), ['brain', 'excel', 'sheet']);
  });

  test('breadcrumb is empty with no selection', () {
    expect(controller().breadcrumb, isEmpty);
  });

  test('setGraph clears a selection that no longer exists', () {
    final c = controller()..focus('sheet');
    c.setGraph(
      nodes: const [
        GraphNode(id: 'brain', label: 'BRAIN', kind: GraphNodeKind.hub),
      ],
      edges: const [],
    );
    expect(c.selected, isNull);
    expect(c.canGoBack, isFalse);
  });

  test('projectToScreen defers to the installed projector', () {
    final c = controller();
    expect(c.projectToScreen('chat'), isNull);
    c.projector = (id) => id == 'chat' ? const Offset(12, 34) : null;
    expect(c.projectToScreen('chat'), const Offset(12, 34));
    expect(c.projectToScreen('excel'), isNull);
  });
}
