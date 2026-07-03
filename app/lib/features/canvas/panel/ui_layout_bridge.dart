/// Bridges a kernel `uikit` card — the compiled `.ino` `ui:` surface, shipped as
/// a `{name, arguments, children}` tree (V5-4, UI-is-data) — into RFW source
/// text the existing `digitalbrain` palette renders. No per-neuron Dart: a
/// neuron's surface is data, and this is the one generic translation of that
/// data into the RFW dialect the host already speaks.
///
/// The InoLang serializer (`UiWidgetExpr.SerializeJson`) emits every argument
/// value as a *string*, so this bridge coerces each back to a typed RFW literal:
/// `"true"/"false"` → bool, numeric text → number, everything else → a quoted
/// string. Values already typed (a number the kernel injected for a live
/// reminder duration) pass through. `on*` arguments are event handlers and lower
/// to RFW `event "<name>" { ... }`. A `CountdownClock` is given a `snooze`
/// handler so its panel can re-arm the reminder (the round-trip in
/// `_handlePanelEvent`). UiKit container names map onto the palette's layout
/// primitives; palette and core widget names pass through.
library;

/// Synapse contract fired by the reminder panel's snooze affordance.
const String _snoozeSynapse = 'DigitalBrain.WidgetCanvas.Snooze';

/// Returns RFW source for [layout], or `null` if it is not a renderable
/// `{name, ...}` node.
String? uiLayoutToRfwSource(Map<String, Object?> layout) {
  if (layout['name'] is! String) return null;
  return 'import digitalbrain;\nwidget root = ${_node(layout)};';
}

/// Heuristic: a `uikit` surface is a node whose top level carries a widget
/// `name`. Used by the panel body to decide whether to apply the bridge.
bool looksLikeUiLayout(Map<String, Object?> data) => data['name'] is String;

String _node(Map<String, Object?> node) {
  final rawName = node['name']?.toString() ?? 'Panel';
  final name = _mapName(rawName);
  final args =
      (node['arguments'] as Map?)?.cast<String, Object?>() ??
      const <String, Object?>{};
  final children = node['children'];

  final parts = <String>[];
  args.forEach((key, value) => parts.add('$key: ${_arg(key, value)}'));

  // Give the reminder primitive a snooze affordance unless the surface already
  // declares one. The event carries the synapse type so the generic panel-event
  // path fires it back to the kernel with no per-widget Dart.
  if (rawName == 'CountdownClock' && !args.containsKey('onSnooze')) {
    parts.add(
      'onSnooze: event "snooze" { type: "$_snoozeSynapse", minutes: 5 }',
    );
  }

  if (children is List && children.isNotEmpty) {
    final kids = children
        .whereType<Map>()
        .map((c) => _node(c.cast<String, Object?>()))
        .join(', ');
    if (kids.isNotEmpty) parts.add('children: [$kids]');
  }

  return '$name(${parts.join(', ')})';
}

/// Renders one argument. `on*` keys are RFW event handlers; everything else is a
/// value coerced to a typed literal.
String _arg(String key, Object? value) {
  if (_isEventKey(key)) {
    final eventName = value?.toString() ?? key;
    return 'event "$eventName" {}';
  }
  return _value(value);
}

bool _isEventKey(String key) =>
    key.length > 2 && key.startsWith('on') && _isUpper(key.codeUnitAt(2));

bool _isUpper(int codeUnit) => codeUnit >= 0x41 && codeUnit <= 0x5A;

String _value(Object? v) {
  if (v is bool) return v.toString();
  if (v is num) return v.toString();
  if (v is List) return '[${v.map(_value).join(', ')}]';
  if (v is Map) {
    final map = v.cast<String, Object?>();
    if (map['name'] is String) return _node(map);
    final entries = map.entries.map((e) => '${e.key}: ${_value(e.value)}');
    return '{${entries.join(', ')}}';
  }
  if (v is String) return _coerceString(v);
  if (v == null) return 'null';
  return _quote(v.toString());
}

/// The serializer stringifies every scalar; recover the intended RFW literal.
String _coerceString(String s) {
  final lower = s.toLowerCase();
  if (lower == 'true') return 'true';
  if (lower == 'false') return 'false';
  final asInt = int.tryParse(s);
  if (asInt != null) return asInt.toString();
  final asDouble = double.tryParse(s);
  if (asDouble != null) return asDouble.toString();
  return _quote(s);
}

String _quote(String s) =>
    '"${s.replaceAll('\\', '\\\\').replaceAll('"', '\\"').replaceAll('\n', '\\n')}"';

/// UiKit layout primitives → the palette's themed equivalents. Unmapped names
/// (palette widgets and core RFW widgets) pass through untouched.
String _mapName(Object? raw) {
  final name = raw?.toString() ?? 'Panel';
  switch (name) {
    case 'Column':
      return 'VStack';
    case 'Row':
      return 'HStack';
    case 'Card':
    case 'Container':
      return 'Panel';
    case 'Stack':
      return 'ZStack';
    default:
      return name;
  }
}
