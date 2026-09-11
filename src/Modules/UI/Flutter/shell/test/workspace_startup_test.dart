import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat/brain_chat_app.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets(
    'desktop conversation and graph occupy equal left and right panes',
    (tester) async {
      await tester.binding.setSurfaceSize(const Size(1600, 1000));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      await tester.pumpWidget(const BrainChatApp(chatName: 'main'));
      await tester.pump(const Duration(milliseconds: 100));
      final chat = tester.getRect(
        find.byKey(const Key('workspace_conversation')),
      );
      final graph = tester.getRect(find.byKey(const Key('workspace_graph')));
      expect(chat.right, lessThanOrEqualTo(graph.left));
      expect(chat.width, closeTo(graph.width, 1));
      expect(chat.top, graph.top);
      expect(chat.height, graph.height);
      await tester.pumpWidget(const SizedBox.shrink());
    },
  );

  testWidgets('workspace starts without a scripted Home surface', (
    tester,
  ) async {
    var surfaceReads = 0;
    final events = StreamController<SurfaceStreamEvent>.broadcast();
    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        onReadSurface: (_) async {
          surfaceReads++;
          return null;
        },
        surfaceEvents: events.stream,
      ),
    );
    await tester.pump(const Duration(milliseconds: 100));

    expect(surfaceReads, 0);
    expect(events.hasListener, isFalse);
    expect(find.text('Conversation'), findsWidgets);
    expect(find.text('Back to Home'), findsNothing);
    expect(find.text('Surface updates disconnected.'), findsNothing);

    await tester.pumpWidget(const SizedBox.shrink());
    await events.close();
  });
}
