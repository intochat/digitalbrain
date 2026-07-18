import 'package:flutter/material.dart';
import 'package:rfw/rfw.dart';

LocalWidgetLibrary createWeatherWidgets() {
  return LocalWidgetLibrary(<String, LocalWidgetBuilder>{
    'WeatherSummaryCard': (BuildContext context, DataSource source) {
      final destination = source.v<String>(['destination']) ?? '';
      final month = source.v<String>(['month']) ?? '';
      final season = source.v<String>(['season']) ?? '';
      final avgTempC = source.v<int>(['avgTempC']) ?? 0;
      final rainProbability = source.v<double>(['rainProbability']) ?? 0.0;
      return _WeatherSummaryCard(
        destination: destination,
        month: month,
        season: season,
        avgTempC: avgTempC,
        rainProbability: rainProbability,
      );
    },
  });
}

class _WeatherSummaryCard extends StatelessWidget {
  const _WeatherSummaryCard({
    required this.destination,
    required this.month,
    required this.season,
    required this.avgTempC,
    required this.rainProbability,
  });

  final String destination;
  final String month;
  final String season;
  final int avgTempC;
  final double rainProbability;

  IconData _iconForSeason() {
    switch (season.toLowerCase()) {
      case 'wet':
        return Icons.umbrella;
      case 'dry':
        return Icons.wb_sunny;
      default:
        return Icons.cloud;
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final rainPct = (rainProbability * 100).round();
    final accent = season.toLowerCase() == 'wet'
        ? Colors.blueAccent
        : season.toLowerCase() == 'dry'
            ? Colors.amberAccent
            : Colors.grey;

    return Card(
      color: theme.colorScheme.surface,
      margin: const EdgeInsets.symmetric(vertical: 4, horizontal: 12),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Row(
          children: [
            CircleAvatar(
              backgroundColor: accent.withAlpha(40),
              child: Icon(_iconForSeason(), color: accent, size: 22),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    '$month in $destination',
                    style: TextStyle(
                      color: theme.colorScheme.onSurface,
                      fontWeight: FontWeight.w600,
                      fontSize: 14,
                    ),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    '${season.isEmpty ? 'shoulder' : season} season · ~${avgTempC}°C · $rainPct% rain',
                    style: TextStyle(
                      color: theme.colorScheme.onSurface.withAlpha(180),
                      fontSize: 12,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
