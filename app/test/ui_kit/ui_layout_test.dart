import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_registry.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_divider.dart';
import 'package:digitalbrain_flutter/ui_kit/ui_header.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';

Widget _host(Widget child) => MaterialApp(
      home: FTheme(data: FThemes.neutral.light.touch, child: FScaffold(child: child)),
    );

void main() {
  test('registry maps layout nodes', () {
    // ignore: avoid_types_on_closure_parameters
    expect(buildUiNode('ui:divider', const {}, const [], (name, args) {}, buildChild: (_) => const SizedBox()),
        isA<UiKitDivider>());
    // ignore: avoid_types_on_closure_parameters
    expect(buildUiNode('ui:header', {'title': 'S'}, const [], (name, args) {}, buildChild: (_) => const SizedBox()),
        isA<UiKitHeader>());
  });

  testWidgets('Header shows its title', (tester) async {
    await tester.pumpWidget(_host(const UiKitHeader(title: 'Section')));
    expect(find.text('Section'), findsOneWidget);
  });

  testWidgets('Row lays children horizontally', (tester) async {
    final node = {
      'Type': 'ui:Row',
      'Props': const <String, Object?>{},
      'Children': [
        {'Type': 'ui:Text', 'Props': {'text': 'a'}, 'Children': const []},
        {'Type': 'ui:Text', 'Props': {'text': 'b'}, 'Children': const []},
      ],
    };
    await tester.pumpWidget(_host(UiSurfaceTreeRenderer().build(node, (name, args) {}, rfwHost: RfwRuntimeHost())));
    expect(find.text('a'), findsOneWidget);
    expect(find.text('b'), findsOneWidget);
  });
}
