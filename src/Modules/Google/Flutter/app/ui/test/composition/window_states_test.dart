import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Future<void> pumpWindow(
  WidgetTester tester,
  Map<String, Object?> Function() load, {
  Future<void> Function(Map<String, dynamic>)? onAction,
}) async {
  await tester.pumpWidget(
    MaterialApp(
      home: Scaffold(
        body: NeuronView(
          kind: 'surface',
          name: 'window',
          load: (_, _) async => load(),
          onAction: onAction,
        ),
      ),
    ),
  );
}

void main() {
  testWidgets('an empty window renders its declared state', (tester) async {
    await pumpWindow(tester, () => {'state': 'empty'});
    await tester.pumpAndSettle();
    expect(find.text('Nothing here yet'), findsWidgets);
    expect(find.text('This window has no content yet.'), findsOneWidget);
  });

  testWidgets('a denied read renders permission denied', (tester) async {
    await pumpWindow(tester, () => {'state': 'permissionDenied'});
    await tester.pumpAndSettle();
    expect(find.text('Not available to you'), findsOneWidget);
    expect(
      find.text('You do not have permission to view this window.'),
      findsOneWidget,
    );
  });

  testWidgets('an expired connection renders its reconnect state', (
    tester,
  ) async {
    await pumpWindow(tester, () => {'state': 'expiredConnection'});
    await tester.pumpAndSettle();
    expect(find.text('Connection expired'), findsOneWidget);
    expect(find.textContaining('Reconnect'), findsOneWidget);
  });

  testWidgets('a failed read renders a retry', (tester) async {
    await pumpWindow(tester, () => {'state': 'failed', 'message': 'Boom.'});
    await tester.pumpAndSettle();
    expect(find.text('Could not load'), findsOneWidget);
    expect(find.text('Boom.'), findsOneWidget);
    expect(find.text('Retry'), findsOneWidget);
  });

  testWidgets('a throwing loader renders the failed state', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NeuronView(
            kind: 'surface',
            name: 'window',
            load: (_, _) async => throw StateError('nope'),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Could not load'), findsOneWidget);
    expect(find.text('This component could not be loaded.'), findsOneWidget);
  });

  testWidgets('first-run renders the assistant and matching starter prompts', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: FirstRunView(
            state: FirstRunState.fromMetadata({
              'assistantId': 'intocaht',
              'assistantTitle': 'IntoChat',
              'prompts': [
                {
                  'id': 'assistant',
                  'label': 'Ask the assistant',
                  'prompt': 'Ask the assistant something.',
                  'source': 'assistant',
                },
                {
                  'id': 'salesforce',
                  'label': 'Review my Salesforce leads',
                  'prompt': 'Review my leads.',
                  'source': 'salesforce',
                },
              ],
            }),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('IntoChat'), findsOneWidget);
    expect(find.text('Ask the assistant'), findsOneWidget);
    expect(find.text('Review my Salesforce leads'), findsOneWidget);
    expect(find.text('salesforce'), findsOneWidget);
  });

  testWidgets('an unknown kind renders the declared fallback, never blank', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NeuronView(
            kind: 'not-a-kind',
            name: 'window',
            load: (_, _) async => {'definition': <String, Object?>{}},
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(
      find.text('${RendererRegistry.fallbackLabel}: not-a-kind'),
      findsOneWidget,
    );
    expect(find.text(RendererRegistry.fallbackExplanation), findsOneWidget);
  });
}