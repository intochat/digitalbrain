import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/app_spec_view.dart';
import 'package:digitalbrain_flutter_shell/workspace/apps/create_app_screen.dart';

Map<String, dynamic> _feature({bool bound = true}) => {
  'name': 'Shouter',
  'fullyBound': bound,
  'background': <Object>[],
  'scenarios': [
    {
      'name': 'It shouts',
      'line': 2,
      'tags': ['@live'],
      'steps': [
        {
          'line': 3,
          'keyword': 'When',
          'text': 'I ask "hello"',
          'pattern': 'I ask {string}',
          'parameters': [
            {'start': 6, 'length': 7, 'kind': 'string'},
          ],
        },
        {
          'line': 4,
          'keyword': 'Then',
          'text': bound ? 'the answer is "HELLO!"' : 'the reply is very loud',
          'pattern': bound ? 'the answer is {string}' : null,
          'parameters': <Object>[],
        },
      ],
    },
  ],
};

void main() {
  testWidgets('shows scenarios with highlighted parameters and failing steps', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: AppSpecView(
            feature: _feature(),
            run: {
              'scenarios': [
                {
                  'line': 2,
                  'verdict': 1,
                  'steps': [
                    {'line': 3, 'verdict': 0},
                    {'line': 4, 'verdict': 1, 'message': 'The answer was "hi".'},
                  ],
                },
              ],
            },
          ),
        ),
      ),
    );

    expect(find.text('Scenario: It shouts'), findsOneWidget);
    expect(find.text('@live'), findsOneWidget);
    final step = tester.widget<Text>(find.byKey(const ValueKey('step-3')));
    final spans = (step.textSpan! as TextSpan).children!.cast<TextSpan>();
    expect(spans.first.text, 'When ');
    final highlighted = spans.singleWhere((span) => span.text == '"hello"');
    expect(highlighted.style?.backgroundColor, isNotNull);
    expect(find.byKey(const ValueKey('step-message-4')), findsOneWidget);
    expect(find.text('The answer was "hi".'), findsOneWidget);
  });

  testWidgets('drafts, revises and builds an app until it is published', (
    tester,
  ) async {
    final calls = <String>[];
    var status = 1;
    var bound = false;
    Future<dynamic> request(String method, String path, [Object? body]) async {
      calls.add('$method ${path.replaceAll(RegExp('[0-9a-f]{32}'), 'ID')}');
      if (path.endsWith('/revise')) bound = true;
      if (path.endsWith('/build')) status = 3;
      return {
        'draft': {
          'title': 'Shouter',
          'runtime': 'prompt',
          'description': 'Shouts back.',
          'spec': 'Feature: Shouter',
          'status': status,
          'attempts': status == 3
              ? [
                  {'green': false, 'failures': 'line 4: The answer was "hi".'},
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
        'feature': _feature(bound: bound),
      };
    }

    await tester.pumpWidget(MaterialApp(home: CreateAppScreen(request: request)));
    await tester.enterText(
      find.byKey(const ValueKey('describe-app')),
      'Shout back whatever I say',
    );
    await tester.tap(find.byKey(const ValueKey('draft-app')));
    await tester.pumpAndSettle();

    expect(find.text('Shouter · prompt app'), findsOneWidget);
    final build = find.byKey(const ValueKey('build-app'));
    expect(tester.widget<FilledButton>(build).onPressed, isNull);

    await tester.enterText(
      find.byKey(const ValueKey('revise-app')),
      'Check the exact answer',
    );
    await tester.tap(find.byKey(const ValueKey('revise-button')));
    await tester.pumpAndSettle();
    await tester.tap(build);
    await tester.pumpAndSettle();

    expect(calls, [
      'POST /packages/drafts/ID',
      'POST /packages/drafts/ID/revise',
      'POST /packages/drafts/ID/build',
    ]);
    expect(find.byKey(const ValueKey('attempt-1')), findsOneWidget);
    expect(find.text('line 4: The answer was "hi".'), findsOneWidget);
    expect(
      find.text('Published as alice/shouter. Install it from Apps.'),
      findsOneWidget,
    );
  });
}
