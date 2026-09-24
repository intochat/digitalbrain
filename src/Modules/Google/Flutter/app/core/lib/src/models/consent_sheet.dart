import 'app_manifest.dart';

/// One data class named by the consent sheet.
class ConsentDataType {
  const ConsentDataType({
    required this.semanticTypeId,
    required this.reason,
    this.write = false,
  });

  factory ConsentDataType.fromJson(Map<String, dynamic> json) =>
      ConsentDataType(
        semanticTypeId: json['semanticTypeId'] as String? ?? '',
        reason: json['reason'] as String? ?? '',
        write: json['write'] as bool? ?? false,
      );

  final String semanticTypeId;
  final String reason;
  final bool write;
}

/// The sheet a person sees before installing an app: examples, data types with reasons,
/// permissions, meters and a shadow Compute estimate. Approval is recorded, never charged.
class ConsentSheet {
  const ConsentSheet({
    required this.appId,
    required this.version,
    required this.name,
    required this.descriptionForPeople,
    required this.examples,
    required this.dataTypes,
    required this.meters,
    required this.estimatedCompute,
    required this.approved,
  });

  factory ConsentSheet.fromJson(Map<String, dynamic> json) => ConsentSheet(
    appId: json['appId'] as String? ?? '',
    version: json['version'] as String? ?? '',
    name: json['name'] as String? ?? '',
    descriptionForPeople: json['descriptionForPeople'] as String? ?? '',
    examples: [
      for (final example in (json['examples'] as List? ?? const []))
        example as String,
    ],
    dataTypes: [
      for (final dataType in (json['dataTypes'] as List? ?? const []))
        ConsentDataType.fromJson(Map<String, dynamic>.from(dataType as Map)),
    ],
    meters: [
      for (final meter in (json['meters'] as List? ?? const []))
        AppMeterSummary.fromJson(Map<String, dynamic>.from(meter as Map)),
    ],
    estimatedCompute: (json['estimatedCompute'] as num?)?.toDouble() ?? 0,
    approved: json['approved'] as bool? ?? false,
  );

  final String appId;
  final String version;
  final String name;
  final String descriptionForPeople;
  final List<String> examples;
  final List<ConsentDataType> dataTypes;
  final List<AppMeterSummary> meters;
  final double estimatedCompute;
  final bool approved;
}