import 'package:digitalbrain_flutter_shell/chat_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

void main() {
  testWidgets('the workspace exposes Chat, Activity, Brain, and Behaviors destinations', (
    tester,
  ) async {
    await prepareShellSurface(tester);

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        onLoadTopology: () async => shellTopology(),
      ),
    );
    await tester.pumpAndSettle();
    await drainShellTimers(tester);

    expect(find.byKey(const Key('destination_chat')), findsOneWidget);
    expect(find.byKey(const Key('destination_activity')), findsOneWidget);
    expect(find.byKey(const Key('destination_brain')), findsOneWidget);
    expect(find.byKey(const Key('destination_behaviors')), findsOneWidget);

    await tester.tap(find.byKey(const Key('destination_activity')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('activity_screen')), findsOneWidget);
    expect(find.text('No activity yet.'), findsOneWidget);

    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('brain_screen')), findsOneWidget);
    await drainShellTimers(tester);

    await tester.tap(find.byKey(const Key('destination_behaviors')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('behavior_workspace')), findsOneWidget);
    expect(find.byKey(const Key('behavior_library')), findsOneWidget);
    await drainShellTimers(tester);
  });

  testWidgets('narrow windows use bottom navigation', (tester) async {
    tester.view.physicalSize = const Size(600, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(const BrainChatApp(chatName: 'main'));
    await tester.pumpAndSettle();
    await drainShellTimers(tester);

    expect(find.byType(NavigationBar), findsOneWidget);
    expect(find.byType(NavigationRail), findsNothing);
    await drainShellTimers(tester);
  });

  testWidgets('a disconnected edge says so and mounts chat without a send path', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    await tester.pumpWidget(
      const BrainChatApp(chatName: 'main', statusMessage: 'no edge'),
    );
    await tester.pump();

    expect(find.text('not connected'), findsOneWidget);
    expect(find.byKey(const Key('chat_surface')), findsOneWidget);
    await drainShellTimers(tester);
  });
}

