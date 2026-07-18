import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:three_js/three_js.dart' as three;

import 'shell_brain_topology.dart';
import 'shell_theme.dart';

/// Result of a pointer-down hit test on the shell brain canvas.
sealed class ShellPick {
  const ShellPick();
}

/// A comet head was hit. The synapse's [paused] flag was toggled by the picker.
class SynapsePick extends ShellPick {
  const SynapsePick({required this.syn, required this.screen});
  final ShellSynapse syn;
  final Offset screen;
}

/// A neuron mesh was hit (and no comet head was closer).
class NeuronPick extends ShellPick {
  const NeuronPick({required this.alias, required this.cluster});
  final String alias;
  final String cluster;
}

/// In-flight comet record. Lifecycle: spawned by [spawnSynapse], advanced by
/// [_animate], disposed when t ≥ 1.05 or via [dispose].
class ShellSynapse {
  ShellSynapse({
    required this.head,
    required this.halo,
    required this.tail,
    required this.curve,
    required this.duration,
    required this.from,
    required this.to,
    required this.payload,
    required this.gold,
  });

  final three.Mesh head;
  final three.Mesh halo;
  final three.Line tail;
  final three.QuadraticBezierCurve3 curve;
  final double duration; // seconds end-to-end
  final String from;
  final String to;
  final Map<String, dynamic> payload;
  final bool gold;
  double t = 0; // 0..1 progress
  bool paused = false;
}

/// Widget key type exposed so callers can reach [flareNeuron] / [fireCluster] /
/// [spawnSynapse].
///
/// Example:
/// ```dart
/// final _canvasKey = GlobalKey<ShellBrainCanvasState>();
/// ShellBrainCanvas(key: _canvasKey)
/// ...
/// _canvasKey.currentState?.flareNeuron('PlanTrip', 0.8);
/// _canvasKey.currentState?.fireCluster('travel', 1.0);
/// _canvasKey.currentState?.spawnSynapse(from: 'Cortex', to: 'PlanTrip');
/// ```
class ShellBrainCanvas extends StatefulWidget {
  const ShellBrainCanvas({super.key});

  @override
  State<ShellBrainCanvas> createState() => ShellBrainCanvasState();
}

class ShellBrainCanvasState extends State<ShellBrainCanvas> {
  static const double _sphereR = 1.55;
  static const double _jitterHalf = 0.225;
  static const double _radiusJitter = 0.09;
  static const double _minSize = 0.03;
  static const double _sizeRand = 0.025;
  static const double _haloRatio = 3.2;

  late three.ThreeJS _threeJs;
  three.OrbitControls? _controls;

  // Parallel lists — index-matched with ShellTopology.neurons.
  final List<three.Mesh> _neuronMeshes = [];
  final List<three.Mesh> _haloMeshes = [];

  // Cluster-level glow spheres — one per ShellCluster.
  final List<three.Mesh> _clusterGlows = [];

  // Faint bezier filament lines between cluster pairs.
  final List<three.Line> _filaments = [];

  // Active comet synapses — iterated in reverse during _animate for safe removal.
  final List<ShellSynapse> _synapses = [];

  final three.Raycaster _raycaster = three.Raycaster();

  double _elapsed = 0.0;
  bool _sceneReady = false;

  bool _autoFocusEnabled = true;
  String? _focusClusterId;

  @override
  void initState() {
    super.initState();
    _threeJs = three.ThreeJS(
      onSetupComplete: () => setState(() {}),
      setup: _setupScene,
      settings: three.Settings(antialias: true),
    );
  }

  @override
  void dispose() {
    // ThreeJS.dispose() accesses `scene` which is a late field; guard against
    // the case where setup never ran (e.g. widget tests without a GL context).
    if (_sceneReady) {
      _disposeAllSynapses();
      _controls?.dispose();
      _threeJs.dispose();
      three.loading.clear();
    }
    super.dispose();
  }

  void _disposeAllSynapses() {
    for (final s in _synapses) {
      _threeJs.scene.remove(s.head);
      _threeJs.scene.remove(s.tail);
      s.head.geometry?.dispose();
      s.head.material?.dispose();
      s.halo.geometry?.dispose();
      s.halo.material?.dispose();
      s.tail.geometry?.dispose();
      s.tail.material?.dispose();
    }
    _synapses.clear();
  }

