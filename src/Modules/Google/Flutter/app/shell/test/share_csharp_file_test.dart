import 'package:digitalbrain_flutter_shell/workspace/csharp/csharp_manager.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

final digest = {
  'description': {
    'id': 'daily_digest',
    'name': 'Daily digest',
    'purpose': 'Summarizes the day',
    'createdAt': '2026-09-27T10:00:00Z',
  },
  'file': {
    'id': 'daily_digest',
    'source': 'Console.WriteLine("digest");',
    'settings': <String, String>{},
    'status': 0,
    'exitCode': null,
    'startedAt': null,
  },
};

void main() {
  testWidgets('a C# file is shared as a package under a chosen name', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1100, 800);
    tester.view.devicePixelRatio = 1;
    final calls = <String, Map<String, Object?>?>{};
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: CSharpManager(
            request: (method, path, {body}) async {
              calls['$method $path'] = body;
              if (path.isEmpty) {
                return {
                  'items': [digest],
                  'canRun': true,
                };
              }
              if (path == 'daily_digest/share') {
                return {
                  'id': {'owner': 'alice', 'name': body!['name']},
                  'published': 'revision',
                };
              }
              return digest;
            },
            onAsk: (_, _) {},
            onSelected: (_) {},
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Daily digest').first);
    await tester.pumpAndSettle();

    await tester.tap(find.text('Share as package'));
    await tester.pumpAndSettle();
    expect(find.widgetWithText(TextField, 'daily-digest'), findsOneWidget);
    await tester.enterText(
      find.byKey(const ValueKey('share-package-name')),
      'digest',
    );
    await tester.tap(find.text('Share'));
    await tester.pumpAndSettle();

    expect(calls['POST daily_digest/share'], {'name': 'digest'});
    expect(find.textContaining('Shared as alice/digest'), findsOneWidget);
    await tester.pumpWidget(const SizedBox.shrink());
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
}
