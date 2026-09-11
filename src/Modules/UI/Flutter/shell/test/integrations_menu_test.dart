import 'package:digitalbrain_flutter_shell/integrations/integrations_menu.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const providers = ['gmail', 'salesforce', 'github'];

  testWidgets('each integration opens its login URL through the callback', (
    tester,
  ) async {
    final opened = <Uri>[];
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: IntegrationsMenu(
            kernelBaseUri: Uri.parse('https://brain.example'),
            onOpen: (uri) async {
              opened.add(uri);
            },
          ),
        ),
      ),
    );
    expect(find.byType(ListTile), findsNWidgets(3));
    for (final id in providers) {
      await tester.tap(find.byKey(Key('integration_$id')));
      await tester.pump();
      expect(
        opened.last,
        Uri.parse('https://brain.example/integrations/$id/login'),
      );
    }
    expect(opened, hasLength(3));
  });

  testWidgets('all integrations are disabled without a kernel base URI', (
    tester,
  ) async {
    final opened = <Uri>[];
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: IntegrationsMenu(
            kernelBaseUri: null,
            onOpen: (uri) async {
              opened.add(uri);
            },
          ),
        ),
      ),
    );
    expect(find.byType(ListTile), findsNWidgets(3));
    for (final id in providers) {
      final tile = find.byKey(Key('integration_$id'));
      expect(tester.widget<ListTile>(tile).onTap, isNull);
      await tester.tap(tile);
    }
    expect(opened, isEmpty);
  });
}
