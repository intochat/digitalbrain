import 'package:digitalbrain_flutter_shell/workspace/mydata/mydata_window.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('lists masked field handles from the vault', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: MyDataWindow(
            request: (path, {body}) async => {
              'owner': 'local-owner',
              'erased': false,
              'fields': [
                {
                  'fieldPath': 'me.birthDate',
                  'kind': 'Date',
                  'display': '••••',
                  'isSecret': false,
                },
                {
                  'fieldPath': 'me.apiKey',
                  'kind': 'Secret',
                  'display': '•••• set',
                  'isSecret': true,
                },
              ],
            },
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('me.birthDate'), findsOneWidget);
    expect(find.text('me.apiKey'), findsOneWidget);
    expect(find.textContaining('•••• set'), findsOneWidget);
  });

  testWidgets('surfaces a failure without leaking a value', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: MyDataWindow(
            request: (path, {body}) async => throw StateError('offline'),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.textContaining('My Data is unavailable'), findsOneWidget);
  });
}
