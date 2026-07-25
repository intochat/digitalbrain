import 'package:digitalbrain_wire/digitalbrain_wire.dart';
import 'package:test/test.dart';

void main() {
  test('SceneOpenedEvent round-trips JSON field names used by the UI edge', () {
    const event = SceneOpenedEvent(
      sequence: 7,
      sceneKey: 'home',
      title: 'Home',
      commandId: 'cmd',
      shell: 'shell:dev/desk',
    );

    final restored = SceneOpenedEvent.fromJson(event.toJson());
    expect(restored.sequence, 7);
    expect(restored.sceneKey, 'home');
    expect(restored.title, 'Home');
    expect(restored.commandId, 'cmd');
    expect(restored.shell, 'shell:dev/desk');
  });

  test('OpenSceneRequest encodes camelCase for POST /shells/{shell}/scenes', () {
    expect(
      const OpenSceneRequest(sceneKey: 'home', title: 'Home').toJson(),
      {'sceneKey': 'home', 'title': 'Home'},
    );
  });
}
