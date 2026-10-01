import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';

import 'workspace_store.dart';

bool isPassiveReceipt(Map<String, dynamic> entry) =>
    entry['role'] == 'receipt' ||
    (entry['role'] == 'ui' &&
        entry['card'] is Map &&
        (entry['card'] as Map)['kind'] == 'charge-receipt');

/// Previous receipts stay on this device; never promote them into billing facts.
List<ComputeUsageItem> savedComputeHistory(WorkspaceProject project) {
  final result = <String, ComputeUsageItem>{};
  for (final conversation in project.conversations) {
    for (final entry in conversation.messages.where(isPassiveReceipt)) {
      final receipt = entry['receipt'] ?? entry['card'];
      if (receipt is! Map) continue;
      final id = entry['id'] as String?;
      if (id == null) continue;
      final charge =
          receipt['kind'] == 'charge-receipt' || receipt['shadow'] == false;
      final itemId =
          receipt['id'] as String? ??
          receipt['intentId'] as String? ??
          (id.endsWith('/receipt') ? id.substring(0, id.length - 8) : id);
      final previous = result[itemId];
      final appSettlement = receipt['kind'] == 'charge-receipt';
      result[itemId] = ComputeUsageItem(
        id: itemId,
        title:
            receipt['summary'] as String? ??
            (charge ? 'App operation' : conversation.title),
        occurredAt:
            DateTime.tryParse(receipt['occurredAt'] as String? ?? '') ??
            previous?.occurredAt,
        outcome: receipt['outcome'] as String? ?? previous?.outcome ?? '',
        previewCompute: charge
            ? previous?.previewCompute
            : ((receipt.containsKey('previewCompute')
                          ? receipt['previewCompute']
                          : receipt['compute'])
                      as num?)
                  ?.toDouble(),
        chargedCompute: charge && !appSettlement
            ? ((receipt['chargedCompute'] ?? receipt['compute']) as num?)
                  ?.toDouble()
            : previous?.chargedCompute,
        settledCompute: appSettlement
            ? (receipt['chargedCompute'] as num?)?.toDouble()
            : previous?.settledCompute,
        calls: receipt['calls'] is List
            ? _maps(receipt['calls'])
            : previous?.calls ?? const [],
        touched: receipt['touched'] is List
            ? _maps(receipt['touched'])
            : previous?.touched ?? const [],
        modelUsage: receipt['modelUsage'] is List
            ? _maps(receipt['modelUsage'])
            : previous?.modelUsage ?? const [],
        localHistory: true,
      );
    }
  }
  return result.values.toList();
}

List<Map<String, dynamic>> _maps(Object? value) => value is List
    ? value.whereType<Map>().map((v) => Map<String, dynamic>.from(v)).toList()
    : const [];
