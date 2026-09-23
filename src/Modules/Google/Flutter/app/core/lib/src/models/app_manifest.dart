/// One installed or first-party app, as the launcher lists it.
class AppManifestSummary {
  const AppManifestSummary({
    required this.id,
    required this.name,
    required this.description,
    required this.kind,
    this.uiEntry,
  });

  factory AppManifestSummary.fromJson(Map<String, dynamic> json) =>
      AppManifestSummary(
        id: json['id'] as String,
        name: json['name'] as String,
        description: json['description'] as String? ?? '',
        kind: json['kind'] as String? ?? 'declarative',
        uiEntry: json['uiEntry'] as String?,
      );

  final String id;
  final String name;
  final String description;
  final String kind;
  final String? uiEntry;
}
