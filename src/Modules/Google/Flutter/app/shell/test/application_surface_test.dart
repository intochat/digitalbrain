import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/application_surface.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

void main() {
  testWidgets('the application tree chooses its declared renderer', (
    tester,
  ) async {
    final starts = <String>[];
    var conversation = false;
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://test'),
      httpClient: MockClient((request) async {
        final workspace = request.url.pathSegments[1];
        final kind = request.url.queryParameters['kind'];
        final Object response;
        if (request.url.path.endsWith('/start')) {
          starts.add(workspace);
          response = {
            'surface': {'kind': 'surface', 'name': '$workspace/root'},
          };
        } else if (kind == 'surface') {
          response = {
            'definition': {
              'children': [
                {
                  'kind': conversation ? 'card' : 'text',
                  'name': '$workspace/content',
                },
              ],
            },
          };
        } else {
          response = conversation
              ? {'title': 'Research companion', 'body': ''}
              : {'markdown': 'The application selected text'};
        }
        return http.Response(jsonEncode(response), 200);
      }),
    );
    Widget view(String workspace) => MaterialApp(
      home: Scaffold(
        body: ApplicationSurface(
          client: client,
          workspace: workspace,
          application: 'assistant',
        ),
      ),
    );

    await tester.pumpWidget(view('one'));
    await tester.pumpAndSettle();
    expect(find.text('The application selected text'), findsOneWidget);
    expect(find.textContaining('Research companion'), findsNothing);

    conversation = true;
    await tester.pumpWidget(view('two'));
    await tester.pumpAndSettle();
    expect(find.text('Research companion'), findsOneWidget);
    expect(find.text('The application selected text'), findsNothing);
    expect(starts, ['one', 'two']);

    await tester.pumpWidget(view('two'));
    await tester.pumpAndSettle();
    expect(starts, ['one', 'two']);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('a failed application start can be retried', (tester) async {
    var attempts = 0;
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://test'),
      httpClient: MockClient((request) async {
        if (request.url.path.endsWith('/start')) {
          if (++attempts == 1) return http.Response('Unavailable', 503);
          return http.Response(
            jsonEncode({
              'surface': {'kind': 'text', 'name': 'ready'},
            }),
            200,
          );
        }
        return http.Response(jsonEncode({'markdown': 'Ready'}), 200);
      }),
    );
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: ApplicationSurface(
            client: client,
            workspace: 'one',
            application: 'assistant',
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Retry application'), findsOneWidget);
    await tester.tap(find.text('Retry application'));
    await tester.pumpAndSettle();
    expect(find.text('Ready'), findsOneWidget);
    expect(attempts, 2);
  });
}
