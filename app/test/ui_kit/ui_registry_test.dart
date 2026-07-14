import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';

import 'package:digitalbrain_flutter/ui_kit/ui_registry.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_text.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_button.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_panel.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_screen.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_table.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_graph_canvas.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_text_field.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';

Widget _wrap(Widget child) => MaterialApp(
  builder: (_, w) => FTheme(data: FTheme.neutral.light.touch, child: w!),
  home: Scaffold(body: child),
);

Widget _noop(Map<String, Object?> _) => const SizedBox.shrink();

void main() {
  group('buildUiNode', () {
    test('returns SizedBox.shrink for unknown type', () {
      final result = buildUiNode(
        'unknown:widget',
        {},
        [],
        (n, a) {},
        buildChild: _noop,
      );
      expect(result, isA<SizedBox>());
    });

    test('maps ui:text to UiKitText', () {
      final node = buildUiNode(
        'ui:text',
        {'text': 'hello'},
        [],
        (_, _) {},
        buildChild: _noop,
      );
      expect(node, isA<UiKitText>());
    });

    test('maps UI:TEXT (uppercase) to UiKitText', () {
      final node = buildUiNode(
        'UI:TEXT',
        {'text': 'hello'},
        [],
        (_, _) {},
        buildChild: _noop,
      );
      expect(node, isA<UiKitText>());
    });

    test('maps ui:button to UiKitButton', () {
      final node = buildUiNode(
        'ui:button',
        {'label': 'Go', 'pack': 'p', 'experienceId': 'e', 'eventName': 'n'},
        [],
        (_, _) {},
        buildChild: _noop,
      );
      expect(node, isA<UiKitButton>());
    });

    test('maps ui:panel to UiKitPanel', () {
      final node = buildUiNode(
        'ui:panel',
        {},
        [],
        (_, _) {},
        buildChild: _noop,
      );
      expect(node, isA<UiKitPanel>());
    });

    test('maps ui:screen to UiKitScreen', () {
      final node = buildUiNode(
        'ui:screen',
        {},
        [],
        (_, _) {},
        buildChild: _noop,
      );
      expect(node, isA<UiKitScreen>());
    });

    test('maps ui:textfield to UiKitTextField', () {
      final node = buildUiNode(
        'ui:textfield',
        {'name': 'email'},
        [],
        (_, _) {},
        buildChild: _noop,
      );
      expect(node, isA<UiKitTextField>());
    });

    test('maps ui:table to UiKitTable with parsed columns and rows', () {
      final node = buildUiNode(
        'ui:table',
        {
          'columns': ['Month', 'Revenue'],
          'rows': [
            ['Jan', '12000'],
            ['Feb', '14500'],
          ],
        },
        [],
        (_, _) {},
        buildChild: _noop,
      );
      expect(node, isA<UiKitTable>());
      final table = node as UiKitTable;
      expect(table.columns, ['Month', 'Revenue']);
      expect(table.rows, [
        ['Jan', '12000'],
        ['Feb', '14500'],
      ]);
    });

    test('maps ui:graphcanvas to UiKitGraphCanvas', () {
      final node = buildUiNode(
        'ui:graphcanvas',
        {
          'title': 'Object relation',
          'nodes': [
            {'id': 'object-1', 'label': 'Object 1'},
            {'id': 'object-2', 'label': 'Object 2'},
          ],
          'edges': [
            {'from': 'object-1', 'to': 'object-2', 'label': 'relates to'},
          ],
        },
        [],
        (_, _) {},
        buildChild: _noop,
      );
      expect(node, isA<UiKitGraphCanvas>());
    });

    testWidgets('ui:table renders header and row cells', (tester) async {
      final node = buildUiNode(
        'ui:table',
        {
          'columns': ['Month', 'Revenue'],
          'rows': [
            ['Jan', '12000'],
          ],
        },
        [],
        (_, _) {},
        buildChild: _noop,
      );

      await tester.pumpWidget(_wrap(node));
      expect(find.text('Month'), findsOneWidget);
      expect(find.text('Jan'), findsOneWidget);
      expect(find.text('12000'), findsOneWidget);
    });

    testWidgets('ui:graphcanvas renders two-node relation graph', (
      tester,
    ) async {
      final node = buildUiNode(
        'ui:graphcanvas',
        {
          'title': 'Object relation',
          'nodes': [
            {'id': 'object-1', 'label': 'Object 1'},
            {'id': 'object-2', 'label': 'Object 2'},
          ],
          'edges': [
            {'from': 'object-1', 'to': 'object-2', 'label': 'relates to'},
          ],
        },
        [],
        (_, _) {},
        buildChild: _noop,
      );

      await tester.pumpWidget(_wrap(SizedBox(width: 700, child: node)));
      expect(find.text('Object relation'), findsOneWidget);
      expect(find.text('Object 1'), findsOneWidget);
      expect(find.text('Object 2'), findsOneWidget);
      expect(find.text('relates to'), findsOneWidget);
    });

    testWidgets(
      'ui:graphcanvas renders schema fields without dropping labels',
      (tester) async {
        final node = buildUiNode(
          'ui:graphcanvas',
          {
            'title': r'E:\budget.db schema',
            'layout': 'schema',
            'nodes': [
              {
                'id': 'accounts',
                'label': 'accounts',
                'kind': 'table',
                'fields': [
                  {'name': 'id', 'type': 'INTEGER', 'badge': 'PK', 'key': true},
                  {'name': 'name', 'type': 'TEXT', 'badge': 'NOT NULL'},
                ],
              },
              {
                'id': 'transactions',
                'label': 'transactions',
                'kind': 'table',
                'fields': [
                  {'name': 'id', 'type': 'INTEGER', 'badge': 'PK', 'key': true},
                  {'name': 'account_id', 'type': 'INTEGER', 'badge': 'FK'},
                  {'name': 'amount', 'type': 'REAL', 'badge': 'NOT NULL'},
                ],
              },
            ],
            'edges': [
              {
                'from': 'transactions',
                'to': 'accounts',
                'label': 'account_id -> id',
              },
            ],
          },
          [],
          (_, _) {},
          buildChild: _noop,
        );

        await tester.pumpWidget(_wrap(SizedBox(width: 760, child: node)));
        expect(find.text('accounts'), findsOneWidget);
        expect(find.text('transactions'), findsOneWidget);
        expect(find.text('account_id'), findsOneWidget);
        expect(find.text('account_id -> id'), findsOneWidget);
      },
    );

    testWidgets('ui:text renders correctly in widget tree', (tester) async {
      final node = buildUiNode(
        'ui:text',
        {'text': 'Registry text'},
        [],
        (_, _) {},
        buildChild: _noop,
      );

      await tester.pumpWidget(_wrap(node));
      expect(find.text('Registry text'), findsOneWidget);
    });

    testWidgets(
      'UiSurfaceTreeRenderer routes ui:Screen+ui:Text through buildUiNode',
      (tester) async {
        final tree = <String, Object?>{
          'Type': 'ui:Screen',
          'Props': <String, Object?>{},
          'Children': [
            <String, Object?>{
              'Type': 'ui:Text',
              'Props': <String, Object?>{'text': 'Hello from ui:Text'},
              'Children': <Object?>[],
            },
          ],
        };

        final renderer = UiSurfaceTreeRenderer();
        final rfwHost = RfwRuntimeHost();
        final widget = renderer.build(tree, (_, _) {}, rfwHost: rfwHost);

        await tester.pumpWidget(_wrap(widget));
        expect(find.text('Hello from ui:Text'), findsOneWidget);
      },
    );

    testWidgets('ui:button fires event with correct payload', (tester) async {
      Map<String, Object?>? capturedArgs;

      final node = buildUiNode(
        'ui:button',
        {
          'label': 'Press me',
          'pack': 'demo',
          'experienceId': 'tour',
          'eventName': 'clicked',
        },
        [],
        (name, args) => capturedArgs = args,
        buildChild: _noop,
      );

      await tester.pumpWidget(_wrap(node));
      await tester.tap(find.text('Press me'));
      await tester.pumpAndSettle();

      expect(capturedArgs?['synapseType'], 'ExperienceStep');
      final props = capturedArgs?['props'] as Map<String, Object?>;
      expect(props['pack'], 'demo');
      expect(props['eventName'], 'clicked');
    });
  });
}
