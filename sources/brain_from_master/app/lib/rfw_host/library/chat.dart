part of 'package:digitalbrain_flutter/rfw_host/digitalbrain_rfw_library.dart';

Widget _synapseStream(BuildContext c, DataSource s) {
  final cid = _str(s, 'correlationId');
  final h = _d(s, 'height', 40);
  final feed = SynapseStreamScope.maybeOf(c);
  if (feed == null) return SizedBox(height: h);
  return SizedBox(
    height: h,
    child: ListenableBuilder(
      listenable: feed,
      builder: (c, _) {
        final edges = feed.forCorrelation(cid).toList();
        return Row(
          children: [
            for (final e in edges)
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 2),
                child: GlowIcon(
                  spec: GlowIconSpec(
                    seed: e.typeName.hashCode,
                    size: 10,
                    tone: colorForSynapseType(e.typeName),
                    shapeHint: 'orb',
                  ),
                ),
              ),
          ],
        );
      },
    ),
  );
}
