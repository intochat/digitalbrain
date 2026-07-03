import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_form_scope.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_list.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_tile.dart';

Widget _host(Widget child) => MaterialApp(
      home: FTheme(data: FThemes.neutral.light.touch, child: FScaffold(child: child)),
    );

void main() {
  testWidgets('Tile shows title and fires ExperienceStep on tap when goTo set', (tester) async {
    Map<String, Object?>? captured;
    await tester.pumpWidget(_host(UiKitTile(
      title: 'Go', subtitle: 'sub', pack: 'p', experienceId: 'p', eventName: 'next',
      onEvent: (n, a) => captured = a,
    )));
    expect(find.text('Go'), findsOneWidget);
    await tester.tap(find.text('Go'));
    await tester.pumpAndSettle();
    final propsMap = captured!['props'] as Map<String, Object?>;
    expect(propsMap['eventName'], 'next');
  });

  testWidgets('List tile fires ExperienceStep including form scope captured values', (tester) async {
    final controller = UiKitFormController()..set('name', 'Alice');
    Map<String, Object?>? captured;

    await tester.pumpWidget(_host(
      UiKitFormScope(
        controller: controller,
        child: UiKitList(
          tileDescriptors: const [
            {
              'props': {
                'title': 'Proceed',
                'subtitle': '',
                'pack': 'demo',
                'experienceId': 'exp1',
                'eventName': 'submit',
              },
            },
          ],
          onEvent: (name, args) => captured = args,
        ),
      ),
    ));

    expect(find.text('Proceed'), findsOneWidget);
    await tester.tap(find.text('Proceed'));
    await tester.pumpAndSettle();

    final propsMap = captured!['props'] as Map<String, Object?>;
    expect(propsMap['eventName'], 'submit');
    expect(propsMap['name'], 'Alice');
  });
}
