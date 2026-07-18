import 'dart:convert';
import 'dart:math' as math;
import 'dart:ui';

import 'package:flutter/foundation.dart';

import 'package:digitalbrain_flutter/grpc/digitalbrain.pb.dart' as gw;
import 'package:digitalbrain_flutter/features/canvas/panel/canvas_panel.dart';
import 'package:digitalbrain_flutter/features/canvas/panel/layout_store.dart';

/// The window manager. Turns each [gw.RfwCardEnvelope] from `WatchHomeFeed`
/// into a draggable/dockable [CanvasPanel], owns z-order + geometry, drives the
/// one-click auto-layout, and persists/restores the arrangement per brain.
class PanelManager extends ChangeNotifier {
  PanelManager({
    required this.brainId,
    LayoutStore? store,
    this.onAutoLayoutSettled,
  }) : _store = store ?? LayoutStore();

  final String brainId;
  final LayoutStore _store;

  /// Fired after an auto-layout so the host can emit a viewport signal that
  /// keeps the background graph camera in sync (W-5). [target] is the centroid
  /// of the tidied grid in canvas space.
  final void Function(Offset target)? onAutoLayoutSettled;

  final List<CanvasPanel> _panels = <CanvasPanel>[];
  int _topZ = 0;
  Size _canvasSize = const Size(1280, 800);

  /// Panels in paint order (lowest z first).
  List<CanvasPanel> get panels {
    final ordered = List<CanvasPanel>.of(_panels)
      ..sort((a, b) => a.z.compareTo(b.z));
    return List.unmodifiable(ordered);
  }

  List<CanvasPanel> get minimized =>
      List.unmodifiable(_panels.where((p) => p.state == PanelState.minimized));

  void setCanvasSize(Size size) {
    if (size.width <= 0 || size.height <= 0) return;
    _canvasSize = size;
  }

  CanvasPanel? _byId(String id) {
    for (final p in _panels) {
      if (p.id == id) return p;
    }
    return null;
  }

  /// Ingest a card. Same correlation id ⇒ update the existing panel's surface in
  /// place (e.g. a reminder re-arming after snooze); otherwise spawn a panel.
  void upsertFromEnvelope(gw.RfwCardEnvelope env) {
    final surface = _surfaceFromEnvelope(env);
    final title = env.callerNeuronType.isNotEmpty
        ? _shortTitle(env.callerNeuronType)
        : (surface.data['title']?.toString() ?? 'Surface');

    final existing = _byId(env.correlationId);
    if (existing != null) {
      existing.surface = surface;
      existing.title = title;
      existing.state = PanelState.normal;
      _raise(existing);
      notifyListeners();
      return;
    }

    final rect = _cascadeSlot(_panels.length);
    _panels.add(
      CanvasPanel(
        id: env.correlationId.isEmpty
            ? 'panel-${_panels.length}-${env.rootWidget}'
            : env.correlationId,
        title: title,
        rect: rect,
        z: ++_topZ,
        surface: surface,
      ),
    );
    notifyListeners();
  }

  RfwSurfaceSpec _surfaceFromEnvelope(gw.RfwCardEnvelope env) {
    Map<String, Object?> data = const {};
    String? source;
    if (env.dataJson.isNotEmpty) {
      try {
        final decoded = jsonDecode(env.dataJson);
        if (decoded is Map) {
          data = decoded.cast<String, Object?>();
          source = data['source'] as String?;
        }
      } catch (_) {
        // Malformed payload degrades to an empty surface rather than crashing.
      }
    }
    return RfwSurfaceSpec(
      libraryName: env.libraryName.isEmpty ? 'digitalbrain' : env.libraryName,
      rootWidget: env.rootWidget.isEmpty ? 'root' : env.rootWidget,
      source: source,
      data: data,
    );
  }

