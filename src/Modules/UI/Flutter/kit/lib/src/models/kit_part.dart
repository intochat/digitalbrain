/// Wire shape for kit components embedded in chat [CustomMessage.metadata]
/// or composed on surfaces. Kinds align with C# contracts (Button, Chart, …).
sealed class KitPart {
  const KitPart();

  String get kind;

  Map<String, Object?> toMetadata();

  static KitPart? tryParse(Map<String, dynamic>? metadata) {
    if (metadata == null) {
      return null;
    }
    final kind = metadata['kind'] as String?;
    return switch (kind) {
      KitButtonPart.kindName => KitButtonPart.fromMetadata(metadata),
      KitChartPart.kindName => KitChartPart.fromMetadata(metadata),
      KitCardPart.kindName => KitCardPart.fromMetadata(metadata),
      _ => null,
    };
  }
}

final class KitButtonPart extends KitPart {
  const KitButtonPart({
    required this.buttonId,
    required this.label,
    required this.action,
    this.offerCommandId,
  });

  static const kindName = 'button';

  final String buttonId;
  final String label;
  final String action;
  final String? offerCommandId;

  @override
  String get kind => kindName;

  factory KitButtonPart.fromMetadata(Map<String, dynamic> metadata) {
    return KitButtonPart(
      buttonId: metadata['buttonId'] as String? ?? '',
      label: metadata['label'] as String? ?? 'Action',
      action: metadata['action'] as String? ?? '',
      offerCommandId: metadata['offerCommandId'] as String?,
    );
  }

  @override
  Map<String, Object?> toMetadata() => {
        'kind': kindName,
        'buttonId': buttonId,
        'label': label,
        'action': action,
        if (offerCommandId != null) 'offerCommandId': offerCommandId,
      };
}

final class KitChartPoint {
  const KitChartPoint({required this.label, required this.value});

  final String label;
  final num value;

  factory KitChartPoint.fromJson(Map<String, dynamic> json) {
    return KitChartPoint(
      label: json['label'] as String? ?? '',
      value: json['value'] as num? ?? 0,
    );
  }

  Map<String, Object?> toJson() => {'label': label, 'value': value};
}

final class KitChartPart extends KitPart {
  const KitChartPart({
    required this.title,
    required this.points,
    this.chartKind = 'bar',
  });

  static const kindName = 'chart';

  final String title;
  final List<KitChartPoint> points;
  final String chartKind;

  @override
  String get kind => kindName;

  factory KitChartPart.fromMetadata(Map<String, dynamic> metadata) {
    final raw = metadata['points'];
    final points = raw is List
        ? raw
            .whereType<Map>()
            .map((e) => KitChartPoint.fromJson(Map<String, dynamic>.from(e)))
            .toList(growable: false)
        : const <KitChartPoint>[];
    return KitChartPart(
      title: metadata['title'] as String? ?? 'Chart',
      points: points,
      chartKind: metadata['chartKind'] as String? ?? 'bar',
    );
  }

  @override
  Map<String, Object?> toMetadata() => {
        'kind': kindName,
        'title': title,
        'chartKind': chartKind,
        'points': [for (final p in points) p.toJson()],
      };
}

final class KitCardPart extends KitPart {
  const KitCardPart({
    required this.title,
    required this.body,
    this.fields = const [],
  });

  static const kindName = 'card';

  final String title;
  final String body;
  final List<({String label, String value})> fields;

  @override
  String get kind => kindName;

  factory KitCardPart.fromMetadata(Map<String, dynamic> metadata) {
    final raw = metadata['fields'];
    final fields = raw is List
        ? raw
            .whereType<Map>()
            .map(
              (e) => (
                label: e['label'] as String? ?? '',
                value: e['value'] as String? ?? '',
              ),
            )
            .toList(growable: false)
        : const <({String label, String value})>[];
    return KitCardPart(
      title: metadata['title'] as String? ?? '',
      body: metadata['body'] as String? ?? '',
      fields: fields,
    );
  }

  @override
  Map<String, Object?> toMetadata() => {
        'kind': kindName,
        'title': title,
        'body': body,
        'fields': [
          for (final f in fields) {'label': f.label, 'value': f.value},
        ],
      };
}
