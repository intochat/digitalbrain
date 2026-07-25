import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:test/test.dart';

void main() {
  test('projectSceneOpened maps edge event to host view model', () {
    final view = projectSceneOpened(
      const SceneOpenedEvent(
        sequence: 3,
        sceneKey: 'countdown',
        title: 'Countdown',
        commandId: 'c',
        shell: 'shell:dev/desk',
      ),
    );

    expect(view.sceneKey, 'countdown');
    expect(view.title, 'Countdown');
    expect(view.sequence, 3);
  });
}
