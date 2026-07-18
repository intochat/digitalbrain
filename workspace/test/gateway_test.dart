import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:workspace/blocks/block_action.dart';
import 'package:workspace/gateway/brain_gateway.dart';

BrainGateway _gateway(http.Client client) {
  return BrainGateway(
    httpBase: 'http://gateway.test',
    wsBase: 'ws://gateway.test',
    client: client,
  );
}

void main() {
  group('BrainGateway.fetchSnapshot', () {
    test('parses a UiSurfaceSnapshot from the response body', () async {
      final client = MockClient((request) async {
        expect(request.method, 'GET');
        expect(request.url.path, '/ui/surface');
        expect(request.url.queryParameters['surfaceId'], 'surface-1');
        return http.Response(
          jsonEncode({
            'schemaVersion': 1,
            'surface': {
              'surfaceId': 'surface-1',
              'revision': 7,
              'blocks': [
                {
                  'kind': 'text',
                  'text': 'hello',
                  'actions': <Map<String, dynamic>>[],
                },
              ],
            },
          }),
          200,
        );
      });
      final gateway = _gateway(client);

      final snapshot = await gateway.fetchSnapshot('surface-1');

      expect(snapshot.surface.surfaceId, 'surface-1');
      expect(snapshot.surface.revision, 7);
      expect(snapshot.surface.blocks.single.text, 'hello');
    });

    test('throws GatewayException with the code on a 409 response', () async {
      final client = MockClient((request) async {
        return http.Response(
          jsonEncode({'code': 'surface.not_found', 'detail': 'gone'}),
          409,
        );
      });
      final gateway = _gateway(client);

      await expectLater(
        () => gateway.fetchSnapshot('missing'),
        throwsA(
          isA<GatewayException>()
              .having((e) => e.code, 'code', 'surface.not_found')
              .having((e) => e.detail, 'detail', 'gone'),
        ),
      );
    });
  });

  group('BrainGateway.sendSurfaceAction', () {
    test('posts opaque action id with expected revision', () async {
      late Map<String, dynamic> capturedBody;
      late Uri capturedUri;
      final client = MockClient((request) async {
        capturedUri = request.url;
        expect(request.method, 'POST');
        capturedBody = jsonDecode(request.body) as Map<String, dynamic>;
        return http.Response(jsonEncode({'status': 'accepted'}), 200);
      });
      final gateway = _gateway(client);

      await gateway.sendSurfaceAction(
        surfaceId: 'surface-1',
        actionId: 'approve',
        expectedRevision: 3,
      );

      expect(capturedUri.path, '/ui/action');
      expect(capturedBody['surfaceId'], 'surface-1');
      expect(capturedBody['actionId'], 'approve');
      expect(capturedBody['expectedRevision'], 3);
      expect(capturedBody.containsKey('inputJson'), isFalse);
      expect(capturedBody.containsKey('contract'), isFalse);
    });
  });

  group('BrainGateway.mapFrame', () {
    test('rejects unversioned ping frames', () {
      expect(
        () => BrainGateway.mapFrame(jsonEncode({'ping': true})),
        throwsA(
          isA<GatewayException>().having(
            (e) => e.code,
            'code',
            'schema.unsupported',
          ),
        ),
      );
    });

    test('throws for non-JSON text', () {
      expect(
        () => BrainGateway.mapFrame('not json'),
        throwsA(
          isA<GatewayException>().having(
            (e) => e.code,
            'code',
            'frame.invalid',
          ),
        ),
      );
    });

    test('maps a surface snapshot frame', () {
      final frame = BrainGateway.mapFrame(
        jsonEncode({
          'schemaVersion': 1,
          'type': 'snapshot',
          'sequence': 1,
          'surface': {
            'surfaceId': 'surface-1',
            'revision': 1,
            'blocks': <Map<String, dynamic>>[],
          },
        }),
      );

      expect(frame.sequence, 1);
    });

    test('throws for unknown schema version', () {
      expect(
        () => BrainGateway.mapFrame(
          jsonEncode({
            'schemaVersion': 99,
            'type': 'snapshot',
            'sequence': 1,
            'surface': {
              'surfaceId': 'surface-1',
              'revision': 1,
              'blocks': <Map<String, dynamic>>[],
            },
          }),
        ),
        throwsA(
          isA<GatewayException>().having(
            (e) => e.code,
            'code',
            'schema.unsupported',
          ),
        ),
      );
    });
  });
}
