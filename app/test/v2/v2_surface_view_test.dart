import 'dart:async';

import 'package:digitalbrain_flutter/v2/protocol/surface_protocol.dart';
import 'package:digitalbrain_flutter/v2/v2_runtime.dart';
import 'package:digitalbrain_flutter/v2/widgets/v2_ino_composer.dart';
import 'package:digitalbrain_flutter/v2/widgets/v2_ino_conversation_view.dart';
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

  testWidgets('uses product copy when a rendered option is no longer usable', (
    tester,
  ) async {
    final surface = testSurface(
      payload: {
        'kind': 'widgetTree',
        'tree': {
          'type': 'button',
          'props': {'label': 'Continue', 'actionBindingId': 'missing-binding'},
        },
        'data': <String, Object?>{},
      },
    );

    await tester.pumpWidget(
      _host(V2SurfaceView(surface: surface, onSubmitAction: _unexpectedAction)),
    );
    await tester.tap(find.text('Continue'));
    await tester.pump(const Duration(milliseconds: 150));

    expect(find.text('That option is no longer available.'), findsOneWidget);
    expect(find.textContaining('surface'), findsNothing);
  });

  testWidgets('renders and submits the typed INO conversation optimistically', (
    tester,
  ) async {
    const prompt = 'What can you help me with in this workspace?';
    final receipt = Completer<V2ActionResult>();
    String? binding;
    Map<String, Object?>? submittedInput;

    await tester.pumpWidget(
      _host(
        V2SurfaceView(
          surface: _inoSurface(),
          onSubmitAction: (surface, bindingId, input) async {
            binding = bindingId;
            submittedInput = input;
            return receipt.future;
          },
        ),
      ),
    );

    expect(find.byKey(v2InoConversationKey), findsOneWidget);
    expect(find.byKey(v2InoComposerFieldKey), findsOneWidget);
    expect(find.text('Ask INO about this workspace.'), findsOneWidget);
    expect(find.byKey(v2InoEmptyTranscriptKey), findsOneWidget);

    await tester.enterText(find.byKey(v2InoComposerFieldKey), prompt);
    await tester.pump();
    await tester.tap(find.byKey(v2InoSendButtonKey));
    await tester.pump();

    expect(binding, 'ino.send');
    expect(submittedInput, {'prompt': prompt});
    expect(find.text(prompt), findsOneWidget);
    expect(find.text('Sending'), findsOneWidget);
    expect(
      tester.widget<FilledButton>(find.byKey(v2InoSendButtonKey)).onPressed,
      isNull,
    );

    receipt.complete(
      const V2ActionResult(
        operationId: 'operation-a',
        idempotencyKey: 'idempotency-a',
      ),
    );
    await tester.pump();
    await tester.pump();

    expect(find.text('Queued'), findsOneWidget);
    expect(
      tester
          .widget<TextField>(find.byKey(v2InoComposerFieldKey))
          .focusNode
          ?.hasFocus,
      isTrue,
    );
    expect(find.textContaining('operation-a'), findsNothing);
    expect(find.textContaining('idempotency-a'), findsNothing);

    await tester.enterText(
      find.byKey(v2InoComposerFieldKey),
      'A second message while acceptance is reconciling',
    );
    await tester.pump();
    expect(
      tester.widget<FilledButton>(find.byKey(v2InoSendButtonKey)).onPressed,
      isNull,
    );
  });

  testWidgets('enforces the production prompt bound before submission', (
    tester,
  ) async {
    Map<String, Object?>? submittedInput;
    await tester.pumpWidget(
      _host(
        V2SurfaceView(
          surface: _inoSurface(),
          onSubmitAction: (surface, bindingId, input) async {
            submittedInput = input;
            return const V2ActionResult(
              operationId: 'operation-a',
              idempotencyKey: 'idempotency-a',
            );
          },
        ),
      ),
    );

    await tester.enterText(
      find.byKey(v2InoComposerFieldKey),
      List.filled(v2InoMaximumPromptLength + 1, 'x').join(),
    );
    await tester.pump();
    final field = tester.widget<TextField>(find.byKey(v2InoComposerFieldKey));
    expect(field.controller?.text.length, v2InoMaximumPromptLength);
    await tester.tap(find.byKey(v2InoSendButtonKey));
    await tester.pump();
    expect(
      (submittedInput?['prompt'] as String?)?.length,
      v2InoMaximumPromptLength,
    );
  });

  testWidgets(
    'preserves draft and focus through queued running responding and success',
    (tester) async {
      const prompt = 'What can you help me with in this workspace?';
      Future<V2ActionResult> submit(
        Object surface,
        String binding,
        Map<String, Object?> input,
      ) async => const V2ActionResult(
        operationId: 'operation-a',
        idempotencyKey: 'idempotency-a',
      );

      await tester.pumpWidget(
        _host(V2SurfaceView(surface: _inoSurface(), onSubmitAction: submit)),
      );
      await tester.enterText(find.byKey(v2InoComposerFieldKey), prompt);
      await tester.pump();
      await tester.tap(find.byKey(v2InoSendButtonKey));
      await tester.pump();
      await tester.pump();
      await tester.enterText(
        find.byKey(v2InoComposerFieldKey),
        'Keep this next draft',
      );

      await _pumpInoRevision(
        tester,
        surface: _inoSurface(
          sequence: 2,
          revision: 2,
          messages: [inoMessage(role: 'user', text: prompt, state: 'queued')],
          operation: inoOperation(state: 'queued'),
        ),
        submit: submit,
      );
      expect(find.text(prompt), findsOneWidget);
      expect(find.text('Your message is queued.'), findsOneWidget);
      expect(
        tester.widget<FilledButton>(find.byKey(v2InoSendButtonKey)).onPressed,
        isNull,
      );

      await _pumpInoRevision(
        tester,
        surface: _inoSurface(
          sequence: 3,
          revision: 3,
          messages: [inoMessage(role: 'user', text: prompt, state: 'running')],
          operation: inoOperation(state: 'running'),
        ),
        submit: submit,
      );
      expect(find.text('INO is working on it.'), findsOneWidget);
      expect(
        tester.widget<FilledButton>(find.byKey(v2InoSendButtonKey)).onPressed,
        isNull,
      );

      await _pumpInoRevision(
        tester,
        surface: _inoSurface(
          sequence: 4,
          revision: 4,
          messages: [
            inoMessage(role: 'user', text: prompt, state: 'responding'),
          ],
          operation: inoOperation(state: 'responding'),
        ),
        submit: submit,
      );
      expect(find.text('INO is writing a response.'), findsOneWidget);
      expect(
        tester.widget<FilledButton>(find.byKey(v2InoSendButtonKey)).onPressed,
        isNull,
      );

      await _pumpInoRevision(
        tester,
        surface: _inoSurface(
          sequence: 5,
          revision: 5,
          messages: [
            inoMessage(role: 'user', text: prompt, state: 'succeeded'),
            inoMessage(
              role: 'assistant',
              text: 'I can help you understand and work with this workspace.',
              state: 'succeeded',
            ),
          ],
          operation: inoOperation(state: 'succeeded'),
        ),
        submit: submit,
      );

      final field = tester.widget<TextField>(find.byKey(v2InoComposerFieldKey));
      expect(field.controller?.text, 'Keep this next draft');
      expect(field.focusNode?.hasFocus, isTrue);
      expect(find.text(prompt), findsOneWidget);
      expect(
        find.text('I can help you understand and work with this workspace.'),
        findsOneWidget,
      );
      expect(find.text('Response ready.'), findsOneWidget);
      expect(
        tester.widget<FilledButton>(find.byKey(v2InoSendButtonKey)).onPressed,
        isNotNull,
      );
    },
  );

  testWidgets('feed confirmation before the receipt does not lock chat', (
    tester,
  ) async {
    const prompt = 'What can you help me with in this workspace?';
    final receipt = Completer<V2ActionResult>();
    Future<V2ActionResult> submit(
      Object surface,
      String binding,
      Map<String, Object?> input,
    ) => receipt.future;

    await tester.pumpWidget(
      _host(V2SurfaceView(surface: _inoSurface(), onSubmitAction: submit)),
    );
    await tester.enterText(find.byKey(v2InoComposerFieldKey), prompt);
    await tester.pump();
    await tester.tap(find.byKey(v2InoSendButtonKey));
    await tester.pump();
    await tester.pump();
    await _pumpInoRevision(
      tester,
      surface: _inoSurface(
        sequence: 2,
        revision: 2,
        messages: [
          inoMessage(
            turnKey: 'turn-user-first',
            role: 'user',
            text: prompt,
            state: 'queued',
          ),
        ],
        operation: inoOperation(state: 'queued'),
      ),
      submit: submit,
    );
    receipt.complete(
      const V2ActionResult(
        operationId: 'operation-a',
        idempotencyKey: 'idempotency-a',
      ),
    );
    await tester.pump();
    await _pumpInoRevision(
      tester,
      surface: _inoSurface(
        sequence: 3,
        revision: 3,
        messages: [
          inoMessage(
            turnKey: 'turn-user-first',
            role: 'user',
            text: prompt,
            state: 'succeeded',
          ),
          inoMessage(
            turnKey: 'turn-assistant-first',
            role: 'assistant',
            text: 'A model-produced answer.',
            state: 'succeeded',
          ),
        ],
        operation: inoOperation(state: 'succeeded'),
      ),
      submit: submit,
    );
    await tester.enterText(find.byKey(v2InoComposerFieldKey), 'Next question');
    await tester.pump();

    expect(find.byKey(v2InoSubmissionNoticeKey), findsNothing);
    expect(
      tester.widget<FilledButton>(find.byKey(v2InoSendButtonKey)).onPressed,
      isNotNull,
    );
  });

  testWidgets('definite preflight rejection restores the draft', (
    tester,
  ) async {
    const prompt = 'Keep this message';
    await tester.pumpWidget(
      _host(
        V2SurfaceView(
          surface: _inoSurface(),
          onSubmitAction: (surface, binding, input) async =>
              throw StateError('stale action'),
        ),
      ),
    );
    await tester.enterText(find.byKey(v2InoComposerFieldKey), prompt);
    await tester.pump();
    await tester.tap(find.byKey(v2InoSendButtonKey));
    await tester.pump();
    await tester.pump();

    final field = tester.widget<TextField>(find.byKey(v2InoComposerFieldKey));
    expect(field.controller?.text, prompt);
    expect(find.text('Checking delivery'), findsNothing);
    expect(
      find.text('That message wasn\'t sent. Please try again.'),
      findsOneWidget,
    );
    expect(
      tester.widget<FilledButton>(find.byKey(v2InoSendButtonKey)).onPressed,
      isNotNull,
    );
  });

  testWidgets('keeps the draft and disables Send while reconnecting', (
    tester,
  ) async {
    Future<V2ActionResult> submit(
      Object surface,
      String binding,
      Map<String, Object?> input,
    ) async => const V2ActionResult(
      operationId: 'operation-a',
      idempotencyKey: 'idempotency-a',
    );
    final surface = _inoSurface();

    await tester.pumpWidget(
      _host(V2SurfaceView(surface: surface, onSubmitAction: submit)),
    );
    await tester.enterText(
      find.byKey(v2InoComposerFieldKey),
      'Keep this draft offline',
    );
    await tester.pumpWidget(
      _host(
        V2SurfaceView(
          surface: surface,
          onSubmitAction: submit,
          actionEnabled: false,
          reconnecting: true,
        ),
      ),
    );
    await tester.pump();

    expect(find.byKey(v2InoReconnectBannerKey), findsOneWidget);
    expect(
      tester
          .widget<TextField>(find.byKey(v2InoComposerFieldKey))
          .controller
          ?.text,
      'Keep this draft offline',
    );
    expect(
      tester.widget<FilledButton>(find.byKey(v2InoSendButtonKey)).onPressed,
      isNull,
    );

    await tester.pumpWidget(
      _host(V2SurfaceView(surface: surface, onSubmitAction: submit)),
    );
    await tester.pump();
    expect(find.byKey(v2InoReconnectBannerKey), findsNothing);
    expect(
      tester
          .widget<TextField>(find.byKey(v2InoComposerFieldKey))
          .controller
          ?.text,
      'Keep this draft offline',
    );
  });

  testWidgets('shows Retry only for an explicitly retryable failure', (
    tester,
  ) async {
    var submissions = 0;
    Future<V2ActionResult> submit(
      Object surface,
      String binding,
      Map<String, Object?> input,
    ) async {
      submissions++;
      expect(input, {'prompt': 'Please try this'});
      return const V2ActionResult(
        operationId: 'operation-a',
        idempotencyKey: 'idempotency-a',
      );
    }

    await tester.pumpWidget(
      _host(
        V2SurfaceView(
          surface: _inoSurface(
            messages: [
              inoMessage(
                role: 'user',
                text: 'Please try this',
                state: 'failed',
              ),
            ],
            operation: inoOperation(
              state: 'failed',
              safeReason: 'INO was temporarily unavailable.',
            ),
          ),
          onSubmitAction: submit,
        ),
      ),
    );
    expect(find.text('INO was temporarily unavailable.'), findsOneWidget);
    expect(find.byKey(v2InoRetryButtonKey), findsNothing);

    await tester.pumpWidget(
      _host(
        V2SurfaceView(
          surface: _inoSurface(
            sequence: 2,
            revision: 2,
            messages: [
              inoMessage(
                role: 'user',
                text: 'Please try this',
                state: 'failed',
              ),
            ],
            operation: inoOperation(
              state: 'failed',
              retryable: true,
              safeReason: 'INO was temporarily unavailable.',
            ),
          ),
          onSubmitAction: submit,
        ),
      ),
    );
    await tester.tap(find.byKey(v2InoRetryButtonKey));
    await tester.pump();
    await tester.pump();
    expect(submissions, 1);
    expect(find.text('Please try this'), findsOneWidget);

    await tester.pumpWidget(
      _host(
        V2SurfaceView(
          surface: _inoSurface(
            sequence: 3,
            revision: 3,
            messages: [
              inoMessage(
                turnKey: 'turn-user-original',
                role: 'user',
                text: 'Please try this',
                state: 'failed',
              ),
              inoMessage(
                turnKey: 'turn-user-retry',
                role: 'user',
                text: 'Please try this',
                state: 'succeeded',
              ),
              inoMessage(
                turnKey: 'turn-assistant-retry',
                role: 'assistant',
                text: 'The retry completed.',
                state: 'succeeded',
              ),
            ],
            operation: inoOperation(state: 'succeeded'),
          ),
          onSubmitAction: submit,
        ),
      ),
    );
    await tester.enterText(find.byKey(v2InoComposerFieldKey), 'Another prompt');
    await tester.pump();
    expect(
      tester.widget<FilledButton>(find.byKey(v2InoSendButtonKey)).onPressed,
      isNotNull,
    );
  });

  testWidgets('terminal state is announced as a live status', (tester) async {
    await tester.pumpWidget(
      _host(
        V2SurfaceView(
          surface: _inoSurface(operation: inoOperation(state: 'succeeded')),
          onSubmitAction: _unexpectedAction,
        ),
      ),
    );

    final semantics = tester.getSemantics(find.byKey(v2InoOperationStatusKey));
    expect(semantics.flagsCollection.isLiveRegion, isTrue);
  });

  testWidgets(
    'follows a front-pruned transcript only while the reader is at the bottom',
    (tester) async {
      List<Map<String, Object?>> transcript(int start) => List.generate(18, (
        offset,
      ) {
        final number = start + offset;
        return inoMessage(
          turnKey: 'turn-${number.toString().padLeft(3, '0')}',
          role: number.isEven ? 'user' : 'assistant',
          text:
              'Turn $number ${List.filled(12, 'with readable detail').join(' ')}',
          state: 'succeeded',
        );
      });

      Future<V2ActionResult> submit(
        Object surface,
        String binding,
        Map<String, Object?> input,
      ) async => const V2ActionResult(
        operationId: 'operation-a',
        idempotencyKey: 'idempotency-a',
      );

      await tester.pumpWidget(
        _host(
          V2SurfaceView(
            surface: _inoSurface(
              messages: transcript(0),
              operation: inoOperation(state: 'succeeded'),
            ),
            onSubmitAction: submit,
          ),
        ),
      );
      await tester.pumpAndSettle();
      final transcriptController = tester
          .widget<ListView>(find.byKey(v2InoTranscriptKey))
          .controller!;
      expect(transcriptController.position.maxScrollExtent, greaterThan(80));
      expect(
        transcriptController.position.pixels,
        moreOrLessEquals(
          transcriptController.position.maxScrollExtent,
          epsilon: 1,
        ),
      );
      expect(
        find.byKey(const ValueKey('v2-ino-turn-turn-017')),
        findsOneWidget,
      );

      await _pumpInoRevision(
        tester,
        surface: _inoSurface(
          sequence: 2,
          revision: 2,
          messages: transcript(1),
          operation: inoOperation(state: 'succeeded'),
        ),
        submit: submit,
      );
      await tester.pumpAndSettle();
      expect(
        transcriptController.position.pixels,
        moreOrLessEquals(
          transcriptController.position.maxScrollExtent,
          epsilon: 1,
        ),
      );
      expect(
        find.byKey(const ValueKey('v2-ino-turn-turn-017')),
        findsOneWidget,
      );

      transcriptController.position.jumpTo(0);
      await tester.pump();
      await _pumpInoRevision(
        tester,
        surface: _inoSurface(
          sequence: 3,
          revision: 3,
          messages: transcript(2),
          operation: inoOperation(state: 'succeeded'),
        ),
        submit: submit,
      );
      await tester.pumpAndSettle();
      expect(
        transcriptController.position.pixels,
        lessThan(transcriptController.position.maxScrollExtent - 80),
      );
    },
  );

  testWidgets('scope key teardown clears the conversation draft', (
    tester,
  ) async {
    Future<V2ActionResult> submit(
      Object surface,
      String binding,
      Map<String, Object?> input,
    ) async => const V2ActionResult(
      operationId: 'operation-a',
      idempotencyKey: 'idempotency-a',
    );
    Widget scoped(int epoch) => _host(
      KeyedSubtree(
        key: ValueKey<int>(epoch),
        child: V2SurfaceView(surface: _inoSurface(), onSubmitAction: submit),
      ),
    );

    await tester.pumpWidget(scoped(1));
    await tester.enterText(
      find.byKey(v2InoComposerFieldKey),
      'Private draft from the old scope',
    );
    await tester.pumpWidget(scoped(2));
    await tester.pump();

    expect(
      tester
          .widget<TextField>(find.byKey(v2InoComposerFieldKey))
          .controller
          ?.text,
      isEmpty,
    );
    expect(find.text('Private draft from the old scope'), findsNothing);
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
    await tester.tap(find.text('Continue'));
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

SurfaceEnvelope _inoSurface({
  int sequence = 1,
  int revision = 1,
  List<Map<String, Object?>> messages = const [],
  Map<String, Object?>? operation,
}) => testSurface(
  sequence: sequence,
  revision: revision,
  payload: inoConversationPayload(messages: messages, operation: operation),
  actions: [testInoActionJson(surfaceRevision: revision)],
);

Future<void> _pumpInoRevision(
  WidgetTester tester, {
  required SurfaceEnvelope surface,
  required Future<V2ActionResult> Function(
    Object surface,
    String binding,
    Map<String, Object?> input,
  )
  submit,
}) async {
  await tester.pumpWidget(
    _host(V2SurfaceView(surface: surface, onSubmitAction: submit)),
  );
  await tester.pump();
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
