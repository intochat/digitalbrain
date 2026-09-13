import 'package:digitalbrain_ui/digitalbrain_ui.dart';

/// The `kind: "graph"` tool result as the shell renders it.
final class GraphArtifactData {
  const GraphArtifactData({
    required this.title,
    required this.nodes,
    required this.edges,
  });

  factory GraphArtifactData.fromMap(Map<String, dynamic> data) {
    final nodes = <GraphNode>[
      for (final raw in (data['nodes'] as List? ?? const []))
        if (raw is Map)
          GraphNode(
            id: '${raw['id']}',
            label: '${raw['label'] ?? raw['id']}',
            kind: _kind('${raw['kind'] ?? 'leaf'}'),
            cluster: raw['cluster'] as String?,
          ),
    ];
    final edges = <GraphEdge>[
      for (final raw in (data['edges'] as List? ?? const []))
        if (raw is Map)
          GraphEdge(
            id: '${raw['id'] ?? '${raw['sourceId']}-${raw['targetId']}'}',
            sourceId: '${raw['sourceId']}',
            targetId: '${raw['targetId']}',
            dotted: raw['dotted'] == true,
          ),
    ];
    return GraphArtifactData(
      title: '${data['title'] ?? 'Graph'}',
      nodes: nodes,
      edges: edges,
    );
  }

  final String title;
  final List<GraphNode> nodes;
  final List<GraphEdge> edges;

  static GraphNodeKind _kind(String name) => GraphNodeKind.values.firstWhere(
    (kind) => kind.name == name,
    orElse: () => GraphNodeKind.leaf,
  );
}
