import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:test/test.dart';

void main() {
  test('openScene POSTs OpenSceneRequest to UI HTTP root path', () async {
    http.Request? seen;
    final client = DigitalBrainUiClient(
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

      final client = DigitalBrainUiClient(
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

  test('watchAuthorizations streams authorization journal SSE', () async {
    const body = '''
: connected

id: 1
event: authorization
data: {"sequence":1,"kind":"AuthorizationRequired","commandId":"c1","serverKey":"google.gmail","serverDisplayName":"DigitalBrain Gmail","signInUrl":"https://ui.test/oauth?state=s1","state":"s1","timestamp":"2026-07-28T08:00:00Z"}

id: 2
event: noise
data: {"sequence":2,"kind":"AuthorizationRequired","commandId":"c2","serverKey":"x","state":"x","timestamp":"2026-07-28T08:00:01Z"}

id: 3
event: authorization
data: {"sequence":3,"kind":"AuthorizationCompleted","commandId":"c1","serverKey":"google.gmail","state":"s1","timestamp":"2026-07-28T08:00:02Z"}

''';

    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async {
        expect(request.method, 'GET');
        expect(
          request.url.toString(),
          'http://ui.example:5080/authorizations/events?afterSequence=0',
        );
        return http.Response(
          body,
          200,
          headers: {'content-type': 'text/event-stream'},
        );
      }),
    );

    final events = await client.watchAuthorizations().toList();
    expect(events, hasLength(2));
    expect(events[0].isRequired, isTrue);
    expect(events[0].serverDisplayName, 'DigitalBrain Gmail');
    expect(events[1].isCompleted, isTrue);
    expect(SignInCardProjection.project(events), isEmpty);
  });

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

      final client = DigitalBrainUiClient(
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

  test(
    'streamMessage POSTs to /messages/stream and yields chat-delta frames',
    () async {
      const body = '''
event: chat-delta
data: {"role":"assistant","contents":[{"\$type":"text","text":"the edge "}]}

event: chat-delta
data: {"role":"assistant","contents":[{"\$type":"text","text":"probe answered"}]}

event: noise
data: {"role":"assistant","contents":[{"\$type":"text","text":"ignore"}]}

''';

      http.BaseRequest? seen;
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://ui.example:5080'),
        httpClient: MockClient((request) async {
          seen = request;
          expect(request.method, 'POST');
          expect(
            request.url.toString(),
            'http://ui.example:5080/conversations/pulse/messages/stream',
          );
          expect(jsonDecode(request.body), {'text': 'hello'});
          return http.Response(
            body,
            200,
            headers: {'content-type': 'text/event-stream'},
          );
        }),
      );

      final frames = await client
          .streamMessage(chatName: 'pulse', text: 'hello')
          .toList();

      expect(seen, isNotNull);
      expect(frames, hasLength(2));
      expect(frames.map((frame) => frame.text).join(), 'the edge probe answered');
      expect(frames.every((frame) => frame.role == 'assistant'), isTrue);
    },
  );

  test('openScene and activateControl reject non-202', () async {
    final client = DigitalBrainUiClient(
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

  test('readBrainTopology loads modules and active neurons once', () async {
    var requestCount = 0;
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async {
        expect(request.method, 'GET');
        expect(request.url.toString(), 'http://ui.example:5080/brain/topology');
        requestCount++;
        return http.Response(
          jsonEncode({
            'modules': [
              {'id': 'DigitalBrain.Chat.ChatModule'},
            ],
            'neurons': [
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

    final snapshot = await client.readBrainTopology();

    expect(requestCount, 1);
    expect(snapshot.modules.single.id, 'DigitalBrain.Chat.ChatModule');
    expect(snapshot.neurons.single.id, 'chat:owner/main');
  });

  test('readBrainTopology surfaces a failed topology response', () async {
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient(
        (request) async => http.Response('temporarily unavailable', 503),
      ),
    );

    await expectLater(
      client.readBrainTopology(),
      throwsA(isA<StateError>()),
    );
  });

  test('readBrainTopology aborts a hung request', () async {
    final httpClient = _AbortThenSucceedClient();
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: httpClient,
    );

    await expectLater(
      client.readBrainTopology(requestTimeout: const Duration(milliseconds: 1)),
      throwsA(isA<http.RequestAbortedException>()),
    );

    expect(httpClient.requests, 1);
    expect(httpClient.sawAbortableRequest, isTrue);
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
