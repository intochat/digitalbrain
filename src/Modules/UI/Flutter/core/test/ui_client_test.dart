import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:test/test.dart';

void main() {
  test('openScene POSTs the explicit surface command', () async {
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
    expect(seen!.url.toString(), 'http://ui.example:5080/owner/commands');
    expect(jsonDecode(seen!.body), {
      'kind': 'surface.open',
      'surfaceName': 'desk',
      'surfaceKey': 'home',
      'title': 'Home',
    });
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
            'http://ui.example:5080/surfaces/desk/events?afterSequence=0',
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

      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://ui.example:5080'),
        httpClient: MockClient((request) async {
          expect(
            request.url.toString(),
            'http://ui.example:5080/surfaces/desk/events?afterSequence=0',
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
    'streamMessage POSTs the explicit chat command and yields chat-delta frames',
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
            'http://ui.example:5080/owner/commands',
          );
          expect(jsonDecode(request.body), {
            'kind': 'chat.send',
            'chatName': 'pulse',
            'text': 'hello',
          });
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
      expect(
        frames.map((frame) => frame.text).join(),
        'the edge probe answered',
      );
      expect(frames.every((frame) => frame.role == 'assistant'), isTrue);
    },
  );

  test('openScene rejects non-202', () async {
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async => http.Response('nope', 500)),
    );

    await expectLater(
      client.openScene(shellName: 'desk', sceneKey: 'home', title: 'Home'),
      throwsA(isA<StateError>()),
    );
  });

  test(
    'readChart GETs /kit/charts/{name} and parses the chart offer',
    () async {
      http.BaseRequest? seen;
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://ui.example:5080'),
        httpClient: MockClient((request) async {
          seen = request;
          return http.Response(
            jsonEncode({
              'title': 'Weekly usage',
              'chartKind': 'bar',
              'points': [
                {'label': 'Mon', 'value': 1},
                {'label': 'Tue', 'value': 2},
              ],
            }),
            200,
            headers: {'content-type': 'application/json'},
          );
        }),
      );

      final chart = await client.readChart('weekly-usage');

      expect(seen, isNotNull);
      expect(seen!.method, 'GET');
      expect(
        seen!.url.toString(),
        'http://ui.example:5080/kit/charts/weekly-usage',
      );
      expect(chart, isNotNull);
      expect(chart!.title, 'Weekly usage');
      expect(chart.chartKind, 'bar');
      expect(chart.points.map((p) => p.label), ['Mon', 'Tue']);
    },
  );

  test(
    'readSpreadsheet GETs /kit/spreadsheets/{name} and parses the sheet',
    () async {
      http.BaseRequest? seen;
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://ui.example:5080'),
        httpClient: MockClient((request) async {
          seen = request;
          return http.Response(
            jsonEncode({
              'title': 'Yesterday',
              'sheetName': 'Sheet1',
              'columns': ['Item', 'Qty'],
              'rows': [
                {
                  'cells': ['Shoes', '2'],
                },
              ],
            }),
            200,
            headers: {'content-type': 'application/json'},
          );
        }),
      );

      final sheet = await client.readSpreadsheet('sheet-abc');

      expect(seen, isNotNull);
      expect(seen!.method, 'GET');
      expect(
        seen!.url.toString(),
        'http://ui.example:5080/kit/spreadsheets/sheet-abc',
      );
      expect(sheet, isNotNull);
      expect(sheet!.title, 'Yesterday');
      expect(sheet.rows.first, ['Shoes', '2']);
    },
  );

  test('readGraph GETs /kit/graphs/{name} and parses the graph', () async {
    http.BaseRequest? seen;
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async {
        seen = request;
        return http.Response(
          jsonEncode({
            'title': 'Module deps',
            'nodes': [
              {'id': 'brain', 'label': 'BRAIN', 'kind': 'hub'},
              {
                'id': 'excel',
                'label': 'EXCEL',
                'kind': 'leaf',
                'cluster': 'modules',
              },
            ],
            'edges': [
              {
                'id': 'brain-excel',
                'sourceId': 'brain',
                'targetId': 'excel',
                'dotted': true,
              },
            ],
          }),
          200,
          headers: {'content-type': 'application/json'},
        );
      }),
    );

    final graph = await client.readGraph('graph-abc');

    expect(seen, isNotNull);
    expect(seen!.method, 'GET');
    expect(seen!.url.toString(), 'http://ui.example:5080/kit/graphs/graph-abc');
    expect(graph, isNotNull);
    expect(graph!.title, 'Module deps');
    expect(graph.nodes.map((n) => n.id), ['brain', 'excel']);
    expect(graph.nodes.first.kind, 'hub');
    expect(graph.nodes.last.cluster, 'modules');
    expect(graph.edges.single.sourceId, 'brain');
    expect(graph.edges.single.dotted, isTrue);
  });

  test('readGraph returns null on 404', () async {
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async => http.Response('', 404)),
    );

    expect(await client.readGraph('missing'), isNull);
  });

  test('readChart returns null on 404', () async {
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async => http.Response('', 404)),
    );

    expect(await client.readChart('missing'), isNull);
  });

  test('readChart throws StateError on non-200/404', () async {
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async => http.Response('bad', 400)),
    );

    await expectLater(client.readChart('bad name'), throwsA(isA<StateError>()));
  });

  test('readImage GETs /kit/images/{name} and returns the raw map', () async {
    http.BaseRequest? seen;
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async {
        seen = request;
        return http.Response(
          jsonEncode({
            'prompt': 'a cat',
            'model': 'dall-e',
            'mediaType': 'image/png',
          }),
          200,
          headers: {'content-type': 'application/json'},
        );
      }),
    );

    final image = await client.readImage('cat-pic');

    expect(seen, isNotNull);
    expect(seen!.method, 'GET');
    expect(seen!.url.toString(), 'http://ui.example:5080/kit/images/cat-pic');
    expect(image, {
      'prompt': 'a cat',
      'model': 'dall-e',
      'mediaType': 'image/png',
    });
  });

  test('readImage returns null on 404', () async {
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async => http.Response('', 404)),
    );

    expect(await client.readImage('missing'), isNull);
  });

  test(
    'readImageBytes GETs /kit/images/{name}/content and returns raw bytes',
    () async {
      http.BaseRequest? seen;
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://ui.example:5080'),
        httpClient: MockClient((request) async {
          seen = request;
          return http.Response.bytes(
            [1, 2, 3, 4],
            200,
            headers: {'content-type': 'image/png'},
          );
        }),
      );

      final bytes = await client.readImageBytes('cat-pic');

      expect(seen, isNotNull);
      expect(seen!.method, 'GET');
      expect(
        seen!.url.toString(),
        'http://ui.example:5080/kit/images/cat-pic/content',
      );
      expect(bytes, [1, 2, 3, 4]);
    },
  );

  test('readImageBytes returns null on 404', () async {
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://ui.example:5080'),
      httpClient: MockClient((request) async => http.Response('', 404)),
    );

    expect(await client.readImageBytes('missing'), isNull);
  });

  test(
    'behavior client lists, saves, tests, runs fake data, and generates',
    () async {
      final requests = <String>[];
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://ui.example:5080'),
        httpClient: MockClient((request) async {
          requests.add('${request.method} ${request.url.path}');
          if (request.url.path == '/behaviors' && request.method == 'GET') {
            return http.Response(
              jsonEncode([
                {
                  'name': 'bitcoin-tracker',
                  'title': 'Bitcoin',
                  'source': 'Feature: Bitcoin',
                  'active': true,
                  'lastTest': {
                    'allGreen': true,
                    'scenarios': 1,
                    'failures': [],
                  },
                  'diagnostics': [],
                },
              ]),
              200,
            );
          }
          if (request.url.path.endsWith('/test')) {
            return http.Response(
              jsonEncode({'allGreen': true, 'scenarios': 1, 'failures': []}),
              200,
            );
          }
          if (request.url.path.endsWith('/fake')) {
            return http.Response(
              jsonEncode({'eventId': 'fake-1', 'description': 'x.post fake'}),
              200,
            );
          }
          if (request.url.path == '/behaviors/generate') {
            return http.Response(
              jsonEncode({
                'source': 'Feature: Generated',
                'model': 'gemma4:e2b',
                'compilation': {'success': true, 'diagnostics': []},
              }),
              200,
            );
          }
          return http.Response('{}', 200);
        }),
      );

      final listed = await client.listBehaviors();
      await client.saveBehavior('bitcoin-tracker', 'Feature: Bitcoin');
      final report = await client.testBehavior('bitcoin-tracker');
      final fake = await client.runBehaviorFake('bitcoin-tracker');
      final generated = await client.generateBehavior('track bitcoin');

      expect(listed.single.active, isTrue);
      expect(report.allGreen, isTrue);
      expect(fake, 'x.post fake');
      expect(generated.model, 'gemma4:e2b');
      expect(generated.success, isTrue);
      expect(
        requests,
        containsAll([
          'GET /behaviors',
          'PUT /behaviors/bitcoin-tracker',
          'POST /behaviors/bitcoin-tracker/test',
          'POST /behaviors/bitcoin-tracker/fake',
          'POST /behaviors/generate',
        ]),
      );
    },
  );
}
