import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

class ComputeLimitsPanel extends StatelessWidget {
  const ComputeLimitsPanel({super.key, required this.client});

  final DigitalBrainUiClient client;

  @override
  Widget build(BuildContext context) => SizedBox(
    width: 360,
    height: 180,
    child: Padding(
      padding: const EdgeInsets.all(24),
      child: FutureBuilder<Map<String, dynamic>>(
        future: client.readComputeLimits(),
        builder: (context, result) {
          if (result.hasError) {
            return Text('Compute status unavailable: ${result.error}');
          }
          if (!result.hasData) {
            return const Center(child: CircularProgressIndicator());
          }
          final limits = result.data!;
          final spent = (limits['spentCompute'] as num?)?.toDouble() ?? 0;
          final limit = (limits['limitCompute'] as num?)?.toDouble() ?? 0;
          final percent = limit > 0 ? spent / limit * 100 : 0.0;
          final reached = limits['hardStopped'] == true || percent >= 100;
          return Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Compute', style: Theme.of(context).textTheme.titleLarge),
              const SizedBox(height: 16),
              Text(
                limit > 0
                    ? '${spent.toStringAsFixed(1)} of ${limit.toStringAsFixed(1)} Compute used'
                    : '${spent.toStringAsFixed(1)} Compute used · No account limit',
              ),
              if (limit > 0) ...[
                const SizedBox(height: 12),
                LinearProgressIndicator(value: (spent / limit).clamp(0.0, 1.0)),
                const SizedBox(height: 8),
                Text(
                  reached
                      ? 'Account limit reached'
                      : percent >= 90
                      ? 'Approaching account limit'
                      : percent >= 75
                      ? '75% of account limit used'
                      : '${percent.toStringAsFixed(0)}% of account limit used',
                ),
              ],
            ],
          );
        },
      ),
    ),
  );
}
