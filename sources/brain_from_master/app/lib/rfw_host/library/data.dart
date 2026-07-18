part of 'package:digitalbrain_flutter/rfw_host/digitalbrain_rfw_library.dart';

Widget _timeline(BuildContext c, DataSource s) {
  final n = s.length(['items']);
  final rows = <Widget>[];
  for (var i = 0; i < n; i++) {
    final tone = _tone(_sp(s, ['items', i, 'tone'], 'indigo'));
    final last = i == n - 1;
    final time = _sp(s, ['items', i, 'time']);
    final title = _sp(s, ['items', i, 'title']);
    final desc = _sp(s, ['items', i, 'desc']);
    rows.add(
      IntrinsicHeight(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Column(
              children: [
                Container(
                  width: 11,
                  height: 11,
                  decoration: BoxDecoration(
                    color: tone,
                    shape: BoxShape.circle,
                    boxShadow: [
                      BoxShadow(
                        color: tone.withValues(alpha: 0.5),
                        blurRadius: 6,
                      ),
                    ],
                  ),
                ),
                if (!last)
                  Expanded(
                    child: Container(
                      width: 1.5,
                      color: DigitalBrainColors.hairlineStrong,
                    ),
                  ),
              ],
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Padding(
                padding: EdgeInsets.only(bottom: last ? 0 : 18),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      time,
                      style: GoogleFonts.jetBrainsMono(
                        fontSize: 10,
                        color: DigitalBrainColors.inkLow,
                        letterSpacing: 1.0,
                      ),
                    ),
                    const SizedBox(height: 3),
                    Text(
                      title,
                      style:
                          (Theme.of(c).textTheme.bodyLarge ?? const TextStyle())
                              .copyWith(
                                color: DigitalBrainColors.ink,
                                fontWeight: FontWeight.w600,
                              ),
                    ),
                    if (desc.isNotEmpty) ...[
                      const SizedBox(height: 2),
                      Text(
                        desc,
                        style: GoogleFonts.manrope(
                          fontSize: 13,
                          color: DigitalBrainColors.inkMid,
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
  return Column(mainAxisSize: MainAxisSize.min, children: rows);
}
