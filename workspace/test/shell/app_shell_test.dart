import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:workspace/shell/app_shell.dart';
import 'package:workspace/surface/ui_surface_client.dart';
import 'package:workspace/surface/ui_surface_models.dart';
import 'package:workspace/theme/brain_theme.dart';

import '../helpers/memory_feed_cursor_store.dart';

class _IdleClient implements UiSurfaceClient {
  final StreamController<UiFeedMessage> _messages =
      StreamController<UiFeedMessage>.broadcast();

  @override
  Future<UiSurfaceSnapshot> fetchSnapshot(String surfaceId) async {
    return UiSurfaceSnapshot(
      surface: UiSurface(surfaceId: surfaceId, revision: 0, blocks: const []),
    );
  }

  @override
  Future<void> sendSurfaceAction({
    required String surfaceId,
    required String actionId,
    required int expectedRevision,
  }) async {}

  @override
  Stream<UiFeedMessage> watch({required int cursor}) => _messages.stream;

  Future<void> close() => _messages.close();
}

void main() {
  testWidgets('shell hosts the surface renderer entry point', (tester) async {
    final client = _IdleClient();
    addTearDown(client.close);

    await tester.pumpWidget(
      MaterialApp(
        theme: BrainTheme.dark,
        home: AppShell(client, cursorStore: MemoryFeedCursorStore()),
      ),
    );
    await tester.pump();

    expect(find.text('Surfaces'), findsOneWidget);
    expect(find.text('Waiting for surface…'), findsOneWidget);
  });

  testWidgets('theme smoke: app builds with BrainTheme.dark without error', (
    tester,
  ) async {
    final client = _IdleClient();
    addTearDown(client.close);

    await tester.pumpWidget(
      MaterialApp(
        theme: BrainTheme.dark,
        home: AppShell(client, cursorStore: MemoryFeedCursorStore()),
      ),
    );
    await tester.pump();

    expect(tester.takeException(), isNull);
  });
}
