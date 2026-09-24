import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
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
    expect(find.text('No app can read your data.'), findsOneWidget);
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

  testWidgets('lists grants and revokes one in a single click', (tester) async {
    final grants = <GrantSummary>[
      GrantSummary(
        appId: 'intochat.leadgenerator',
        semanticTypeId: 'person.birthDate',
        mode: 'Once',
        grantedAt: DateTime.utc(2026, 9, 23, 12),
      ),
      GrantSummary(
        appId: 'intochat.image-editor',
        semanticTypeId: 'person.email.work',
        mode: 'Always',
        grantedAt: DateTime.utc(2026, 9, 22, 9),
      ),
    ];
    final revoked = <GrantSummary>[];

    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: MyDataWindow(
            request: (path, {body}) async => {'fields': const []},
            loadGrants: () async => grants,
            revokeGrant: (grant) async => revoked.add(grant),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('person.birthDate'), findsOneWidget);
    expect(find.textContaining('intochat.leadgenerator · Once'), findsOneWidget);
    expect(find.textContaining('granted 2026-09-23'), findsOneWidget);
    expect(find.text('Revoke'), findsNWidgets(2));

    await tester.tap(find.text('Revoke').first);
    await tester.pumpAndSettle();

    expect(revoked.single.semanticTypeId, 'person.birthDate');
    expect(find.text('person.birthDate'), findsNothing);
    expect(find.text('person.email.work'), findsOneWidget);
  });
}