  /// Enables or disables auto-rotate and auto-focus cluster tracking.
  ///
  /// Passing false immediately halts orbit rotation and clears any pending
  /// cluster focus. Passing true resumes both. Intended for callers that want
  /// to pause the scene during user-driven interactions.
  void setAutoFocus(bool enabled) {
    _autoFocusEnabled = enabled;
    _controls?.autoRotate = enabled;
    if (!enabled) _focusClusterId = null;
  }

  /// Nudges the camera toward the cluster with [clusterId] over subsequent frames.
  ///
  /// No-ops when [setAutoFocus] has been called with false. Cluster lookup
  /// falls back to the first cluster if [clusterId] is unknown. Full lerp
  /// targeting (theta/phi state machine matching brain.js) deferred to T3.4.
  void focusOnCluster(String clusterId) {
    if (!_autoFocusEnabled) return;
    _focusClusterId = clusterId;
  }

  /// Flares the neuron identified by [alias].
  ///
  /// Increases the neuron's transient glow by [mag] (clamped to 1.0).
  /// Used by the comet arrival handler (T3.4).
  void flareNeuron(String alias, double mag) {
    final neurons = ShellTopology.neurons;
    for (var i = 0; i < neurons.length && i < _neuronMeshes.length; i++) {
      if (neurons[i].alias == alias) {
        final mesh = _neuronMeshes[i];
        final current = (mesh.userData['flare'] as double?) ?? 0.0;
        mesh.userData['flare'] = math.max(current, mag.clamp(0.0, 1.0));
        return;
      }
    }
  }

  /// Projects a world-space point to a screen-space [Offset], or null when
  /// the point is behind the near plane (NDC z > 1).
  ///
  /// Uses [three.Vector3.project] which mutates in place; the cascade form
  /// `..project(camera)` is the canonical Dart pattern confirmed by the three_js
  /// Dart docs.
  Offset? projectVec3(double x, double y, double z) {
    if (!_sceneReady) return null;
    final v = three.Vector3(x, y, z)..project(_threeJs.camera);
    if (v.z > 1) return null;
    final w = _threeJs.width;
    final h = _threeJs.height;
    return Offset((v.x * 0.5 + 0.5) * w, (-v.y * 0.5 + 0.5) * h);
  }

  /// Like [projectVec3] but also returns the NDC z so callers can derive a
  /// depth-based opacity. NDC z ∈ [-1, 1]; points with z > 1 (behind near
  /// plane) return null.
  ({Offset offset, double z})? projectVec3WithDepth(
      double x, double y, double z) {
    if (!_sceneReady) return null;
    final v = three.Vector3(x, y, z)..project(_threeJs.camera);
    if (v.z > 1) return null;
    final w = _threeJs.width;
    final h = _threeJs.height;
    return (
      offset: Offset((v.x * 0.5 + 0.5) * w, (-v.y * 0.5 + 0.5) * h),
      z: v.z,
    );
  }

  /// Fires the cluster glow identified by [clusterId].
  ///
  /// Sets the glow's fire field to max(current, [mag]), which decays at 0.9/s
  /// during the animation loop. Opacity trace: fire=1.0 → 0.05 + 0.32 = 0.37.
  void fireCluster(String clusterId, double mag) {
    for (final glow in _clusterGlows) {
      if (glow.userData['clusterId'] == clusterId) {
        final cur = (glow.userData['fire'] as double?) ?? 0.0;
        glow.userData['fire'] = math.max(cur, mag);
      }
    }
  }

