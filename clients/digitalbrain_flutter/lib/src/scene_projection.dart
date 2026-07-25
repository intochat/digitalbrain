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

SceneViewModel projectSceneOpened(SceneOpenedEvent event) {
  return SceneViewModel(
    sceneKey: event.sceneKey,
    title: event.title,
    sequence: event.sequence,
  );
}
