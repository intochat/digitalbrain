class InstalledAppSummary {
  const InstalledAppSummary({
    required this.id,
    required this.title,
    required this.description,
    required this.operations,
  });
  final String id, title, description;
  final List<String> operations;
  factory InstalledAppSummary.fromJson(Map<String, dynamic> json) =>
      InstalledAppSummary(
        id: json['id'] as String,
        title: json['title'] as String,
        description: json['description'] as String? ?? '',
        operations: [
          for (final operation in (json['app'] as Map)['operations'] as List)
            (operation as Map)['name'] as String,
        ],
      );
}
