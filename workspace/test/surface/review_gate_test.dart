import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:workspace/gateway/brain_gateway.dart';
import 'package:workspace/surface/file_feed_cursor_store.dart';
import 'package:workspace/surface/ui_surface_client.dart';
import 'package:workspace/surface/ui_surface_controller.dart';
import 'package:workspace/surface/ui_surface_models.dart';
import 'package:workspace/surface/ui_surface_renderer.dart';
import 'package:workspace/theme/brain_theme.dart';

import '../helpers/memory_feed_cursor_store.dart';

class _RecordingClient implements UiSurfaceClient {
  final List<Map<String, dynamic>> actions = [];
  final List<String> snapshotRequests = [];
  final List<int> watchCursors = [];
  final StreamController<UiFeedMessage> _messages =
      StreamController<UiFeedMessage>.broadcast();
  bool failSnapshot = false;

  void emit(UiFeedMessage message) => _messages.add(message);

  void emitError(Object error) => _messages.addError(error);

  @override
  Future<UiSurfaceSnapshot> fetchSnapshot(String surfaceId) async {
    snapshotRequests.add(surfaceId);
    if (failSnapshot) {
      throw GatewayException('surface.unavailable', 'snapshot failed');
    }
    return UiSurfaceSnapshot(
      surface: UiSurface(
        surfaceId: surfaceId,
        revision: 10,
        blocks: [const UiBlock(kind: 'text', text: 'recovered', actions: [])],
      ),
    );
  }

  @override
  Future<void> sendSurfaceAction({
    required String surfaceId,
    required String actionId,
    required int expectedRevision,
  }) async {
    actions.add({
      'surfaceId': surfaceId,
      'actionId': actionId,
      'expectedRevision': expectedRevision,
    });
  }

  @override
  Stream<UiFeedMessage> watch({required int cursor}) {
    watchCursors.add(cursor);
    return _messages.stream;
  }

  Future<void> close() => _messages.close();
}

Map<String, dynamic> _snapshotJson({
  int? schemaVersion = 1,
  String surfaceId = 'surface-1',
  int revision = 1,
  int? sequence,
  List<Map<String, dynamic>>? blocks,
  bool omitSequence = false,
}) {
  final json = <String, dynamic>{
    'schemaVersion': ?schemaVersion,
    'type': 'snapshot',
    if (!omitSequence) 'sequence': sequence ?? 1,
    'surface': {
      'surfaceId': surfaceId,
      'revision': revision,
      'blocks':
          blocks ??
          [
            {
              'kind': 'text',
              'text': 'Hello surface',
              'actions': [
                {
                  'id': 'approve',
                  'label': 'Approve',
                  'expectedRevision': revision,
                },
              ],
            },
          ],
    },
  };
  return json;
}

Map<String, dynamic> _patchJson({
  int schemaVersion = 1,
  String surfaceId = 'surface-1',
  required int fromRevision,
  required int toRevision,
  required List<Map<String, dynamic>> operations,
  int? sequence,
}) {
  return {
    'schemaVersion': schemaVersion,
    'type': 'patch',
    'sequence': sequence ?? toRevision,
    'surfaceId': surfaceId,
    'fromRevision': fromRevision,
    'toRevision': toRevision,
    'operations': operations,
  };
}

