import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/behaviors/behavior_assistant_change.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

void main() {
  testWidgets('assistant change requires scenario approval before code generation', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    var approved = false;

    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: BehaviorAssistantChangeView(
            document: shellBehaviorDocument(),
            proposal: const BehaviorChangeProposal(
              proposalId: 'p1',
              behaviorId: 'com.demo',
              requestText: 'also enrich phone',
              proposedFeatureText:
                  'Feature: account enrichment\n  Scenario: also enrich phone\n',
              proposedFeatureName: 'account-enrichment',
              status: 'awaiting-scenario-approval',
              diffSummary: 'Add scenario before source generation.',
            ),
            onApproveScenario: () => approved = true,
            onRejectScenario: () {},
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('behavior_assistant_change')), findsOneWidget);
    expect(find.byKey(const Key('behavior_change_proposal')), findsOneWidget);
    expect(find.textContaining('Scenario: also enrich phone'), findsOneWidget);
    expect(find.byKey(const Key('behavior_change_approve_scenario')), findsOneWidget);
    await tester.ensureVisible(
      find.byKey(const Key('behavior_change_approve_scenario')),
    );
    await tester.tap(find.byKey(const Key('behavior_change_approve_scenario')));
    expect(approved, isTrue);
  });
}
