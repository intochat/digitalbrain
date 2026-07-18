import 'package:digitalbrain_flutter/digital_brain_ui/digital_brain_ui.dart';
import 'package:flutter/material.dart';

import 'brain_painter.dart';
import 'cluster_layout.dart';
import 'domain_palette.dart';

class NeuronLabels extends StatelessWidget {
  const NeuronLabels({
    super.key,
    required this.projection,
    required this.nodes,
  });
  final Projection projection;
  final List<GraphNode> nodes;

  static const double _iconSize = 14;

  @override
  Widget build(BuildContext context) {
    final children = <Widget>[];
    for (final n in nodes) {
      final proj = projection.project(n.position);
      // LOD: hide back-of-sphere and tiny labels.
      if (proj.depth > 80) continue;
      final scale = (Projection.focal / (Projection.focal + proj.depth)).clamp(
        0.0,
        2.0,
      );
      final nodeRadiusPx = 10.0 * scale * projection.zoom;
      if (nodeRadiusPx < 4) continue;

      final label = shortNeuronLabel(n.id);
      final tone = styleForDomain(n.domain).color;
      final color = tone.withValues(alpha: 0.80);
      children.add(
        Positioned(
          left: proj.screen.dx - 50,
          top: proj.screen.dy + nodeRadiusPx + 2,
          width: 100,
          child: IgnorePointer(
            child: Row(
              mainAxisAlignment: MainAxisAlignment.center,
              mainAxisSize: MainAxisSize.min,
              children: [
                GlowIcon(
                  spec: GlowIconSpec(
                    seed: n.id.hashCode,
                    size: _iconSize,
                    tone: tone,
                    shapeHint: 'orb',
                  ),
                ),
                const SizedBox(width: 3),
                Flexible(
                  child: Text(
                    label,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      color: color,
                      fontSize: 9.5,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      );
    }
    return Stack(children: children);
  }
}