void main() {
  test(
    'gateway_http_missing_schema_version_fails_closed',
    () async {
      final client = MockClient((request) async {
        return http.Response(
          jsonEncode({
            'surface': {
              'surfaceId': 'surface-1',
              'revision': 1,
              'blocks': <Map<String, dynamic>>[],
            },
          }),
          200,
        );
      });
      final gateway = BrainGateway(
        httpBase: 'http://gateway.test',
        wsBase: 'ws://gateway.test',
        client: client,
      );

      await expectLater(
        () => gateway.fetchSnapshot('surface-1'),
        throwsA(
          isA<GatewayException>().having(
            (e) => e.code,
            'code',
            'schema.unsupported',
          ),
        ),
      );
    },
  );

  test(
    'gateway_http_unknown_schema_version_fails_closed',
    () async {
      final client = MockClient((request) async {
        return http.Response(
          jsonEncode({
            'schemaVersion': 99,
            'surface': {
              'surfaceId': 'surface-1',
              'revision': 1,
              'blocks': <Map<String, dynamic>>[],
            },
          }),
          200,
        );
      });
      final gateway = BrainGateway(
        httpBase: 'http://gateway.test',
        wsBase: 'ws://gateway.test',
        client: client,
      );

      await expectLater(
        () => gateway.fetchSnapshot('surface-1'),
        throwsA(
          isA<GatewayException>().having(
            (e) => e.code,
            'code',
            'schema.unsupported',
          ),
        ),
      );
    },
  );

  test(
    'gateway_map_frame_unknown_schema_throws_protocol_closed',
    () {
      expect(
        () => BrainGateway.mapFrame(
          jsonEncode(_snapshotJson(schemaVersion: 99)),
        ),
        throwsA(
          isA<GatewayException>().having(
            (e) => e.code,
            'code',
            'schema.unsupported',
          ),
        ),
      );
    },
  );

  test(
    'gateway_map_frame_missing_schema_throws_protocol_closed',
    () {
      expect(
        () => BrainGateway.mapFrame(
          jsonEncode(_snapshotJson(schemaVersion: null)),
        ),
        throwsA(
          isA<GatewayException>().having(
            (e) => e.code,
            'code',
            'schema.unsupported',
          ),
        ),
      );
    },
  );

  test(
    'gateway_map_frame_missing_sequence_throws_protocol_closed',
    () {
      expect(
        () => BrainGateway.mapFrame(
          jsonEncode(_snapshotJson(omitSequence: true)),
        ),
        throwsA(
          isA<GatewayException>().having(
            (e) => e.code,
            'code',
            'sequence.invalid',
          ),
        ),
      );
    },
  );

  test(
    'gateway_map_frame_negative_sequence_throws_protocol_closed',
    () {
      expect(
        () => BrainGateway.mapFrame(
          jsonEncode(_snapshotJson(sequence: -1)),
        ),
        throwsA(
          isA<GatewayException>().having(
            (e) => e.code,
            'code',
            'sequence.invalid',
          ),
        ),
      );
    },
  );

  test('gateway_send_action_posts_canonical_ui_action_route', () async {
    late Uri capturedUri;
    final client = MockClient((request) async {
      capturedUri = request.url;
      return http.Response(jsonEncode({'status': 'accepted'}), 200);
    });
    final gateway = BrainGateway(
      httpBase: 'http://gateway.test',
      wsBase: 'ws://gateway.test',
      client: client,
    );

    await gateway.sendSurfaceAction(
      surfaceId: 'surface-1',
      actionId: 'approve',
      expectedRevision: 2,
    );

    expect(capturedUri.path, '/ui/action');
  });

  test('cursor_not_advanced_before_frame_accepted', () async {
    final store = MemoryFeedCursorStore();
    final client = _RecordingClient();
    final controller = UiSurfaceController(
      client: client,
      cursorStore: store,
    );
    addTearDown(() async {
      controller.dispose();
      await client.close();
    });

    await controller.start();
    expect(store.read(), isNull);
    expect(controller.feedCursor, 0);

    client.emit(
      UiFeedMessage.parse(_snapshotJson(revision: 5, sequence: 1)),
    );
    await Future<void>.delayed(Duration.zero);

    expect(controller.surface('surface-1'), isNotNull);
    expect(store.read(), 1);
    expect(controller.feedCursor, 1);
  });

  test('gap_recovery_persists_triggering_sequence_after_snapshot', () async {
    final store = MemoryFeedCursorStore();
    final client = _RecordingClient();
    final controller = UiSurfaceController(
      client: client,
      cursorStore: store,
    );
    addTearDown(() async {
      controller.dispose();
      await client.close();
    });

    await controller.start();
    client.emit(UiFeedMessage.parse(_snapshotJson(revision: 1, sequence: 1)));
    await Future<void>.delayed(Duration.zero);
    expect(store.read(), 1);

    client.emit(
      UiFeedMessage.parse(
        _patchJson(
          fromRevision: 4,
          toRevision: 5,
          sequence: 2,
          operations: [
            {'op': 'replace', 'path': '/blocks/0/text', 'value': 'Gap'},
          ],
        ),
      ),
    );
    await Future<void>.delayed(const Duration(milliseconds: 20));

    expect(client.snapshotRequests, contains('surface-1'));
    expect(controller.surface('surface-1')!.revision, 10);
    expect(store.read(), 2);
    expect(controller.feedCursor, 2);
  });

  test('gap_recovery_failure_keeps_old_cursor_and_closes', () async {
    final store = MemoryFeedCursorStore();
    final client = _RecordingClient()..failSnapshot = true;
    final controller = UiSurfaceController(
      client: client,
      cursorStore: store,
    );
    addTearDown(() async {
      controller.dispose();
      await client.close();
    });

    await controller.start();
    client.failSnapshot = false;
    client.emit(UiFeedMessage.parse(_snapshotJson(revision: 1, sequence: 1)));
    await Future<void>.delayed(Duration.zero);
    expect(store.read(), 1);

    client.failSnapshot = true;
    client.emit(
      UiFeedMessage.parse(
        _patchJson(
          fromRevision: 8,
          toRevision: 9,
          sequence: 2,
          operations: [
            {'op': 'replace', 'path': '/blocks/0/text', 'value': 'Gap'},
          ],
        ),
      ),
    );
    await Future<void>.delayed(const Duration(milliseconds: 20));

    expect(store.read(), 1);
    expect(controller.feedCursor, 1);
    expect(controller.closedFailure, isNotNull);
    expect(controller.surface('surface-1')!.revision, 1);
  });

  test('duplicate_sequence_is_ignored_without_state_regression', () async {
    final store = MemoryFeedCursorStore();
    final client = _RecordingClient();
    final controller = UiSurfaceController(
      client: client,
      cursorStore: store,
    );
    addTearDown(() async {
      controller.dispose();
      await client.close();
    });

    await controller.start();
    client.emit(
      UiFeedMessage.parse(
        _snapshotJson(
          revision: 2,
          sequence: 1,
          blocks: [
            {
              'kind': 'text',
              'text': 'Current',
              'actions': <Map<String, dynamic>>[],
            },
          ],
        ),
      ),
    );
    await Future<void>.delayed(Duration.zero);

    client.emit(
      UiFeedMessage.parse(
        _snapshotJson(
          revision: 99,
          sequence: 1,
          blocks: [
            {
              'kind': 'text',
              'text': 'Replay',
              'actions': <Map<String, dynamic>>[],
            },
          ],
        ),
      ),
    );
    await Future<void>.delayed(Duration.zero);

    expect(controller.surface('surface-1')!.textOrFirst, 'Current');
    expect(controller.surface('surface-1')!.revision, 2);
    expect(store.read(), 1);
  });

  testWidgets('stale_action_expected_revision_fails_closed_or_refreshes', (
    tester,
  ) async {
    final client = _RecordingClient();
    final controller = UiSurfaceController(
      client: client,
      cursorStore: MemoryFeedCursorStore(),
    );
    addTearDown(() async {
      controller.dispose();
      await client.close();
    });

    await tester.pumpWidget(
      MaterialApp(
        theme: BrainTheme.dark,
        home: UiSurfaceRenderer(controller: controller),
      ),
    );
    await controller.start();
    client.emit(
      UiFeedMessage.parse(
        _snapshotJson(
          revision: 5,
          sequence: 1,
          blocks: [
            {
              'kind': 'text',
              'text': 'Hello surface',
              'actions': [
                {
                  'id': 'approve',
                  'label': 'Approve',
                  'expectedRevision': 2,
                },
              ],
            },
          ],
        ),
      ),
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 20));

    await tester.tap(find.text('Approve'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 20));

    expect(client.actions, isEmpty);
    expect(
      client.snapshotRequests.contains('surface-1') ||
          controller.closedFailure != null,
      isTrue,
    );
  });

  testWidgets('action_sends_action_expected_revision_not_surface_revision', (
    tester,
  ) async {
    final client = _RecordingClient();
    final controller = UiSurfaceController(
      client: client,
      cursorStore: MemoryFeedCursorStore(),
    );
    addTearDown(() async {
      controller.dispose();
      await client.close();
    });

    await tester.pumpWidget(
      MaterialApp(
        theme: BrainTheme.dark,
        home: UiSurfaceRenderer(controller: controller),
      ),
    );
    await controller.start();
    client.emit(
      UiFeedMessage.parse(
        _snapshotJson(
          revision: 7,
          sequence: 1,
          blocks: [
            {
              'kind': 'text',
              'text': 'Hello surface',
              'actions': [
                {
                  'id': 'approve',
                  'label': 'Approve',
                  'expectedRevision': 7,
                },
              ],
            },
          ],
        ),
      ),
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 20));

    await tester.tap(find.text('Approve'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 20));

    expect(client.actions, hasLength(1));
    expect(client.actions.single['expectedRevision'], 7);
  });

  test('file_feed_cursor_store_survives_restart_round_trip', () async {
    final dir = await Directory.systemTemp.createTemp('brain-cursor-');
    addTearDown(() async {
      if (await dir.exists()) {
        await dir.delete(recursive: true);
      }
    });
    final path = '${dir.path}${Platform.pathSeparator}cursor.txt';

    final first = FileFeedCursorStore(path);
    expect(first.read(), isNull);
    first.write(42);
    expect(first.read(), 42);

    final second = FileFeedCursorStore(path);
    expect(second.read(), 42);

    final client = _RecordingClient();
    final controller = UiSurfaceController(
      client: client,
      cursorStore: second,
    );
    addTearDown(() async {
      controller.dispose();
      await client.close();
    });
    await controller.start();
    expect(client.watchCursors, [42]);
    expect(controller.feedCursor, 42);
  });

  test('controller_transport_error_sets_sanitized_closed_failure', () async {
    final client = _RecordingClient();
    final controller = UiSurfaceController(
      client: client,
      cursorStore: MemoryFeedCursorStore(),
    );
    addTearDown(() async {
      controller.dispose();
      await client.close();
    });

    await controller.start();
    client.emitError(GatewayException('transport.error', 'socket exploded'));
    await Future<void>.delayed(Duration.zero);

    expect(controller.closedFailure, isNotNull);
    expect(controller.closedFailure, isNot(contains('socket exploded')));
  });

  test('memory_feed_cursor_store_is_not_exported_from_lib', () {
    final libFile = File('lib/surface/feed_cursor_store.dart');
    expect(libFile.existsSync(), isTrue);
    final source = libFile.readAsStringSync();
    expect(source.contains('class MemoryFeedCursorStore'), isFalse);
  });
}

extension on UiSurface {
  String get textOrFirst => blocks.isEmpty ? '' : blocks.first.text;
}
