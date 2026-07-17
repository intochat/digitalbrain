import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:workspace/gateway/brain_gateway.dart';
import 'package:workspace/gateway/envelope.dart';

void main() {
  group('BrainGateway.invoke', () {
    test('returns the decoded receipt on a 200 response', () async {
      final client = MockClient((request) async {
        expect(request.method, 'POST');
        expect(request.url.toString(), 'http://gateway.test/ui/invoke');
        final body = jsonDecode(request.body) as Map<String, dynamic>;
        expect(body['address'], 'chat/main');
        expect(body['contract'], 'chat.post.v1');
        expect(body['commandId'], 'cmd-1');
        return http.Response(
          jsonEncode({
            'commandId': 'cmd-1',
            'revision': 3,
            'status': 'applied',
            'outputJson': '{}',
          }),
          200,
        );
      });
      final gateway = BrainGateway(
        httpBase: 'http://gateway.test',
        wsBase: 'ws://gateway.test',
        client: client,
      );

      final receipt = await gateway.invoke(
        'chat/main',
        'chat.post.v1',
        '{"text":"hi"}',
        'cmd-1',
      );

      expect(receipt['revision'], 3);
      expect(receipt['status'], 'applied');
    });

    test('throws GatewayException with the code on a 409 response', () async {
      final client = MockClient((request) async {
        return http.Response(
          jsonEncode({'code': 'input.invalid', 'detail': 'x'}),
          409,
        );
      });
      final gateway = BrainGateway(
        httpBase: 'http://gateway.test',
        wsBase: 'ws://gateway.test',
        client: client,
      );

      await expectLater(
        () => gateway.invoke('chat/main', 'chat.post.v1', '{}', 'cmd-2'),
        throwsA(
          isA<GatewayException>()
              .having((e) => e.code, 'code', 'input.invalid')
              .having((e) => e.detail, 'detail', 'x'),
        ),
      );
    });

    test('throws a http.error GatewayException for other statuses', () async {
      final client = MockClient((request) async {
        return http.Response('server exploded', 500);
      });
      final gateway = BrainGateway(
        httpBase: 'http://gateway.test',
        wsBase: 'ws://gateway.test',
        client: client,
      );

      await expectLater(
        () => gateway.invoke('chat/main', 'chat.post.v1', '{}', 'cmd-3'),
        throwsA(
          isA<GatewayException>().having((e) => e.code, 'code', 'http.error'),
        ),
      );
    });
  });

  group('BrainGateway.read', () {
    test('parses a NeuronSnapshot from the response body', () async {
      final client = MockClient((request) async {
        expect(request.method, 'GET');
        expect(request.url.path, '/ui/read');
        expect(request.url.queryParameters['address'], 'chat/main');
        expect(request.url.queryParameters['projection'], 'default');
        return http.Response(
          jsonEncode({'revision': 7, 'stateJson': '{"messages":[]}'}),
          200,
        );
      });
      final gateway = BrainGateway(
        httpBase: 'http://gateway.test',
        wsBase: 'ws://gateway.test',
        client: client,
      );

      final snapshot = await gateway.read('chat/main');

      expect(snapshot.revision, 7);
      expect(snapshot.stateJson, '{"messages":[]}');
    });
  });

  group('BrainGateway.describe', () {
    test('parses a NeuronDescription from the response body', () async {
      final client = MockClient((request) async {
        expect(request.method, 'GET');
        expect(request.url.path, '/ui/describe');
        expect(request.url.queryParameters['address'], 'chat/main');
        return http.Response(
          jsonEncode({
            'kind': 'chat',
            'revision': 7,
            'contracts': ['chat.post.v1'],
          }),
          200,
        );
      });
      final gateway = BrainGateway(
        httpBase: 'http://gateway.test',
        wsBase: 'ws://gateway.test',
        client: client,
      );

      final description = await gateway.describe('chat/main');

      expect(description.kind, 'chat');
      expect(description.revision, 7);
      expect(description.contracts, ['chat.post.v1']);
    });
  });

  group('BrainGateway.mapFrame', () {
    test('skips ping frames', () {
      expect(BrainGateway.mapFrame(jsonEncode({'ping': true})), isNull);
    });

    test('maps a record frame to a FeedFrame', () {
      final frame = BrainGateway.mapFrame(
        jsonEncode({
          'sequence': 12,
          'record': {'kind': 'chat.posted'},
        }),
      );

      expect(frame, isNotNull);
      expect(frame!.sequence, 12);
      expect(frame.record['kind'], 'chat.posted');
    });
  });
}
