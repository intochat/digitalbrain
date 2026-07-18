import 'package:flutter/material.dart';
import 'package:rfw/rfw.dart';

LocalWidgetLibrary createEventWidgets() {
  return LocalWidgetLibrary(<String, LocalWidgetBuilder>{
    'EventCard': (BuildContext context, DataSource source) {
      final title = source.v<String>(['title']) ?? '';
      final dateLabel = source.v<String>(['dateLabel']) ?? '';
      final venueName = source.v<String>(['venueName']) ?? '';
      final category = source.v<String>(['category']) ?? '';
      final ticketSummary = source.v<String>(['ticketSummary']) ?? '';
      final description = source.v<String>(['description']) ?? '';
      final onSelect =
          source.handler(['onSelect'], (HandlerTrigger trigger) => trigger);
      return _EventCard(
        title: title,
        dateLabel: dateLabel,
        venueName: venueName,
        category: category,
        ticketSummary: ticketSummary,
        description: description,
        onSelect: onSelect,
      );
    },
    'EventSkipButton': (BuildContext context, DataSource source) {
      final onSkip =
          source.handler(['onSkip'], (HandlerTrigger trigger) => trigger);
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: 8, horizontal: 12),
        child: Align(
          alignment: Alignment.centerRight,
          child: TextButton(
            onPressed: onSkip,
            child: const Text('Skip events'),
          ),
        ),
      );
    },
  });
}

class _EventCard extends StatelessWidget {
  const _EventCard({
    required this.title,
    required this.dateLabel,
    required this.venueName,
    required this.category,
    required this.ticketSummary,
    required this.description,
    this.onSelect,
  });

  final String title;
  final String dateLabel;
  final String venueName;
  final String category;
  final String ticketSummary;
  final String description;
  final VoidCallback? onSelect;

  IconData _iconForCategory() {
    switch (category.toLowerCase()) {
      case 'music':
        return Icons.music_note;
      case 'exhibit':
        return Icons.palette;
      case 'sports':
        return Icons.sports_baseball;
      case 'food':
        return Icons.restaurant;
      case 'culture':
        return Icons.temple_buddhist;
      case 'outdoors':
        return Icons.terrain;
      default:
        return Icons.event;
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
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
                Icon(_iconForCategory(),
                    size: 18, color: theme.colorScheme.primary),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    title,
                    style: TextStyle(
                      color: theme.colorScheme.onSurface,
                      fontWeight: FontWeight.w600,
                      fontSize: 14,
                    ),
                  ),
                ),
                Text(
                  ticketSummary,
                  style: TextStyle(
                    color: theme.colorScheme.primary,
                    fontWeight: FontWeight.bold,
                    fontSize: 13,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 6),
            Row(
              children: [
                Icon(Icons.calendar_today,
                    size: 13,
                    color: theme.colorScheme.onSurface.withAlpha(150)),
                const SizedBox(width: 4),
                Text(
                  dateLabel,
                  style: TextStyle(
                    color: theme.colorScheme.onSurface.withAlpha(180),
                    fontSize: 12,
                  ),
                ),
                const SizedBox(width: 12),
                Icon(Icons.place,
                    size: 13,
                    color: theme.colorScheme.onSurface.withAlpha(150)),
                const SizedBox(width: 4),
                Expanded(
                  child: Text(
                    venueName,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      color: theme.colorScheme.onSurface.withAlpha(180),
                      fontSize: 12,
                    ),
                  ),
                ),
              ],
            ),
            if (description.isNotEmpty) ...[
              const SizedBox(height: 8),
              Text(
                description,
                style: TextStyle(
                  color: theme.colorScheme.onSurface.withAlpha(170),
                  fontSize: 12,
                ),
              ),
            ],
            if (onSelect != null) ...[
              const SizedBox(height: 10),
              FilledButton(
                onPressed: onSelect,
                child: const Text('Add to trip'),
              ),
            ],
          ],
        ),
      ),
    );
  }
}
