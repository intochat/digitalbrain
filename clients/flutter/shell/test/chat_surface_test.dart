import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';
void main() {
  testWidgets('journal turns project as message text on the chat surface', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', turns: turns.stream, onSend: (_) async {}),
    );

    turns.add(shellTurn(1, true, 'how is my account?'));
    turns.add(shellTurn(2, false, 'Your account is up to date.'));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('chat_surface')), findsOneWidget);
    expect(find.text('how is my account?'), findsOneWidget);
    expect(find.text('Your account is up to date.'), findsOneWidget);
    await drainShellTimers(tester);
  });

  testWidgets('a repeated sequence is projected once', (tester) async {
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', turns: turns.stream, onSend: (_) async {}),
    );

    turns.add(shellTurn(7, false, 'only once'));
    turns.add(shellTurn(7, false, 'only once'));
    await tester.pumpAndSettle();

    expect(find.text('only once'), findsOneWidget);
    await drainShellTimers(tester);
  });

  testWidgets('sending hands the text to the edge and shows the journal answer', (
    tester,
  ) async {
    await prepareShellSurface(tester);
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

    await tester.enterText(find.byType(TextField), 'enrich my account');
    await tester.testTextInput.receiveAction(TextInputAction.send);
    await tester.pumpAndSettle();

    expect(sent, ['enrich my account']);
    expect(find.text('enrich my account'), findsWidgets);

    turns.add(shellTurn(1, true, 'enrich my account'));
    turns.add(shellTurn(2, false, 'Done.'));
    await tester.pumpAndSettle();

    expect(find.text('Done.'), findsOneWidget);
    await drainShellTimers(tester);
  });
}

