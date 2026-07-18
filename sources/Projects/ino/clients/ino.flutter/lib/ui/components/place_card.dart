import 'package:flutter/material.dart';
import 'package:rfw/rfw.dart';

LocalWidgetLibrary createPlaceWidgets() {
  return LocalWidgetLibrary(<String, LocalWidgetBuilder>{
    'PlaceCard': (BuildContext context, DataSource source) {
      final name = source.v<String>(['name']) ?? '';
      final type = source.v<String>(['type']) ?? '';
      final rating = source.v<double>(['rating']) ?? 0.0;
      final reviewCount = source.v<int>(['reviewCount']) ?? 0;
      final description = source.v<String>(['description']) ?? '';
      final onSelect = source.handler(['onSelect'], (HandlerTrigger trigger) => trigger);
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
                  Icon(Icons.place, size: 18, color: theme.colorScheme.primary),
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
                ],
              ),
              const SizedBox(height: 6),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                decoration: BoxDecoration(
                  color: theme.colorScheme.primary.withAlpha(30),
                  borderRadius: BorderRadius.circular(6),
                ),
                child: Text(
                  type,
                  style: TextStyle(
                    color: theme.colorScheme.primary,
                    fontSize: 12,
                    fontWeight: FontWeight.w500,
                  ),
                ),
              ),
              const SizedBox(height: 8),
              Row(
                children: [
                  Icon(Icons.star, size: 16, color: Colors.amber),
                  const SizedBox(width: 4),
                  Text(
                    rating.toStringAsFixed(1),
                    style: TextStyle(
                      color: theme.colorScheme.onSurface,
                      fontSize: 13,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                  const SizedBox(width: 4),
                  Text(
                    '($reviewCount reviews)',
                    style: TextStyle(
                      color: theme.colorScheme.onSurface.withAlpha(150),
                      fontSize: 12,
                    ),
                  ),
                ],
              ),
              if (description.isNotEmpty) ...[
                const SizedBox(height: 8),
                Text(
                  description,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    color: theme.colorScheme.onSurface.withAlpha(180),
                    fontSize: 13,
                  ),
                ),
              ],
              if (onSelect != null) ...[
                const SizedBox(height: 10),
                SizedBox(
                  width: double.infinity,
                  child: FilledButton(
                    onPressed: onSelect,
                    child: const Text('Add'),
                  ),
                ),
              ],
            ],
          ),
        ),
      );
    },
  });
}
