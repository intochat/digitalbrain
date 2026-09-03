import 'package:digitalbrain_flutter_shell/chat_screen.dart';
import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

void main() {
  testWidgets(
    'the workspace exposes Chat, Graph, Activity, Behaviors, Kit, and Windowing destinations',
    (tester) async {
      await prepareShellSurface(tester);

      await tester.pumpWidget(const BrainChatApp(chatName: 'main'));
      await tester.pumpAndSettle();
      await drainShellTimers(tester);

      expect(find.byKey(const Key('destination_chat')), findsOneWidget);
      expect(find.byKey(const Key('destination_graph')), findsOneWidget);
      expect(find.byKey(const Key('destination_activity')), findsOneWidget);
      expect(find.byKey(const Key('destination_behaviors')), findsOneWidget);
      expect(find.byKey(const Key('destination_kit')), findsOneWidget);
      expect(find.byKey(const Key('destination_windowing')), findsOneWidget);

      await tester.tap(find.byKey(const Key('destination_graph')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('graph_home_screen')), findsOneWidget);
      expect(find.byKey(const Key('kit_graph')), findsOneWidget);

      await tester.tap(find.byKey(const Key('destination_activity')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('activity_screen')), findsOneWidget);
      expect(find.text('No activity yet.'), findsOneWidget);

      await tester.tap(find.byKey(const Key('destination_behaviors')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('behavior_workspace')), findsOneWidget);
      expect(find.text('Behavior IDE'), findsOneWidget);
      expect(
        find.textContaining('Connect to the DigitalBrain edge'),
        findsOneWidget,
      );
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

  testWidgets('Behavior IDE loads, highlights, tests, and runs fake data', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1200, 850);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    var fakeRuns = 0;
    const source = '''Feature: Bitcoin tracker
  @behavior
  Scenario: Track Bitcoin
    Given Market.Symbol("BTCUSD")
    When Market.Price changes
    Then notify UI.Chat("main")
  @test
  Scenario: Fake price
    Given fake event "market.price" from "BTCUSD" with text "breakout" and value 95000
    When behavior "Track Bitcoin" runs
    Then UI.Chat("main") contains a behavior notification''';

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        onLoadBehaviors: () async => const [
          BehaviorSummary(
            name: 'bitcoin-tracker',
            title: 'Track Bitcoin',
            source: source,
            active: true,
            diagnostics: [],
            lastTest: BehaviorTestReport(
              allGreen: true,
              scenarios: 1,
              failures: [],
            ),
          ),
        ],
        onLoadBehaviorSteps: () async => const [
          BehaviorStepSuggestion(
            keyword: 'Then',
            template: 'notify UI.Chat("main")',
            description: 'Notify chat',
          ),
        ],
        onSaveBehavior: (_, _) async {},
        onTestBehavior: (_) async => const BehaviorTestReport(
          allGreen: true,
          scenarios: 1,
          failures: [],
        ),
        onActivateBehavior: (_, {required active}) async {},
        onRunBehaviorFake: (_) async {
          fakeRuns++;
          return 'market.price fake published';
        },
        onGenerateBehavior: (_) async => const BehaviorGeneration(
          source: source,
          model: 'gemma4:e2b',
          success: true,
          diagnostics: [],
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_behaviors')));
    await tester.pumpAndSettle();

    expect(find.text('Behavior IDE'), findsOneWidget);
    expect(find.byKey(const Key('behavior_bitcoin-tracker')), findsOneWidget);
    expect(find.byKey(const Key('behavior_editor')), findsOneWidget);
    expect(find.textContaining('1 Reqnroll Behaviors ready'), findsOneWidget);
    await tester.tap(find.byKey(const Key('behavior_test')));
    await tester.pumpAndSettle();
    expect(find.text('1 scenarios green'), findsOneWidget);
    await tester.tap(find.byKey(const Key('behavior_fake')));
    await tester.pumpAndSettle();
    expect(fakeRuns, 1);
    expect(find.text('market.price fake published'), findsOneWidget);
    await drainShellTimers(tester);
  });

  testWidgets('assistant hint runs the Bitcoin fake scenario through chat', (
    tester,
  ) async {
    String? sent;
    await prepareShellSurface(tester);
    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', onSend: (text) async => sent = text),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('assistant_hint_run_bitcoin_fake')));
    await tester.pump();
    expect(sent, 'Run the bitcoin behavior with fake data');
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
