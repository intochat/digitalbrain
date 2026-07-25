import 'package:digitalbrain_wire/digitalbrain_wire.dart';

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

  List<SceneViewModel> get scenes => List.unmodifiable(_scenes);

  SceneViewModel? get latest => _scenes.isEmpty ? null : _scenes.last;

  void apply(SceneOpenedEvent event) {
    final view = SceneViewModel(
      sceneKey: event.sceneKey,
      title: event.title,
      sequence: event.sequence,
    );
    final index = _scenes.indexWhere((scene) => scene.sceneKey == view.sceneKey);
    if (index >= 0) {
      _scenes[index] = view;
    } else {
      _scenes.add(view);
    }
  }
}
