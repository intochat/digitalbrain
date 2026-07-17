import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:workspace/blocks/block_action.dart';
import 'package:workspace/blocks/block_document.dart';
import 'package:workspace/blocks/block_view.dart';

Future<void> pumpBlockView(
  WidgetTester tester,
  Map<String, dynamic> doc, {
  void Function(BlockAction)? onAction,
}) async {
  final parsed = BlockDocument.parse(jsonEncode(doc));
  await tester.pumpWidget(
    MaterialApp(
      home: Scaffold(body: BlockView(parsed, onAction: onAction)),
    ),
  );
}

void main() {
  testWidgets('text renders its value', (tester) async {
    await pumpBlockView(tester, {
      'version': 1,
      'blocks': [
        {'kind': 'text', 'value': 'Hello world'},
      ],
    });

    expect(find.text('Hello world'), findsOneWidget);
  });

  testWidgets('metric renders label and value', (tester) async {
    await pumpBlockView(tester, {
      'version': 1,
      'blocks': [
        {'kind': 'metric', 'label': 'Latency', 'value': 42},
      ],
    });

    expect(find.text('Latency'), findsOneWidget);
    expect(find.text('42'), findsOneWidget);
  });

  testWidgets('field renders label and value', (tester) async {
    await pumpBlockView(tester, {
      'version': 1,
      'blocks': [
        {'kind': 'field', 'label': 'Owner', 'value': 'vlad'},
      ],
    });

    expect(find.text('Owner: '), findsOneWidget);
    expect(find.text('vlad'), findsOneWidget);
  });

  testWidgets('columns renders each child', (tester) async {
    await pumpBlockView(tester, {
      'version': 1,
      'blocks': [
        {
          'kind': 'columns',
          'children': [
            {'kind': 'text', 'value': 'Left'},
            {'kind': 'text', 'value': 'Right'},
          ],
        },
      ],
    });

    expect(find.text('Left'), findsOneWidget);
    expect(find.text('Right'), findsOneWidget);
  });

  testWidgets(
    'table with no columns renders a fallback tile without throwing',
    (tester) async {
      await pumpBlockView(tester, {
        'version': 1,
        'blocks': [
          {'kind': 'table', 'columns': <dynamic>[], 'rows': <dynamic>[]},
        ],
      });

      expect(find.text('unsupported block: table'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets('list renders each item', (tester) async {
    await pumpBlockView(tester, {
      'version': 1,
      'blocks': [
        {
          'kind': 'list',
          'items': ['Alpha', 'Beta'],
        },
      ],
    });

    expect(find.text('• Alpha'), findsOneWidget);
    expect(find.text('• Beta'), findsOneWidget);
  });

  testWidgets('table renders headers and a cell', (tester) async {
    await pumpBlockView(tester, {
      'version': 1,
      'blocks': [
        {
          'kind': 'table',
          'columns': ['A', 'B'],
          'rows': [
            ['1', '2'],
          ],
        },
      ],
    });

    expect(find.text('A'), findsOneWidget);
    expect(find.text('B'), findsOneWidget);
    expect(find.text('1'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('jagged table row does not throw and still renders', (
    tester,
  ) async {
    await pumpBlockView(tester, {
      'version': 1,
      'blocks': [
        {
          'kind': 'table',
          'columns': ['A', 'B', 'C'],
          'rows': [
            ['1'],
          ],
        },
      ],
    });

    expect(find.text('A'), findsOneWidget);
    expect(find.text('1'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('timeline with two entries renders both titles', (tester) async {
    await pumpBlockView(tester, {
      'version': 1,
      'blocks': [
        {
          'kind': 'timeline',
          'entries': [
            {'kind': 'entry', 'title': 'E1', 'detail': 'D1'},
            {'kind': 'entry', 'title': 'E2', 'detail': 'D2'},
          ],
        },
      ],
    });

    expect(find.text('E1'), findsOneWidget);
    expect(find.text('E2'), findsOneWidget);
  });

  testWidgets('media with a bad url shows the alt text', (tester) async {
    await pumpBlockView(tester, {
      'version': 1,
      'blocks': [
        {'kind': 'media', 'url': 'not-a-real-url', 'alt': 'Broken image'},
      ],
    });

    await tester.pumpAndSettle();

    expect(find.text('Broken image'), findsOneWidget);
  });

  testWidgets('progress renders a LinearProgressIndicator', (tester) async {
    await pumpBlockView(tester, {
      'version': 1,
      'blocks': [
        {'kind': 'progress', 'label': 'Sync', 'fraction': 0.5},
      ],
    });

    expect(find.byType(LinearProgressIndicator), findsOneWidget);
  });

  testWidgets('actionRow tap invokes onAction with matching contract', (
    tester,
  ) async {
    BlockAction? captured;
    await pumpBlockView(tester, {
      'version': 1,
      'blocks': [
        {
          'kind': 'actionRow',
          'actions': [
            {
              'label': 'Approve',
              'contract': 'effect.approve.v1',
              'inputJson': '{}',
            },
          ],
        },
      ],
    }, onAction: (action) => captured = action);

    await tester.tap(find.text('Approve'));
    await tester.pump();

    expect(captured, isNotNull);
    expect(captured!.contract, 'effect.approve.v1');
  });

  testWidgets('nested section renders a child text block', (tester) async {
    await pumpBlockView(tester, {
      'version': 1,
      'blocks': [
        {
          'kind': 'section',
          'title': 'Section title',
          'children': [
            {'kind': 'text', 'value': 'Child text'},
          ],
        },
      ],
    });

    expect(find.text('Section title'), findsOneWidget);
    expect(find.text('Child text'), findsOneWidget);
  });

  testWidgets('unknown kind renders a fallback tile without throwing', (
    tester,
  ) async {
    await pumpBlockView(tester, {
      'version': 1,
      'blocks': [
        {'kind': 'bogus'},
      ],
    });

    expect(find.text('unsupported block: bogus'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
