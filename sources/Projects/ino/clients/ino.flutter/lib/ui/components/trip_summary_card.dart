import 'package:flutter/material.dart';
import 'package:rfw/rfw.dart';

LocalWidgetLibrary createSummaryWidgets() {
  return LocalWidgetLibrary(<String, LocalWidgetBuilder>{
    'TripSummaryCard': (BuildContext context, DataSource source) {
      final destination = source.v<String>(['destination']) ?? '';
      final weatherSummary = source.v<String>(['weatherSummary']) ?? '';
      final flight = source.v<String>(['flight']) ?? '';
      final hotel = source.v<String>(['hotel']) ?? '';
      final event = source.v<String>(['event']) ?? '';
      final activity = source.v<String>(['activity']) ?? '';
      return _TripSummaryCard(
        destination: destination,
        weatherSummary: weatherSummary,
        flight: flight,
        hotel: hotel,
        event: event,
        activity: activity,
      );
    },
  });
}

class _TripSummaryCard extends StatelessWidget {
  const _TripSummaryCard({
    required this.destination,
    required this.weatherSummary,
    required this.flight,
    required this.hotel,
    required this.event,
    required this.activity,
  });

  final String destination;
  final String weatherSummary;
  final String flight;
  final String hotel;
  final String event;
  final String activity;

  Widget _row(BuildContext context, IconData icon, String label, String value) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 3),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 16, color: theme.colorScheme.primary),
          const SizedBox(width: 8),
          SizedBox(
            width: 70,
            child: Text(
              label,
              style: TextStyle(
                color: theme.colorScheme.onSurface.withAlpha(160),
                fontSize: 12,
                fontWeight: FontWeight.w500,
              ),
            ),
          ),
          Expanded(
            child: Text(
              value,
              style: TextStyle(
                color: theme.colorScheme.onSurface,
                fontSize: 13,
                fontWeight: FontWeight.w500,
              ),
            ),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      color: theme.colorScheme.surface,
      margin: const EdgeInsets.symmetric(vertical: 6, horizontal: 12),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Row(
              children: [
                Icon(Icons.flight_takeoff,
                    color: theme.colorScheme.primary, size: 22),
                const SizedBox(width: 10),
                Text(
                  'Trip to $destination',
                  style: TextStyle(
                    color: theme.colorScheme.onSurface,
                    fontWeight: FontWeight.w700,
                    fontSize: 16,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 4),
            Padding(
              padding: const EdgeInsets.only(left: 32),
              child: Text(
                weatherSummary,
                style: TextStyle(
                  color: theme.colorScheme.onSurface.withAlpha(170),
                  fontSize: 12,
                ),
              ),
            ),
            const Divider(height: 18),
            _row(context, Icons.flight, 'Flight', flight),
            _row(context, Icons.hotel, 'Hotel', hotel),
            _row(context, Icons.event, 'Event', event),
            _row(context, Icons.landscape, 'Activity', activity),
          ],
        ),
      ),
    );
  }
}
