import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/built_in_app_view.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

void main() {
  testWidgets(
    'renders server neurons and refreshes preferences after an action',
    (tester) async {
      var revision = 0;
      Map<String, dynamic>? preferences;
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://test'),
        httpClient: MockClient((request) async {
          Object response;
          if (request.url.path.endsWith('/built-in/settings/open')) {
            expect(request.method, 'POST');
            response = {
              'revision': revision,
              'surface': {'kind': 'surface', 'name': 'settings-root'},
              'preferences': {'theme': revision == 0 ? 'light' : 'dark'},
            };
          } else if (request.url.path.endsWith('/apps/event')) {
            expect(jsonDecode(request.body), {
              'kind': 'button',
              'name': 'theme-toggle',
              'action': 'toggle',
            });
            revision++;
            response = {};
          } else if (request.url.queryParameters['kind'] == 'surface') {
            response = {
              'definition': {
                'children': [
                  {'kind': 'button', 'name': 'theme-toggle'},
                ],
              },
            };
          } else {
            response = {
              'definition': {
                'label': revision == 0
                    ? 'Use dark theme'
                    : 'Dark theme enabled',
                'action': 'toggle',
              },
            };
          }
          return http.Response(jsonEncode(response), 200);
        }),
      );
      await tester.pumpWidget(
        MaterialApp(
          home: BuiltInAppView(
            client: client,
            workspaceId: 'one',
            appId: 'settings',
            onClose: () {},
            onPreferences: (value) => preferences = value,
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Use dark theme'), findsOneWidget);
      await tester.tap(find.text('Use dark theme'));
      await tester.pumpAndSettle();
      expect(find.text('Dark theme enabled'), findsOneWidget);
      expect(preferences, {'theme': 'dark'});
    },
  );

  testWidgets('failed opening can be retried', (tester) async {
    var failing = true;
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://test'),
      httpClient: MockClient((request) async {
        if (failing) return http.Response('unavailable', 503);
        return http.Response(
          jsonEncode(
            request.url.path.endsWith('/open')
                ? {
                    'surface': {'kind': 'text', 'name': 'welcome'},
                  }
                : {
                    'definition': {'markdown': 'Welcome back'},
                  },
          ),
          200,
        );
      }),
    );
    await tester.pumpWidget(
      MaterialApp(
        home: BuiltInAppView(
          client: client,
          workspaceId: 'one',
          appId: 'assistant',
          onClose: () {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.textContaining('503'), findsOneWidget);
    failing = false;
    await tester.tap(find.text('Retry'));
    await tester.pumpAndSettle();
    expect(find.text('Welcome back'), findsOneWidget);
  });
}
