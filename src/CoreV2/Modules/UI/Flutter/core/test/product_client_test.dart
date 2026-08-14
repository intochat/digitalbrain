import 'dart:convert';

import 'package:digitalbrain_corev2/digitalbrain_corev2.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:test/test.dart';

void main() {
  test('client speaks the chat, journal, and BrainGraph protocol', () async {
    final requests = <http.Request>[];
    final client = DigitalBrainProductClient(
      baseUri: Uri.parse('http://product.test:5100'),
      httpClient: MockClient((request) async {
        requests.add(request);
        if (request.url.path == '/v2/modules') {
          return http.Response(
            '[{"id":"proof","displayName":"Proof","status":"ready","setupMessage":null}]',
            200,
          );
        }
        if (request.url.path == '/v2/operations') {
          return http.Response(
            '[{"id":"Proof.Run@1","moduleId":"proof","displayName":"Run durable proof","inputSchema":"{}","resultSchema":"{}"}]',
            200,
          );
        }
        if (request.url.path == '/v2/chat') {
          return http.Response(
            '{"activityId":"9e6a3057-5ba1-49f3-923d-400617914658","turn":{"response":"Proof completed.","tools":[{"operationId":"Proof.Run@1","resultJson":"{}"}]}}',
            200,
          );
        }
        if (request.url.path == '/v2/brain') {
          return http.Response(
            '{"workspaceId":"local","sequence":2,"observedAt":"2026-08-14T20:00:00Z","neurons":[{"id":"proof/source/local","moduleId":"proof","roleId":"source","scope":"local","firingCount":1}],"synapses":[]}',
            200,
          );
        }
        if (request.url.path.endsWith('/journal')) {
          return http.Response(
            '{"workspaceId":"local","activityId":"9e6a3057-5ba1-49f3-923d-400617914658","afterSequence":0,"lastSequence":1,"records":[{"sequence":1,"recordId":"11111111-1111-1111-1111-111111111111","workspaceId":"local","activityId":"9e6a3057-5ba1-49f3-923d-400617914658","principalId":"owner","neuronId":"ui/chat/principal","direction":0,"contractId":"Chat.UserMessage@1","firingId":"22222222-2222-2222-2222-222222222222","causeFiringId":null,"synapseId":null,"synapseRevision":null,"occurredAt":"2026-08-14T20:00:00Z","routeCount":0,"outcome":"received","summary":"hello"}],"hasMore":false}',
            200,
          );
        }
        if (request.method == 'POST') {
          return http.Response(
            '{"activityId":"9e6a3057-5ba1-49f3-923d-400617914658","operationId":"Proof.Run@1"}',
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
    final chat = await client.sendChat(
      'Wire and run proof',
      idempotencyKey: 'chat-1',
    );
    final brain = await client.getBrain();
    final journal = await client.getJournal(chat.activityId);

    expect(modules.single.statusLabel, 'Ready');
    expect(receipt.operationId, 'Proof.Run@1');
    expect(chat.turn.response, 'Proof completed.');
    expect(brain.neurons.single.roleId, 'source');
    expect(journal.records.single.contractId, 'Chat.UserMessage@1');
    final invocation = requests.firstWhere(
      (request) => request.url.path.contains('/v2/operations/'),
    );
    expect(invocation.url.toString(), contains('Proof.Run%401:invoke'));
    expect(invocation.headers['Idempotency-Key'], 'request-1');
    expect(jsonDecode(invocation.body), {
      'input': {'value': 'hello'},
    });
  });
}
