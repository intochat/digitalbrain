/// One data class an app reads or writes, and why.
class AppPermissionSummary {
  const AppPermissionSummary({
    required this.semanticTypeId,
    required this.reason,
    this.write = false,
  });

  factory AppPermissionSummary.fromJson(Map<String, dynamic> json) =>
      AppPermissionSummary(
        semanticTypeId: json['semanticTypeId'] as String? ?? '',
        reason: json['reason'] as String? ?? '',
        write: json['write'] as bool? ?? false,
      );

  final String semanticTypeId;
  final String reason;
  final bool write;
}

/// One meter an app declares, with its shadow price in Compute (never charged at consent time).
class AppMeterSummary {
  const AppMeterSummary({
    required this.meterId,
    required this.unit,
    required this.aggregation,
    this.proposedPriceInCompute,
  });

  factory AppMeterSummary.fromJson(Map<String, dynamic> json) =>
      AppMeterSummary(
        meterId: json['meterId'] as String? ?? '',
        unit: json['unit'] as String? ?? '',
        aggregation: json['aggregation'] as String? ?? '',
        proposedPriceInCompute: (json['proposedPriceInCompute'] as num?)
            ?.toDouble(),
      );

  final String meterId;
  final String unit;
  final String aggregation;
  final double? proposedPriceInCompute;
}

/// One installed or first-party app, as the launcher lists it.
class AppManifestSummary {
  const AppManifestSummary({
    required this.id,
    required this.name,
    required this.description,
    required this.kind,
    this.uiEntry,
    this.examples = const [],
    this.permissions = const [],
    this.meters = const [],
  });

  factory AppManifestSummary.fromJson(Map<String, dynamic> json) =>
      AppManifestSummary(
        id: json['id'] as String,
        name: json['name'] as String,
        description: json['description'] as String? ?? '',
        kind: json['kind'] as String? ?? 'declarative',
        uiEntry: json['uiEntry'] as String?,
        examples: [
          for (final example in (json['examplePrompts'] as List? ?? const []))
            example as String,
        ],
        permissions: [
          for (final permission in (json['permissions'] as List? ?? const []))
            AppPermissionSummary.fromJson(
              Map<String, dynamic>.from(permission as Map),
            ),
        ],
        meters: [
          for (final meter in (json['meters'] as List? ?? const []))
            AppMeterSummary.fromJson(Map<String, dynamic>.from(meter as Map)),
        ],
      );

  final String id;
  final String name;
  final String description;
  final String kind;
  final String? uiEntry;
  final List<String> examples;
  final List<AppPermissionSummary> permissions;
  final List<AppMeterSummary> meters;
}