import 'package:digitalbrain_flutter_shell/user_actions/user_action_card.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

void main() {
  testWidgets('user action card shows module text and authorize without secrets', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    var opened = false;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: UserActionCard(
            model: UserActionCardModel(
              moduleId: 'google.gmail',
              displayText: 'Connect Gmail to continue enrichment',
              actionUrl: Uri.parse('https://example.test/oauth'),
              taskId: 'task:owner/enrich-1',
              continuationState: 'waiting',
            ),
            onAuthorize: () => opened = true,
          ),
        ),
      ),
    );

    expect(find.text('Connect Gmail to continue enrichment'), findsOneWidget);
    expect(find.textContaining('Task task:owner/enrich-1'), findsOneWidget);
    expect(find.textContaining('secret'), findsNothing);
    expect(find.textContaining('token'), findsNothing);
    await tester.tap(find.text('Connect / Authorize'));
    expect(opened, isTrue);
  });
}
