import 'dart:math' as math;

import 'package:flutter/widgets.dart';
import 'package:three_js/three_js.dart' as three;

import 'graph_camera.dart';
import 'graph_models.dart';
import 'graph_scene.dart';

/// three_js implementation of [GraphScene].
///
/// Mesh construction follows the shell brain canvas pattern: a node sphere plus
/// an additive halo per node, and a quadratic-bezier line per edge bowing
/// outward past the shell. Node positions come from the caller's layout map
/// rather than a hardcoded topology.
final class ThreeGraphScene implements GraphScene {
  ThreeGraphScene() {
    _threeJs = three.ThreeJS(
      onSetupComplete: () {},
      setup: _setup,
      settings: three.Settings(antialias: true),
    );
  }

  static const _shellRadius = 1.55;
  static const _hubSize = 0.13;
  static const _nodeSize = 0.075;
  static const _haloRatio = 3.0;
  static const _cameraDistance = 4.6;

  late final three.ThreeJS _threeJs;
  final three.Raycaster _raycaster = three.Raycaster();

  final List<three.Mesh> _nodeMeshes = [];
  final Map<String, three.Mesh> _meshById = {};
  three.Group? _nodeGroup;
  three.Group? _edgeGroup;

  bool _ready = false;
  bool _disposed = false;

  // Buffered until the renderer finishes setup.
  List<GraphNode> _nodes = const [];
  List<GraphEdge> _edges = const [];
  Map<String, GraphPoint> _layout = const {};
  GraphCameraState _pose = const GraphCameraState(
    yaw: 0.5,
    pitch: -0.18,
    zoom: 1,
  );

  Future<void> _setup() async {
    _threeJs.scene = three.Scene();
    _threeJs.camera = three.PerspectiveCamera(
      45,
      _threeJs.width / math.max(_threeJs.height, 1),
      0.01,
      100,
    );
    _nodeGroup = three.Group();
    _edgeGroup = three.Group();
    _threeJs.scene.add(_nodeGroup!);
    _threeJs.scene.add(_edgeGroup!);
    _ready = true;
    _rebuild();
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
      mesh.geometry?.dispose();
      mesh.material?.dispose();
    }
    _nodeMeshes.clear();
    _meshById.clear();
    _edgeGroup!.children.clear();

    for (final node in _nodes) {
      final point = _layout[node.id];
      if (point == null) continue;

      final colour = _colourFor(node);
      final hub = node.kind == GraphNodeKind.hub;
      final size = hub ? _hubSize : _nodeSize;
      final scale = hub ? 0.0 : _shellRadius;

      final mesh = three.Mesh(
        three.SphereGeometry(size, 20, 20),
        three.MeshBasicMaterial.fromMap({
          'color': colour,
          'transparent': true,
          'opacity': node.dimmed ? 0.4 : 0.9,
        }),
      )..position.setValues(point.x * scale, point.y * scale, point.z * scale);
      mesh.userData['nodeId'] = node.id;

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

    // Bow the arc outward past the shell so edges read as synapses, not chords.
    var mx = (from.x + to.x) * 0.5;
    var my = (from.y + to.y) * 0.5;
    var mz = (from.z + to.z) * 0.5;
    final length = math.sqrt(mx * mx + my * my + mz * mz);
    if (length > 1e-6) {
      final arc = _shellRadius + 0.3;
      mx = mx / length * arc;
      my = my / length * arc;
      mz = mz / length * arc;
    }

    final curve = three.QuadraticBezierCurve3(
      from,
      three.Vector3(mx, my, mz),
      to,
    );

    return three.Line(
      three.BufferGeometry().setFromPoints(curve.getPoints(32)),
      three.LineBasicMaterial.fromMap({
        'color': 0x7B9BE3,
        'transparent': true,
        'opacity': edge.dotted ? 0.12 : 0.24,
        'blending': three.AdditiveBlending,
      }),
    );
  }

  /// Cluster-stable colour. Hubs are always the kit's signal cyan.
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
    final width = _threeJs.width;
    final height = _threeJs.height;
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
    return hits.first.object?.userData['nodeId'] as String?;
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
      (v.x * 0.5 + 0.5) * _threeJs.width,
      (-v.y * 0.5 + 0.5) * _threeJs.height,
    );
  }

  @override
  Widget build(BuildContext context) => _threeJs.build();

  @override
  void dispose() {
    if (_disposed) return;
    _disposed = true;
    if (!_ready) return;
    for (final mesh in _nodeMeshes) {
      mesh.geometry?.dispose();
      mesh.material?.dispose();
    }
    _nodeMeshes.clear();
    _meshById.clear();
    _threeJs.dispose();
    three.loading.clear();
  }
}
