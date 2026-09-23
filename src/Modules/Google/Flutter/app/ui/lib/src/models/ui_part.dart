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
      UiVideoPart.kindName => UiVideoPart.fromMetadata(metadata),
      UiWebBrowserPart.kindName => UiWebBrowserPart.fromMetadata(metadata),
      UiExpanderPart.kindName => UiExpanderPart.fromMetadata(metadata),
      UiFormPart.kindName => UiFormPart.fromMetadata(metadata),
      _ => null,
    };
  }
}

final class UiFormField {
  const UiFormField({
    required this.name,
    required this.label,
    required this.kind,
    this.required = false,
    this.choices = const [],
    this.value,
    this.secretSet = false,
    this.supported = true,
  });

  final String name;
  final String label;
  final String kind;
  final bool required;
  final List<String> choices;
  final String? value;
  final bool secretSet;
  final bool supported;

  factory UiFormField.fromJson(Map<String, dynamic> json) {
    return UiFormField(
      name: json['name'] as String? ?? '',
      label: json['label'] as String? ?? '',
      kind: json['kind'] as String? ?? 'PlainText',
      required: json['required'] as bool? ?? false,
      choices: (json['choices'] as List? ?? const [])
          .map((choice) => choice.toString())
          .toList(growable: false),
      value: json['value'] as String?,
      secretSet: json['secretSet'] as bool? ?? false,
      supported: json['supported'] as bool? ?? true,
    );
  }

  Map<String, Object?> toJson() => {
    'name': name,
    'label': label,
    'kind': kind,
    'required': required,
    'choices': choices,
    'value': value,
    'secretSet': secretSet,
    'supported': supported,
  };
}

/// Declarative form read from one [FormState]; rendered field by field, no per-field neuron.
final class UiFormPart extends UiPart {
  const UiFormPart({
    required this.title,
    required this.fields,
    this.submitted = false,
  });

  static const kindName = 'form';

  final String title;
  final List<UiFormField> fields;
  final bool submitted;

  @override
  String get kind => kindName;

  @override
  String get copyText => title;

  factory UiFormPart.fromMetadata(Map<String, dynamic> metadata) {
    final raw = metadata['fields'];
    final fields = raw is List
        ? raw
              .whereType<Map>()
              .map((field) => UiFormField.fromJson(Map<String, dynamic>.from(field)))
              .toList(growable: false)
        : const <UiFormField>[];
    return UiFormPart(
      title: metadata['title'] as String? ?? '',
      fields: fields,
      submitted: metadata['submitted'] as bool? ?? false,
    );
  }

  @override
  Map<String, Object?> toMetadata() => {
    'kind': kindName,
    'title': title,
    'submitted': submitted,
    'fields': [for (final field in fields) field.toJson()],
  };
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
      chartKind:
          metadata['chartKind'] as String? ??
          metadata['kind'] as String? ??
          'bar',
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
    this.children = const [],
  });

  static const kindName = 'card';

  final List<({String kind, String name})> children;
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
      children: (metadata['children'] as List? ?? []).whereType<Map>().map((c) => (kind: c['kind'] as String, name: c['name'] as String)).toList(),
    );
  }

  @override
  Map<String, Object?> toMetadata() => {
    'kind': kindName,
    'title': title,
    'body': body,
    'children': [for (final c in children) {'kind':c.kind,'name':c.name}],
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

final class UiVideoPart extends UiPart {
  const UiVideoPart({
    required this.name,
    required this.url,
    this.playing = false,
    this.position = 0,
    this.duration = 0,
  });

  static const kindName = 'video';

  final String name;
  final String url;
  final bool playing;
  final double position;
  final double duration;

  @override
  String get kind => kindName;

  @override
  String get copyText => url;

  factory UiVideoPart.fromMetadata(Map<String, dynamic> metadata) =>
      UiVideoPart(
        name: metadata['name'] as String? ?? '',
        url: metadata['url'] as String? ?? '',
        playing: metadata['playing'] as bool? ?? false,
        position: (metadata['position'] as num?)?.toDouble() ?? 0,
        duration: (metadata['duration'] as num?)?.toDouble() ?? 0,
      );

  @override
  Map<String, Object?> toMetadata() => {
    'kind': kindName,
    'name': name,
    'url': url,
    'playing': playing,
    'position': position,
    'duration': duration,
  };
}

final class UiWebBrowserPart extends UiPart {
  const UiWebBrowserPart({
    required this.name,
    required this.uri,
    required this.title,
  });

  static const kindName = 'webbrowser';

  final String name;
  final String uri;
  final String title;

  @override
  String get kind => kindName;

  @override
  String get copyText => uri;

  factory UiWebBrowserPart.fromMetadata(Map<String, dynamic> metadata) =>
      UiWebBrowserPart(
        name: metadata['name'] as String? ?? '',
        uri: metadata['uri'] as String? ?? '',
        title: metadata['title'] as String? ?? '',
      );

  @override
  Map<String, Object?> toMetadata() => {
    'kind': kindName,
    'name': name,
    'uri': uri,
    'title': title,
  };
}

final class UiExpanderPart extends UiPart {
  const UiExpanderPart({
    required this.name,
    required this.header,
    required this.expanded,
  });

  static const kindName = 'expander';

  final String name;
  final String header;
  final bool expanded;

  @override
  String get kind => kindName;

  @override
  String get copyText => header;

  factory UiExpanderPart.fromMetadata(Map<String, dynamic> metadata) =>
      UiExpanderPart(
        name: metadata['name'] as String? ?? '',
        header: metadata['header'] as String? ?? '',
        expanded: metadata['expanded'] as bool? ?? false,
      );

  @override
  Map<String, Object?> toMetadata() => {
    'kind': kindName,
    'name': name,
    'header': header,
    'expanded': expanded,
  };
}
