import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:digitalbrain_ui/src/composition/neuron_view.dart';

void main() {
  testWidgets('surface card collection descendants have bounded space', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NeuronView(
            kind: 'surface',
            name: 'root',
            load: (kind, name) async => {
              'definition': switch (kind) {
                'surface' => {
                  'children': [
                    {'kind': 'card', 'name': 'card'},
                  ],
                },
                'card' => {
                  'title': 'Nested',
                  'children': [
                    {'kind': 'collection', 'name': 'list'},
                  ],
                },
                _ => {
                  'items': [
                    {
                      'id': 'one',
                      'label': 'Nested item',
                      'kind': 'file',
                      'canActivate': false,
                    },
                  ],
                },
              },
            },
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    expect(find.text('Nested item'), findsOneWidget);
  });
  testWidgets('Card metadata preserves and renders live child references', (
    tester,
  ) async {
    final part = UiCardPart.fromMetadata({
      'title': 'Container',
      'body': '',
      'children': [
        {'kind': 'collection', 'name': 'items'},
      ],
    });
    expect(part.children.single.name, 'items');
    expect(part.toMetadata()['children'], [
      {'kind': 'collection', 'name': 'items'},
    ]);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: UiCard(
            part: part,
            loadChild: (_, _) async => {
              'definition': {
                'items': [
                  {
                    'id': 'one',
                    'label': 'Child item',
                    'kind': 'file',
                    'canActivate': false,
                  },
                ],
              },
            },
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Child item'), findsOneWidget);
  });

  testWidgets('surface renders collection children and routes activation', (
    tester,
  ) async {
    String? activated;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NeuronView(
            kind: 'surface',
            name: 'root',
            load: (kind, name) async => switch (kind) {
              'surface' => {
                'definition': {
                  'children': [
                    {'kind': 'collection', 'name': 'items'},
                  ],
                },
              },
              _ => {
                'revision': 1,
                'definition': {
                  'items': [
                    {
                      'id': 'image',
                      'label': 'Untitled.png',
                      'kind': 'image',
                      'canActivate': true,
                    },
                  ],
                },
              },
            },
            onActivate: (item) => activated = item['id'] as String,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Untitled.png'));
    expect(activated, 'image');
  });
  testWidgets('recursive child is isolated instead of recurring forever', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NeuronView(
            kind: 'surface',
            name: 'root',
            load: (_, _) async => {
              'definition': {
                'children': [
                  {'kind': 'surface', 'name': 'root'},
                ],
              },
            },
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('This component cannot be displayed.'), findsOneWidget);
  });
}
