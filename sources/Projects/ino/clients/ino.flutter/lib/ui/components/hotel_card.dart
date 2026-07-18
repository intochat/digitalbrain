import 'package:flutter/material.dart';
import 'package:rfw/rfw.dart';

LocalWidgetLibrary createHotelWidgets() {
  return LocalWidgetLibrary(<String, LocalWidgetBuilder>{
    'HotelCard': (BuildContext context, DataSource source) {
      final name = source.v<String>(['name']) ?? '';
      final location = source.v<String>(['location']) ?? '';
      final price = source.v<int>(['price']) ?? 0;
      final rating = source.v<double>(['rating']) ?? 0.0;
      final stars = source.v<int>(['stars']) ?? 0;
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
                  Icon(Icons.hotel, size: 18, color: theme.colorScheme.primary),
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
                  Text(
                    '\$$price/night',
                    style: TextStyle(
                      color: theme.colorScheme.primary,
                      fontWeight: FontWeight.bold,
                      fontSize: 16,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 8),
              Row(
                children: [
                  Icon(
                    Icons.location_on,
                    size: 14,
                    color: theme.colorScheme.onSurface.withAlpha(150),
                  ),
                  const SizedBox(width: 4),
                  Expanded(
                    child: Text(
                      location,
                      style: TextStyle(
                        color: theme.colorScheme.onSurface.withAlpha(150),
                        fontSize: 13,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 8),
              Row(
                children: [
                  Row(
                    mainAxisSize: MainAxisSize.min,
                    children: List.generate(
                      5,
                      (i) => Icon(
                        i < stars ? Icons.star : Icons.star_border,
                        size: 16,
                        color: i < stars
                            ? Colors.amber
                            : theme.colorScheme.onSurface.withAlpha(80),
                      ),
                    ),
                  ),
                  const SizedBox(width: 8),
                  Text(
                    rating.toStringAsFixed(1),
                    style: TextStyle(
                      color: theme.colorScheme.onSurface,
                      fontSize: 13,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                ],
              ),
              if (onSelect != null) ...[
                const SizedBox(height: 10),
                SizedBox(
                  width: double.infinity,
                  child: FilledButton(
                    onPressed: onSelect,
                    child: const Text('Select'),
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
