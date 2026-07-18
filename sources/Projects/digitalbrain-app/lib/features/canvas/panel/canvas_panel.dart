import 'dart:convert';
import 'dart:ui';

/// A neuron's RFW surface ready to render. When [source] is present it is an
/// inline RFW document (the embedded-`source` card contract — see the kernel's
/// GoogleAuthCard pattern); otherwise [rootWidget] names a host primitive.
class RfwSurfaceSpec {
  const RfwSurfaceSpec({
    required this.libraryName,
    required this.rootWidget,
    required this.source,
    required this.data,
  });

  final String libraryName;
  final String rootWidget;
  final String? source;
  final Map<String, Object?> data;

  Map<String, Object?> toJson() => {
    'libraryName': libraryName,
    'rootWidget': rootWidget,
    if (source != null) 'source': source,
    'data': data,
  };

  static RfwSurfaceSpec fromJson(Map<String, Object?> j) => RfwSurfaceSpec(
    libraryName: (j['libraryName'] as String?) ?? 'digitalbrain',
    rootWidget: (j['rootWidget'] as String?) ?? 'root',
    source: j['source'] as String?,
    data: (j['data'] as Map?)?.cast<String, Object?>() ?? const {},
  );
}

enum PanelState { normal, minimized }

/// One free-floating, dockable widget on the canvas. A "window" is just a
/// neuron's RFW surface given a position, a z-index and a drag handle (W-1).
class CanvasPanel {
  CanvasPanel({
    required this.id,
    required this.title,
    required this.rect,
    required this.z,
    required this.surface,
    this.state = PanelState.normal,
  });

  /// Correlation id from the RfwCardEnvelope — the panel's stable identity.
  final String id;
  String title;
  Rect rect;
  int z;
  PanelState state;
  RfwSurfaceSpec surface;

  Map<String, Object?> toJson() => {
    'id': id,
    'title': title,
    'x': rect.left,
    'y': rect.top,
    'w': rect.width,
    'h': rect.height,
    'z': z,
    'state': state.name,
    'surface': surface.toJson(),
  };

  static CanvasPanel fromJson(Map<String, Object?> j) => CanvasPanel(
    id: j['id'] as String,
    title: (j['title'] as String?) ?? '',
    rect: Rect.fromLTWH(
      (j['x'] as num?)?.toDouble() ?? 40,
      (j['y'] as num?)?.toDouble() ?? 40,
      (j['w'] as num?)?.toDouble() ?? 320,
      (j['h'] as num?)?.toDouble() ?? 280,
    ),
    z: (j['z'] as num?)?.toInt() ?? 0,
    state: PanelState.values.firstWhere(
      (s) => s.name == j['state'],
      orElse: () => PanelState.normal,
    ),
    surface: RfwSurfaceSpec.fromJson(
      (j['surface'] as Map?)?.cast<String, Object?>() ?? const {},
    ),
  );

  static String encodeLayout(List<CanvasPanel> panels) =>
      jsonEncode(panels.map((p) => p.toJson()).toList());

  static List<CanvasPanel> decodeLayout(String s) {
    final raw = jsonDecode(s);
    if (raw is! List) return const [];
    return raw
        .whereType<Map>()
        .map((m) => CanvasPanel.fromJson(m.cast<String, Object?>()))
        .toList();
  }
}
