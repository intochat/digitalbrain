import 'dart:math' as math;

import 'package:flutter/widgets.dart';
import 'package:flutter/foundation.dart';
import 'package:three_js/three_js.dart' as three;

import '../../theme/ui_theme.dart';
import 'graph_camera.dart';
import 'graph_models.dart';
import 'graph_scene.dart';

/// three_js implementation of [GraphScene].
///
/// Open-space scene with stable node coordinates and finite activity markers.
final class ThreeGraphScene
    implements GraphScene, AnimatedGraphScene, GraphSceneReadiness {
  ThreeGraphScene() {
    _threeJs = three.ThreeJS(
      onSetupComplete: () {
        if (_disposed) {
          _disposeRenderer();
        } else {
          _initialized.value = true;
        }
      },
      setup: _safeSetup,
      settings: three.Settings(
        antialias: true,
        clearColor: UiPalette.surfaceSunken.toARGB32() & 0xFFFFFF,
        clearAlpha: 1,
      ),
    );
    // ThreeJS.dispose reads these late-final fields even before setup.
    _threeJs.scene = three.Scene();
    _threeJs.camera = three.PerspectiveCamera(45, 1, 0.01, 100);
  }

  static const _shellRadius = 1.0;
  static const _hubSize = 0.13;
  static const _nodeSize = 0.075;
  static const _haloRatio = 3.0;
  static const _cameraDistance = 18.0;

  late final three.ThreeJS _threeJs;
  final three.Raycaster _raycaster = three.Raycaster();

  final List<three.Mesh> _nodeMeshes = [];
  final Map<String, three.Mesh> _meshById = {};
  three.Group? _nodeGroup;
  three.Group? _edgeGroup;
  final Map<String, _ActivePulse> _activePulses = {};

  bool _ready = false;
  bool _disposed = false;
  bool _initializationStarted = false;
  bool _rendererDisposed = false;
  Size? _viewSize;
  final _initialized = ValueNotifier(false);
  final _failed = ValueNotifier(false);
  @override
  ValueListenable<bool> get ready => _initialized;
  @override
  ValueListenable<bool> get failed => _failed;

  Future<void> _safeSetup() async {
    try {
      await _setup();
    } catch (_) {
      if (!_disposed) _failed.value = true;
    }
  }

  // Buffered until the renderer finishes setup.
  List<GraphNode> _nodes = const [];
  List<GraphEdge> _edges = const [];
  Map<String, GraphPoint> _layout = const {};
  List<GraphPulse> _pendingPulses = const [];
  GraphCameraState _pose = const GraphCameraState(
    yaw: 0.5,
    pitch: -0.18,
    zoom: 1,
  );

  Future<void> _setup() async {
    if (_disposed) return;
    _threeJs.camera.aspect = _threeJs.width / math.max(_threeJs.height, 1);
    _threeJs.camera.updateProjectionMatrix();
    _nodeGroup = three.Group();
    _edgeGroup = three.Group();
    _threeJs.scene.add(_nodeGroup!);
    _threeJs.scene.add(_edgeGroup!);
    _ready = true;
    _rebuild();
    final pending = _pendingPulses;
    _pendingPulses = const [];
    setPulses(pending);
    applyCamera(_pose);
  }

  @override
  Future<void> load(
    List<GraphNode> nodes,
    List<GraphEdge> edges,
    Map<String, GraphPoint> layout,
  ) async {
    _nodes = nodes;
    _edges = edges;
    _layout = layout;
    if (_ready) _rebuild();
  }

  void _rebuild() {
    if (!_ready || _disposed) return;

    for (final mesh in _nodeMeshes) {
      _nodeGroup!.remove(mesh);
      _disposeObject(mesh);
    }
    _nodeMeshes.clear();
    _meshById.clear();
    for (final edge in List<three.Object3D>.of(_edgeGroup!.children)) {
      _edgeGroup!.remove(edge);
      _disposeObject(edge);
    }

    for (final node in _nodes) {
      final point = _layout[node.id];
      if (point == null) continue;

      final colour = _colourFor(node);
      final hub = node.kind == GraphNodeKind.hub;
      final module = node.kind == GraphNodeKind.module;
      final size = hub
          ? _hubSize
          : module
          ? 0.48
          : _nodeSize;
      final scale = _shellRadius;

      final mesh = three.Mesh(
        three.SphereGeometry(size, 20, 20),
        three.MeshBasicMaterial.fromMap({
          'color': colour,
          'transparent': true,
          'opacity': module
              ? 0.035
              : node.dimmed
              ? 0.4
              : 0.9,
          'depthWrite': !module,
        }),
      )..position.setValues(point.x * scale, point.y * scale, point.z * scale);
      mesh.userData['nodeId'] = node.id;
      mesh.userData['module'] = module;

      if (!module) {
        final halo = three.Mesh(
          three.SphereGeometry(size * _haloRatio, 18, 18),
          three.MeshBasicMaterial.fromMap({
            'color': colour,
            'transparent': true,
            'opacity': 0.16,
            'blending': three.AdditiveBlending,
            'depthWrite': false,
          }),
        );
        mesh.add(halo);
      }

      _nodeGroup!.add(mesh);
      _nodeMeshes.add(mesh);
      _meshById[node.id] = mesh;
    }

    for (final edge in _edges) {
      final a = _layout[edge.sourceId];
      final b = _layout[edge.targetId];
      if (a == null || b == null) continue;
      _edgeGroup!.add(_edgeLine(a, b, edge));
    }
  }

  three.Line _edgeLine(GraphPoint a, GraphPoint b, GraphEdge edge) {
    final from = three.Vector3(
      a.x * _shellRadius,
      a.y * _shellRadius,
      a.z * _shellRadius,
    );
    final to = three.Vector3(
      b.x * _shellRadius,
      b.y * _shellRadius,
      b.z * _shellRadius,
    );

    return three.Line(
      three.BufferGeometry().setFromPoints(_curve(from, to).getPoints(32)),
      three.LineBasicMaterial.fromMap({
        'color': edge.decorated ? 0x65C5A0 : 0x7B9BE3,
        'transparent': true,
        'opacity': edge.decorated ? 0.65 : 0.34,
        'blending': three.AdditiveBlending,
      }),
    );
  }

  three.QuadraticBezierCurve3 _curve(three.Vector3 from, three.Vector3 to) {
    return three.QuadraticBezierCurve3(
      from,
      three.Vector3(
        (from.x + to.x) * 0.5,
        (from.y + to.y) * 0.5 + 0.24,
        (from.z + to.z) * 0.5,
      ),
      to,
    );
  }

  @override
  void setPulses(List<GraphPulse> pulses) {
    if (_disposed) return;
    if (!_ready) {
      _pendingPulses = [
        ..._pendingPulses,
        for (final pulse in pulses)
          if (!_pendingPulses.any((item) => item.signature == pulse.signature))
            pulse,
      ].take(32).toList(growable: false);
      return;
    }
    for (final pulse in pulses) {
      final key = pulse.operationId ?? pulse.signature;
      if (_activePulses[key] case final previous?) {
        if (previous.pulse.signature == pulse.signature) continue;
        _threeJs.scene.remove(previous.mesh);
        _disposeObject(previous.mesh);
        _activePulses.remove(key);
      }
      if (_activePulses.length >= 32) {
        continue;
      }
      final color = switch (pulse.outcome) {
        GraphPulseOutcome.signal => 0x43E4A1,
        GraphPulseOutcome.failed => 0xF06978,
        GraphPulseOutcome.completed => 0x7EE6CA,
        _ => 0xFFF0BD,
      };
      final mesh = three.Mesh(
        three.SphereGeometry(pulse.local ? 0.09 : 0.055, 16, 16),
        three.MeshBasicMaterial.fromMap({'color': color}),
      );
      _threeJs.scene.add(mesh);
      _activePulses[key] = _ActivePulse(pulse, mesh);
    }
  }

  @override
  void clearPulses() {
    _pendingPulses = const [];
    if (!_ready || _disposed) return;
    for (final active in _activePulses.values) {
      _threeJs.scene.remove(active.mesh);
      _disposeObject(active.mesh);
    }
    _activePulses.clear();
  }

  @override
  void advance(double seconds) {
    if (!_ready || _disposed) return;
    for (final entry in _activePulses.entries.toList()) {
      final active = entry.value;
      final pulse = active.pulse;
      final from = _meshById[pulse.fromId];
      final to = _meshById[pulse.toId];
      active.elapsed += seconds;
      if (from == null ||
          to == null ||
          active.elapsed >= GraphPulse.travelTime.inMilliseconds / 1000) {
        _threeJs.scene.remove(active.mesh);
        _disposeObject(active.mesh);
        _activePulses.remove(entry.key);
        continue;
      }
      final progress =
          (active.elapsed / (GraphPulse.travelTime.inMilliseconds / 1000))
              .clamp(0.0, 1.0);
      final point = pulse.local
          ? from.position
          : _curve(from.position, to.position).getPoint(progress)
                as three.Vector3;
      active.mesh.position.setValues(point.x, point.y, point.z);
      if (pulse.local) {
        final scale = 1 + progress * 3;
        active.mesh.scale.setValues(scale, scale, scale);
      }
    }
  }

  /// Cluster-stable colour. Hubs are always the ui's signal cyan.
  int _colourFor(GraphNode node) {
    if (node.kind == GraphNodeKind.hub) return 0x3DDCFF;
    const palette = [
      0x7B9BE3,
      0x65C5A0,
      0xE09261,
      0xE8C56A,
      0xC49BFF,
      0x5EC8E8,
    ];
    final key = node.cluster ?? node.id;
    var hash = 0x811c9dc5;
    for (final unit in key.codeUnits) {
      hash ^= unit;
      hash = (hash * 0x01000193) & 0xFFFFFFFF;
    }
    return palette[hash % palette.length];
  }

  @override
  void applyCamera(GraphCameraState state) {
    _pose = state;
    if (!_ready || _disposed) return;

    // Inverse of GraphCamera.focusOn: place the eye along the focused node's
    // own direction, so that node ends up facing the viewer.
    final distance = _cameraDistance / state.zoom;
    final cosPitch = math.cos(state.pitch);
    _threeJs.camera.position.setValues(
      -math.sin(state.yaw) * cosPitch * distance,
      -math.sin(state.pitch) * distance,
      math.cos(state.yaw) * cosPitch * distance,
    );
    _threeJs.camera.lookAt(three.Vector3(0, 0, 0));
  }

  @override
  String? pick(Offset local) {
    if (!_ready || _disposed || _nodeMeshes.isEmpty) return null;
    final width = _viewSize?.width ?? _threeJs.width;
    final height = _viewSize?.height ?? _threeJs.height;
    if (width == 0 || height == 0) return null;

    _raycaster.setFromCamera(
      three.Vector2((local.dx / width) * 2 - 1, -((local.dy / height) * 2 - 1)),
      _threeJs.camera,
    );

    final hits = _raycaster.intersectObjects(
      _nodeMeshes.cast<three.Object3D>(),
      false,
    );
    if (hits.isEmpty) return null;
    // An envelope must not intercept the neurons it contains. It remains
    // selectable when the ray does not hit any contained neuron.
    final hit = hits.firstWhere(
      (hit) => hit.object?.userData['module'] != true,
      orElse: () => hits.first,
    );
    return hit.object?.userData['nodeId'] as String?;
  }

  @override
  Offset? project(String nodeId) {
    if (!_ready || _disposed) return null;
    final mesh = _meshById[nodeId];
    if (mesh == null) return null;

    final v = three.Vector3(mesh.position.x, mesh.position.y, mesh.position.z)
      ..project(_threeJs.camera);
    if (v.z > 1) return null;

    return Offset(
      (v.x * 0.5 + 0.5) * (_viewSize?.width ?? _threeJs.width),
      (-v.y * 0.5 + 0.5) * (_viewSize?.height ?? _threeJs.height),
    );
  }

  @override
  Widget build(BuildContext context) => ValueListenableBuilder<bool>(
    valueListenable: _initialized,
    builder: (context, _, _) => LayoutBuilder(
      builder: (context, constraints) {
        final size = constraints.biggest;
        if (_viewSize != size && _ready && !_disposed && size.height > 0) {
          _threeJs.camera.aspect = size.width / size.height;
          _threeJs.camera.updateProjectionMatrix();
        }
        _viewSize = size;
        _initializationStarted = true;
        // The embedded graph uses its pane dimensions, not the whole app window.
        return MediaQuery(
          data: MediaQuery.of(context).copyWith(size: size),
          child: _threeJs.build(),
        );
      },
    ),
  );

  @override
  void dispose() {
    if (_disposed) return;
    _disposed = true;
    _initialized.dispose();
    _failed.dispose();
    if (!_ready) {
      // The SDK schedules native initialization without a cancellation hook.
      // Once started, let it finish and dispose from onSetupComplete; disposing
      // sooner clears dimensions still needed by its pending async work.
      if (!_initializationStarted) _disposeRenderer();
      return;
    }
    for (final mesh in _nodeMeshes) {
      _nodeGroup!.remove(mesh);
      _disposeObject(mesh);
    }
    for (final edge in List<three.Object3D>.of(_edgeGroup!.children)) {
      _edgeGroup!.remove(edge);
      _disposeObject(edge);
    }
    for (final active in _activePulses.values) {
      _threeJs.scene.remove(active.mesh);
      _disposeObject(active.mesh);
    }
    _activePulses.clear();
    _nodeMeshes.clear();
    _meshById.clear();
    _disposeRenderer();
  }

  void _disposeRenderer() {
    if (_rendererDisposed) return;
    _rendererDisposed = true;
    _threeJs.dispose();
  }

  void _disposeObject(three.Object3D object) {
    for (final child in List<three.Object3D>.of(object.children)) {
      object.remove(child);
      _disposeObject(child);
    }
    if (object is three.Mesh) {
      object.geometry?.dispose();
      object.material?.dispose();
    } else if (object is three.Line) {
      object.geometry?.dispose();
      object.material?.dispose();
    }
  }
}

final class _ActivePulse {
  _ActivePulse(this.pulse, this.mesh);
  final GraphPulse pulse;
  final three.Mesh mesh;
  double elapsed = 0;
}
