import 'package:flutter/material.dart';
import 'package:rfw/rfw.dart';

LocalWidgetLibrary createActivityWidgets() {
  return LocalWidgetLibrary(<String, LocalWidgetBuilder>{
    'ActivityCard': (BuildContext context, DataSource source) {
      final name = source.v<String>(['name']) ?? '';
      final category = source.v<String>(['category']) ?? '';
      final rating = source.v<double>(['rating']) ?? 0.0;
      final isIndoor = source.v<bool>(['isIndoor']) ?? false;
      final weatherBadge = source.v<String>(['weatherBadge']) ?? '';
      final onSelect =
          source.handler(['onSelect'], (HandlerTrigger trigger) => trigger);
      return _ActivityCard(
        name: name,
        category: category,
        rating: rating,
        isIndoor: isIndoor,
        weatherBadge: weatherBadge,
        onSelect: onSelect,
      );
    },
  });
}

class _ActivityCard extends StatelessWidget {
  const _ActivityCard({
    required this.name,
    required this.category,
    required this.rating,
    required this.isIndoor,
    required this.weatherBadge,
    this.onSelect,
  });

  final String name;
  final String category;
  final double rating;
  final bool isIndoor;
  final String weatherBadge;
  final VoidCallback? onSelect;

  Color _badgeColor(BuildContext context) {
    final theme = Theme.of(context);
    final lower = weatherBadge.toLowerCase();
    if (lower.contains('rain')) return Colors.blueAccent;
    if (lower.contains('sunny') || lower.contains('cool off')) {
      return Colors.amberAccent;
    }
    return theme.colorScheme.primary;
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final badgeColor = _badgeColor(context);
    return Card(
      color: theme.colorScheme.surface,
      margin: const EdgeInsets.symmetric(vertical: 4, horizontal: 12),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Row(
              children: [
                Icon(
                  isIndoor ? Icons.museum : Icons.landscape,
                  size: 18,
                  color: theme.colorScheme.primary,
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    name,
                    style: TextStyle(
                      color: theme.colorScheme.onSurface,
                      fontWeight: FontWeight.w600,
                      fontSize: 14,
                    ),
                  ),
                ),
                Row(
                  children: [
                    const Icon(Icons.star, size: 14, color: Colors.amber),
                    const SizedBox(width: 2),
                    Text(
                      rating.toStringAsFixed(1),
                      style: TextStyle(
                        color: theme.colorScheme.onSurface,
                        fontWeight: FontWeight.w600,
                        fontSize: 13,
                      ),
                    ),
                  ],
                ),
              ],
            ),
            const SizedBox(height: 6),
            Row(
              children: [
                Container(
                  padding: const EdgeInsets.symmetric(
                      horizontal: 8, vertical: 3),
                  decoration: BoxDecoration(
                    color: badgeColor.withAlpha(35),
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: Text(
                    weatherBadge.isEmpty ? 'All weather' : weatherBadge,
                    style: TextStyle(
                      color: badgeColor,
                      fontSize: 11,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                Text(
                  category,
                  style: TextStyle(
                    color: theme.colorScheme.onSurface.withAlpha(170),
                    fontSize: 12,
                  ),
                ),
              ],
            ),
            if (onSelect != null) ...[
              const SizedBox(height: 10),
              FilledButton(
                onPressed: onSelect,
                child: const Text('Pick this activity'),
              ),
            ],
          ],
        ),
      ),
    );
  }
}
