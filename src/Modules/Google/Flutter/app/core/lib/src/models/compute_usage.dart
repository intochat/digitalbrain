/// Financial amounts are nullable: missing usage is never silently priced at zero.
class ComputeUsageItem {
  const ComputeUsageItem({
    required this.id,
    required this.title,
    this.occurredAt,
    this.outcome = '',
    this.previewCompute,
    this.chargedCompute,
    this.settledCompute,
    this.reservedCompute,
    this.priceBookVersion,
    this.modelUsage = const [],
    this.calls = const [],
    this.touched = const [],
    this.localHistory = false,
  });
  final String id, title, outcome;
  final DateTime? occurredAt;
  final double? previewCompute, chargedCompute, settledCompute, reservedCompute;
  final String? priceBookVersion;
  final List<Map<String, dynamic>> modelUsage, calls, touched;
  final bool localHistory;

  factory ComputeUsageItem.fromJson(Map<String, dynamic> json) =>
      ComputeUsageItem(
        id: json['id'] as String,
        title: json['title'] as String? ?? 'Activity',
        occurredAt: DateTime.tryParse(json['occurredAt'] as String? ?? ''),
        outcome: json['outcome'] as String? ?? '',
        previewCompute: (json['previewCompute'] as num?)?.toDouble(),
        chargedCompute: (json['chargedCompute'] as num?)?.toDouble(),
        settledCompute: (json['settledCompute'] as num?)?.toDouble(),
        reservedCompute: (json['reservedCompute'] as num?)?.toDouble(),
        priceBookVersion: json['priceBookVersion'] as String?,
        modelUsage: _maps(json['modelUsage']),
        calls: _maps(json['calls']),
        touched: _maps(json['touched']),
      );
  static List<Map<String, dynamic>> _maps(Object? value) => value is List
      ? value.whereType<Map>().map((v) => Map<String, dynamic>.from(v)).toList()
      : const [];
}

class ComputeUsagePage {
  const ComputeUsagePage(this.items, this.nextCursor);
  final List<ComputeUsageItem> items;
  final String? nextCursor;
  factory ComputeUsagePage.fromJson(Map<String, dynamic> json) =>
      ComputeUsagePage(
        (json['items'] as List)
            .map(
              (item) => ComputeUsageItem.fromJson(
                Map<String, dynamic>.from(item as Map),
              ),
            )
            .toList(),
        json['nextCursor'] as String?,
      );
}

class ComputeAccountSummary {
  const ComputeAccountSummary({
    this.chargedCompute,
    this.settledCompute,
    this.reservedCompute,
    this.spentCompute,
    this.limitCompute,
    this.hardStopped = false,
  });
  final double? chargedCompute,
      settledCompute,
      reservedCompute,
      spentCompute,
      limitCompute;
  final bool hardStopped;
  factory ComputeAccountSummary.fromJson(Map<String, dynamic> json) =>
      ComputeAccountSummary(
        chargedCompute: (json['chargedCompute'] as num?)?.toDouble(),
        settledCompute: (json['settledCompute'] as num?)?.toDouble(),
        reservedCompute: (json['reservedCompute'] as num?)?.toDouble(),
        spentCompute: (json['spentCompute'] as num?)?.toDouble(),
        limitCompute: (json['limitCompute'] as num?)?.toDouble(),
        hardStopped: json['hardStopped'] == true,
      );
}
