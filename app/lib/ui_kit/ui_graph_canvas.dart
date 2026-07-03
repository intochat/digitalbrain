import 'dart:math' as math;

import 'package:flutter/widgets.dart';
import 'package:forui/forui.dart';

class UiKitGraphCanvas extends StatelessWidget {
  const UiKitGraphCanvas._({
    required String title,
    required List<_GraphNodeData> nodes,
    required List<_GraphEdgeData> edges,
    String layout = 'force',
    String? summary,
  }) : _title = title,
       _nodes = nodes,
       _edges = edges,
       _layout = layout,
       _summary = summary;

  factory UiKitGraphCanvas.fromProps(Map<String, Object?> props) {
    final nodes = (props['nodes'] as List? ?? const [])
        .map(_GraphNodeData.parse)
        .whereType<_GraphNodeData>()
        .toList();
    final edges = (props['edges'] as List? ?? const [])
        .map(_GraphEdgeData.parse)
        .whereType<_GraphEdgeData>()
        .toList();
    return UiKitGraphCanvas._(
      title: (props['title'] ?? 'Graph').toString(),
      layout: (props['layout'] ?? 'force').toString(),
      summary: props['summary']?.toString(),
      nodes: nodes,
      edges: edges,
    );
  }

  final String _title;
  final String _layout;
  final String? _summary;
  final List<_GraphNodeData> _nodes;
  final List<_GraphEdgeData> _edges;

  @override
  Widget build(BuildContext context) {
    final theme = FTheme.of(context);
    if (_nodes.isEmpty) {
      return Text(_title, style: theme.typography.sm);
    }

    return Semantics(
      identifier: 'graph-canvas',
      container: true,
      child: LayoutBuilder(
        builder: (context, constraints) {
          final graph = _GraphLayout.compute(_nodes, _layout, constraints);
          final viewportHeight = math.min(
            math.max(graph.size.height, 220.0),
            520.0,
          );

          return SizedBox(
            height: viewportHeight,
            child: ClipRect(
              child: InteractiveViewer(
                constrained: false,
                boundaryMargin: const EdgeInsets.all(160),
                minScale: 0.45,
                maxScale: 2.4,
                child: SizedBox(
                  width: graph.size.width,
                  height: graph.size.height,
                  child: Stack(
                    clipBehavior: Clip.none,
                    children: [
                      Positioned.fill(
                        child: CustomPaint(
                          painter: _GraphEdgePainter(
                            edges: _edges,
                            rects: graph.rects,
                            color: theme.colors.border.withValues(alpha: 0.85),
                          ),
                        ),
                      ),
                      Positioned(
                        left: 24,
                        top: 16,
                        right: 24,
                        child: _GraphHeader(title: _title, summary: _summary),
                      ),
                      for (final edge in _edges)
                        if (edge.label.isNotEmpty &&
                            graph.rects.containsKey(edge.from) &&
                            graph.rects.containsKey(edge.to))
                          _EdgeLabel(
                            edge: edge,
                            from: graph.rects[edge.from]!,
                            to: graph.rects[edge.to]!,
                          ),
                      for (final node in _nodes)
                        Positioned.fromRect(
                          rect: graph.rects[node.id]!,
                          child: _GraphNodeCard(node: node),
                        ),
                    ],
                  ),
                ),
              ),
            ),
          );
        },
      ),
    );
  }
}

class _GraphHeader extends StatelessWidget {
  const _GraphHeader({required this.title, this.summary});

  final String title;
  final String? summary;

  @override
  Widget build(BuildContext context) {
    final theme = FTheme.of(context);
    return Row(
      children: [
        Expanded(
          child: Text(
            title,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: theme.typography.sm.copyWith(fontWeight: FontWeight.w700),
          ),
        ),
        if (summary != null && summary!.isNotEmpty) ...[
          const SizedBox(width: 12),
          Flexible(
            child: Text(
              summary!,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              textAlign: TextAlign.right,
              style: theme.typography.xs.copyWith(
                color: theme.colors.mutedForeground,
              ),
            ),
          ),
        ],
      ],
    );
  }
}

class _GraphNodeCard extends StatelessWidget {
  const _GraphNodeCard({required this.node});

  final _GraphNodeData node;

