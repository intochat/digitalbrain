import 'dart:convert';
import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

class _PendingActivityClient extends http.BaseClient {
  _PendingActivityClient({this.respondWithIdleStream = false});
  final bool respondWithIdleStream;
  final started = Completer<void>();
  final aborted = Completer<void>();
  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    final abortable = request as http.AbortableRequest;
    started.complete();
    if (respondWithIdleStream) {
      final body = StreamController<List<int>>();
      unawaited(
        abortable.abortTrigger!.then((_) async {
          aborted.complete();
          await body.close();
        }),
      );
      return http.StreamedResponse(body.stream, 200);
    }
    await abortable.abortTrigger;
    aborted.complete();
    throw http.RequestAbortedException();
  }
}

void main() {
  test('activity snapshot parses metadata and gap', () {
    final snapshot = ActivitySnapshot.fromJson({
      'nextSequence': 4,
      'gap': true,
      'observedAt': '2026-09-25T12:00:00Z',
      'events': [
        {
          'scopeId': 'workspace-one',
          'sequence': 4,
          'id': 'event',
          'operationId': 'operation',
          'at': '2026-09-25T12:00:00Z',
          'kind': 4,
          'sourceId': 'source',
          'targetId': null,
          'type': 'Changed',
          'status': 'published',
        },
      ],
    });
    expect(snapshot.gap, isTrue);
    expect(snapshot.events.single.kind, 'SignalPublished');
    expect(snapshot.events.single.sourceId, 'source');
  });

  test('client reads a scoped snapshot', () async {
    final paths = <String>[];
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://localhost:5000'),
      httpClient: MockClient((request) async {
        paths.add(request.url.path);
        return http.Response(
          jsonEncode({
            'nextSequence': 0,
            'gap': false,
            'observedAt': '2026-09-25T12:00:00Z',
            'events': [],
          }),
          200,
        );
      }),
    );
    final snapshot = await client.readActivity('my space');
    expect(snapshot.events, isEmpty);
    expect(paths.single, '/workspaces/my%20space/activity');
    client.close();
  });

  test('stream reconnects from the last received cursor', () async {
    final cursors = <String?>[];
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://localhost:5000'),
      httpClient: MockClient((request) async {
        cursors.add(request.url.queryParameters['after']);
        final sequence = cursors.length;
        return http.Response(
          'event: activity\nid: $sequence\ndata: ${jsonEncode({'id': 'event-$sequence', 'operationId': 'operation', 'sequence': sequence, 'at': '2026-09-25T12:00:00Z', 'kind': 0, 'type': 'Read', 'status': 'started'})}\n\n',
          200,
        );
      }),
    );
    final updates = await client
        .watchActivity('one', afterSequence: 0)
        .take(3)
        .toList();
    expect(updates[0], isA<ActivityItem>());
    expect(updates[1], isA<ActivityDisconnected>());
    expect(updates[2], isA<ActivityItem>());
    expect(cursors, ['0', '1']);
    client.close();
  });

  test('cancelling before response headers aborts the request', () async {
    final pending = _PendingActivityClient();
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://localhost:5000'),
      httpClient: pending,
    );
    final subscription = client
        .watchActivity('one', afterSequence: 0)
        .listen((_) {});
    await pending.started.future.timeout(const Duration(seconds: 1));
    await subscription.cancel().timeout(const Duration(seconds: 1));
    await pending.aborted.future.timeout(const Duration(seconds: 1));
    client.close();
  });

  test('stream sends the snapshot generation to detect a restart', () async {
    final queries = <Map<String, String>>[];
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://localhost:5000'),
      httpClient: MockClient((request) async {
        queries.add(request.url.queryParameters);
        return http.Response('', 200);
      }),
    );
    await client
        .watchActivity('one', afterSequence: 1, generation: 'feed-id')
        .take(1)
        .toList();
    expect(queries.single['generation'], 'feed-id');
    client.close();
  });

  test('cancelling an idle response aborts the stream', () async {
    final pending = _PendingActivityClient(respondWithIdleStream: true);
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://localhost:5000'),
      httpClient: pending,
    );
    final subscription = client
        .watchActivity('one', afterSequence: 0)
        .listen((_) {});
    await pending.started.future.timeout(const Duration(seconds: 1));
    await subscription.cancel().timeout(const Duration(seconds: 1));
    await pending.aborted.future.timeout(const Duration(seconds: 1));
    client.close();
  });
}
