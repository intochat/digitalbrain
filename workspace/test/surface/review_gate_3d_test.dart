import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:workspace/gateway/brain_gateway.dart';
import 'package:workspace/gateway/ui_watch_channel.dart';
import 'package:workspace/surface/ui_surface_client.dart';
import 'package:workspace/surface/ui_surface_controller.dart';
import 'package:workspace/surface/ui_surface_models.dart';
import 'package:workspace/surface/ui_surface_renderer.dart';
import 'package:workspace/theme/brain_theme.dart';

import '../helpers/memory_feed_cursor_store.dart';

class _WatchSession {
  _WatchSession(this.cursor)
    : controller = StreamController<UiFeedMessage>.broadcast();

  final int cursor;
  final StreamController<UiFeedMessage> controller;
  bool closed = false;

  void emit(UiFeedMessage message) => controller.add(message);

  Future<void> complete() async {
    closed = true;
    await controller.close();
  }
}

class _ReconnectClient implements UiSurfaceClient {
  final List<int> watchCursors = [];
  final List<_WatchSession> sessions = [];
  UiSurfaceSnapshot Function(String surfaceId)? snapshotFactory;
  bool failSnapshot = false;
  int reconnectFailuresRemaining = 0;

  _WatchSession get latest => sessions.last;

  @override
  Future<UiSurfaceSnapshot> fetchSnapshot(String surfaceId) async {
    if (failSnapshot) {
      throw GatewayException('surface.unavailable', 'snapshot failed');
    }
    if (snapshotFactory != null) {
      return snapshotFactory!(surfaceId);
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
  }) async {}

  @override
  Stream<UiFeedMessage> watch({required int cursor}) {
    if (reconnectFailuresRemaining > 0) {
      reconnectFailuresRemaining--;
      return Stream<UiFeedMessage>.error(
        GatewayException('transport.error', 'socket exploded'),
      );
    }
    watchCursors.add(cursor);
    final session = _WatchSession(cursor);
    sessions.add(session);
    return session.controller.stream;
  }
}

class _FakeWatchChannel implements UiWatchChannel {
  _FakeWatchChannel()
    : _incoming = StreamController<dynamic>.broadcast(),
      ready = Future<void>.value();

  final StreamController<dynamic> _incoming;
  bool closed = false;
  int closeCount = 0;

  @override
  final Future<void> ready;

  @override
  Stream<dynamic> get stream => _incoming.stream;

  void add(dynamic value) => _incoming.add(value);

  @override
  Future<void> close() async {
    closed = true;
    closeCount++;
    await _incoming.close();
  }
}

UiFeedMessage _snapshot({
  required int sequence,
  String surfaceId = 'surface-1',
  int revision = 1,
  List<UiBlock>? blocks,
}) {
  return UiSnapshotMessage(
    schemaVersion: 1,
    sequence: sequence,
    snapshot: UiSurfaceSnapshot(
      surface: UiSurface(
        surfaceId: surfaceId,
        revision: revision,
        blocks:
            blocks ??
            [
              const UiBlock(kind: 'text', text: 'Hello', actions: []),
            ],
      ),
    ),
  );
}

UiFeedMessage _patch({
  required int sequence,
  String surfaceId = 'surface-1',
  required int fromRevision,
  required int toRevision,
  String text = 'Patched',
}) {
  return UiPatchMessage(
    schemaVersion: 1,
    sequence: sequence,
    patch: UiSurfacePatch(
      surfaceId: surfaceId,
      fromRevision: fromRevision,
      toRevision: toRevision,
      operations: [
        UiPatchOperation(op: 'replace', path: '/blocks/0/text', value: text),
      ],
    ),
  );
}

