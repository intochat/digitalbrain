import 'dart:async';

import 'package:digitalbrain_flutter_shell/integrations/connections_settings.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('changing account ignores an earlier delayed response', (
    tester,
  ) async {
    final delayed = Completer<Object?>();
    Future<Object?> oldRequest(String path, {Map<String, Object?>? body}) =>
        delayed.future;
    Future<Object?> newRequest(
      String path, {
      Map<String, Object?>? body,
    }) async => [
      {'integrationId': 'gmail', 'id': 'new-account', 'status': 'Configured'},
    ];
    Widget surface(
      Future<Object?> Function(String, {Map<String, Object?>? body}) request,
    ) => MaterialApp(
      home: Scaffold(body: ConnectionsSettings(request: request)),
    );
    await tester.pumpWidget(surface(oldRequest));
    await tester.pumpWidget(surface(newRequest));
    await tester.pumpAndSettle();
    delayed.complete([
      {'integrationId': 'gmail', 'id': 'old-account', 'status': 'Configured'},
    ]);
    await tester.pumpAndSettle();
    expect(find.textContaining('new-account'), findsOneWidget);
    expect(find.textContaining('old-account'), findsNothing);
  });
  test('legacy Connected is only Configured', () {
    expect(connectionStatus('Connected'), 'Configured');
    expect(connectionStatus('Expired'), 'Expired');
    expect(connectionStatus('Failing'), 'Unavailable');
  });
  testWidgets('narrow connections hide tokens and request authorization', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(390, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    String? action;
    Uri? opened;
    Future<Object?> connections(
      String path, {
      Map<String, Object?>? body,
    }) async => [
      {'id': 'mail', 'integrationId': 'gmail', 'status': 'Connected'},
    ];
    Future<Object?> services(String path, {Map<String, Object?>? body}) async {
      if (path.isEmpty) {
        return [
          {
            'id': 'gmail',
            'name': 'Google',
            'available': true,
            'capabilities': 'Gmail only',
          },
        ];
      }
      action = path;
      return {
        'url': 'https://example.test/integrations/gmail/login?request=one-use',
      };
    }

    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: SingleChildScrollView(
            child: ConnectionsSettings(
              request: connections,
              serviceRequest: services,
              onOpen: (url) async {
                opened = url;
              },
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Configured'), findsOneWidget);
    expect(find.byType(TextField), findsNothing);
    await tester.tap(find.text('Add connection'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Connect'));
    await tester.pumpAndSettle();
    expect(action, 'gmail/start');
    expect(opened?.queryParameters['request'], 'one-use');
    expect(find.text('Verified'), findsNothing);
    expect(tester.takeException(), isNull);
  });
}