  @override
  Widget build(BuildContext context) {
    final theme = FTheme.of(context);
    final visibleFields = node.fields.take(7).toList();
    final hiddenCount = node.fields.length - visibleFields.length;

    return Container(
      padding: const EdgeInsets.all(10),
      decoration: BoxDecoration(
        color: theme.colors.card,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: theme.colors.border, width: 0.8),
        boxShadow: [
          BoxShadow(
            color: const Color(0xFF000000).withValues(alpha: 0.08),
            blurRadius: 10,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  node.label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.typography.sm.copyWith(
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
              if (node.kind.isNotEmpty) _SmallBadge(text: node.kind),
            ],
          ),
          const SizedBox(height: 8),
          for (final field in visibleFields) _FieldRow(field: field),
          if (hiddenCount > 0)
            Text(
              '+ $hiddenCount more',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: theme.typography.xs.copyWith(
                color: theme.colors.mutedForeground,
              ),
            ),
        ],
      ),
    );
  }
}

class _FieldRow extends StatelessWidget {
  const _FieldRow({required this.field});

  final _GraphFieldData field;

  @override
  Widget build(BuildContext context) {
    final theme = FTheme.of(context);
    return SizedBox(
      height: 22,
      child: Row(
        children: [
          Expanded(
            flex: 5,
            child: Text(
              field.name,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: theme.typography.xs.copyWith(
                fontWeight: field.key ? FontWeight.w700 : FontWeight.w400,
              ),
            ),
          ),
          if (field.type.isNotEmpty)
            Expanded(
              flex: 3,
              child: Text(
                field.type,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                textAlign: TextAlign.right,
                style: theme.typography.xs.copyWith(
                  color: theme.colors.mutedForeground,
                ),
              ),
            ),
          if (field.badge.isNotEmpty) ...[
            const SizedBox(width: 6),
            _SmallBadge(text: field.badge),
          ],
        ],
      ),
    );
  }
}

class _SmallBadge extends StatelessWidget {
  const _SmallBadge({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    final theme = FTheme.of(context);
    return Container(
      constraints: const BoxConstraints(maxWidth: 82),
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(
        color: theme.colors.primary.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(6),
        border: Border.all(
          color: theme.colors.primary.withValues(alpha: 0.20),
          width: 0.6,
        ),
      ),
      child: Text(
        text,
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
        style: theme.typography.xs.copyWith(color: theme.colors.primary),
      ),
    );
  }
}

class _EdgeLabel extends StatelessWidget {
  const _EdgeLabel({required this.edge, required this.from, required this.to});

  final _GraphEdgeData edge;
  final Rect from;
  final Rect to;

  @override
  Widget build(BuildContext context) {
    final theme = FTheme.of(context);
    final mid = Offset(
      (from.center.dx + to.center.dx) / 2,
      (from.center.dy + to.center.dy) / 2,
    );
    return Positioned(
      left: mid.dx - 76,
      top: mid.dy - 12,
      width: 152,
      child: Center(
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 3),
          decoration: BoxDecoration(
            color: theme.colors.background.withValues(alpha: 0.92),
            borderRadius: BorderRadius.circular(6),
            border: Border.all(color: theme.colors.border, width: 0.6),
          ),
          child: Text(
            edge.label,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: theme.typography.xs.copyWith(
              color: theme.colors.mutedForeground,
            ),
          ),
        ),
      ),
    );
  }
}

class _GraphEdgePainter extends CustomPainter {
  _GraphEdgePainter({
    required this.edges,
    required this.rects,
    required this.color,
  });

  final List<_GraphEdgeData> edges;
  final Map<String, Rect> rects;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..strokeWidth = 1.4
      ..style = PaintingStyle.stroke;

    for (final edge in edges) {
      final from = rects[edge.from];
      final to = rects[edge.to];
      if (from == null || to == null) continue;

      final start = _edgePoint(from, to.center);
      final end = _edgePoint(to, from.center);
      canvas.drawLine(start, end, paint);
      _drawArrow(canvas, paint, start, end);
    }
  }

  Offset _edgePoint(Rect rect, Offset toward) {
    final center = rect.center;
    final dx = toward.dx - center.dx;
    final dy = toward.dy - center.dy;
    if (dx.abs() > dy.abs()) {
      return Offset(dx > 0 ? rect.right : rect.left, center.dy);
    }
    return Offset(center.dx, dy > 0 ? rect.bottom : rect.top);
  }

  void _drawArrow(Canvas canvas, Paint paint, Offset start, Offset end) {
    final angle = math.atan2(end.dy - start.dy, end.dx - start.dx);
    const size = 8.0;
    final p1 = Offset(
      end.dx - size * math.cos(angle - math.pi / 6),
      end.dy - size * math.sin(angle - math.pi / 6),
    );
    final p2 = Offset(
      end.dx - size * math.cos(angle + math.pi / 6),
      end.dy - size * math.sin(angle + math.pi / 6),
    );
    final path = Path()
      ..moveTo(end.dx, end.dy)
      ..lineTo(p1.dx, p1.dy)
      ..moveTo(end.dx, end.dy)
      ..lineTo(p2.dx, p2.dy);
    canvas.drawPath(path, paint);
  }

  @override
  bool shouldRepaint(covariant _GraphEdgePainter oldDelegate) =>
      oldDelegate.edges != edges ||
      oldDelegate.rects != rects ||
      oldDelegate.color != color;
}

