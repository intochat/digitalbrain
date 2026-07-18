import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
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

  void emit(UiFeedMessage message) => _messages.add(message);

  @override
  Future<UiSurfaceSnapshot> fetchSnapshot(String surfaceId) async {
    snapshotRequests.add(surfaceId);
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
  int schemaVersion = 1,
  String surfaceId = 'surface-1',
  int revision = 1,
  List<Map<String, dynamic>>? blocks,
}) {
  return {
    'schemaVersion': schemaVersion,
    'type': 'snapshot',
    'sequence': revision,
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
            {
              'kind': 'failure',
              'text': 'neuron failure',
              'actions': <Map<String, dynamic>>[],
            },
          ],
    },
  };
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
  testWidgets('snapshot_renders_supported_block_kinds', (tester) async {
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
    client.emit(UiFeedMessage.parse(_snapshotJson()));
    await tester.pumpAndSettle();

    expect(find.text('Hello surface'), findsOneWidget);
    expect(find.text('neuron failure'), findsOneWidget);
    expect(find.text('Approve'), findsOneWidget);
  });

  test('contiguous_patch_updates_surface', () async {
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
    client.emit(UiFeedMessage.parse(_snapshotJson(revision: 1)));
    await Future<void>.delayed(Duration.zero);

    client.emit(
      UiFeedMessage.parse(
        _patchJson(
          fromRevision: 1,
          toRevision: 2,
          operations: [
            {'op': 'replace', 'path': '/blocks/0/text', 'value': 'Patched body'},
          ],
        ),
      ),
    );
    await Future<void>.delayed(Duration.zero);

    final surface = controller.surface('surface-1');
    expect(surface, isNotNull);
    expect(surface!.revision, 2);
    expect(surface.blocks[0].text, 'Patched body');
  });

  test('duplicate_patch_is_ignored', () async {
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
    client.emit(
      UiFeedMessage.parse(
        _snapshotJson(
          revision: 2,
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
        _patchJson(
          fromRevision: 1,
          toRevision: 2,
          operations: [
            {'op': 'replace', 'path': '/blocks/0/text', 'value': 'Stale'},
          ],
        ),
      ),
    );
    await Future<void>.delayed(Duration.zero);

    final surface = controller.surface('surface-1')!;
    expect(surface.revision, 2);
    expect(surface.blocks[0].text, 'Current');
    expect(client.snapshotRequests, isEmpty);
  });

  test('revision_gap_requests_snapshot', () async {
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
    client.emit(UiFeedMessage.parse(_snapshotJson(revision: 1)));
    await Future<void>.delayed(Duration.zero);

    client.emit(
      UiFeedMessage.parse(
        _patchJson(
          fromRevision: 4,
          toRevision: 5,
          operations: [
            {'op': 'replace', 'path': '/blocks/0/text', 'value': 'Gap'},
          ],
        ),
      ),
    );
    await Future<void>.delayed(const Duration(milliseconds: 20));

    expect(client.snapshotRequests, contains('surface-1'));
    final surface = controller.surface('surface-1')!;
    expect(surface.revision, 10);
    expect(surface.blocks[0].text, 'recovered');
  });

  testWidgets('action_sends_surface_action_and_expected_revision', (
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
    client.emit(UiFeedMessage.parse(_snapshotJson(revision: 3)));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Approve'));
    await tester.pumpAndSettle();

    expect(client.actions, hasLength(1));
    expect(client.actions.single['surfaceId'], 'surface-1');
    expect(client.actions.single['actionId'], 'approve');
    expect(client.actions.single['expectedRevision'], 3);
  });

  test('reconnect_persists_and_reuses_feed_cursor', () async {
    final store = MemoryFeedCursorStore();
    final client1 = _RecordingClient();
    final first = UiSurfaceController(client: client1, cursorStore: store);
    await first.start();
    client1.emit(UiFeedMessage.parse(_snapshotJson(revision: 7)));
    await Future<void>.delayed(Duration.zero);
    expect(store.read(), 7);
    first.dispose();
    await client1.close();

    final client2 = _RecordingClient();
    final second = UiSurfaceController(client: client2, cursorStore: store);
    addTearDown(() async {
      second.dispose();
      await client2.close();
    });
    await second.start();

    expect(client2.watchCursors, [7]);
    expect(second.feedCursor, 7);
  });

  test('unknown_schema_version_fails_closed', () async {
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
    expect(
      () => UiFeedMessage.parse(_snapshotJson(schemaVersion: 99)),
      throwsFormatException,
    );

    final rejected = controller.ingestRaw(
      jsonEncode(_snapshotJson(schemaVersion: 99)),
    );
    expect(rejected, isFalse);
    expect(controller.surface('surface-1'), isNull);
    expect(controller.closedFailure, isNotNull);
    expect(controller.closedFailure, contains('schema'));
  });

  testWidgets('renderer_displays_sanitized_failure_without_raw_provider_data', (
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
          revision: 1,
          blocks: [
            {
              'kind': 'failure',
              'text': 'neuron failure',
              'actions': <Map<String, dynamic>>[],
              'providerPayload': {
                'prompt': 'secret system prompt',
                'apiKey': 'sk-live-secret',
                'raw': 'OPENAI_ERROR stack',
              },
            },
          ],
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('neuron failure'), findsOneWidget);
    expect(find.textContaining('secret system prompt'), findsNothing);
    expect(find.textContaining('sk-live-secret'), findsNothing);
    expect(find.textContaining('OPENAI_ERROR'), findsNothing);
    expect(find.textContaining('providerPayload'), findsNothing);
  });
}
