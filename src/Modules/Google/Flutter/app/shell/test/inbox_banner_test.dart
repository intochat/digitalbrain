import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/inbox_banner.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

void main() {
  testWidgets('live banner sends button and expander gestures to the backend', (
    tester,
  ) async {
    var expanded = false;
    var label = 'Fire';
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://localhost:8080'),
      httpClient: MockClient((request) async {
        final path = request.url.path;
        if (request.method == 'POST') {
          if (path.endsWith('/toggle')) expanded = !expanded;
          if (path.endsWith('/click')) label = 'Fired';
        }
        final Object value = path == '/ui/inbox'
            ? <String>[]
            : path.contains('/expanders/')
            ? {'name': 'e2e', 'header': 'More', 'expanded': expanded}
            : path.contains('/buttons/')
            ? {'name': 'e2e', 'label': label, 'action': 'fire'}
            : <String, dynamic>{};
        return http.Response(jsonEncode(value), 200);
      }),
    );
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(body: InboxBanner(client: client)),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Collapsed'), findsOneWidget);
    await tester.tap(find.text('Collapsed'));
    await tester.tap(find.text('Fire'));
    await tester.pump(const Duration(seconds: 2));
    await tester.pumpAndSettle();
    expect(find.text('Expanded'), findsOneWidget);
    expect(find.text('Fired'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
    client.close();
  });
}
