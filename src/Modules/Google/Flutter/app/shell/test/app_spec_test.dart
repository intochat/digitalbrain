import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/app_spec_view.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/create_app_screen.dart';

const _spec = '''
# Shouter

Shouts back whatever you say.

## Scenario: It shouts

Asking "hello" answers "HELLO!".
''';

void main() {
  testWidgets('shows scenario verdicts next to their headings', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: SingleChildScrollView(
            child: AppSpecView(
              spec: _spec,
              run: {
                'scenarios': [
                  {
                    'name': 'It shouts',
                    'passed': false,
                    'message': 'The answer was "hi".',
                  },
                ],
                'exitCode': 1,
              },
            ),
          ),
        ),
      ),
    );

    expect(find.text('Scenario: It shouts'), findsOneWidget);
    expect(
      find.byKey(const ValueKey('scenario-message-It shouts')),
      findsOneWidget,
    );
    expect(find.text('The answer was "hi".'), findsOneWidget);
    expect(find.text('Shouts back whatever you say.'), findsOneWidget);
  });

  testWidgets('drafts, revises and builds an app until it is published', (
    tester,
  ) async {
    final calls = <String>[];
    var status = 1;
    Future<dynamic> request(String method, String path, [Object? body]) async {
      calls.add('$method ${path.replaceAll(RegExp('[0-9a-f]{32}'), 'ID')}');
      if (path.endsWith('/build')) status = 3;
      return {
        'draft': {
          'title': 'Shouter',
          'runtime': 'prompt',
          'description': 'Shouts back.',
          'spec': _spec,
          'status': status,
          'attempts': status == 3
              ? [
                  {
                    'green': false,
                    'failures': 'Scenario \'It shouts\': The answer was "hi".',
                  },
                  {'green': true, 'failures': ''},
                ]
              : <Object>[],
          'published': status == 3
              ? {
                  'package': {'owner': 'alice', 'name': 'shouter'},
                  'revision': 'r2',
                }
              : null,
        },
        'verification': status == 3
            ? {
                'green': true,
                'run': {
                  'scenarios': [
                    {'name': 'It shouts', 'passed': true, 'message': ''},
                  ],
                  'exitCode': 0,
                },
              }
            : null,
      };
    }

    await tester.pumpWidget(
      MaterialApp(home: CreateAppScreen(request: request)),
    );
    await tester.enterText(
      find.byKey(const ValueKey('describe-app')),
      'Shout back whatever I say',
    );
    await tester.tap(find.byKey(const ValueKey('draft-app')));
    await tester.pumpAndSettle();

    expect(find.text('Shouter · prompt app'), findsOneWidget);
    expect(find.text('Scenario: It shouts'), findsOneWidget);

    await tester.enterText(
      find.byKey(const ValueKey('revise-app')),
      'Check the exact answer',
    );
    await tester.ensureVisible(find.byKey(const ValueKey('revise-button')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('revise-button')));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.byKey(const ValueKey('build-app')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('build-app')));
    await tester.pumpAndSettle();

    expect(calls, [
      'POST /packages/drafts/ID',
      'POST /packages/drafts/ID/revise',
      'POST /packages/drafts/ID/build',
    ]);
    expect(find.byKey(const ValueKey('attempt-1')), findsOneWidget);
    expect(
      find.text('Scenario \'It shouts\': The answer was "hi".'),
      findsOneWidget,
    );
    await tester.dragUntilVisible(
      find.byKey(const ValueKey('published-app')),
      find.byType(ListView),
      const Offset(0, -200),
    );
    expect(
      find.text('Published as alice/shouter. Install it from Apps.'),
      findsOneWidget,
    );
  });
}
