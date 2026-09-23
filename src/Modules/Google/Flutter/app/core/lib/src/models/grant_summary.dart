/// One access grant an app holds over an owner's field, as My Data lists it.
class GrantSummary {
  const GrantSummary({
    required this.appId,
    required this.semanticTypeId,
    required this.mode,
    required this.grantedAt,
  });

  factory GrantSummary.fromJson(Map<String, dynamic> json) => GrantSummary(
    appId: json['appId'] as String? ?? '',
    semanticTypeId: json['semanticTypeId'] as String? ?? '',
    mode: _mode(json['mode']),
    grantedAt: DateTime.tryParse(json['grantedAt'] as String? ?? '') ?? DateTime(0),
  );

  /// The server sends the mode as a number (`GrantMode`); accept a name too, for tolerating a
  /// string-enum serializer.
  static String _mode(Object? value) => switch (value) {
    String text => text,
    0 => 'Once',
    1 => 'ThisChat',
    2 => 'Always',
    _ => 'Always',
  };

  final String appId;
  final String semanticTypeId;

  /// `Once`, `ThisChat` or `Always`.
  final String mode;
  final DateTime grantedAt;
}