  // New panels land in a tidy grid so the default dashboard (clock, flight
  // globe, reminder, 3D canvas, settings) arranges legibly on first launch
  // instead of a diagonal pile. The top margin clears the floating top bar.
  Rect _cascadeSlot(int index) {
    const w = 340.0;
    const h = 300.0;
    const gap = 24.0;
    const topMargin = 96.0;
    final columns = math.max(
      1,
      ((_canvasSize.width - gap) / (w + gap)).floor(),
    );
    final col = index % columns;
    final row = index ~/ columns;
    final x = (gap + col * (w + gap)).clamp(
      0.0,
      math.max(0.0, _canvasSize.width - w),
    );
    final y = (topMargin + row * (h + gap)).clamp(
      0.0,
      math.max(0.0, _canvasSize.height - h),
    );
    return Rect.fromLTWH(x.toDouble(), y.toDouble(), w, h);
  }

  void raise(String id) {
    final p = _byId(id);
    if (p == null) return;
    _raise(p);
    notifyListeners();
  }

  void _raise(CanvasPanel p) {
    if (p.z == _topZ) return;
    p.z = ++_topZ;
  }

  void move(String id, Offset delta) {
    final p = _byId(id);
    if (p == null) return;
    final next = p.rect.shift(delta);
    p.rect = _clampToCanvas(next);
    notifyListeners();
  }

  void resize(String id, Size delta) {
    final p = _byId(id);
    if (p == null) return;
    final w = (p.rect.width + delta.width).clamp(220.0, _canvasSize.width);
    final h = (p.rect.height + delta.height).clamp(160.0, _canvasSize.height);
    p.rect = Rect.fromLTWH(p.rect.left, p.rect.top, w, h);
    notifyListeners();
  }

  Rect _clampToCanvas(Rect r) {
    final maxLeft = math.max(0.0, _canvasSize.width - r.width);
    final maxTop = math.max(0.0, _canvasSize.height - r.height);
    return Rect.fromLTWH(
      r.left.clamp(0.0, maxLeft),
      r.top.clamp(0.0, maxTop),
      r.width,
      r.height,
    );
  }

  void toggleMinimize(String id) {
    final p = _byId(id);
    if (p == null) return;
    p.state = p.state == PanelState.minimized
        ? PanelState.normal
        : PanelState.minimized;
    if (p.state == PanelState.normal) _raise(p);
    notifyListeners();
  }

  void close(String id) {
    _panels.removeWhere((p) => p.id == id);
    notifyListeners();
  }

  /// One-click cleanup: re-flow every (non-minimized) panel into a tidy grid,
  /// sized to the panel count, and notify the host to settle the camera.
  void autoLayout() {
    final visible = _panels.where((p) => p.state == PanelState.normal).toList()
      ..sort((a, b) => a.z.compareTo(b.z));
    if (visible.isEmpty) return;

    const margin = 32.0;
    const gap = 20.0;
    final cols = math.max(1, math.sqrt(visible.length).ceil());
    final rows = (visible.length / cols).ceil();
    final cellW = (_canvasSize.width - margin * 2 - gap * (cols - 1)) / cols;
    final cellH = (_canvasSize.height - margin * 2 - gap * (rows - 1)) / rows;

    for (var i = 0; i < visible.length; i++) {
      final r = i ~/ cols;
      final c = i % cols;
      visible[i].rect = Rect.fromLTWH(
        margin + c * (cellW + gap),
        margin + r * (cellH + gap),
        math.max(220.0, cellW),
        math.max(160.0, cellH),
      );
    }
    notifyListeners();
    onAutoLayoutSettled?.call(
      Offset(_canvasSize.width / 2, _canvasSize.height / 2),
    );
  }

  Future<void> saveLayout() =>
      _store.save(brainId, CanvasPanel.encodeLayout(_panels));

  /// Restore a previously saved arrangement. Surfaces persist with the layout
  /// so panels repaint immediately on reload; a live card later updates them.
  Future<void> restoreLayout() async {
    final raw = await _store.load(brainId);
    if (raw == null || raw.isEmpty) return;
    final restored = CanvasPanel.decodeLayout(raw);
    if (restored.isEmpty) return;
    _panels
      ..clear()
      ..addAll(restored);
    _topZ = _panels.fold<int>(0, (m, p) => math.max(m, p.z));
    notifyListeners();
  }
}

String _shortTitle(String fqn) {
  final dot = fqn.lastIndexOf('.');
  final tail = dot >= 0 ? fqn.substring(dot + 1) : fqn;
  return tail.replaceAll('Neuron', '');
}
