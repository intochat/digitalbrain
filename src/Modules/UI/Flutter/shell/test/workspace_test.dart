import 'package:digitalbrain_flutter_shell/chat_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

void main() {
  testWidgets(
    'the workspace exposes Chat, Activity, Behaviors, Kit, and Windowing destinations',
    (tester) async {
      await prepareShellSurface(tester);

      await tester.pumpWidget(const BrainChatApp(chatName: 'main'));
      await tester.pumpAndSettle();
      await drainShellTimers(tester);

      expect(find.byKey(const Key('destination_chat')), findsOneWidget);
      expect(find.byKey(const Key('destination_activity')), findsOneWidget);
      expect(find.byKey(const Key('destination_behaviors')), findsOneWidget);
      expect(find.byKey(const Key('destination_kit')), findsOneWidget);
      expect(find.byKey(const Key('destination_windowing')), findsOneWidget);

      await tester.tap(find.byKey(const Key('destination_activity')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('activity_screen')), findsOneWidget);
      expect(find.text('No activity yet.'), findsOneWidget);

      await tester.tap(find.byKey(const Key('destination_behaviors')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('behavior_workspace')), findsOneWidget);
      expect(find.text('Behavior recipes'), findsOneWidget);
      expect(
        find.textContaining('Google Calendar', findRichText: true),
        findsOneWidget,
      );
      expect(
        find.textContaining('ICalendar', findRichText: true),
        findsWidgets,
      );
      expect(find.text('Planned composition'), findsOneWidget);
      await drainShellTimers(tester);

      await tester.tap(find.byKey(const Key('destination_kit')));
      await tester.pump();
      await drainShellTimers(tester);
      expect(find.byKey(const Key('kit_gallery_screen')), findsOneWidget);
      expect(find.text('UI Kit'), findsOneWidget);

      await tester.tap(find.byKey(const Key('destination_windowing')));
      await tester.pump();
      await drainShellTimers(tester);
      expect(find.byKey(const Key('windowing_screen')), findsOneWidget);
      expect(find.textContaining('Windowing demo'), findsOneWidget);
      expect(find.text('BTC / USD'), findsWidgets);
      expect(find.byKey(const Key('kit_time_chart')), findsOneWidget);
    },
  );

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

  testWidgets(
    'a disconnected edge says so and mounts chat without a send path',
    (tester) async {
      await prepareShellSurface(tester);
      await tester.pumpWidget(
        const BrainChatApp(chatName: 'main', statusMessage: 'no edge'),
      );
      await tester.pump();

      expect(find.text('not connected'), findsOneWidget);
      expect(find.byKey(const Key('chat_surface')), findsOneWidget);
      await drainShellTimers(tester);
    },
  );
}
