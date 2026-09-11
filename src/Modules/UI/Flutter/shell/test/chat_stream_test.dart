import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat/brain_chat_screen.dart';
import 'package:digitalbrain_flutter_shell/chat/workspace_session.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('journal resets replace history and sequence deduplication', () async {
    final events = StreamController<ChatStreamEvent>();
    final session = WorkspaceSession(chatName: 'main', turns: events.stream);
    addTearDown(session.dispose);
    addTearDown(events.close);
    var notifications = 0;
    session.addListener(() => notifications++);
    final original = _turn(10, 'original', 'old-turn');
    events.add(ChatTurnObserved(turn: original));
    events.add(ChatTurnObserved(turn: original));
    await Future<void>.delayed(Duration.zero);
    expect(notifications, 1);

    final transcript = [
      _turn(-2, 'first', 'first-turn', fromUser: true),
      _turn(-1, 'second', 'second-turn'),
    ];
    events.add(ChatJournalReset(cursor: 20, turns: transcript));
    events.add(ChatTurnObserved(turn: transcript.first));
    events.add(ChatTurnObserved(turn: original));
    await Future<void>.delayed(Duration.zero);
    expect(session.projectedTurns.map((turn) => turn.text), [
      'first',
      'second',
      'original',
    ]);
    expect(notifications, 3);

    events.add(const ChatJournalReset(cursor: 21, turns: []));
    await Future<void>.delayed(Duration.zero);
    expect(session.projectedTurns, isEmpty);
    expect(notifications, 4);
  });

  for (final responseFirst in [false, true]) {
    testWidgets(
      'matches receipt when response arrives ${responseFirst ? "first" : "last"}',
      (tester) async {
        final accepted = Completer<ChatSendReceipt>();
        String? receipt;
        Future<ChatSendReceipt> send(String text) {
          expect(text, 'Hello');
          return accepted.future;
        }

        Widget screen(List<ChatTurnEvent> turns) => MaterialApp(
          home: Scaffold(
            body: BrainChatScreen(
              chatName: 'main',
              turns: turns,
              onSend: send,
              onActivityAccepted: (_, commandId) => receipt = commandId,
            ),
          ),
        );
        await tester.pumpWidget(screen(const []));
        final chat = tester.widget<KitChat>(find.byType(KitChat));
        chat.onMessageSend!('  Hello  ');
        await tester.pump();
        expect(
          chat.chatController.messages.whereType<TextMessage>().single.text,
          'Hello',
        );
        expect(
          chat.chatController.messages.whereType<TextStreamMessage>(),
          hasLength(1),
        );
        final turns = [
          _turn(1, 'Hello', 'accepted-turn', fromUser: true),
          _turn(2, 'Answer', 'accepted-turn'),
        ];
        if (responseFirst) {
          await tester.pumpWidget(screen(turns));
          expect(
            chat.chatController.messages.whereType<TextStreamMessage>(),
            hasLength(1),
          );
        }
        accepted.complete(
          const ChatSendReceipt(
            turnId: 'accepted-turn',
            commandId: 'command-accepted-turn',
          ),
        );
        await tester.pump();
        expect(receipt, 'command-accepted-turn');
        if (!responseFirst) {
          expect(
            chat.chatController.messages.whereType<TextStreamMessage>(),
            hasLength(1),
          );
          await tester.pumpWidget(screen(turns));
        }
        await tester.pump();
        expect(
          chat.chatController.messages.whereType<TextStreamMessage>(),
          isEmpty,
        );
        expect(
          chat.chatController.messages.whereType<TextMessage>().map(
            (message) => message.text,
          ),
          ['Hello', 'Answer'],
        );
        await tester.pumpWidget(const SizedBox.shrink());
        await tester.pump(const Duration(seconds: 1));
      },
    );
  }

  testWidgets('repeated transcript resets refresh text with reused sequences', (
    tester,
  ) async {
    Widget screen(String text) => MaterialApp(
      home: Scaffold(
        body: BrainChatScreen(
          chatName: 'main',
          turns: [_turn(-1, text, 'replayed-turn')],
        ),
      ),
    );
    await tester.pumpWidget(screen('Old transcript'));
    await tester.pumpWidget(screen('Replacement transcript'));
    final chat = tester.widget<KitChat>(find.byType(KitChat));
    expect(
      chat.chatController.messages.whereType<TextMessage>().single.text,
      'Replacement transcript',
    );
    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pump(const Duration(seconds: 1));
  });
}

ChatTurnEvent _turn(
  int sequence,
  String text,
  String turnId, {
  bool fromUser = false,
}) => ChatTurnEvent(
  sequence: sequence,
  fromUser: fromUser,
  text: text,
  commandId: 'command-$turnId',
  signal: fromUser ? 'TurnAccepted' : 'Responded',
  neuronId: 'assistant',
  correlationId: 'correlation-$turnId',
  timestamp: DateTime.utc(2026).add(Duration(seconds: sequence)),
  turnId: turnId,
);
