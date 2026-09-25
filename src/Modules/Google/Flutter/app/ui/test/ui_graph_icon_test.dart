import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_ui/src/components/graph/graph_painter.dart';

void main() {
  test('interaction trail travels then fades over fifteen seconds', () {
    final start = DateTime.utc(2026, 9, 25);
    final pulse = GraphPulse(
      fromId: 'a',
      toId: 'b',
      signature: 'call',
      at: start,
    );
    expect(pulse.travelAt(start), 0);
    expect(pulse.travelAt(start.add(const Duration(seconds: 2))), 1);
    expect(
      pulse.opacityAt(start.add(const Duration(seconds: 10))),
      closeTo(1 / 3, .001),
    );
    expect(pulse.opacityAt(start.add(const Duration(seconds: 15))), 0);
  });

  testWidgets('compact graph renders allowlisted neuron icon', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: SizedBox(
          width: 400,
          height: 300,
          child: UiGraph(
            nodes: [
              GraphNode(
                id: 'supabase-table/customers',
                label: 'Customers',
                iconKey: 'supabase',
              ),
            ],
            edges: [],
          ),
        ),
      ),
    );
    expect(find.byType(NeuronIcon), findsOneWidget);
    expect(find.byType(InteractiveViewer), findsOneWidget);
    expect(find.byTooltip('Fit graph'), findsOneWidget);
  });

  testWidgets('compact graph paints concurrent call and source signal', (
    tester,
  ) async {
    final now = DateTime.now().toUtc();
    await tester.pumpWidget(
      MaterialApp(
        home: SizedBox(
          width: 400,
          height: 300,
          child: UiGraph(
            nodes: const [
              GraphNode(id: 'a', label: 'A'),
              GraphNode(id: 'b', label: 'B'),
            ],
            edges: const [GraphEdge(id: 'ab', sourceId: 'a', targetId: 'b')],
            pulses: [
              GraphPulse(fromId: 'a', toId: 'b', signature: 'call', at: now),
              GraphPulse(
                fromId: 'b',
                toId: 'b',
                signature: 'signal',
                at: now,
                outcome: GraphPulseOutcome.signal,
              ),
            ],
          ),
        ),
      ),
    );
    final painter = tester
        .widgetList<CustomPaint>(find.byType(CustomPaint))
        .map((widget) => widget.painter)
        .whereType<GraphPainter>()
        .single;
    expect(painter.pulses.map((pulse) => pulse.signature), ['call', 'signal']);
  });
}
