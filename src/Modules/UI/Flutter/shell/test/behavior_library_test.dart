import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/behaviors/behavior_library.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

void main() {
  testWidgets('library shows empty, loading, error, and item states', (
    tester,
  ) async {
    await prepareShellSurface(tester);

    await tester.pumpWidget(
      const MaterialApp(
        home: BehaviorLibraryView(items: [], loading: true),
      ),
    );
    expect(find.byType(CircularProgressIndicator), findsOneWidget);

    await tester.pumpWidget(
      const MaterialApp(
        home: BehaviorLibraryView(
          items: [],
          loading: false,
          error: 'edge down',
        ),
      ),
    );
    expect(find.text('edge down'), findsOneWidget);

    await tester.pumpWidget(
      MaterialApp(
        home: BehaviorLibraryView(
          items: [
            BehaviorLibraryItem(
              behaviorId: 'com.demo',
              displayName: 'Demo',
              description: 'Purpose text',
              status: 'Active',
              runState: 'Running',
              activationGateOpen: true,
              overview: 'Demo: greets the owner',
              scenarioTitles: const ['greet owner'],
              health: 'healthy',
            ),
          ],
          loading: false,
          onOpen: (_) {},
        ),
      ),
    );
    expect(find.byKey(const Key('behavior_library')), findsOneWidget);
    expect(find.text('Demo'), findsOneWidget);
    expect(find.text('Purpose text'), findsOneWidget);
    expect(find.text('Demo: greets the owner'), findsOneWidget);
    expect(find.text('greet owner'), findsOneWidget);
    expect(find.text('healthy'), findsOneWidget);
  });
}
