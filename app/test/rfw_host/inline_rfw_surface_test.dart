import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';
import 'package:digitalbrain_flutter/rfw_host/inline_rfw_surface.dart';

void main() {
  testWidgets('renders inline RFW source with the supplied semantics id', (tester) async {
    final handle = tester.ensureSemantics();
    final host = RfwRuntimeHost();

    final widget = buildInlineRfwSurface(
      host: host,
      data: const <String, Object?>{
        'source': 'import digitalbrain;\nwidget root = Text(text: "hop-body");',
      },
      fallbackKey: 'fallback',
      defaultRootWidget: 'root',
      onEvent: (_, _) {},
      correlationId: 'travel-hotels',
      semanticsId: 'travel-hotels',
    );

    expect(widget, isNotNull);
    await tester.pumpWidget(MaterialApp(home: Scaffold(body: widget!)));
    await tester.pumpAndSettle();

    expect(find.bySemanticsIdentifier('travel-hotels'), findsOneWidget);
    expect(find.text('hop-body'), findsOneWidget);
    handle.dispose();
  });

  testWidgets('returns null when there is no inline source', (tester) async {
    final host = RfwRuntimeHost();
    final widget = buildInlineRfwSurface(
      host: host,
      data: const <String, Object?>{},
      fallbackKey: 'fallback',
      defaultRootWidget: 'root',
      onEvent: (_, _) {},
    );
    expect(widget, isNull);
  });
}
