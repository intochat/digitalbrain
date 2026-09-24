import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/consent_sheet_view.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const sheet = ConsentSheet(
    appId: 'intochat.leadgenerator',
    version: '1.0.0',
    name: 'LeadGenerator',
    descriptionForPeople: 'Finds new customers for you.',
    examples: ['Find new dental clinics in Berlin'],
    dataTypes: [
      ConsentDataType(
        semanticTypeId: 'company.name',
        reason: 'Names the businesses it finds.',
      ),
    ],
    meters: [
      AppMeterSummary(
        meterId: 'search.request',
        unit: 'request',
        aggregation: 'sum',
        proposedPriceInCompute: 5,
      ),
    ],
    estimatedCompute: 50,
    approved: false,
  );

  testWidgets('shows examples, data types, meters and the shadow estimate', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: ConsentSheetView(
            sheet: sheet,
            onApprove: () {},
            onNotNow: () {},
          ),
        ),
      ),
    );

    expect(find.text('Add LeadGenerator?'), findsOneWidget);
    expect(find.textContaining('dental clinics in Berlin'), findsOneWidget);
    expect(find.textContaining('company.name'), findsOneWidget);
    expect(find.textContaining('search.request'), findsOneWidget);
    expect(find.textContaining('Estimated 50 Compute'), findsOneWidget);
    expect(find.text('Approve'), findsOneWidget);
    expect(find.text('Not now'), findsOneWidget);
  });

  testWidgets('shows permissions with reasons when supplied', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: ConsentSheetView(
            sheet: sheet,
            permissions: const [
              AppPermissionSummary(
                semanticTypeId: 'person.birthDate',
                reason: 'To greet you on your birthday.',
              ),
            ],
            onApprove: () {},
            onNotNow: () {},
          ),
        ),
      ),
    );

    expect(find.textContaining('person.birthDate'), findsOneWidget);
    expect(find.textContaining('birthday'), findsOneWidget);
  });

  testWidgets('Approve and Not now reach their callbacks', (tester) async {
    var approved = false;
    var declined = false;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: ConsentSheetView(
            sheet: sheet,
            onApprove: () => approved = true,
            onNotNow: () => declined = true,
          ),
        ),
      ),
    );

    await tester.tap(find.text('Approve'));
    await tester.tap(find.text('Not now'));
    expect(approved, isTrue);
    expect(declined, isTrue);
  });
}