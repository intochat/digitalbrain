import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:test/test.dart';

void main() {
  test('ShellSurfaceController projects SceneOpened without dropping prior scenes', () {
    final surface = ShellSurfaceController();

    surface.apply(
      const SceneOpenedEvent(
        sequence: 1,
        sceneKey: 'home',
        title: 'Home',
        commandId: 'a',
        shell: 'shell:dev/desk',
      ),
    );
    surface.apply(
      const SceneOpenedEvent(
        sequence: 2,
        sceneKey: 'countdown',
        title: 'Countdown',
        commandId: 'b',
        shell: 'shell:dev/desk',
      ),
    );

    expect(surface.scenes.map((s) => s.sceneKey), ['home', 'countdown']);
    expect(surface.latest?.title, 'Countdown');
    expect(surface.latest?.sequence, 2);
  });

  test('ShellSurfaceController replaces same sceneKey with newer projection', () {
    final surface = ShellSurfaceController();

    surface.apply(
      const SceneOpenedEvent(
        sequence: 1,
        sceneKey: 'home',
        title: 'Home',
        commandId: 'a',
        shell: 'shell:dev/desk',
      ),
    );
    surface.apply(
      const SceneOpenedEvent(
        sequence: 4,
        sceneKey: 'home',
        title: 'Home again',
        commandId: 'c',
        shell: 'shell:dev/desk',
      ),
    );

    expect(surface.scenes, hasLength(1));
    expect(surface.latest?.title, 'Home again');
    expect(surface.latest?.sequence, 4);
  });

  test('parseSseSceneOpenedEvents reads scene-opened frames without restart', () {
    const frames = '''
: connected

id: 3
event: scene-opened
data: {"sequence":3,"sceneKey":"home","title":"Home","commandId":"c","shell":"shell:dev/desk"}

id: 5
event: scene-opened
data: {"sequence":5,"sceneKey":"countdown","title":"Countdown","commandId":"d","shell":"shell:dev/desk"}

''';

    final events = parseSseSceneOpenedEvents(frames).toList();
    expect(events, hasLength(2));
    expect(events[0].sceneKey, 'home');
    expect(events[1].title, 'Countdown');

    final surface = ShellSurfaceController();
    for (final event in events) {
      surface.apply(event);
    }
    expect(surface.scenes.map((s) => '${s.sceneKey}:${s.title}'), [
      'home:Home',
      'countdown:Countdown',
    ]);
  });
}
