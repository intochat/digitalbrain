import 'package:flutter/material.dart';

/// The per-intent receipt the assistant appends after a run: what ran, what it touched, and the
/// shadow Compute preview that is never charged.
class ReceiptCard extends StatelessWidget {
  const ReceiptCard({super.key, required this.receipt});

  final Map<String, dynamic> receipt;

  static String computeLabel(Map<String, dynamic> receipt) {
    final compute = (receipt['compute'] as num?)?.toDouble() ?? 0;
    final usd = (receipt['computeUsd'] as num?)?.toDouble() ?? compute / 100;
    final amount = compute == compute.roundToDouble()
        ? compute.toStringAsFixed(0)
        : compute.toString();
    return '$amount Compute (\$${_trimZeros(usd.toStringAsFixed(4))})';
  }

  static String _trimZeros(String value) {
    if (!value.contains('.')) return value;
    final trimmed = value.replaceAll(RegExp(r'0+$'), '').replaceAll(RegExp(r'\.$'), '');
    return trimmed.isEmpty ? '0' : trimmed;
  }

  static List<String> callLabels(Map<String, dynamic> receipt) => [
    for (final call in (receipt['calls'] as List? ?? const []))
      if (call is Map && call['appId'] is String) '${call['appId']}',
  ];

  static List<String> touchedLabels(Map<String, dynamic> receipt) => [
    for (final touched in (receipt['touched'] as List? ?? const []))
      if (touched is Map && touched['source'] is String) _touchedLabel(touched),
  ];

  static String _touchedLabel(Map touched) {
    final rows = (touched['rowsRead'] as num?)?.toInt() ?? 0;
    return rows > 0 ? '${touched['source']} · $rows rows' : '${touched['source']} · read-only';
  }

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final calls = callLabels(receipt);
    final touched = touchedLabels(receipt);
    final shadow = receipt['shadow'] != false;
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      child: Container(
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: colors.surfaceContainerLow,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: colors.outlineVariant),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Row(
              children: [
                Icon(Icons.receipt_long_outlined, size: 18, color: colors.primary),
                const SizedBox(width: 8),
                const Text('Receipt', style: TextStyle(fontWeight: FontWeight.w600)),
              ],
            ),
            if (calls.isNotEmpty) ...[
              const SizedBox(height: 8),
              Text('What ran · ${calls.join(', ')}', style: TextStyle(fontSize: 12, color: colors.onSurfaceVariant)),
            ],
            if (touched.isNotEmpty) ...[
              const SizedBox(height: 4),
              Text('What it touched · ${touched.join(', ')}', style: TextStyle(fontSize: 12, color: colors.onSurfaceVariant)),
            ],
            const SizedBox(height: 8),
            Text(
              '${computeLabel(receipt)}${shadow ? ', preview price, not charged' : ''}',
              style: TextStyle(fontSize: 12, fontWeight: FontWeight.w500, color: colors.onSurface),
            ),
          ],
        ),
      ),
    );
  }
}
