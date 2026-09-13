/// Wire shape for ui components embedded in chat [CustomMessage.metadata]
/// or composed on surfaces. Kinds align with C# contracts (Button, Chart, …).
sealed class UiPart {
  const UiPart();

  String get kind;

  Map<String, Object?> toMetadata();

  /// Plain text for the chat copy control. Empty means no copy affordance.
  String get copyText;

  static UiPart? tryParse(Map<String, dynamic>? metadata) {
    if (metadata == null) {
      return null;
    }
    final kind = metadata['kind'] as String?;
    return switch (kind) {
      UiButtonPart.kindName => UiButtonPart.fromMetadata(metadata),
      UiChartPart.kindName => UiChartPart.fromMetadata(metadata),
      UiCardPart.kindName => UiCardPart.fromMetadata(metadata),
      UiTimerPart.kindName => UiTimerPart.fromMetadata(metadata),
      UiSheetPart.kindName => UiSheetPart.fromMetadata(metadata),
      _ => null,
    };
  }
}

final class UiTimerPart extends UiPart {
  const UiTimerPart({required this.label, required this.dueAt});

  static const kindName = 'timer';

  final String label;
  final DateTime dueAt;

  @override
  String get kind => kindName;

  @override
  String get copyText => '$label · ${dueAt.toUtc().toIso8601String()}';

  factory UiTimerPart.fromMetadata(Map<String, dynamic> metadata) {
    final rawDueAt = metadata['dueAt'] as String?;
    return UiTimerPart(
      label: metadata['label'] as String? ?? 'Timer',
      dueAt: rawDueAt == null
          ? DateTime.now().toUtc()
          : DateTime.parse(rawDueAt).toUtc(),
    );
  }

  @override
  Map<String, Object?> toMetadata() => {
    'kind': kindName,
    'label': label,
    'dueAt': dueAt.toUtc().toIso8601String(),
  };
}

final class UiButtonPart extends UiPart {
  const UiButtonPart({
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

  @override
  String get copyText => label;

  factory UiButtonPart.fromMetadata(Map<String, dynamic> metadata) {
    return UiButtonPart(
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

final class UiChartPoint {
  const UiChartPoint({required this.label, required this.value});

  final String label;
  final num value;

  factory UiChartPoint.fromJson(Map<String, dynamic> json) {
    return UiChartPoint(
      label: json['label'] as String? ?? '',
      value: json['value'] as num? ?? 0,
    );
  }

  Map<String, Object?> toJson() => {'label': label, 'value': value};
}

final class UiChartPart extends UiPart {
  const UiChartPart({
    required this.title,
    required this.points,
    this.chartKind = 'bar',
  });

  static const kindName = 'chart';

  final String title;
  final List<UiChartPoint> points;
  final String chartKind;

  @override
  String get kind => kindName;

  @override
  String get copyText {
    final rows = [for (final point in points) '${point.label}\t${point.value}'];
    return [title, ...rows].join('\n');
  }

  factory UiChartPart.fromMetadata(Map<String, dynamic> metadata) {
    final raw = metadata['points'];
    final points = raw is List
        ? raw
              .whereType<Map>()
              .map((e) => UiChartPoint.fromJson(Map<String, dynamic>.from(e)))
              .toList(growable: false)
        : const <UiChartPoint>[];
    return UiChartPart(
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

final class UiCardPart extends UiPart {
  const UiCardPart({
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

  @override
  String get copyText {
    final lines = <String>[
      if (title.isNotEmpty) title,
      if (body.isNotEmpty) body,
      for (final field in fields) '${field.label}: ${field.value}',
    ];
    return lines.join('\n');
  }

  factory UiCardPart.fromMetadata(Map<String, dynamic> metadata) {
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
    return UiCardPart(
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

final class UiSheetPart extends UiPart {
  const UiSheetPart({
    required this.title,
    required this.columns,
    required this.rows,
    this.sheetName = 'Sheet1',
  });

  static const kindName = 'spreadsheet';

  final String title;
  final String sheetName;
  final List<String> columns;
  final List<List<String>> rows;

  @override
  String get kind => kindName;

  @override
  String get copyText {
    final header = columns.join('\t');
    final body = [for (final row in rows) row.join('\t')];
    return [if (title.isNotEmpty) title, header, ...body].join('\n');
  }

  factory UiSheetPart.fromMetadata(Map<String, dynamic> metadata) {
    final rawColumns = metadata['columns'];
    final columns = rawColumns is List
        ? rawColumns.map((e) => e.toString()).toList(growable: false)
        : const <String>[];
    final rawRows = metadata['rows'];
    final rows = rawRows is List
        ? rawRows
              .map((row) {
                if (row is List) {
                  return row.map((cell) => cell.toString()).toList();
                }
                if (row is Map) {
                  final cells = row['cells'];
                  if (cells is List) {
                    return cells.map((cell) => cell.toString()).toList();
                  }
                }
                return const <String>[];
              })
              .toList(growable: false)
        : const <List<String>>[];
    return UiSheetPart(
      title: metadata['title'] as String? ?? 'Sheet',
      sheetName: metadata['sheetName'] as String? ?? 'Sheet1',
      columns: columns,
      rows: rows,
    );
  }

  @override
  Map<String, Object?> toMetadata() => {
    'kind': kindName,
    'title': title,
    'sheetName': sheetName,
    'columns': columns,
    'rows': [
      for (final row in rows) {'cells': row},
    ],
  };
}
