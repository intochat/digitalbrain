import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  Future<void> pump(
    WidgetTester tester, {
    bool dismissed = false,
    bool dismissing = false,
    VoidCallback? onDismiss,
  }) async {
    tester.view.physicalSize = const Size(320, 700);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(
      MaterialApp(
        theme: UiTheme.workspace(Brightness.light),
        home: Scaffold(
          body: UiNotificationCard(
            eventId: 'telegram:1',
            title: 'Reminder',
            message: '<script>alert("hello")</script> **plain text**',
            kind: 'reminder',
            createdUnixSeconds: 1789473600,
            dismissed: dismissed,
            dismissing: dismissing,
            onDismiss: onDismiss,
          ),
        ),
      ),
    );
  }

  testWidgets('renders provider markup literally at narrow width', (
    tester,
  ) async {
    await pump(tester);
    expect(find.text('Reminder'), findsOneWidget);
    expect(
      find.text('<script>alert("hello")</script> **plain text**'),
      findsOneWidget,
    );
    expect(find.byKey(const Key('ui_notification_telegram:1')), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('dismiss calls callback', (tester) async {
    var calls = 0;
    await pump(tester, onDismiss: () => calls++);
    await tester.tap(find.text('Dismiss'));
    await tester.pumpAndSettle();
    expect(calls, 1);
  });

  testWidgets('dismissed notification cannot be dismissed again', (
    tester,
  ) async {
    var calls = 0;
    await pump(tester, dismissed: true, onDismiss: () => calls++);
    expect(find.text('Dismissed'), findsOneWidget);
    expect(find.text('Dismiss'), findsNothing);
    expect(calls, 0);
  });

  testWidgets('pending dismissal disables the callback', (tester) async {
    var calls = 0;
    await pump(tester, dismissing: true, onDismiss: () => calls++);
    await tester.tap(find.text('Dismissing…'));
    await tester.pumpAndSettle();
    expect(calls, 0);
  });
}
