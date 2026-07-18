import 'package:flutter/widgets.dart';
import 'package:three_js/three_js.dart' as three;

sealed class PickResult {
  const PickResult();
  factory PickResult.node(String nodeId) = NodePick;
  factory PickResult.pulse(String fireEventId) = PulsePick;
}

class NodePick extends PickResult {
  const NodePick(this.nodeId);
  final String nodeId;
}

class PulsePick extends PickResult {
  const PulsePick(this.fireEventId);
  final String fireEventId;
}

class BrainPicker {
  BrainPicker(this._three) : _raycaster = three.Raycaster();

  final three.ThreeJS _three;
  final three.Raycaster _raycaster;

  PickResult? pick(Offset localPosition, List<three.Object3D> targets) {
    final width = _three.width;
    final height = _three.height;
    if (width == 0 || height == 0) return null;
    final ndc = three.Vector2(
      (localPosition.dx / width) * 2 - 1,
      -((localPosition.dy / height) * 2 - 1),
    );
    _raycaster.setFromCamera(ndc, _three.camera);
    final hits = _raycaster.intersectObjects(targets, false);
    if (hits.isEmpty) return null;
    final mesh = hits.first.object;
    if (mesh == null) return null;
    final nodeId = mesh.userData['nodeId'] as String?;
    if (nodeId != null) return PickResult.node(nodeId);
    final fireEventId = mesh.userData['fireEventId'] as String?;
    if (fireEventId != null) return PickResult.pulse(fireEventId);
    return null;
  }
}
