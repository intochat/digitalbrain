import 'package:flutter/material.dart';

import 'brain_painter.dart';
import 'cluster_layout.dart';
import 'domain_palette.dart';

class ClusterLabels extends StatelessWidget {
  const ClusterLabels({super.key, required this.projection, required this.nodes});
  final Projection projection;
  final List<GraphNode> nodes;

  @override
  Widget build(BuildContext context) {
    final counts = <String, int>{};
    for (final n in nodes) {
      counts[n.domain] = (counts[n.domain] ?? 0) + 1;
    }

    final children = <Widget>[];
    for (final entry in domainPalette.entries) {
      final count = counts[entry.key] ?? 0;
      if (count == 0) continue;
      final proj = projection.project(entry.value.anchor);
      // Hide back-facing cluster labels so they don't overlap the front side.
      if (proj.depth > 80) continue;
      final opacity = (1.0 - (proj.depth + 250) / 500).clamp(0.3, 1.0);
      children.add(Positioned(
        left: proj.screen.dx - 60,
        top:  proj.screen.dy - 8,
        width: 120,
        child: IgnorePointer(
          child: Center(
            child: Opacity(
              opacity: opacity,
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                decoration: BoxDecoration(
                  color: Colors.black.withValues(alpha: 0.35),
                  borderRadius: BorderRadius.circular(4),
                  border: Border.all(color: entry.value.color.withValues(alpha: 0.5)),
                ),
                child: Text(
                  '${entry.value.shortLabel} · $count',
                  style: TextStyle(
                    color: entry.value.color,
                    fontSize: 11,
                    fontWeight: FontWeight.w600,
                    letterSpacing: 0.5,
                  ),
                ),
              ),
            ),
          ),
        ),
      ));
    }
    return Stack(children: children);
  }
}
