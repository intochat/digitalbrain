import 'package:three_js/three_js.dart' as three;
import 'package:ino_flutter/screens/brain/brain_topology.dart';
import 'package:ino_flutter/state/brain_inspector_bloc.dart';

class _ActivePulse {
  _ActivePulse(this.fire, this.mesh, this.from, this.to, this.start);
  final FireEvent fire;
  final three.Mesh mesh;
  final three.Vector3 from;
  final three.Vector3 to;
  final double start;
  double t = 0;
}

class BrainPulseAnimator {
  BrainPulseAnimator(this._scene, BrainTopology topology) {
    _positions = {
      for (final n in topology.nodes) n.id: three.Vector3(n.x, n.y, n.z),
    };
  }

  static const double _travelSeconds = 1.4;

  final three.Scene _scene;
  late final Map<String, three.Vector3> _positions;
  final List<_ActivePulse> _active = [];
  double _now = 0;
  String? _pausedFireEventId;

  void setPaused(String? fireEventId) => _pausedFireEventId = fireEventId;

  FireEvent? lookupFire(String fireEventId) {
    for (final p in _active) {
      if (p.fire.id == fireEventId) return p.fire;
    }
    return null;
  }

  Iterable<three.Object3D> get meshes => _active.map((p) => p.mesh);

  void tick(double dt) {
    _now += dt;
    final paused = _pausedFireEventId;
    for (var i = _active.length - 1; i >= 0; i--) {
      final p = _active[i];
      if (paused != null && p.fire.id == paused) continue;
      p.t = ((_now - p.start) / _travelSeconds).clamp(0.0, 1.0);
      final eased = p.t * p.t * (3 - 2 * p.t);
      p.mesh.position.setValues(
        p.from.x + (p.to.x - p.from.x) * eased,
        p.from.y + (p.to.y - p.from.y) * eased,
        p.from.z + (p.to.z - p.from.z) * eased,
      );
      if (p.t >= 1.0) {
        _scene.remove(p.mesh);
        _active.removeAt(i);
      }
    }
  }

  void spawn(FireEvent fire) {
    final from = _positions[fire.fromId];
    final to = _positions[fire.toId];
    if (from == null || to == null) return;
    final mesh = three.Mesh(
      three.SphereGeometry(0.07, 12, 8),
      three.MeshBasicMaterial.fromMap({
        'color': 0x5EEAD4,
        'transparent': true,
        'opacity': 0.95,
      }),
    );
    mesh.userData['fireEventId'] = fire.id;
    mesh.position.setValues(from.x, from.y, from.z);
    _scene.add(mesh);
    _active.add(_ActivePulse(fire, mesh, from, to, _now));
  }

  void dispose() {
    for (final p in _active) {
      _scene.remove(p.mesh);
    }
    _active.clear();
  }
}
