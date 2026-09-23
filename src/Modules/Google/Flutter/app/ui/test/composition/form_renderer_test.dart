import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('form renders a date picker and a masked secret from one read', (
    tester,
  ) async {
    final dispatched = <Map<String, dynamic>>[];
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NeuronView(
            kind: 'form',
            name: 'intake',
            load: (kind, name) async => {
              'definition': {
                'title': 'Customer intake',
                'revision': 1,
                'submitted': false,
                'fields': [
                  {
                    'name': 'birthDate',
                    'label': 'Date of birth',
                    'kind': 'Date',
                    'required': true,
                    'choices': <String>[],
                    'value': null,
                    'secretSet': false,
                    'supported': true,
                  },
                  {
                    'name': 'password',
                    'label': 'Password',
                    'kind': 'Secret',
                    'required': false,
                    'choices': <String>[],
                    'value': null,
                    'secretSet': true,
                    'supported': true,
                  },
                ],
              },
            },
            onAction: (event) async => dispatched.add(event),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Customer intake'), findsOneWidget);
    expect(find.text('Date of birth *'), findsOneWidget);
    expect(find.byIcon(Icons.calendar_today), findsOneWidget);
    expect(find.text('•••• set'), findsOneWidget);
    expect(
      find.byWidgetPredicate((widget) => widget is TextField && widget.obscureText),
      findsOneWidget,
    );

    await tester.tap(find.byIcon(Icons.calendar_today));
    await tester.pumpAndSettle();
    expect(find.byType(DatePickerDialog), findsOneWidget);
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
  });

  testWidgets('form metadata round-trips typed fields', (tester) async {
    final part = UiFormPart.fromMetadata({
      'title': 'Plan',
      'submitted': true,
      'fields': [
        {
          'name': 'tier',
          'label': 'Tier',
          'kind': 'Choice',
          'required': true,
          'choices': ['free', 'pro'],
          'value': 'pro',
          'secretSet': false,
          'supported': true,
        },
      ],
    });
    expect(part.fields.single.kind, 'Choice');
    expect(part.fields.single.choices, ['free', 'pro']);
    expect(part.toMetadata()['submitted'], true);
  });

  testWidgets('secret input posts to the vault and dispatches only the reference', (
    tester,
  ) async {
    final dispatched = <Map<String, dynamic>>[];
    final saved = <String>[];
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NeuronView(
            kind: 'form',
            name: 'login',
            load: (kind, name) async => {
              'definition': {
                'title': 'Login',
                'revision': 1,
                'submitted': false,
                'fields': [
                  {
                    'name': 'password',
                    'label': 'Password',
                    'kind': 'Secret',
                    'required': false,
                    'choices': <String>[],
                    'value': null,
                    'secretSet': false,
                    'supported': true,
                  },
                ],
              },
            },
            onAction: (event) async => dispatched.add(event),
            secretSaver: (field, value) async {
              saved.add(value);
              return 'secret://owner/pw-1';
            },
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.enterText(find.byType(TextField), 'hunter2');
    await tester.tap(find.byKey(const Key('form_secret_save')));
    await tester.pumpAndSettle();

    expect(saved, ['hunter2']);
    final secretEvent = dispatched.singleWhere((e) => e['action'] == 'secret');
    expect(secretEvent['field'], 'password');
    expect(secretEvent['value'], 'secret://owner/pw-1');
    expect(
      dispatched.any((event) => '${event['value']}'.contains('hunter2')),
      isFalse,
    );
  });
}
