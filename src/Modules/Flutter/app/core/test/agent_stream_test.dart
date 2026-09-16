import 'dart:async';
import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:http/http.dart' as http;
import 'package:test/test.dart';

class CaptureClient extends http.BaseClient {
  final response = StreamController<List<int>>();
  final request = Completer<http.BaseRequest>();
  @override
  Future<http.StreamedResponse> send(http.BaseRequest value) async {
    request.complete(value);
    return http.StreamedResponse(response.stream, 200);
  }
}

class PendingClient extends http.BaseClient {
  final request = Completer<http.AbortableRequest>();
  @override
  Future<http.StreamedResponse> send(http.BaseRequest value) async {
    final abortable = value as http.AbortableRequest;
    request.complete(abortable);
    await abortable.abortTrigger;
    throw http.RequestAbortedException(value.url);
  }
}

void main() {
  test('cancellation aborts while waiting for response headers', () async {
    final transport = PendingClient();
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://localhost'),
      httpClient: transport,
    );
    final errors = <Object>[];
    final subscription = client
        .runAgent(threadId: 't', runId: 'r', text: 'Hello')
        .listen((_) {}, onError: errors.add);
    final request = await transport.request.future;
    await subscription.cancel();
    await request.abortTrigger;
    await Future<void>.delayed(Duration.zero);
    expect(errors, isEmpty);
  });

  test('terminal event keeps HTTP alive until delayed EOF', () async {
    final transport = CaptureClient();
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://localhost'),
      httpClient: transport,
    );
    final observed = <AgentEvent>[];
    var closed = false;
    var aborted = false;
    final subscription = client
        .runAgent(threadId: 't', runId: 'r', text: 'Hello')
        .listen(
          observed.add,
          onDone: () {
            closed = true;
          },
        );
    final request = await transport.request.future as http.AbortableRequest;
    unawaited(
      request.abortTrigger!.then((_) {
        aborted = true;
      }),
    );
    transport.response.add(utf8.encode('data: {"type":"RUN_FINISHED"}\n\n'));
    await Future<void>.delayed(Duration.zero);
    expect(observed.single.type, 'RUN_FINISHED');
    expect(closed, false);
    expect(aborted, false);
    await transport.response.close();
    await Future<void>.delayed(Duration.zero);
    expect(closed, true);
    await subscription.cancel();
  });

  test('decodes split UTF8, CRLF, multiline data and standard event types', () async {
    final source =
        ': keepalive\r\ndata: {"type":"RUN_STARTED",\r\ndata: "threadId":"t","runId":"r"}\r\n\r\n'
        'data: {"type":"TEXT_MESSAGE_START","messageId":"m"}\n\n'
        'data: {"type":"TEXT_MESSAGE_CONTENT","messageId":"m","delta":"héllo"}\n\n'
        'data: {"type":"TEXT_MESSAGE_END","messageId":"m"}\n\n'
        'data: {"type":"TOOL_CALL_START","toolCallId":"c","toolCallName":"search_web"}\n\n'
        'data: {"type":"TOOL_CALL_ARGS","toolCallId":"c","delta":"{}"}\n\n'
        'data: {"type":"TOOL_CALL_END","toolCallId":"c"}\n\n'
        'data: {"type":"TOOL_CALL_RESULT","toolCallId":"c","content":"{}"}\n\n'
        'data: {"type":"RUN_FINISHED"}\n\n';
    final events = await decodeAgentEvents(
      Stream.fromIterable(utf8.encode(source).map((b) => [b])),
    ).toList();
    expect(events.length, 9);
    expect(events[2].data['delta'], 'héllo');
    expect(events[7].type, 'TOOL_CALL_RESULT');
  });

  test('preserves run errors and rejects malformed event JSON', () async {
    final events = await decodeAgentEvents(
      Stream.value(
        utf8.encode('data: {"type":"RUN_ERROR","message":"failed"}\n\n'),
      ),
    ).toList();
    expect(events.single.data['message'], 'failed');
    await expectLater(
      decodeAgentEvents(Stream.value(utf8.encode('data: invalid\n\n'))),
      emitsError(isA<FormatException>()),
    );
  });

  test('POST sends only new input with session parent and auth; cancellation aborts', () async {
    final transport = CaptureClient();
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://localhost:5000'),
      httpClient: transport,
      credentials: const BasicCredentials(
        username: 'owner',
        password: 'secret',
      ),
    );
    final subscription = client
        .runAgent(
          threadId: 'thread',
          runId: 'run',
          parentRunId: 'parent',
          text: 'Next',
        )
        .listen((_) {});
    final request = await transport.request.future;
    expect(request.url.path, '/agent');
    expect(request.method, 'POST');
    expect(request.headers['authorization'], startsWith('Basic '));
    final body = jsonDecode((request as http.Request).body) as Map;
    expect(body['parentRunId'], 'parent');
    expect(body['messages'], [containsPair('content', 'Next')]);
    expect(body['tools'], isEmpty);
    expect(request, isA<http.AbortableRequest>());
    final aborted = expectLater(
      (request as http.AbortableRequest).abortTrigger,
      completes,
    );
    await subscription.cancel();
    await aborted;
    await transport.response.close();
  });
}
