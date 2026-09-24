import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/app_studio.dart';

void main() {
  Future<void> open(WidgetTester tester, AppStudioRequest request) async {
    tester.view.physicalSize = const Size(1200, 1200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: AppStudio(
          workspaceId: 'workspace-one',
          request: request,
          onClose: () {},
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets(
    'creates a connected composition and displays server run output',
    (tester) async {
      final installed = <Map<String, dynamic>>[];
      Map? creation;
      String? operationId;
      await open(tester, (method, path, [body]) async {
        if (method == 'GET') return installed;
        if (path.endsWith('/app-runtime')) {
          creation = body as Map;
          final manifest = Map<String, dynamic>.from(
            creation!['manifest'] as Map,
          );
          manifest['id'] = 'alice/greeting';
          final snapshot = {
            'manifest': manifest,
            'configuration': {'prefix': 'Hello '},
            'output': '',
            'status': 0,
          };
          installed.add(snapshot);
          return snapshot;
        }
        if (path.endsWith('/configure')) {
          expect((body as Map)['configuration'], {'prefix': 'Welcome '});
          installed.first['configuration'] = body['configuration'];
          return installed.first;
        }
        if (path.endsWith('/run')) {
          operationId = (body as Map)['operationId'] as String;
          expect(body['value'], 'Sam');
          return {...installed.first, 'output': 'WELCOME SAM', 'status': 1};
        }
        if (path.endsWith('/publish')) {
          return {'appId': 'alice/greeting', 'version': '1.0.0'};
        }
        throw StateError('Unexpected request $method $path');
      });
      await tester.tap(find.text('Create'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('app-slug')), 'greeting');
      await tester.enterText(
        find.byKey(const Key('app-name')),
        'Friendly greeting',
      );
      await tester.enterText(
        find.byKey(const Key('app-description')),
        'Greet someone',
      );
      await tester.enterText(find.byKey(const Key('create-app-prefix')), 'Hi ');
      await tester.tap(find.byKey(const Key('create-app')));
      await tester.pumpAndSettle();
      expect(creation?['name'], 'greeting');
      expect(
        ((creation?['manifest'] as Map)['composition'] as Map)['defaults'],
        {'prefix': 'Hi '},
      );
      expect(
        ((creation?['manifest'] as Map)['composition'] as Map)['parts'],
        hasLength(2),
      );
      expect(find.text('Friendly greeting'), findsWidgets);
      await tester.enterText(find.byKey(const Key('app-prefix')), 'Welcome ');
      await tester.tap(find.byKey(const Key('save-app-configuration')));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('app-input')), 'Sam');
      await tester.tap(find.byKey(const Key('run-app')));
      await tester.pumpAndSettle();
      expect(find.text('WELCOME SAM'), findsOneWidget);
      expect(operationId, matches(RegExp(r'^[0-9a-f-]{36}$')));
      await tester.tap(find.byKey(const Key('publish-app')));
      await tester.pumpAndSettle();
      expect(find.text('Published alice/greeting · 1.0.0'), findsOneWidget);
    },
  );

  testWidgets('installs the selected exact marketplace version', (
    tester,
  ) async {
    Map? installation;
    await open(tester, (method, path, [body]) async {
      if (path == '/marketplace/apps') {
        return [
          {
            'appId': 'alice/greeting',
            'version': '1.2.0',
            'manifest': {
              'name': 'Greeting',
              'descriptionForPeople': 'Friendly words',
            },
            'state': 1,
          },
        ];
      }
      if (path.endsWith('/install')) {
        installation = body as Map;
        return {
          'manifest': {
            'id': 'alice/greeting',
            'name': 'Greeting',
            'version': '1.2.0',
          },
          'configuration': {},
          'output': '',
        };
      }
      return [];
    });
    await tester.tap(find.text('Marketplace'));
    await tester.pumpAndSettle();
    expect(find.text('Friendly words'), findsOneWidget);
    await tester.tap(find.text('Install'));
    await tester.pumpAndSettle();
    expect(installation, {
      'publisher': 'alice',
      'name': 'greeting',
      'version': '1.2.0',
      'configuration': {},
    });
    expect(find.byKey(const Key('run-app')), findsOneWidget);
  });

  testWidgets('shows request failures and allows refreshing', (tester) async {
    var failing = true;
    await open(tester, (method, path, [body]) async {
      if (failing) throw StateError('Service unavailable');
      return [];
    });
    expect(find.textContaining('Service unavailable'), findsOneWidget);
    failing = false;
    await tester.tap(find.byTooltip('Refresh apps'));
    await tester.pumpAndSettle();
    expect(find.text('No apps installed yet.'), findsOneWidget);
  });
}
