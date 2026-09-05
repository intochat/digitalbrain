import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/widgets.dart';

final class FakeGraphScene implements GraphScene, AnimatedGraphScene {
  List<GraphNode> nodes = const [];
  List<GraphEdge> edges = const [];
  GraphPulse? pulse;
  bool disposed = false;

  @override
  Future<void> load(
    List<GraphNode> nodes,
    List<GraphEdge> edges,
    Map<String, GraphPoint> layout,
  ) async {
    this.nodes = nodes;
    this.edges = edges;
  }

  @override
  void applyCamera(GraphCameraState state) {}
  @override
  void advance(double seconds) {}
  @override
  void setPulse(GraphPulse? pulse) => this.pulse = pulse;
  @override
  String? pick(Offset local) => null;
  @override
  Offset? project(String nodeId) => null;
  @override
  Widget build(BuildContext context) =>
      const SizedBox.expand(key: Key('fake_graph_scene'));
  @override
  void dispose() => disposed = true;
}
