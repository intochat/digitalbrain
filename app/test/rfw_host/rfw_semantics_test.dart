import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';

void main() {
  testWidgets('rendered surface carries a stable semantics identifier', (tester) async {
    final handle = tester.ensureSemantics();
    final host = RfwRuntimeHost();
    const src = '''
      import digitalbrain;
      widget root = Text(text: "hello-pack");
    ''';
    host.ensureLoaded('e2e-doc', src);

    await tester.pumpWidget(MaterialApp(
      home: host.render(
        'e2e-doc',
        data: const <String, Object?>{},
        onEvent: (_, _) {},
        semanticsId: 'pack-surface-e2e',
        semanticsLabel: 'pack-surface-e2e',
      ),
    ));
    await tester.pumpAndSettle();

    expect(find.bySemanticsIdentifier('pack-surface-e2e'), findsOneWidget);
    handle.dispose();
  });
}
