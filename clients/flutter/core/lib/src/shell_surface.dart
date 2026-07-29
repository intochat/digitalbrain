import 'ui_edge_models.dart';

final class SceneViewModel {
  const SceneViewModel({
    required this.sceneKey,
    required this.title,
    required this.sequence,
  });

  final String sceneKey;
  final String title;
  final int sequence;
}

final class ShellSurfaceController {
  final List<SceneViewModel> _scenes = [];
  SceneViewModel? _latest;

  List<SceneViewModel> get scenes => List.unmodifiable(_scenes);

  SceneViewModel? get latest => _latest;

  SceneViewModel apply(SceneOpenedEvent event) {
    final view = SceneViewModel(
      sceneKey: event.sceneKey,
      title: event.title,
      sequence: event.sequence,
    );
    final index = _scenes.indexWhere(
      (scene) => scene.sceneKey == view.sceneKey,
    );
    if (index >= 0) {
      _scenes[index] = view;
    } else {
      _scenes.add(view);
    }
    _latest = view;
    return view;
  }
}