  /// Spawns a comet from neuron [from] (alias) to neuron [to] (alias) along a
  /// bezier arc that bows outward above the sphere shell. Returns the active
  /// record so callers can pause/resume; null if either alias is unknown
  /// or the scene isn't ready.
  ShellSynapse? spawnSynapse({
    required String from,
    required String to,
    Map<String, dynamic> payload = const {},
    bool gold = false,
    double duration = 0.48,
  }) {
    if (!_sceneReady) return null;

    final fromNeuron = ShellTopology.aliasLookup(from);
    final toNeuron = ShellTopology.aliasLookup(to);
    if (fromNeuron == null || toNeuron == null) return null;

    three.Mesh? fromMesh;
    three.Mesh? toMesh;
    for (final m in _neuronMeshes) {
      if (m.userData['neuronAlias'] == from) fromMesh = m;
      if (m.userData['neuronAlias'] == to) toMesh = m;
      if (fromMesh != null && toMesh != null) break;
    }
    if (fromMesh == null || toMesh == null) return null;

    final ax = fromMesh.position.x;
    final ay = fromMesh.position.y;
    final az = fromMesh.position.z;
    final bx = toMesh.position.x;
    final by = toMesh.position.y;
    final bz = toMesh.position.z;

    // Arc midpoint: average of the two endpoints, then pushed outward to
    // SPHERE_R + 0.42 — matches brain.js line 133–134.
    final mx = (ax + bx) * 0.5;
    final my = (ay + by) * 0.5;
    final mz = (az + bz) * 0.5;
    final mLen = math.sqrt(mx * mx + my * my + mz * mz);
    final arcR = _sphereR + 0.42;
    final mid = three.Vector3(
      mx / mLen * arcR,
      my / mLen * arcR,
      mz / mLen * arcR,
    );

    final curve = three.QuadraticBezierCurve3(
      three.Vector3(ax, ay, az),
      mid,
      three.Vector3(bx, by, bz),
    );

    final color = gold
        ? InoShellTheme.gold.toARGB32() & 0xFFFFFF
        : InoShellTheme.cyan.toARGB32() & 0xFFFFFF;

    // Tail: 50-point static line at low additive opacity; fades with comet progress.
    final tailGeo = three.BufferGeometry().setFromPoints(curve.getPoints(50));
    final tailMat = three.LineBasicMaterial.fromMap({
      'color': color,
      'transparent': true,
      'opacity': 0.12,
      'blending': three.AdditiveBlending,
    });
    final tail = three.Line(tailGeo, tailMat);
    _threeJs.scene.add(tail);

    // Head sphere.
    final headGeo = three.SphereGeometry(0.04, 14, 14);
    final headMat = three.MeshBasicMaterial.fromMap({
      'color': color,
      'transparent': true,
      'opacity': 1.0,
      'blending': three.AdditiveBlending,
    });
    final head = three.Mesh(headGeo, headMat)
      ..position.setValues(ax, ay, az);
    _threeJs.scene.add(head);

    // Halo parented to head — transforms with it automatically.
    final haloGeo = three.SphereGeometry(0.13, 18, 18);
    final haloMat = three.MeshBasicMaterial.fromMap({
      'color': color,
      'transparent': true,
      'opacity': 0.35,
      'blending': three.AdditiveBlending,
      'depthWrite': false,
    });
    final halo = three.Mesh(haloGeo, haloMat);
    head.add(halo);

    // Source flare + cluster fire on spawn; target at ~70% of travel time.
    flareNeuron(from, 1.0);
    fireCluster(fromNeuron.cluster, 0.6);
    Future.delayed(
      Duration(milliseconds: (duration * 700).round()),
      () {
        if (!mounted || !_sceneReady) return;
        flareNeuron(to, 1.0);
        fireCluster(toNeuron.cluster, 1.0);
      },
    );

    final syn = ShellSynapse(
      head: head,
      halo: halo,
      tail: tail,
      curve: curve,
      duration: duration,
      from: from,
      to: to,
      payload: payload,
      gold: gold,
    );
    _synapses.add(syn);
    return syn;
  }

  /// Read-only view of the currently in-flight comet synapses.
  List<ShellSynapse> get activeSynapses => List.unmodifiable(_synapses);

