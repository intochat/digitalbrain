import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:workspace/kinds/connection_health.dart';

void main() {
  testWidgets('notAuthorized health shows a Reauthorize fix button', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: ConnectionHealth(
            data: {
              'provider': 'google',
              'health': 'notAuthorized',
              'fix': 'reauthorize',
            },
          ),
        ),
      ),
    );

    expect(find.text('Reauthorize'), findsOneWidget);
  });

  testWidgets(
    'notConfigured health with a connect fix shows a Connect button',
    (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: Scaffold(
            body: ConnectionHealth(
              data: {
                'provider': 'google',
                'health': 'notConfigured',
                'fix': 'connect',
              },
            ),
          ),
        ),
      );

      expect(find.text('Connect'), findsOneWidget);
    },
  );

  testWidgets('healthy renders a healthy chip and no fix button', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: ConnectionHealth(
            data: {'provider': 'google', 'health': 'healthy', 'fix': 'none'},
          ),
        ),
      ),
    );

    expect(find.text('healthy'), findsOneWidget);
    expect(find.byType(OutlinedButton), findsNothing);
  });

  testWidgets('chip color differs between healthy and notAuthorized', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: ConnectionHealth(
            data: {'provider': 'google', 'health': 'healthy', 'fix': 'none'},
          ),
        ),
      ),
    );
    final healthyChip = tester.widget<Chip>(find.byType(Chip));

    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: ConnectionHealth(
            data: {
              'provider': 'google',
              'health': 'notAuthorized',
              'fix': 'reauthorize',
            },
          ),
        ),
      ),
    );
    final warnChip = tester.widget<Chip>(find.byType(Chip));

    expect(
      healthyChip.backgroundColor,
      isNot(equals(warnChip.backgroundColor)),
    );
  });
}
