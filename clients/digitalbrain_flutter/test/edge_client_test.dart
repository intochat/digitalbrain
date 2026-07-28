import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:test/test.dart';

void main() {
  test('openScene POSTs OpenSceneRequest to Ui edge root path', () async {
    http.Request? seen;
    final client = DigitalBrainUiEdgeClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async {
        seen = request;
        return http.Response('', 202);
      }),
    );

    await client.openScene(
      shellName: 'desk',
      sceneKey: 'home',
      title: 'Home',
    );

    expect(seen, isNotNull);
    expect(seen!.method, 'POST');
    expect(seen!.url.toString(), 'http://ui.example:5080/shells/desk/scenes');
    expect(jsonDecode(seen!.body), {'sceneKey': 'home', 'title': 'Home'});
  });

  test(
    'watchShellEvents streams scene-opened SSE into pure projection only',
    () async {
      const body = '''
: connected

id: 3
event: scene-opened
data: {"sequence":3,"sceneKey":"home","title":"Home","commandId":"c","shell":"shell:dev/desk"}

id: 4
event: noise
data: {"sequence":4,"sceneKey":"ignore","title":"Ignore","commandId":"n","shell":"shell:dev/desk"}

id: 5
event: scene-opened
data: {"sequence":5,"sceneKey":"countdown","title":"Countdown","commandId":"d","shell":"shell:dev/desk"}

''';

      final client = DigitalBrainUiEdgeClient(
        baseUri: Uri.parse('http://ui.example:5080'),
        httpClient: MockClient((request) async {
          expect(request.method, 'GET');
          expect(
            request.url.toString(),
            'http://ui.example:5080/shells/desk/events?afterSequence=0',
          );
          return http.Response(
            body,
            200,
            headers: {'content-type': 'text/event-stream'},
          );
        }),
      );

      final surface = ShellSurfaceController();
      final projected = <SceneViewModel>[];
      await for (final event in client.watchShellEvents(shellName: 'desk')) {
        projected.add(surface.apply(event));
      }

      expect(projected.map((v) => v.sceneKey), ['home', 'countdown']);
      expect(surface.scenes.map((s) => s.sceneKey), ['home', 'countdown']);
      expect(surface.latest?.title, 'Countdown');
    },
  );

  test(
    'watchShellEvents multi-event SSE projects into one ShellSurfaceController without restart',
    () async {
      const body = '''
: connected

id: 1
event: scene-opened
data: {"sequence":1,"sceneKey":"home","title":"Home","commandId":"a","shell":"shell:dev/desk"}

id: 2
event: scene-opened
data: {"sequence":2,"sceneKey":"countdown","title":"Countdown","commandId":"b","shell":"shell:dev/desk"}

id: 3
event: scene-opened
data: {"sequence":3,"sceneKey":"home","title":"Home refreshed","commandId":"c","shell":"shell:dev/desk"}

''';

      final client = DigitalBrainUiEdgeClient(
        baseUri: Uri.parse('http://ui.example:5080'),
        httpClient: MockClient((request) async {
          expect(
            request.url.toString(),
            'http://ui.example:5080/shells/desk/events?afterSequence=0',
          );
          return http.Response(
            body,
            200,
            headers: {'content-type': 'text/event-stream'},
          );
        }),
      );

      final surface = ShellSurfaceController();
      final surfaceIdentity = identityHashCode(surface);
      final intermediate = <List<String>>[];

      await for (final event in client.watchShellEvents(shellName: 'desk')) {
        surface.apply(event);
        intermediate.add(
          surface.scenes.map((s) => '${s.sceneKey}:${s.title}').toList(),
        );
      }

      expect(identityHashCode(surface), surfaceIdentity);
      expect(intermediate, [
        ['home:Home'],
        ['home:Home', 'countdown:Countdown'],
        ['home:Home refreshed', 'countdown:Countdown'],
      ]);
      expect(surface.scenes, hasLength(2));
      expect(surface.latest?.title, 'Home refreshed');
      expect(surface.latest?.sequence, 3);
    },
  );

  test('openScene and activateControl reject non-202', () async {
    final client = DigitalBrainUiEdgeClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async => http.Response('nope', 500)),
    );

    await expectLater(
      client.openScene(shellName: 'desk', sceneKey: 'home', title: 'Home'),
      throwsA(isA<StateError>()),
    );
    await expectLater(
      client.activateControl(
        sceneName: 'home',
        controlId: 'primary',
        intent: 'submit',
      ),
      throwsA(isA<StateError>()),
    );
  });
}
