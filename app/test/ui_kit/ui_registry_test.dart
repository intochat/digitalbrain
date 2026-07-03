import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';

import 'package:digitalbrain_flutter/ui_kit/ui_registry.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_text.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_button.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_panel.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_screen.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_table.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_text_field.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';

Widget _wrap(Widget child) => MaterialApp(
  builder: (_, w) => FTheme(data: FThemes.neutral.light.touch, child: w!),
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