  /// Pointer-down hit test. Tries comet heads first; if none hit, falls
  /// through to neuron meshes. Returns null when both miss or the scene isn't ready.
  /// [local] is pixels relative to the canvas widget — matches [_threeJs.width]
  /// / [_threeJs.height] coordinate space exactly (same pattern as [BrainPicker]).
  ShellPick? pickNode(Offset local) {
    if (!_sceneReady) return null;

    final width = _threeJs.width;
    final height = _threeJs.height;
    if (width == 0 || height == 0) return null;

    final ndc = three.Vector2(
      (local.dx / width) * 2 - 1,
      -((local.dy / height) * 2 - 1),
    );
    _raycaster.setFromCamera(ndc, _threeJs.camera);

    // 1. Comet heads (existing behaviour: toggles paused).
    if (_synapses.isNotEmpty) {
      final heads = _synapses.map((s) => s.head as three.Object3D).toList(growable: false);
      final hits = _raycaster.intersectObjects(heads, false);
      if (hits.isNotEmpty) {
        final hitObject = hits.first.object;
        if (hitObject != null) {
          for (final s in _synapses) {
            if (identical(s.head, hitObject)) {
              s.paused = !s.paused;
              return SynapsePick(syn: s, screen: local);
            }
          }
        }
      }
    }

    // 2. Neuron meshes (no toggling — just identify). Raycaster was already
    // aimed by setFromCamera above; reusing it here is safe because the camera
    // direction does not depend on the candidate object list.
    final neuronHits = _raycaster.intersectObjects(
      _neuronMeshes.cast<three.Object3D>(), false);
    if (neuronHits.isNotEmpty) {
      final mesh = neuronHits.first.object;
      if (mesh != null) {
        final alias = mesh.userData['neuronAlias'] as String?;
        final cluster = mesh.userData['clusterId'] as String?;
        if (alias != null && cluster != null) {
          return NeuronPick(alias: alias, cluster: cluster);
        }
      }
    }
    return null;
  }

  Future<void> _setupScene() async {
    _threeJs.scene = three.Scene();

    _threeJs.camera = three.PerspectiveCamera(
      45,
      _threeJs.width / _threeJs.height,
      0.01,
      100,
    );
    _threeJs.camera.position.setValues(0, 0.2, 4.5);
    _threeJs.camera.lookAt(three.Vector3(0, 0, 0));

    final nodeGroup = three.Group();
    final haloGroup = three.Group();
    _threeJs.scene.add(nodeGroup);
    _threeJs.scene.add(haloGroup);

    final rng = math.Random(42);
    final neurons = ShellTopology.neurons;
    final clusterById = {for (final c in ShellTopology.clusters) c.id: c};

    for (final neuron in neurons) {
      final cluster = clusterById[neuron.cluster]!;
      final colorHex = cluster.color.toARGB32() & 0xFFFFFF;

      // Project cluster centre onto the sphere shell.
      final cx = cluster.position.x;
      final cy = cluster.position.y;
      final cz = cluster.position.z;
      final len = math.sqrt(cx * cx + cy * cy + cz * cz);
      final nx = cx / len;
      final ny = cy / len;
      final nz = cz / len;

      final centerX = nx * _sphereR;
      final centerY = ny * _sphereR;
      final centerZ = nz * _sphereR;

      // Uniform jitter in [-_jitterHalf, +_jitterHalf]³.
      final jx = (rng.nextDouble() - 0.5) * _jitterHalf * 2;
      final jy = (rng.nextDouble() - 0.5) * _jitterHalf * 2;
      final jz = (rng.nextDouble() - 0.5) * _jitterHalf * 2;

      // Re-project jittered point onto a slightly varied shell radius.
      final px = centerX + jx;
      final py = centerY + jy;
      final pz = centerZ + jz;
      final pLen = math.sqrt(px * px + py * py + pz * pz);
      final targetR = _sphereR + (rng.nextDouble() - 0.5) * _radiusJitter * 2;
      final posX = (px / pLen) * targetR;
      final posY = (py / pLen) * targetR;
      final posZ = (pz / pLen) * targetR;

      final sz = _minSize + rng.nextDouble() * _sizeRand;

      final neuronMesh = three.Mesh(
        three.SphereGeometry(sz, 18, 18),
        three.MeshBasicMaterial.fromMap({
          'color': colorHex,
          'transparent': true,
          'opacity': 0.85,
        }),
      );
      neuronMesh.position.setValues(posX, posY, posZ);
      neuronMesh.userData['neuronAlias'] = neuron.alias;
      neuronMesh.userData['clusterId'] = neuron.cluster;
      neuronMesh.userData['baseSize'] = sz;
      neuronMesh.userData['baseOpacity'] = 0.78;
      neuronMesh.userData['flare'] = 0.0;
      nodeGroup.add(neuronMesh);
      _neuronMeshes.add(neuronMesh);

      final haloMesh = three.Mesh(
        three.SphereGeometry(sz * _haloRatio, 18, 18),
        three.MeshBasicMaterial.fromMap({
          'color': colorHex,
          'transparent': true,
          'opacity': 0.0,
          'depthWrite': false,
          'blending': three.AdditiveBlending,
        }),
      );
      haloMesh.position.setValues(posX, posY, posZ);
      haloGroup.add(haloMesh);
      _haloMeshes.add(haloMesh);
    }

    _placeClusterGlows();
    _placeFilaments();

    // autoRotateSpeed 0.6 ≈ visually slow orbit (~0.05 rad/s), matching
    // /brain's tuning (0.4) scaled up slightly for the tighter shell radius.
    _controls = three.OrbitControls(_threeJs.camera, _threeJs.globalKey)
      ..target.setValues(0, 0, 0)
      ..enableDamping = true
      ..dampingFactor = 0.06
      ..enablePan = false
      ..minDistance = 2.6
      ..maxDistance = 7.0
      ..autoRotate = true
      ..autoRotateSpeed = 0.6;

    _threeJs.addAnimationEvent(_animate);
    _sceneReady = true;
  }

