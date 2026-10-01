import 'dart:async';
import 'dart:convert';

import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets(
    'field edits precede button actions and refresh preserves input identity',
    (tester) async {
      var draft = '';
      final editSaved = Completer<void>();
      final events = <Map<String, dynamic>>[];
      Future<Map<String, dynamic>> load(String kind, String name) async =>
          switch (kind) {
            'surface' => {
              'children': [
                {'kind': 'textfield', 'name': 'draft'},
                {'kind': 'button', 'name': 'send'},
              ],
              'extents': [100, 48],
            },
            'textfield' => {
              'label': 'Message',
              'kind': 'multiline',
              'value': draft,
            },
            _ => {'label': 'Submit', 'action': 'send', 'enabled': true},
          };
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: NeuronView(
              kind: 'surface',
              name: 'root',
              load: load,
              onAction: (event) async {
                events.add(event);
                if (event['kind'] == 'textfield') {
                  await editSaved.future;
                  draft = event['value'] as String;
                } else {
                  expect(draft, 'Queued input');
                  draft = '';
                }
              },
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      final input = find.byType(TextField);
      final original = tester.state(input);
      await tester.enterText(input, 'Queued input');
      await tester.tap(find.text('Submit'));
      await tester.pump(const Duration(seconds: 2));
      expect(events.length, 1);
      expect(find.text('Queued input'), findsOneWidget);
      expect(tester.state(input), same(original));
      editSaved.complete();
      await tester.pumpAndSettle();
      expect(events.map((event) => event['kind']), ['textfield', 'button']);
      expect(tester.widget<TextField>(input).controller!.text, isEmpty);
      expect(tester.widget<TextField>(input).focusNode!.hasFocus, isFalse);
      expect(tester.state(input), same(original));
      await tester.pumpWidget(const SizedBox());
    },
  );

  testWidgets('dynamic nested lists render auto-height markdown cards', (
    tester,
  ) async {
    var includeAnswer = false;
    Future<Map<String, dynamic>> load(String kind, String name) async =>
        switch (name) {
          'root' => {
            'mode': 'list',
            'children': [
              {'kind': 'layout', 'name': 'page'},
            ],
          },
          'page' => {
            'mode': 'list',
            'children': [
              {'kind': 'card', 'name': 'question'},
              if (includeAnswer) {'kind': 'card', 'name': 'answer'},
            ],
          },
          'question' => {'title': 'User', 'body': 'Question', 'children': []},
          _ => {'title': 'Assistant', 'body': 'An **answer**', 'children': []},
        };
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NeuronView(kind: 'layout', name: 'root', load: load),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Question'), findsOneWidget);
    includeAnswer = true;
    await tester.pump(const Duration(seconds: 2));
    await tester.pumpAndSettle();
    expect(find.text('Assistant'), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('declared button activation returns the complete artifact', (
    tester,
  ) async {
    final artifact = {
      'id': 'table',
      'title': 'Leads',
      'kind': 'table',
      'remoteManaged': true,
    };
    Map<String, dynamic>? activated;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NeuronView(
            kind: 'button',
            name: 'open',
            load: (_, _) async => {
              'label': 'Open result',
              'enabled': true,
              'action': 'activate',
              'activation': jsonEncode(artifact),
            },
            onActivate: (value) => activated = value,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Open result'));
    await tester.pumpAndSettle();
    expect(activated, artifact);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets(
    'field sends its last edit even when its surface is removed immediately',
    (tester) async {
      final events = <Map<String, dynamic>>[];
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: NeuronView(
              kind: 'textfield',
              name: 'draft',
              load: (_, _) async => {'kind': 'multiline', 'value': ''},
              onAction: (event) async => events.add(event),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextField), 'Saved on the neuron');
      await tester.pumpWidget(const SizedBox());
      await tester.pump();
      expect(events.single, {
        'kind': 'textfield',
        'name': 'draft',
        'value': 'Saved on the neuron',
      });
    },
  );
}
