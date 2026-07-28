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

    await client.openScene(shellName: 'desk', sceneKey: 'home', title: 'Home');

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

  test(
    'watchBrainTopology polls live modules and neurons without restart',
    () async {
      var requestCount = 0;
      final client = DigitalBrainUiEdgeClient(
        baseUri: Uri.parse('http://ui.example:5080'),
        httpClient: MockClient((request) async {
          expect(request.method, 'GET');
          expect(
            request.url.toString(),
            'http://ui.example:5080/brain/topology',
          );
          requestCount++;
          return http.Response(
            jsonEncode({
              'modules': [
                {'id': 'DigitalBrain.Chat.ChatModule'},
              ],
              'capabilities': [
                {'id': 'assistant.general'},
              ],
              'neurons': [
                if (requestCount > 1)
                  {
                    'id': 'chat:owner/main',
                    'grainType': 'chat',
                    'identity': 'owner/main',
                    'placement': 'cluster-1',
                  },
              ],
              'observedAt': '2026-07-28T08:00:00Z',
            }),
            200,
            headers: {'content-type': 'application/json'},
          );
        }),
      );

      final snapshots = await client
          .watchBrainTopology(pollInterval: Duration.zero)
          .take(2)
          .toList();

      expect(snapshots.first.neurons, isEmpty);
      expect(snapshots.last.neurons.single.id, 'chat:owner/main');
      expect(snapshots.last.capabilities.single.id, 'assistant.general');
    },
  );

  test(
    'watchBrainTopology reports a transient failure and keeps polling',
    () async {
      var requestCount = 0;
      final client = DigitalBrainUiEdgeClient(
        baseUri: Uri.parse('http://ui.example:5080'),
        httpClient: MockClient((request) async {
          requestCount++;
          if (requestCount == 1) {
            return http.Response('temporarily unavailable', 503);
          }
          return http.Response(
            jsonEncode({
              'modules': [
                {'id': 'DigitalBrain.Chat.ChatModule'},
              ],
              'capabilities': const [],
              'neurons': const [],
              'observedAt': '2026-07-28T08:00:00Z',
            }),
            200,
            headers: {'content-type': 'application/json'},
          );
        }),
      );
      final errors = <Object>[];

      final snapshot = await client
          .watchBrainTopology(pollInterval: Duration.zero)
          .handleError(errors.add)
          .first;

      expect(requestCount, 2);
      expect(errors, hasLength(1));
      expect(snapshot.modules.single.id, 'DigitalBrain.Chat.ChatModule');
    },
  );

  test('watchBrainTopology aborts a hung request and keeps polling', () async {
    final httpClient = _AbortThenSucceedClient();
    final client = DigitalBrainUiEdgeClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: httpClient,
    );
    final errors = <Object>[];

    final snapshot = await client
        .watchBrainTopology(
          pollInterval: Duration.zero,
          requestTimeout: const Duration(milliseconds: 1),
        )
        .handleError(errors.add)
        .first;

    expect(httpClient.requests, 2);
    expect(httpClient.sawAbortableRequest, isTrue);
    expect(errors.single, isA<http.RequestAbortedException>());
    expect(snapshot.modules.single.id, 'DigitalBrain.Chat.ChatModule');
  });
}

final class _AbortThenSucceedClient extends http.BaseClient {
  int requests = 0;
  bool sawAbortableRequest = false;

  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    requests++;
    if (requests == 1) {
      sawAbortableRequest = request is http.AbortableRequest;
      final abortable = request as http.AbortableRequest;
      await abortable.abortTrigger;
      throw http.RequestAbortedException(request.url);
    }

    return http.StreamedResponse(
      Stream.value(
        utf8.encode(
          jsonEncode({
            'modules': [
              {'id': 'DigitalBrain.Chat.ChatModule'},
            ],
            'capabilities': const [],
            'neurons': const [],
            'observedAt': '2026-07-28T08:00:00Z',
          }),
        ),
      ),
      200,
      headers: {'content-type': 'application/json'},
    );
  }
}
