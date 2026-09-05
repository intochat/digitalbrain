import 'package:digitalbrain_flutter_shell/chat_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';
import 'support/fake_graph_scene.dart';

void main() {
  testWidgets(
    'the workspace exposes Chat, Onboarding, Graph, Activity, Kit, and Windowing destinations',
    (tester) async {
      await prepareShellSurface(tester);

      await tester.pumpWidget(
        BrainChatApp(chatName: 'main', graphSceneFactory: FakeGraphScene.new),
      );
      await tester.pumpAndSettle();
      await drainShellTimers(tester);

      expect(find.byKey(const Key('destination_chat')), findsOneWidget);
      expect(find.byKey(const Key('destination_onboarding')), findsOneWidget);
      expect(find.byKey(const Key('destination_graph')), findsOneWidget);
      expect(find.byKey(const Key('destination_activity')), findsOneWidget);
      expect(find.byKey(const Key('destination_kit')), findsOneWidget);
      expect(find.byKey(const Key('destination_windowing')), findsOneWidget);

      await tester.tap(find.byKey(const Key('destination_onboarding')));
      await tester.pump();
      await drainShellTimers(tester);
      expect(find.byKey(const Key('onboarding_screen')), findsOneWidget);
      expect(
        find.byKey(const Key('onboarding_capability_rail')),
        findsOneWidget,
      );
      expect(find.byKey(const Key('kit_graph')), findsOneWidget);

      await tester.tap(find.byKey(const Key('destination_graph')));
      await tester.pump();
      expect(find.byKey(const Key('graph_home_screen')), findsOneWidget);
      expect(find.byKey(const Key('kit_graph_view')), findsOneWidget);

      await tester.tap(find.byKey(const Key('destination_activity')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('activity_screen')), findsOneWidget);
      expect(find.text('No activity yet.'), findsOneWidget);

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

  testWidgets('assistant hint requests local code review through chat', (
    tester,
  ) async {
    String? sent;
    await prepareShellSurface(tester);
    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', onSend: (text) async => sent = text),
    );
    await tester.pumpAndSettle();
    await tester.tap(
      find.byKey(const Key('assistant_hint_personal_code_review')),
    );
    await tester.pump();
    expect(
      sent,
      'Review my local repository diff. Focus on correctness, concurrency, and durable state. '
      'Give actionable findings with file and line references; skip cosmetic comments.',
    );
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