  void _placeClusterGlows() {
    final glowGroup = three.Group();
    _threeJs.scene.add(glowGroup);

    for (final c in ShellTopology.clusters) {
      final colorHex = c.color.toARGB32() & 0xFFFFFF;

      final rawLen = math.sqrt(
        c.position.x * c.position.x +
        c.position.y * c.position.y +
        c.position.z * c.position.z,
      );
      final cx = (c.position.x / rawLen) * (_sphereR * 0.95);
      final cy = (c.position.y / rawLen) * (_sphereR * 0.95);
      final cz = (c.position.z / rawLen) * (_sphereR * 0.95);

      final glow = three.Mesh(
        three.SphereGeometry(c.size * 1.4, 24, 24),
        three.MeshBasicMaterial.fromMap({
          'color': colorHex,
          'transparent': true,
          'opacity': 0.05,
          'depthWrite': false,
          'blending': three.AdditiveBlending,
        }),
      );
      glow.position.setValues(cx, cy, cz);
      glow.userData['clusterId'] = c.id;
      glow.userData['baseOpacity'] = 0.05;
      glow.userData['fire'] = 0.0;

      glowGroup.add(glow);
      _clusterGlows.add(glow);
    }
  }

  void _placeFilaments() {
    final filamentGroup = three.Group();
    _threeJs.scene.add(filamentGroup);

    final clusterById = {for (final c in ShellTopology.clusters) c.id: c};

    for (final (aId, bId) in ShellTopology.filamentPairs) {
      final a = clusterById[aId];
      final b = clusterById[bId];
      if (a == null || b == null) continue;

      final aRawLen = math.sqrt(
        a.position.x * a.position.x +
        a.position.y * a.position.y +
        a.position.z * a.position.z,
      );
      final pa = three.Vector3(
        (a.position.x / aRawLen) * (_sphereR * 0.95),
        (a.position.y / aRawLen) * (_sphereR * 0.95),
        (a.position.z / aRawLen) * (_sphereR * 0.95),
      );

      final bRawLen = math.sqrt(
        b.position.x * b.position.x +
        b.position.y * b.position.y +
        b.position.z * b.position.z,
      );
      final pb = three.Vector3(
        (b.position.x / bRawLen) * (_sphereR * 0.95),
        (b.position.y / bRawLen) * (_sphereR * 0.95),
        (b.position.z / bRawLen) * (_sphereR * 0.95),
      );

      // Pull midpoint toward origin (× 0.6) — matches brain.js line 105-106.
      final mid = three.Vector3(
        (pa.x + pb.x) * 0.5 * 0.6,
        (pa.y + pb.y) * 0.5 * 0.6,
        (pa.z + pb.z) * 0.5 * 0.6,
      );

      final curve = three.QuadraticBezierCurve3(pa, mid, pb);
      final pts = curve.getPoints(40);

      final line = three.Line(
        three.BufferGeometry().setFromPoints(pts),
        three.LineBasicMaterial.fromMap({
          'color': 0x7C8AFF,
          'transparent': true,
          'opacity': 0.05,
        }),
      );
      line.userData['baseOpacity'] = 0.05;

      filamentGroup.add(line);
      _filaments.add(line);
    }
  }

