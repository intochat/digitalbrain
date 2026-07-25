import 'package:digitalbrain_wire/digitalbrain_wire.dart';

import 'scene_projection.dart';

/// Host-local shell projection: accumulates scene view models from edge SSE facts.
/// Domain truth remains journals on the brain; this is pixels/state only.
final class ShellSurfaceController {
  final List<SceneViewModel> _scenes = [];

  List<SceneViewModel> get scenes => List.unmodifiable(_scenes);

  SceneViewModel? get latest => _scenes.isEmpty ? null : _scenes.last;

  void apply(SceneOpenedEvent event) {
    final view = projectSceneOpened(event);
    final index = _scenes.indexWhere((scene) => scene.sceneKey == view.sceneKey);
    if (index >= 0) {
      _scenes[index] = view;
    } else {
      _scenes.add(view);
    }
  }
}
