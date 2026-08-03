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

  testWidgets(
    'authorization required projects a sign-in card and completed resolves it',
    (tester) async {
      await prepareShellSurface(tester);
      final turns = StreamController<ChatTurnEvent>();
      final authorizations = StreamController<AuthorizationEvent>();
      final opened = <Uri>[];
      addTearDown(turns.close);
      addTearDown(authorizations.close);

      await tester.pumpWidget(
        BrainChatApp(
          chatName: 'main',
          turns: turns.stream,
          authorizations: authorizations.stream,
          onOpenSignIn: (url) async => opened.add(url),
        ),
      );

      authorizations.add(
        AuthorizationEvent(
          sequence: 1,
          kind: 'AuthorizationRequired',
          commandId: 'cmd-1',
          serverKey: 'google.gmail',
          serverDisplayName: 'DigitalBrain Gmail',
          signInUrl: 'https://ui.test/oauth?state=s1',
          state: 's1',
          timestamp: DateTime.utc(2026, 7, 28, 8),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('sign_in_card_rail')), findsOneWidget);
      expect(find.text('DigitalBrain Gmail'), findsOneWidget);
      await tester.tap(find.byKey(const Key('sign_in_open_s1')));
      await tester.pumpAndSettle();
      expect(opened, [Uri.parse('https://ui.test/oauth?state=s1')]);

      authorizations.add(
        AuthorizationEvent(
          sequence: 2,
          kind: 'AuthorizationCompleted',
          commandId: 'cmd-1',
          serverKey: 'google.gmail',
          state: 's1',
          timestamp: DateTime.utc(2026, 7, 28, 8, 1),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('sign_in_card_rail')), findsNothing);
      expect(find.text('DigitalBrain Gmail'), findsNothing);
      await drainShellTimers(tester);
    },
  );

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

  testWidgets(
    'topology reloads do not wipe an optimistic send before journal arrives',
    (tester) async {
      await prepareShellSurface(tester);
      final turns = StreamController<ChatTurnEvent>();
      addTearDown(turns.close);

      await tester.pumpWidget(
        BrainChatApp(
          chatName: 'main',
          turns: turns.stream,
          onLoadTopology: () async => shellTopology(),
          onSend: (_) async {},
        ),
      );

      await tester.enterText(find.byType(TextField), 'stay visible');
      await tester.testTextInput.receiveAction(TextInputAction.send);
      await tester.pumpAndSettle();

      expect(find.text('stay visible'), findsWidgets);

      await tester.tap(find.byKey(const Key('destination_brain')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('destination_chat')));
      await tester.pumpAndSettle();

      expect(find.text('stay visible'), findsWidgets);

      turns.add(shellTurn(1, true, 'stay visible'));
      turns.add(shellTurn(2, false, 'still here'));
      await tester.pumpAndSettle();

      expect(find.text('stay visible'), findsOneWidget);
      expect(find.text('still here'), findsOneWidget);
      await drainShellTimers(tester);
    },
  );

  testWidgets(
    'an in-flight stream bubble survives journal user-turn arrival',
    (tester) async {
      await prepareShellSurface(tester);
      final turns = StreamController<ChatTurnEvent>();
      addTearDown(turns.close);

      // Completes after a short delay so the bubble exists while the user
      // journal lands, without leaving an open stream that hangs the test.
      await tester.pumpWidget(
        BrainChatApp(
          chatName: 'main',
          turns: turns.stream,
          onStream: (_) async* {
            await Future<void>.delayed(const Duration(milliseconds: 80));
          },
        ),
      );

      await tester.enterText(find.byType(TextField), 'stream me');
      await tester.testTextInput.receiveAction(TextInputAction.send);
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 20));

      expect(find.text('stream me'), findsWidgets);

      turns.add(shellTurn(1, true, 'stream me'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 20));

      expect(find.text('stream me'), findsWidgets);

      await tester.pump(const Duration(milliseconds: 100));
      turns.add(shellTurn(2, false, 'stream done'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 50));

      expect(find.text('stream me'), findsOneWidget);
      expect(find.text('stream done'), findsOneWidget);
      await drainShellTimers(tester);
    },
  );
}

