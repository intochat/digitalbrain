import 'dart:async';
import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:test/test.dart';

void main() {
  const workId = '4f15ea56-6419-4bc8-b803-d6ba925b47ac';
  const acceptedBody =
      '{"receipt":{"value":"65f17c86-2245-423b-bb31-dbb56a1d9e40"},"work":{"value":"$workId"}}';

  DigitalBrainUiClient clientWith(MockClientHandler handler) {
    final httpClient = MockClient(handler);
    addTearDown(httpClient.close);
    return DigitalBrainUiClient(
      baseUri: Uri.parse('https://brain.example'),
      httpClient: httpClient,
    );
  }

  test('sendMessage posts text and returns a send receipt', () async {
    String? commandId;
    final client = clientWith((request) async {
      expect(request.method, 'POST');
      expect(request.url.path, '/chats/desk/send');
      final body = jsonDecode(request.body) as Map<String, dynamic>;
      commandId = body['commandId'] as String;
      expect(
        commandId,
        matches(
          r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',
        ),
      );
      expect(body, {'commandId': commandId, 'text': 'Hello desk'});
      return http.Response(acceptedBody, 202);
    });

    final receipt = await client.sendMessage(
      chatName: 'desk',
      text: 'Hello desk',
    );
    expect(receipt.turnId, workId);
    expect(receipt.commandId, commandId);
    final second = await client.sendMessage(
      chatName: 'desk',
      text: 'Hello desk',
    );
    expect(second.turnId, workId);
    expect(second.commandId, commandId);
    expect(second.commandId, isNot(receipt.commandId));
  });

  test(
    'sendVoice uploads multipart audio and accepts a JSON receipt',
    () async {
      final client = clientWith((request) async {
        expect(request.method, 'POST');
        expect(request.url.path, '/chats/desk/voice');
        expect(
          request.headers['content-type'],
          startsWith('multipart/form-data;'),
        );
        expect(request.body, contains('name="audio"; filename="voice.wav"'));
        expect(request.body, contains('\r\n\r\naudio bytes\r\n'));
        return http.Response(acceptedBody, 202);
      });

      final receipt = await client.sendVoice(
        chatName: 'desk',
        audioBytes: utf8.encode('audio bytes'),
      );
      expect(receipt.turnId, workId);
      expect(receipt.commandId, isNull);
    },
  );

  for (final status in [503, 422]) {
    test('sendVoice preserves the server message on $status', () async {
      final message = status == 503
          ? 'Voice service is unavailable'
          : 'Audio could not be transcribed';
      final client = clientWith((request) async {
        expect(request.method, 'POST');
        expect(request.url.path, '/chats/desk/voice');
        return http.Response(message, status);
      });

      await expectLater(
        client.sendVoice(chatName: 'desk', audioBytes: [1, 2, 3]),
        throwsA(
          isA<StateError>().having(
            (error) => error.message,
            'message',
            contains(message),
          ),
        ),
      );
    });
  }

  test('cancelTurn posts to the accepted turn cancellation route', () async {
    var requests = 0;
    final client = clientWith((request) async {
      requests++;
      expect(request.method, 'POST');
      expect(request.url.path, '/chats/desk/turns/$workId/cancel');
      return http.Response('', 202);
    });

    await client.cancelTurn(chatName: 'desk', turnId: workId);
    expect(requests, 1);
  });
  test('openSurface posts its surface key and title', () async {
    final client = clientWith((request) async {
      expect(request.method, 'POST');
      expect(request.url.path, '/surfaces/desk/open');
      expect(jsonDecode(request.body), {'surfaceKey': 'home', 'title': 'Home'});
      return http.Response('', 202);
    });
    await client.openSurface(
      surfaceName: 'desk',
      surfaceKey: 'home',
      title: 'Home',
    );
  });

  test('activateControl posts intent and surface key', () async {
    final client = clientWith((request) async {
      expect(request.method, 'POST');
      expect(request.url.path, '/surfaces/desk/controls/confirm/activate');
      expect(jsonDecode(request.body), {
        'intent': 'approve',
        'surfaceKey': 'home',
      });
      return http.Response('', 202);
    });
    await client.activateControl(
      surfaceName: 'desk',
      surfaceKey: 'home',
      controlId: 'confirm',
      intent: 'approve',
    );
  });

  test('chat reader reconnects from the last delivered cursor', () async {
    final secondRequest = Completer<Uri>();
    var connections = 0;
    final httpClient = MockClient.streaming((request, body) async {
      await body.drain<void>();
      expect(request.method, 'GET');
      expect(request.url.path, '/chats/desk/events');
      connections++;
      if (connections == 1) {
        expect(request.url.queryParameters['afterSequence'], '0');
        return http.StreamedResponse(
          Stream.value(
            utf8.encode(
              'event: chat-turn\ndata: {"sequence":5,"fromUser":true,"text":"hello","commandId":"c-1","signal":"TurnAccepted","neuronId":"uichat:desk","correlationId":"r-1","timestamp":"2026-09-10T12:00:00Z","turnId":"t-1","eventId":"e-1"}\n\n',
            ),
          ),
          200,
        );
      }
      if (!secondRequest.isCompleted) secondRequest.complete(request.url);
      return http.StreamedResponse(const Stream.empty(), 200);
    });
    addTearDown(httpClient.close);
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('https://brain.example'),
      httpClient: httpClient,
    );
    final events = <ChatStreamEvent>[];
    final subscription = client
        .watchChatTurns(chatName: 'desk')
        .listen(events.add);
    try {
      final uri = await secondRequest.future.timeout(
        const Duration(seconds: 5),
      );
      expect(uri.queryParameters['afterSequence'], '5');
      expect((events.single as ChatTurnObserved).turn.sequence, 5);
      expect(connections, 2);
    } finally {
      await subscription.cancel();
    }
  });
}
