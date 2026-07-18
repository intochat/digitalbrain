import 'package:flutter/material.dart';

// A small bar chart card rendered inline in chat when ino answers a
// self-awareness query ("what are my most used skills", "show me your
// telemetry"). Data is filled from the TelemetryResponse gRPC message and
// the chart stays consistent with the existing dark-surface card palette
// used by skill/timeline cards elsewhere in the UI.
class BarChartEntry {
  final String label;
  final double value;
  const BarChartEntry(this.label, this.value);
}

class BarChartCard extends StatelessWidget {
  final String title;
  final String? subtitle;
  final List<BarChartEntry> entries;

  const BarChartCard({
    super.key,
    required this.title,
    required this.entries,
    this.subtitle,
  });

  @override
  Widget build(BuildContext context) {
    final maxValue = entries.isEmpty
        ? 1.0
        : entries.map((e) => e.value).reduce((a, b) => a > b ? a : b);

    return Card(
      color: const Color(0xFF161b22),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(14),
        side: const BorderSide(color: Color(0xFF21262d)),
      ),
      elevation: 0,
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Row(
              children: [
                const Icon(Icons.insights, size: 14, color: Color(0xFF6C63FF)),
                const SizedBox(width: 6),
                Text(
                  title,
                  style: const TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.w700,
                    color: Color(0xFFe6e6e6),
                  ),
                ),
              ],
            ),
            if (subtitle != null) ...[
              const SizedBox(height: 2),
              Text(
                subtitle!,
                style: const TextStyle(
                  fontSize: 10,
                  color: Color(0xFF8b949e),
                ),
              ),
            ],
            const SizedBox(height: 12),
            if (entries.isEmpty)
              const Text(
                'No data',
                style: TextStyle(fontSize: 11, color: Color(0xFF555555)),
              )
            else
              ...entries.map((e) => _buildBar(e, maxValue)),
          ],
        ),
      ),
    );
  }

  Widget _buildBar(BarChartEntry entry, double maxValue) {
    final fraction = maxValue > 0 ? entry.value / maxValue : 0.0;
    return Padding(
      padding: const EdgeInsets.only(bottom: 6),
      child: Row(
        children: [
          SizedBox(
            width: 80,
            child: Text(
              entry.label,
              style: const TextStyle(fontSize: 10, color: Color(0xFF8b949e)),
              textAlign: TextAlign.right,
              overflow: TextOverflow.ellipsis,
            ),
          ),
          const SizedBox(width: 8),
          Expanded(
            child: SizedBox(
              height: 16,
              child: Stack(
                children: [
                  Container(
                    decoration: BoxDecoration(
                      color: const Color(0xFF0d1117),
                      borderRadius: BorderRadius.circular(4),
                    ),
                  ),
                  FractionallySizedBox(
                    widthFactor: fraction.clamp(0.0, 1.0),
                    child: Container(
                      decoration: BoxDecoration(
                        gradient: const LinearGradient(
                          colors: [Color(0xFF6C63FF), Color(0xFF7B8CFF)],
                        ),
                        borderRadius: BorderRadius.circular(4),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(width: 8),
          SizedBox(
            width: 36,
            child: Text(
              entry.value == entry.value.toInt().toDouble()
                  ? entry.value.toInt().toString()
                  : entry.value.toStringAsFixed(1),
              style: const TextStyle(fontSize: 10, color: Color(0xFF8b949e)),
            ),
          ),
        ],
      ),
    );
  }
}
