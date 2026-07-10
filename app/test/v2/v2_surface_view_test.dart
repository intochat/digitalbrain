import 'package:digitalbrain_flutter/v2/v2_runtime.dart';
import 'package:digitalbrain_flutter/v2/widgets/v2_surface_view.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';

import 'v2_test_fixtures.dart';

void main() {
  testWidgets('renders a V2 widget-tree surface', (tester) async {
    final surface = testSurface(
      payload: {
        'kind': 'widgetTree',
        'tree': {
          'type': 'text',
          'props': {'text': 'Workspace surface ready'},
        },
        'data': <String, Object?>{},
      },
    );

    await tester.pumpWidget(
      _host(V2SurfaceView(surface: surface, onSubmitAction: _unexpectedAction)),
    );

    expect(find.text('Workspace surface ready'), findsOneWidget);
  });

  testWidgets('routes widget-tree event through the declared action token', (
    tester,
  ) async {
    final surface = testSurface(
      payload: {
        'kind': 'widgetTree',
        'tree': {
          'type': 'button',
          'props': {
            'label': 'Refresh surface',
            'actionBindingId': 'refresh-binding',
          },
        },
        'data': <String, Object?>{},
      },
      actions: [testActionJson()],
    );
    String? binding;
    Map<String, Object?>? input;

    await tester.pumpWidget(
      _host(
        V2SurfaceView(
          surface: surface,
          onSubmitAction: (surface, bindingId, actionInput) async {
            binding = bindingId;
            input = actionInput;
            return const V2ActionResult(
              operationId: 'operation-a',
              idempotencyKey: 'idempotency-a',
            );
          },
        ),
      ),
    );
    await tester.tap(find.text('Refresh surface'));
    await tester.pump(const Duration(milliseconds: 150));

    expect(binding, 'refresh-binding');
    expect(input, isEmpty);
  });

  testWidgets('renders the current V2 workspace projection shape', (
    tester,
  ) async {
    final surface = testSurface(
      payload: {
        'kind': 'widgetTree',
        'tree': {
          'Type': 'forui:fcard',
          'Props': {
            'title': 'DigitalBrain Runtime V2',
            'subtitle': 'Authenticated workspace surface',
          },
          'Children': [
            {
              'Type': 'text',
              'Props': {'text': 'Your private V2 workspace feed is connected.'},
            },
            {
              'Type': 'forui:fbutton',
              'Props': {
                'label': 'Refresh workspace',
                'actionBindingId': 'refresh-binding',
              },
            },
          ],
        },
        'data': {'kind': 'v2-workspace-home', 'status': 'ready', 'revision': 1},
      },
      actions: [testActionJson()],
    );
    String? binding;

    await tester.pumpWidget(
      _host(
        V2SurfaceView(
          surface: surface,
          onSubmitAction: (surface, bindingId, input) async {
            binding = bindingId;
            return const V2ActionResult(
              operationId: 'operation-a',
              idempotencyKey: 'idempotency-a',
            );
          },
        ),
      ),
    );

    expect(find.text('DigitalBrain Runtime V2'), findsOneWidget);
    expect(
      find.text('Your private V2 workspace feed is connected.'),
      findsOneWidget,
    );
    await tester.tap(find.text('Refresh workspace'));
    await tester.pump(const Duration(milliseconds: 150));
    expect(binding, 'refresh-binding');
  });

  testWidgets('renders a native V2 surface and submits its action', (
    tester,
  ) async {
    final surface = testSurface(actions: [testActionJson()]);
    var submitted = false;

    await tester.pumpWidget(
      _host(
        V2SurfaceView(
          surface: surface,
          onSubmitAction: (surface, binding, input) async {
            submitted = true;
            expect(binding, 'refresh-binding');
            return const V2ActionResult(
              operationId: 'operation-a',
              idempotencyKey: 'idempotency-a',
            );
          },
        ),
      ),
    );

    expect(find.text('V2 ready'), findsOneWidget);
    expect(find.text('Authenticated surface'), findsOneWidget);
    await tester.tap(find.text('Ui surface refresh'));
    await tester.pump();
    expect(submitted, isTrue);
  });

  testWidgets('renders a text RFW V2 surface through the fixed dictionary', (
    tester,
  ) async {
    final surface = testSurface(
      payload: {
        'kind': 'rfw',
        'rootWidget': 'root',
        'data': <String, Object?>{},
        'libraryText': '''
import digitalbrain;
widget root = Text(text: "RFW V2 ready");
''',
      },
    );

    await tester.pumpWidget(
      _host(V2SurfaceView(surface: surface, onSubmitAction: _unexpectedAction)),
    );
    await tester.pump();

    expect(find.text('RFW V2 ready'), findsOneWidget);
  });
}

Widget _host(Widget child) => MaterialApp(
  home: FTheme(
    data: FThemes.neutral.light.touch,
    child: Scaffold(body: SizedBox.expand(child: child)),
  ),
);

Future<V2ActionResult> _unexpectedAction(
  Object surface,
  String binding,
  Map<String, Object?> input,
) => throw StateError('Unexpected action.');
