import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat_screen.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

void main() {
  testWidgets('compact history overlay shares draft and active request', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    var subscriptions = 0;
    var cancellations = 0;
    final replies = StreamController<ChatDelta>(
      onListen: () => subscriptions++,
      onCancel: () => cancellations++,
    );
    addTearDown(() async {
      if (subscriptions == 0) await replies.stream.listen(null).cancel();
      await replies.close();
    });
    final sent = <String>[];
    var turns = [
      shellTurn(1, true, 'Earlier question', commandId: 'old'),
      shellTurn(2, false, 'Earlier answer', commandId: 'old'),
    ];
    Future<void> show() => tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Align(
            alignment: Alignment.bottomCenter,
            child: SizedBox(
              width: 700,
              child: BrainChatScreen(
                key: const Key('same_conversation'),
                chatName: 'main',
                turns: turns,
                presentation: BrainChatPresentation.compact,
                compactReplyMaxHeight: 100,
                onStream: (text) {
                  sent.add(text);
                  return replies.stream;
                },
              ),
            ),
          ),
        ),
      ),
    );
    await show();
    await tester.pumpAndSettle();
    expect(find.byType(KitMarkdown), findsOneWidget);
    await tester.enterText(find.byType(EditableText), 'Review this diff');
    await tester.sendKeyEvent(LogicalKeyboardKey.enter);
    await tester.pump();
    expect(sent, ['Review this diff']);
    expect(subscriptions, 1);
    replies.add(
      const ChatDelta.accepted(commandId: 'review', turnId: 't-review'),
    );
    await tester.pump();
    await tester.enterText(find.byType(EditableText), 'Keep this draft');
    await tester.tap(find.byKey(const Key('chat_open_history')));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 100));
    expect(find.byKey(const Key('chat_history_overlay')), findsOneWidget);
    expect(
      tester.widget<EditableText>(find.byType(EditableText)).controller.text,
      'Keep this draft',
    );
    expect(subscriptions, 1);
    expect(cancellations, 0);
    turns = [
      ...turns,
      shellTurn(3, true, 'Review this diff', commandId: 'review'),
      shellTurn(4, false, 'Review completed', commandId: 'review'),
    ];
    await show();
    await tester.pump();
    await tester.sendKeyEvent(LogicalKeyboardKey.escape);
    await tester.pump();
    expect(find.byKey(const Key('chat_history_overlay')), findsNothing);
    expect(
      tester.widget<EditableText>(find.byType(EditableText)).controller.text,
      'Keep this draft',
    );
    expect(
      tester.widget<KitMarkdown>(find.byType(KitMarkdown)).text,
      'Review completed',
    );
    expect(subscriptions, 1);
    await tester.pumpWidget(const SizedBox());
    await drainShellTimers(tester);
    expect(cancellations, 1);
    expect(tester.takeException(), isNull);
  }, timeout: const Timeout(Duration(seconds: 60)));

  testWidgets(
    'switching full and compact presentation keeps the request alive',
    (tester) async {
      await prepareShellSurface(tester);
      var subscriptions = 0;
      var cancellations = 0;
      final replies = StreamController<ChatDelta>(
        onListen: () => subscriptions++,
        onCancel: () => cancellations++,
      );
      addTearDown(() async {
        if (subscriptions == 0) await replies.stream.listen(null).cancel();
        await replies.close();
      });
      var presentation = BrainChatPresentation.compact;
      Future<void> show() => tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: BrainChatScreen(
              key: const Key('same_conversation'),
              chatName: 'main',
              turns: const [],
              presentation: presentation,
              onStream: (_) => replies.stream,
            ),
          ),
        ),
      );
      await show();
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(EditableText), 'Hello');
      await tester.testTextInput.receiveAction(TextInputAction.send);
      await tester.pump();
      replies.add(
        const ChatDelta.accepted(commandId: 'hello', turnId: 't-hello'),
      );
      await tester.pump();
      presentation = BrainChatPresentation.full;
      await show();
      await tester.pump();
      expect(find.byKey(const Key('chat_surface')), findsOneWidget);
      expect(subscriptions, 1);
      expect(cancellations, 0);
      presentation = BrainChatPresentation.compact;
      await show();
      await tester.pump();
      expect(find.byKey(const Key('compact_chat_surface')), findsOneWidget);
      expect(subscriptions, 1);
      expect(cancellations, 0);
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
      expect(cancellations, 1);
      expect(tester.takeException(), isNull);
    },
    timeout: const Timeout(Duration(seconds: 60)),
  );

  testWidgets('compact chat keeps a pending Gmail login actionable', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    Uri? opened;
    final offer = ChatTurnEvent.fromJson({
      'sequence': 1,
      'fromUser': false,
      'text': 'Connect Gmail to continue.',
      'commandId': 'login',
      'signal': 'Responded',
      'neuronId': 'chat:dev/main',
      'caller': 'chat:dev/main',
      'correlationId': 'login-correlation',
      'timestamp': '2026-09-01T10:00:00Z',
      'turnId': 'login-turn',
      'status': 'WaitingForUser',
      'userAction': {
        'id': 'gmail-login',
        'provider': 'gmail',
        'displayName': 'Gmail',
        'message': 'Sign in to continue this request.',
        'loginUrl':
            'http://localhost:5080/integrations/gmail/login?request=trusted-request',
        'expiresAt': '2035-01-01T00:00:00Z',
        'resumeToolNames': ['gmail_search_threads'],
      },
    });
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Align(
            alignment: Alignment.bottomCenter,
            child: SizedBox(
              width: 700,
              child: BrainChatScreen(
                chatName: 'main',
                turns: [offer],
                presentation: BrainChatPresentation.compact,
                compactReplyMaxHeight: 350,
                kernelBaseUri: Uri.parse('http://localhost:5080'),
                onOpenSignIn: (uri) async {
                  opened = uri;
                },
              ),
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.ensureVisible(
      find.byKey(const Key('user_action_authorize_gmail')),
    );
    await tester.tap(find.byKey(const Key('user_action_authorize_gmail')));
    await tester.pump();
    expect(opened?.path, '/integrations/gmail/login');
    await tester.pumpWidget(const SizedBox());
    await drainShellTimers(tester);
    expect(tester.takeException(), isNull);
  }, timeout: const Timeout(Duration(seconds: 60)));
}
