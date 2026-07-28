import 'package:digitalbrain_wire/digitalbrain_wire.dart';
import 'package:test/test.dart';

void main() {
  test('SceneOpenedEvent reads JSON field names used by the UI edge', () {
    final event = SceneOpenedEvent.fromJson({
      'sequence': 7,
      'sceneKey': 'home',
      'title': 'Home',
      'commandId': 'cmd',
      'shell': 'shell:dev/desk',
    });

    expect(event.sequence, 7);
    expect(event.sceneKey, 'home');
    expect(event.title, 'Home');
    expect(event.commandId, 'cmd');
    expect(event.shell, 'shell:dev/desk');
  });

  test('OpenSceneRequest encodes camelCase for POST /shells/{shell}/scenes', () {
    expect(
      const OpenSceneRequest(sceneKey: 'home', title: 'Home').toJson(),
      {'sceneKey': 'home', 'title': 'Home'},
    );
  });

  test('ActivateControlRequest omits null sceneKey', () {
    expect(
      const ActivateControlRequest(intent: 'submit').toJson(),
      {'intent': 'submit'},
    );
    expect(
      const ActivateControlRequest(intent: 'submit', sceneKey: 'home').toJson(),
      {'intent': 'submit', 'sceneKey': 'home'},
    );
  });
}
