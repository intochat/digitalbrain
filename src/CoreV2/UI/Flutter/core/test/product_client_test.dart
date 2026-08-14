import 'dart:convert';

import 'package:digitalbrain_corev2/digitalbrain_corev2.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:test/test.dart';

void main() {
  test('client discovers and invokes canonical module operations', () async {
    final requests = <http.Request>[];
    final client = DigitalBrainProductClient(
      baseUri: Uri.parse('http://product.test:5100'),
      httpClient: MockClient((request) async {
        requests.add(request);
        if (request.url.path == '/v2/modules') {
          return http.Response(
            '[{"id":"proof","displayName":"Proof","status":0,"setupMessage":null}]',
            200,
          );
        }
        if (request.url.path == '/v2/operations') {
          return http.Response(
            '[{"id":"proof/run@1","moduleId":"proof","displayName":"Run durable proof","inputSchema":"{}","resultSchema":"{}"}]',
            200,
          );
        }
        if (request.method == 'POST') {
          return http.Response(
            '{"activity":"9e6a3057-5ba1-49f3-923d-400617914658","operationId":"proof/run@1"}',
            202,
          );
        }
        throw StateError('Unexpected request ${request.method} ${request.url}');
      }),
    );

    final modules = await client.getModules();
    final operations = await client.getOperations();
    final receipt = await client.invoke(operations.single.id, {
      'value': 'hello',
    }, idempotencyKey: 'request-1');

    expect(modules.single.statusLabel, 'Ready');
    expect(receipt.operationId, 'proof/run@1');
    expect(requests.last.url.toString(), contains('proof%2Frun%401:invoke'));
    expect(requests.last.headers['Idempotency-Key'], 'request-1');
    expect(jsonDecode(requests.last.body), {
      'input': {'value': 'hello'},
    });
  });
}
