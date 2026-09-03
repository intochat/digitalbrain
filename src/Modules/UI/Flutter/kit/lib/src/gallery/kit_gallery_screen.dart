import 'package:flutter/material.dart';

import '../components/button/kit_button.dart';
import '../components/card/kit_card.dart';
import '../components/chart/kit_chart.dart';
import '../components/graph/graph_models.dart';
import '../components/graph/kit_graph.dart';
import '../components/graph/kit_graph_controller.dart';
import '../components/graph/kit_graph_navigator.dart';
import '../components/graph/kit_graph_view.dart';
import '../components/sheet/kit_sheet.dart';
import '../components/view/kit_view.dart';
import '../models/kit_part.dart';
import '../theme/kit_theme.dart';

/// Offline gallery of kit components (no backend).
final class KitGalleryScreen extends StatelessWidget {
  const KitGalleryScreen({super.key, this.onButtonPressed});

  final ValueChanged<KitButtonPart>? onButtonPressed;

  static const _demoChart = KitChartPart(
    title: 'Weekly throughput',
    points: [
      KitChartPoint(label: 'Mon', value: 42),
      KitChartPoint(label: 'Tue', value: 68),
      KitChartPoint(label: 'Wed', value: 51),
      KitChartPoint(label: 'Thu', value: 89),
      KitChartPoint(label: 'Fri', value: 74),
    ],
  );

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      key: const Key('kit_gallery_screen'),
      color: KitPalette.surface,
      child: Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 1100),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(32, 28, 32, 48),
            children: [
              const Text('UI Kit', style: KitType.heading),
              const SizedBox(height: 8),
              const Text(
                'Components shared by surfaces and chat CustomMessage bubbles.',
                style: KitType.bodyMuted,
              ),
              const SizedBox(height: 28),
              const Text('View · calculator', style: KitType.title),
              const SizedBox(height: 12),
              const KitView(kind: 'calculator', display: '42', phase: 'result'),
              const SizedBox(height: 28),
              const Text('Button', style: KitType.title),
              const SizedBox(height: 12),
              KitButton(
                part: const KitButtonPart(
                  buttonId: 'publish-summary',
                  label: 'Publish summary',
                  action: 'publish-summary',
                  offerCommandId: 'demo',
                ),
                onPressed: onButtonPressed,
              ),
              const SizedBox(height: 28),
              const Text('Chart', style: KitType.title),
              const SizedBox(height: 12),
              const KitChart(part: _demoChart),
              const SizedBox(height: 28),
              const Text('Graph', style: KitType.title),
              const SizedBox(height: 12),
              const SizedBox(
                height: 320,
                child: KitGraph(
                  nodes: [
                    GraphNode(
                      id: 'feed',
                      label: 'Feed',
                      kind: GraphNodeKind.hub,
                    ),
                    GraphNode(id: 'relay', label: 'relay', dimmed: true),
                    GraphNode(id: 'chart', label: 'chart'),
                  ],
                  edges: [
                    GraphEdge(
                      id: 'feed-to-relay',
                      sourceId: 'feed',
                      targetId: 'relay',
                    ),
                    GraphEdge(
                      id: 'relay-to-chart',
                      sourceId: 'relay',
                      targetId: 'chart',
                      decorated: true,
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 28),
              const Text('Graph (3D)', style: KitType.title),
              const SizedBox(height: 12),
              const _Graph3DSection(),
              const SizedBox(height: 28),
              const Text('Sheet', style: KitType.title),
              const SizedBox(height: 12),
              const KitSheet(
                part: KitSheetPart(
                  title: 'Yesterday',
                  sheetName: 'Sheet1',
                  columns: ['Item', 'Qty'],
                  rows: [
                    ['Shoes', '2'],
                    ['Laces', '1'],
                  ],
                ),
              ),
              const SizedBox(height: 28),
              const Text('Card', style: KitType.title),
              const SizedBox(height: 12),
              const KitCard(
                part: KitCardPart(
                  title: 'Sales summary',
                  body: 'Last week closed above plan.',
                  fields: [
                    (label: 'Revenue', value: '\$128k'),
                    (label: 'Delta', value: '+12%'),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Live 3D graph for the gallery. Stateful so the controller -- and with it the
/// camera and navigation history -- survives rebuilds.
final class _Graph3DSection extends StatefulWidget {
  const _Graph3DSection();

  @override
  State<_Graph3DSection> createState() => _Graph3DSectionState();
}

final class _Graph3DSectionState extends State<_Graph3DSection> {
  late final KitGraphController _controller = KitGraphController(
    nodes: const [
      GraphNode(id: 'brain', label: 'BRAIN', kind: GraphNodeKind.hub),
      GraphNode(id: 'chat', label: 'CHAT', cluster: 'core'),
      GraphNode(id: 'excel', label: 'EXCEL', cluster: 'modules'),
      GraphNode(id: 'ui', label: 'UI', cluster: 'core'),
      GraphNode(id: 'budget', label: 'budget.xlsx', cluster: 'entities'),
      GraphNode(id: 'revenue', label: 'revenue-chart', cluster: 'entities'),
    ],
    edges: const [
      GraphEdge(id: 'brain-chat', sourceId: 'brain', targetId: 'chat'),
      GraphEdge(id: 'brain-excel', sourceId: 'brain', targetId: 'excel'),
      GraphEdge(id: 'brain-ui', sourceId: 'brain', targetId: 'ui'),
      GraphEdge(id: 'excel-budget', sourceId: 'excel', targetId: 'budget'),
      GraphEdge(id: 'ui-revenue', sourceId: 'ui', targetId: 'revenue'),
      GraphEdge(id: 'chat-excel', sourceId: 'chat', targetId: 'excel', dotted: true),
    ],
  );

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 380,
      child: DecoratedBox(
        decoration: BoxDecoration(
          color: KitPalette.surfaceSunken,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: KitPalette.line),
        ),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(12),
          child: Column(
            children: [
              Expanded(child: KitGraphView(controller: _controller)),
              KitGraphNavigator(controller: _controller),
            ],
          ),
        ),
      ),
    );
  }
}
