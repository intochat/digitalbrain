import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

ChatTurnEvent _turn(int sequence, bool fromUser, String text) => ChatTurnEvent(
  sequence: sequence,
  fromUser: fromUser,
  text: text,
  commandId: 'c$sequence',
);

void main() {
  testWidgets('the workspace exposes Chat, Activity, and Brain destinations', (
    tester,
  ) async {
    await tester.pumpWidget(const BrainChatApp(chatName: 'main'));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('destination_chat')), findsOneWidget);
    expect(find.byKey(const Key('destination_activity')), findsOneWidget);
    expect(find.byKey(const Key('destination_brain')), findsOneWidget);

    await tester.tap(find.byKey(const Key('destination_activity')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('activity_screen')), findsOneWidget);
    expect(find.text('No activity yet.'), findsOneWidget);

    await tester.tap(find.byKey(const Key('destination_brain')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('brain_screen')), findsOneWidget);
    expect(find.text('General assistant'), findsOneWidget);
    expect(
      find.text('Gmail message → Salesforce Account description'),
      findsOneWidget,
    );
    expect(find.text('Approval required'), findsOneWidget);
  });

  testWidgets('activity shows journal facts without message content', (
    tester,
  ) async {
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', turns: turns.stream, onSend: (_) async {}),
    );

    turns.add(_turn(1, true, 'private customer message'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('destination_activity')));
    await tester.pumpAndSettle();

    expect(find.text('UserMessaged'), findsOneWidget);
    expect(find.text('sequence 001'), findsOneWidget);
    expect(find.text('command c1'), findsOneWidget);
    expect(find.text('private customer message'), findsNothing);
  });

  testWidgets('the shared event projection survives destination changes', (
    tester,
  ) async {
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', turns: turns.stream, onSend: (_) async {}),
    );

    await tester.tap(find.byKey(const Key('destination_activity')));
    await tester.pumpAndSettle();
    turns.add(_turn(9, false, 'arrived while activity was open'));
    await tester.pumpAndSettle();

    expect(find.text('AssistantResponded'), findsOneWidget);

    await tester.tap(find.byKey(const Key('destination_chat')));
    await tester.pumpAndSettle();
    expect(find.text('arrived while activity was open'), findsOneWidget);
    expect(find.text('009'), findsOneWidget);
  });

  testWidgets('narrow windows use bottom navigation', (tester) async {
    tester.view.physicalSize = const Size(600, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(const BrainChatApp(chatName: 'main'));
    await tester.pumpAndSettle();

    expect(find.byType(NavigationBar), findsOneWidget);
    expect(find.byType(NavigationRail), findsNothing);
  });

  testWidgets('an empty conversation invites the owner to act', (tester) async {
    await tester.pumpWidget(const BrainChatApp(chatName: 'main'));
    await tester.pump();

    expect(find.text('Nothing yet.'), findsOneWidget);
    expect(find.text('Ask your brain to do something.'), findsOneWidget);
    expect(find.byKey(const Key('chat_journal')), findsNothing);
  });

  testWidgets('each turn carries its journal sequence and speaker', (
    tester,
  ) async {
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', turns: turns.stream, onSend: (_) async {}),
    );

    turns.add(_turn(1, true, 'how is my account?'));
    turns.add(_turn(2, false, 'Your account is up to date.'));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('turn_1')), findsOneWidget);
    expect(find.byKey(const Key('turn_2')), findsOneWidget);
    expect(find.text('001'), findsOneWidget);
    expect(find.text('002'), findsOneWidget);
    expect(find.text('you'), findsOneWidget);
    expect(find.text('brain'), findsOneWidget);
    expect(find.text('how is my account?'), findsOneWidget);
    expect(find.text('Your account is up to date.'), findsOneWidget);
  });

  testWidgets('a repeated sequence is projected once', (tester) async {
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', turns: turns.stream, onSend: (_) async {}),
    );

    turns.add(_turn(7, false, 'only once'));
    turns.add(_turn(7, false, 'only once'));
    await tester.pumpAndSettle();

    expect(find.text('only once'), findsOneWidget);
  });

  testWidgets('sending hands the text to the edge and awaits the brain', (
    tester,
  ) async {
    final sent = <String>[];
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        turns: turns.stream,
        onSend: (text) async => sent.add(text),
      ),
    );

    await tester.enterText(
      find.byKey(const Key('chat_composer')),
      'enrich my account',
    );
    await tester.tap(find.byKey(const Key('chat_send')));
    await tester.pumpAndSettle();

    expect(sent, ['enrich my account']);
    expect(find.text('thinking'), findsOneWidget);

    turns.add(_turn(1, false, 'Done.'));
    await tester.pumpAndSettle();

    expect(find.text('thinking'), findsNothing);
    expect(find.text('Done.'), findsOneWidget);
  });

  testWidgets('a disconnected edge disables sending and says so', (
    tester,
  ) async {
    await tester.pumpWidget(
      const BrainChatApp(chatName: 'main', statusMessage: 'no edge'),
    );
    await tester.pump();

    expect(find.text('not connected'), findsOneWidget);
    final send = tester.widget<IconButton>(find.byKey(const Key('chat_send')));
    expect(send.onPressed, isNull);
  });
}