void main() {
  test('sequence_gap_reconnects_from_unchanged_cursor_then_applies_missing',
      () async {
    final store = MemoryFeedCursorStore();
    final client = _ReconnectClient();
    final controller = UiSurfaceController(
      client: client,
      cursorStore: store,
    );
    addTearDown(controller.dispose);

    await controller.start();
    expect(client.watchCursors, [0]);

    client.latest.emit(
      _snapshot(sequence: 1, revision: 1, blocks: [
        const UiBlock(kind: 'text', text: 'Seed', actions: []),
      ]),
    );
    await Future<void>.delayed(Duration.zero);
    expect(controller.feedCursor, 1);
    expect(store.read(), 1);

    client.latest.emit(
      _patch(
        sequence: 3,
        fromRevision: 1,
        toRevision: 2,
        text: 'Should not apply yet',
      ),
    );
    await Future<void>.delayed(const Duration(milliseconds: 30));

    expect(controller.surface('surface-1')!.blocks.single.text, 'Seed');
    expect(controller.feedCursor, 1);
    expect(store.read(), 1);
    expect(client.watchCursors, [0, 1]);

    client.latest.emit(
      _patch(sequence: 2, fromRevision: 1, toRevision: 2, text: 'Filled gap'),
    );
    await Future<void>.delayed(Duration.zero);
    expect(controller.surface('surface-1')!.blocks.single.text, 'Filled gap');
    expect(controller.feedCursor, 2);
    expect(store.read(), 2);
  });

  test('sequence_gap_reconnect_failure_surfaces_sanitized_connection_failure',
      () async {
    final store = MemoryFeedCursorStore();
    final client = _ReconnectClient();
    final controller = UiSurfaceController(
      client: client,
      cursorStore: store,
    );
    addTearDown(controller.dispose);

    await controller.start();
    client.latest.emit(_snapshot(sequence: 1, revision: 1));
    await Future<void>.delayed(Duration.zero);

    client.reconnectFailuresRemaining = 1;
    client.latest.emit(
      _patch(sequence: 4, fromRevision: 1, toRevision: 2),
    );
    await Future<void>.delayed(const Duration(milliseconds: 30));

    expect(store.read(), 1);
    expect(controller.feedCursor, 1);
    expect(controller.closedFailure, 'connection failure');
  });

  test('patch_recovery_stale_snapshot_does_not_apply_or_advance_cursor',
      () async {
    final store = MemoryFeedCursorStore();
    final client = _ReconnectClient()
      ..snapshotFactory = (surfaceId) => UiSurfaceSnapshot(
            surface: UiSurface(
              surfaceId: surfaceId,
              revision: 2,
              blocks: [
                const UiBlock(kind: 'text', text: 'stale snap', actions: []),
              ],
            ),
          );
    final controller = UiSurfaceController(
      client: client,
      cursorStore: store,
    );
    addTearDown(controller.dispose);

    await controller.start();
    client.latest.emit(
      _snapshot(
        sequence: 1,
        revision: 1,
        blocks: [const UiBlock(kind: 'text', text: 'Current', actions: [])],
      ),
    );
    await Future<void>.delayed(Duration.zero);

    client.latest.emit(
      _patch(sequence: 2, fromRevision: 5, toRevision: 6, text: 'Gap'),
    );
    await Future<void>.delayed(const Duration(milliseconds: 20));

    expect(controller.surface('surface-1')!.revision, 1);
    expect(controller.surface('surface-1')!.blocks.single.text, 'Current');
    expect(store.read(), 1);
    expect(controller.feedCursor, 1);
    expect(controller.closedFailure, isNotNull);
  });

  test('patch_recovery_wrong_surface_snapshot_does_not_apply_or_advance',
      () async {
    final store = MemoryFeedCursorStore();
    final client = _ReconnectClient()
      ..snapshotFactory = (_) => const UiSurfaceSnapshot(
            surface: UiSurface(
              surfaceId: 'other-surface',
              revision: 99,
              blocks: [UiBlock(kind: 'text', text: 'wrong', actions: [])],
            ),
          );
    final controller = UiSurfaceController(
      client: client,
      cursorStore: store,
    );
    addTearDown(controller.dispose);

    await controller.start();
    client.latest.emit(
      _snapshot(
        sequence: 1,
        revision: 1,
        blocks: [const UiBlock(kind: 'text', text: 'Current', actions: [])],
      ),
    );
    await Future<void>.delayed(Duration.zero);

    client.latest.emit(
      _patch(sequence: 2, fromRevision: 5, toRevision: 6),
    );
    await Future<void>.delayed(const Duration(milliseconds: 20));

    expect(controller.surface('surface-1')!.blocks.single.text, 'Current');
    expect(controller.surface('other-surface'), isNull);
    expect(store.read(), 1);
    expect(controller.feedCursor, 1);
    expect(controller.closedFailure, isNotNull);
  });

  test('snapshot_frame_does_not_regress_existing_surface_revision', () async {
    final store = MemoryFeedCursorStore();
    final client = _ReconnectClient();
    final controller = UiSurfaceController(
      client: client,
      cursorStore: store,
    );
    addTearDown(controller.dispose);

    await controller.start();
    client.latest.emit(
      _snapshot(
        sequence: 1,
        revision: 5,
        blocks: [const UiBlock(kind: 'text', text: 'Newer', actions: [])],
      ),
    );
    await Future<void>.delayed(Duration.zero);

    client.latest.emit(
      _snapshot(
        sequence: 2,
        revision: 3,
        blocks: [const UiBlock(kind: 'text', text: 'Older', actions: [])],
      ),
    );
    await Future<void>.delayed(Duration.zero);

    expect(controller.surface('surface-1')!.revision, 5);
    expect(controller.surface('surface-1')!.blocks.single.text, 'Newer');
    expect(store.read(), 1);
    expect(controller.feedCursor, 1);
    expect(controller.closedFailure, isNotNull);
  });

  test('gateway_protocol_failure_cancels_subscription_closes_channel_and_stream',
      () async {
    final fake = _FakeWatchChannel();
    var connectCount = 0;
    final gateway = BrainGateway(
      httpBase: 'http://gateway.test',
      wsBase: 'ws://gateway.test',
      watchChannelFactory: (uri) async {
        connectCount++;
        expect(uri.queryParameters['cursor'], '0');
        return fake;
      },
    );

    final errors = <Object>[];
    var done = false;
    final sub = gateway.watch(cursor: 0).listen(
      (_) {},
      onError: errors.add,
      onDone: () => done = true,
    );

    await Future<void>.delayed(Duration.zero);
    fake.add(jsonEncode({'ping': true}));
    await Future<void>.delayed(const Duration(milliseconds: 20));

    expect(errors, hasLength(1));
    expect(
      errors.single,
      isA<GatewayException>().having(
        (e) => e.code,
        'code',
        anyOf('frame.invalid', 'schema.unsupported', 'sequence.invalid'),
      ),
    );
    expect(
      (errors.single as GatewayException).detail,
      isNot(contains('ping')),
    );
    expect(fake.closed, isTrue);
    expect(fake.closeCount, greaterThanOrEqualTo(1));
    expect(done, isTrue);
    expect(connectCount, 1);
    await sub.cancel();
  });

  test('gateway_rejects_unversioned_ping_without_silent_null', () {
    expect(
      () => BrainGateway.mapFrame(jsonEncode({'ping': true})),
      throwsA(
        isA<GatewayException>().having(
          (e) => e.code,
          'code',
          anyOf('frame.invalid', 'schema.unsupported', 'sequence.invalid'),
        ),
      ),
    );
  });

  test('controller_onDone_reconnects_from_durable_cursor', () async {
    final store = MemoryFeedCursorStore()..write(2);
    final client = _ReconnectClient();
    final controller = UiSurfaceController(
      client: client,
      cursorStore: store,
    );
    addTearDown(controller.dispose);

    await controller.start();
    expect(client.watchCursors, [2]);
    await client.latest.complete();
    await Future<void>.delayed(const Duration(milliseconds: 30));

    expect(client.watchCursors, [2, 2]);
    expect(controller.closedFailure, isNull);
  });

  test('controller_dispose_suppresses_reconnect_on_done', () async {
    final store = MemoryFeedCursorStore()..write(4);
    final client = _ReconnectClient();
    final controller = UiSurfaceController(
      client: client,
      cursorStore: store,
    );

    await controller.start();
    expect(client.watchCursors, [4]);
    final session = client.latest;
    controller.dispose();
    await session.complete();
    await Future<void>.delayed(const Duration(milliseconds: 30));

    expect(client.watchCursors, [4]);
  });

  testWidgets('failure_frame_maps_allowlisted_code_without_raw_text', (
    tester,
  ) async {
    final client = _ReconnectClient();
    final controller = UiSurfaceController(
      client: client,
      cursorStore: MemoryFeedCursorStore(),
    );
    addTearDown(controller.dispose);

    await tester.pumpWidget(
      MaterialApp(
        theme: BrainTheme.dark,
        home: UiSurfaceRenderer(controller: controller),
      ),
    );
    await controller.start();
    client.latest.emit(_snapshot(sequence: 1, revision: 1));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 20));

    client.latest.emit(
      const UiFailureMessage(
        schemaVersion: 1,
        sequence: 2,
        code: 'neuron.failure',
      ),
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 20));

    expect(find.text('neuron failure'), findsOneWidget);
    expect(find.textContaining('sk-live'), findsNothing);
    expect(controller.sanitizedFailure, 'neuron failure');
  });

  test('failure_frame_ignores_adversarial_text_and_provider_payload', () {
    final message = UiFeedMessage.parse({
      'schemaVersion': 1,
      'type': 'failure',
      'sequence': 1,
      'code': 'neuron.failure',
      'text': 'sk-live-secret OPENAI_ERROR',
      'providerPayload': {'apiKey': 'sk-live-secret'},
    });
    expect(message, isA<UiFailureMessage>());
    final failure = message as UiFailureMessage;
    expect(failure.code, 'neuron.failure');
    expect(failure.sanitizedText, 'neuron failure');
    expect(failure.sanitizedText, isNot(contains('sk-live')));
  });

  test('unknown_failure_code_maps_to_operation_failed', () {
    final message = UiFeedMessage.parse({
      'schemaVersion': 1,
      'type': 'failure',
      'sequence': 1,
      'code': 'provider.raw.leak',
      'text': 'secret stack',
    }) as UiFailureMessage;
    expect(message.sanitizedText, 'operation failed');
  });

  testWidgets('renderer_displays_supported_vertical_block_kinds', (
    tester,
  ) async {
    final client = _ReconnectClient();
    final controller = UiSurfaceController(
      client: client,
      cursorStore: MemoryFeedCursorStore(),
    );
    addTearDown(controller.dispose);

    await tester.pumpWidget(
      MaterialApp(
        theme: BrainTheme.dark,
        home: UiSurfaceRenderer(controller: controller),
      ),
    );
    await controller.start();
    client.latest.emit(
      _snapshot(
        sequence: 1,
        revision: 1,
        blocks: const [
          UiBlock(kind: 'text', text: 'plain text', actions: []),
          UiBlock(kind: 'failure', text: 'failed step', actions: []),
          UiBlock(kind: 'topic', text: 'discussion topic', actions: []),
          UiBlock(kind: 'status', text: 'running', actions: []),
          UiBlock(kind: 'message', text: 'participant said hi', actions: []),
          UiBlock(kind: 'checkpoint', text: 'checkpoint-1', actions: []),
          UiBlock(kind: 'mystery', text: 'should stay inert', actions: []),
        ],
      ),
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 20));

    expect(find.text('plain text'), findsOneWidget);
    expect(find.text('failed step'), findsOneWidget);
    expect(find.text('discussion topic'), findsOneWidget);
    expect(find.text('running'), findsOneWidget);
    expect(find.text('participant said hi'), findsOneWidget);
    expect(find.text('checkpoint-1'), findsOneWidget);
    expect(find.text('should stay inert'), findsNothing);
    expect(find.text('unsupported block'), findsOneWidget);
  });
}
