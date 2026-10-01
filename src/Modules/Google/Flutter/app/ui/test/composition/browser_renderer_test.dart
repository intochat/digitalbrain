import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('browser composition is explicit on unsupported platforms', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NeuronView(
            kind: 'webbrowser',
            name: 'workspace/app/browser',
            load: (_, _) async => {'title': 'Customer Researcher', 'uri': ''},
            onAction: (_) async =>
                fail('Unsupported browser must not send an attachment'),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byType(UiWebBrowser), findsOneWidget);
    expect(
      find.text('Live research requires the Windows desktop app.'),
      findsOneWidget,
    );
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
  });
}