class _GraphLayout {
  const _GraphLayout({required this.size, required this.rects});

  final Size size;
  final Map<String, Rect> rects;

  static _GraphLayout compute(
    List<_GraphNodeData> nodes,
    String layout,
    BoxConstraints constraints,
  ) {
    const nodeWidth = 242.0;
    const horizontalGap = 70.0;
    const verticalGap = 46.0;
    const padding = 24.0;
    const headerHeight = 64.0;

    final availableWidth = constraints.maxWidth.isFinite
        ? math.max(constraints.maxWidth, 320.0)
        : 680.0;
    final maxColumns = math.max(
      1,
      ((availableWidth - padding * 2 + horizontalGap) /
              (nodeWidth + horizontalGap))
          .floor(),
    );
    final preferredColumns = layout.toLowerCase() == 'schema'
        ? math.min(nodes.length, math.max(1, maxColumns))
        : math.min(nodes.length, nodes.length <= 2 ? 2 : maxColumns);
    final columns = math.max(1, preferredColumns);

    final rects = <String, Rect>{};
    var maxBottom = 0.0;
    for (var i = 0; i < nodes.length; i++) {
      final row = i ~/ columns;
      final col = i % columns;
      final height = _nodeHeight(nodes[i]);
      final left = padding + col * (nodeWidth + horizontalGap);
      final top = headerHeight + row * (height + verticalGap);
      final rect = Rect.fromLTWH(left, top, nodeWidth, height);
      rects[nodes[i].id] = rect;
      maxBottom = math.max(maxBottom, rect.bottom);
    }

    final canvasWidth = math.max(
      availableWidth,
      padding * 2 + columns * nodeWidth + (columns - 1) * horizontalGap,
    );
    final canvasHeight = math.max(220.0, maxBottom + padding);
    return _GraphLayout(size: Size(canvasWidth, canvasHeight), rects: rects);
  }

  static double _nodeHeight(_GraphNodeData node) {
    final visibleFieldCount = math.min(node.fields.length, 7);
    final moreHeight = node.fields.length > visibleFieldCount ? 18.0 : 0.0;
    return 60.0 + visibleFieldCount * 22.0 + moreHeight;
  }
}

class _GraphNodeData {
  const _GraphNodeData({
    required this.id,
    required this.label,
    required this.kind,
    required this.fields,
  });

  static _GraphNodeData? parse(Object? value) {
    final map = _stringMap(value);
    final id = (map['id'] ?? '').toString();
    if (id.isEmpty) return null;
    return _GraphNodeData(
      id: id,
      label: (map['label'] ?? id).toString(),
      kind: (map['kind'] ?? '').toString(),
      fields: (map['fields'] as List? ?? const [])
          .map(_GraphFieldData.parse)
          .whereType<_GraphFieldData>()
          .toList(),
    );
  }

  final String id;
  final String label;
  final String kind;
  final List<_GraphFieldData> fields;
}

class _GraphFieldData {
  const _GraphFieldData({
    required this.name,
    required this.type,
    required this.badge,
    required this.key,
  });

  static _GraphFieldData? parse(Object? value) {
    final map = _stringMap(value);
    final name = (map['name'] ?? '').toString();
    if (name.isEmpty) return null;
    return _GraphFieldData(
      name: name,
      type: (map['type'] ?? '').toString(),
      badge: (map['badge'] ?? '').toString(),
      key: map['key'] == true,
    );
  }

  final String name;
  final String type;
  final String badge;
  final bool key;
}

class _GraphEdgeData {
  const _GraphEdgeData({
    required this.from,
    required this.to,
    required this.label,
  });

  static _GraphEdgeData? parse(Object? value) {
    final map = _stringMap(value);
    final from = (map['from'] ?? '').toString();
    final to = (map['to'] ?? '').toString();
    if (from.isEmpty || to.isEmpty) return null;
    return _GraphEdgeData(
      from: from,
      to: to,
      label: (map['label'] ?? '').toString(),
    );
  }

  final String from;
  final String to;
  final String label;
}

Map<String, Object?> _stringMap(Object? value) {
  if (value is! Map) return const {};
  return Map<String, Object?>.fromEntries(
    value.entries.map((entry) => MapEntry(entry.key.toString(), entry.value)),
  );
}
