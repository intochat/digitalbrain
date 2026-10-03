import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

String computeAmount(double? value) {
  if (value == null) return 'Unreported';
  if (value > 0 && value < .0001) return '<0.0001';
  return value.toStringAsFixed(4).replaceFirst(RegExp(r'\.?0+$'), '');
}

String usageTime(DateTime? value) {
  if (value == null) return 'Time unavailable';
  final local = value.toLocal();
  String two(int v) => v.toString().padLeft(2, '0');
  return '${local.year}-${two(local.month)}-${two(local.day)} ${two(local.hour)}:${two(local.minute)}';
}

String usageAmountLabel(ComputeUsageItem item) {
  final labels = <String>[
    if (item.chargedCompute != null &&
        (item.chargedCompute! > 0 || item.previewCompute == null))
      '${computeAmount(item.chargedCompute)} Charged',
    if (item.reservedCompute != null && item.reservedCompute! > 0)
      '${computeAmount(item.reservedCompute)} Reserved',
    if (item.settledCompute != null && item.settledCompute! > 0)
      '${computeAmount(item.settledCompute)} App settled',
    if (item.previewCompute != null)
      '${computeAmount(item.previewCompute)} Preview',
  ];
  return labels.isEmpty ? 'Unreported' : labels.join('\n');
}

class ComputeUsageDetails extends StatelessWidget {
  const ComputeUsageDetails({super.key, required this.item});
  final ComputeUsageItem item;
  @override
  Widget build(BuildContext context) {
    final labelStyle = Theme.of(context).textTheme.titleSmall;
    Widget section(String title, List<Widget> children) => Padding(
      padding: const EdgeInsets.only(bottom: 20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title, style: labelStyle),
          const SizedBox(height: 8),
          ...children,
        ],
      ),
    );
    return ListView(
      padding: const EdgeInsets.all(20),
      children: [
        SelectableText(
          item.title,
          style: Theme.of(context).textTheme.titleMedium,
        ),
        const SizedBox(height: 6),
        Text(
          '${usageTime(item.occurredAt)}${item.outcome.isEmpty ? '' : ' · ${item.outcome}'}',
        ),
        const SizedBox(height: 20),
        section('Compute', [
          if (item.previewCompute != null)
            Text(
              'Preview · not charged: ${computeAmount(item.previewCompute)} Compute',
            ),
          if (item.settledCompute != null)
            Text(
              'App settlements: ${computeAmount(item.settledCompute)} Compute',
            ),
          if (item.chargedCompute != null)
            Text('Charged: ${computeAmount(item.chargedCompute)} Compute'),
          if (item.reservedCompute != null)
            Text('Reserved: ${computeAmount(item.reservedCompute)} Compute'),
          if (item.previewCompute == null &&
              item.chargedCompute == null &&
              item.reservedCompute == null &&
              item.settledCompute == null)
            const Text('Cost unreported'),
          if (item.priceBookVersion != null)
            Text('Price book ${item.priceBookVersion}'),
          if (item.localHistory)
            const Text('Saved on this device · not included in account totals'),
        ]),
        section('Model resources', [
          if (item.modelUsage.isEmpty)
            const Text('Model usage was not recorded for this activity.'),
          for (final model in item.modelUsage)
            Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    '${model['model'] ?? 'Unknown model'} · ${model['provider'] ?? 'Unknown provider'}',
                    style: labelStyle,
                  ),
                  if (model['usageReported'] == false)
                    const Text('Usage unreported by provider'),
                  for (final entry in const {
                    'inputTokens': 'Input',
                    'cachedInputTokens': 'Cached input',
                    'reasoningTokens': 'Reasoning',
                    'outputTokens': 'Output',
                    'totalTokens': 'Total',
                  }.entries)
                    Text(
                      '${entry.value}: ${model[entry.key] ?? 'Unreported'}${model[entry.key] == null ? '' : ' tokens'}',
                    ),
                ],
              ),
            ),
        ]),
        section('Tools and apps', [
          if (item.calls.isEmpty) const Text('No tool calls recorded.'),
          for (final call in item.calls)
            Text(
              '${call['operation'] ?? call['appId'] ?? 'Operation'} · ${call['succeeded'] == true
                  ? 'Succeeded'
                  : call['succeeded'] == false
                  ? 'Failed'
                  : 'Status unreported'}${call['errorMessage'] == null ? '' : '\n${call['errorMessage']}'}${call['errorCode'] == null ? '' : ' (${call['errorCode']})'}',
            ),
        ]),
        section('Data accessed', [
          if (item.touched.isEmpty) const Text('No data access recorded.'),
          for (final source in item.touched)
            Text(
              '${source['source'] ?? 'Data source'} · ${source['rowsRead'] ?? 'Unreported'} rows${source['readOnly'] == true ? ' · read-only' : ''}',
            ),
        ]),
      ],
    );
  }
}
