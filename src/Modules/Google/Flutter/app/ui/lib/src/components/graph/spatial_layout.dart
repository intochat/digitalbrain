import 'graph_models.dart';

/// Stable, open-space placement. Every node depends only on its own identity
/// and cluster, so new activity never moves an existing neuron.
Map<String, GraphPoint> spatialLayoutGraph(List<GraphNode> nodes) => {
  for (final node in nodes) node.id: node.position ?? _place(node),
};

GraphPoint _place(GraphNode node) {
  final cluster = _hash(node.cluster ?? node.id);
  final member = _hash(node.id);
  final column = (cluster % 7) - 3;
  final row = ((cluster ~/ 7) % 7) - 3;
  final layer = ((cluster ~/ 49) % 5) - 2;
  return GraphPoint(
    column * 1.8 + (member % 100) / 100 * 0.8,
    row * 1.5 + ((member ~/ 100) % 100) / 100 * 0.8,
    layer * 1.4 + ((member ~/ 10000) % 100) / 100 * 0.8,
  );
}

int _hash(String value) {
  var hash = 0x811c9dc5;
  for (final code in value.codeUnits) {
    hash = ((hash ^ code) * 0x01000193) & 0x7fffffff;
  }
  return hash;
}