  void _animate(double dt) {
    _controls?.update();

    _elapsed += dt;
    final t = _elapsed;

    if (_autoFocusEnabled && _focusClusterId != null) {
      final cluster = ShellTopology.clusters.firstWhere(
        (c) => c.id == _focusClusterId,
        orElse: () => ShellTopology.clusters.first,
      );

      // Normalize the cluster's position to get the unit direction on the sphere.
      final cx = cluster.position.x;
      final cy = cluster.position.y;
      final cz = cluster.position.z;
      final len = math.sqrt(cx * cx + cy * cy + cz * cz);
      final dirX = cx / len;
      final dirY = cy / len;
      final dirZ = cz / len;

      // Camera sits at +dir*dist so the cluster faces toward screen centre.
      final cam = _threeJs.camera.position;
      final dist = math.sqrt(cam.x * cam.x + cam.y * cam.y + cam.z * cam.z);
      // 0.04 smoothing factor matches brain.js phi-smoothing constant.
      cam.x += (dirX * dist - cam.x) * 0.04;
      cam.y += (dirY * dist - cam.y) * 0.04;
      cam.z += (dirZ * dist - cam.z) * 0.04;
      _threeJs.camera.lookAt(three.Vector3(0, 0, 0));
    }

    for (var i = 0; i < _neuronMeshes.length; i++) {
      final mesh = _neuronMeshes[i];
      final halo = _haloMeshes[i];

      final baseOpacity = (mesh.userData['baseOpacity'] as double?) ?? 0.78;
      var flare = (mesh.userData['flare'] as double?) ?? 0.0;

      // Decay flare towards zero.
      if (flare > 0.0) {
        flare = math.max(0.0, flare - 1.6 * dt);
        mesh.userData['flare'] = flare;
      }

      final pulse = 1.0 + 0.06 * math.sin(t * 1.2 + i * 0.7);
      final scale = pulse + flare * 1.6;
      final opacity = math.min(1.0, baseOpacity + flare * 0.3);

      mesh.scale.setScalar(scale);
      (mesh.material as three.MeshBasicMaterial).opacity = opacity;

      (halo.material as three.MeshBasicMaterial).opacity = 0.12 + flare * 0.55;
      halo.scale.setScalar(1.0 + flare * 0.6);
    }

    for (final glow in _clusterGlows) {
      var fire = (glow.userData['fire'] as double?) ?? 0.0;
      if (fire > 0) {
        fire = math.max(0.0, fire - dt * 0.9);
        glow.userData['fire'] = fire;
      }
      final base = (glow.userData['baseOpacity'] as double?) ?? 0.05;
      (glow.material as three.MeshBasicMaterial).opacity = base + fire * 0.32;
      glow.scale.setScalar(1.0 + fire * 0.5);
    }

    // Integrate comet synapses — reverse iteration so removeAt(i) is safe.
    for (var i = _synapses.length - 1; i >= 0; i--) {
      final s = _synapses[i];
      if (!s.paused) s.t += dt / s.duration;
      final u = math.min(1.0, s.t);

      // getPoint(u): parameter-uniform sample along the bezier at t=u.
      // Cast to Vector3 — QuadraticBezierCurve3 always returns Vector3 points.
      final p = s.curve.getPoint(u) as three.Vector3?;
      if (p != null) s.head.position.setValues(p.x, p.y, p.z);

      // Tail fades as the comet approaches the target.
      (s.tail.material as three.LineBasicMaterial).opacity =
          0.10 + (1 - u) * 0.30;
      // Head fades out near arrival.
      (s.head.material as three.MeshBasicMaterial).opacity =
          0.6 + 0.4 * (1 - u);
      // Halo brightest at midpoint (u=0.5), dark at both ends.
      (s.halo.material as three.MeshBasicMaterial).opacity =
          0.25 + 0.45 * (1 - (u - 0.5).abs() * 2);

      if (s.t >= 1.05 && !s.paused) {
        _threeJs.scene.remove(s.head);
        _threeJs.scene.remove(s.tail);
        s.head.geometry?.dispose();
        s.head.material?.dispose();
        s.halo.geometry?.dispose();
        s.halo.material?.dispose();
        s.tail.geometry?.dispose();
        s.tail.material?.dispose();
        _synapses.removeAt(i);
      }
    }
  }

  @override
  Widget build(BuildContext context) => _threeJs.build();
}
