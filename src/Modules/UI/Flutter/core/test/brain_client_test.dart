import 'dart:convert';
import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:test/test.dart';

void main() {
  test(
    'brain snapshot preserves edge provenance, scope, timestamps and bounded payloads',
    () async {
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://localhost:5080'),
        httpClient: MockClient((request) async {
          expect(request.url.path, '/chats/main/brain');
          return http.Response(
            jsonEncode({
              'rootId': 'chat:main',
              'observedAt': '2026-09-05T12:00:00Z',
              'scope': 'Current chat',
              'truncated': true,
              'nodes': [
                {
                  'id': 'chat:main',
                  'type': 'chat',
                  'name': 'main',
                  'label': 'Chat',
                  'module': 'UI',
                  'handledSignals': ['Tick'],
                },
              ],
              'synapses': [
                {
                  'id': 'e1',
                  'sourceId': 'timer:main',
                  'targetId': 'chat:main',
                  'signalType': 'Tick',
                  'kind': 'Bound',
                  'fireCount': 4,
                  'canUnsubscribe': true,
                },
              ],
              'activity': [
                {
                  'id': 'a1',
                  'neuronId': 'chat:main',
                  'direction': 'Incoming',
                  'sequence': 3,
                  'signalType': 'Tick',
                  'timestamp': '2026-09-05T11:59:59Z',
                  'summary': 'Handled Tick',
                  'payloadPreview': {'count': 2},
                },
              ],
            }),
            200,
          );
        }),
      );
      final snapshot = await client.readBrain(chatName: 'main');
      expect(snapshot.truncated, isTrue);
      expect(snapshot.scope, 'Current chat');
      expect(snapshot.nodes.single.handledSignals, ['Tick']);
      expect(snapshot.synapses.single.canUnsubscribe, isTrue);
      expect(snapshot.synapses.single.fireCount, 4);
      expect(snapshot.activity.single.payloadPreview, {'count': 2});
    },
  );
  test(
    'subscription write uses the authenticated source-owned route',
    () async {
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://localhost:5080'),
        httpClient: MockClient((request) async {
          expect(request.method, 'POST');
          expect(request.url.path, '/chats/main/brain/subscriptions');
          expect(jsonDecode(request.body), {
            'sourceId': 'timer:review',
            'targetId': 'behavior:review',
            'signalType': 'Tick',
            'subscribed': false,
          });
          return http.Response('', 204);
        }),
      );
      await client.setBrainSubscription(
        chatName: 'main',
        sourceId: 'timer:review',
        targetId: 'behavior:review',
        signalType: 'Tick',
        subscribed: false,
      );
    },
  );
}
