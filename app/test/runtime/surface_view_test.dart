import 'dart:async';

import 'package:digitalbrain_flutter/runtime/protocol/surface_protocol.dart';
import 'package:digitalbrain_flutter/runtime/runtime.dart';
import 'package:digitalbrain_flutter/runtime/widgets/ino_composer.dart';
import 'package:digitalbrain_flutter/runtime/widgets/ino_conversation_view.dart';
import 'package:digitalbrain_flutter/runtime/widgets/surface_view.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:forui/forui.dart';

import 'test_fixtures.dart';

void main() {
  testWidgets('renders a runtime widget-tree surface', (tester) async {
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
      _host(SurfaceView(surface: surface, onSubmitAction: _unexpectedAction)),
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
        SurfaceView(
          surface: surface,
          onSubmitAction: (surface, bindingId, actionInput) async {
            binding = bindingId;
            input = actionInput;
            return const ActionResult(
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
      _host(SurfaceView(surface: surface, onSubmitAction: _unexpectedAction)),
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
    final receipt = Completer<ActionResult>();
    String? binding;
    Map<String, Object?>? submittedInput;

    await tester.pumpWidget(
      _host(
        SurfaceView(
          surface: _inoSurface(),
          onSubmitAction: (surface, bindingId, input) async {
            binding = bindingId;
            submittedInput = input;
            return receipt.future;
          },
        ),
      ),
    );

    expect(find.byKey(inoConversationKey), findsOneWidget);
    expect(find.byKey(inoComposerFieldKey), findsOneWidget);
    expect(find.text('Ask INO about this workspace.'), findsOneWidget);
    expect(find.byKey(inoEmptyTranscriptKey), findsOneWidget);

    await tester.enterText(find.byKey(inoComposerFieldKey), prompt);
    await tester.pump();
    await tester.tap(find.byKey(inoSendButtonKey));
    await tester.pump();

    expect(binding, 'ino.send');
    expect(submittedInput, {'prompt': prompt});
    expect(find.text(prompt), findsOneWidget);
    expect(find.text('Sending'), findsOneWidget);
    expect(
      tester.widget<FilledButton>(find.byKey(inoSendButtonKey)).onPressed,
      isNull,
    );

    receipt.complete(
      const ActionResult(
        operationId: 'operation-a',
        idempotencyKey: 'idempotency-a',
      ),
    );
    await tester.pump();
    await tester.pump();

    expect(find.text('Queued'), findsOneWidget);
    expect(
      tester
          .widget<TextField>(find.byKey(inoComposerFieldKey))
          .focusNode
          ?.hasFocus,
      isTrue,
    );
    expect(find.textContaining('operation-a'), findsNothing);
    expect(find.textContaining('idempotency-a'), findsNothing);

    await tester.enterText(
      find.byKey(inoComposerFieldKey),
      'A second message while acceptance is reconciling',
    );
    await tester.pump();
    expect(
      tester.widget<FilledButton>(find.byKey(inoSendButtonKey)).onPressed,
      isNull,
    );
  });

  testWidgets('uses a readable foreground for user turns in dark mode', (
    tester,
  ) async {
    const prompt = 'A restored user turn must remain readable.';
    final theme = ThemeData.dark(useMaterial3: true);

    await tester.pumpWidget(
      _host(
        SurfaceView(
          surface: _inoSurface(
            messages: [
              inoMessage(
                turnKey: 'turn-contrast-user',
                role: 'user',
                text: prompt,
                state: 'succeeded',
              ),
            ],
          ),
          onSubmitAction: _unexpectedAction,
        ),
        theme: theme,
      ),
    );

    final expectedForeground = theme.colorScheme.onPrimaryContainer;
    expect(
      tester.widget<SelectableText>(find.byType(SelectableText)).style?.color,
      expectedForeground,
    );
    expect(
      tester
          .widget<Text>(
            find.byKey(const ValueKey('v2-ino-turn-turn-contrast-user-author')),
          )
          .style
          ?.color,
      expectedForeground,
    );
    expect(
      tester
          .widget<Text>(
            find.byKey(const ValueKey('v2-ino-turn-turn-contrast-user-status')),
          )
          .style
          ?.color,
      expectedForeground,
    );
  });

  testWidgets('enforces the production prompt bound before submission', (
    tester,
  ) async {
    Map<String, Object?>? submittedInput;
    await tester.pumpWidget(
      _host(
        SurfaceView(
          surface: _inoSurface(),
          onSubmitAction: (surface, bindingId, input) async {
            submittedInput = input;
            return const ActionResult(
              operationId: 'operation-a',
              idempotencyKey: 'idempotency-a',
            );
          },
        ),
      ),
    );

    await tester.enterText(
      find.byKey(inoComposerFieldKey),
      List.filled(inoMaximumPromptLength + 1, 'x').join(),
    );
    await tester.pump();
    final field = tester.widget<TextField>(find.byKey(inoComposerFieldKey));
    expect(field.controller?.text.length, inoMaximumPromptLength);
    await tester.tap(find.byKey(inoSendButtonKey));
    await tester.pump();
    expect(
      (submittedInput?['prompt'] as String?)?.length,
      inoMaximumPromptLength,
    );
  });

  testWidgets(
    'preserves draft and focus through queued running responding and success',
    (tester) async {
      const prompt = 'What can you help me with in this workspace?';
      Future<ActionResult> submit(
        Object surface,
        String binding,
        Map<String, Object?> input,
      ) async => const ActionResult(
        operationId: 'operation-a',
        idempotencyKey: 'idempotency-a',
      );

      await tester.pumpWidget(
        _host(SurfaceView(surface: _inoSurface(), onSubmitAction: submit)),
      );
      await tester.enterText(find.byKey(inoComposerFieldKey), prompt);
      await tester.pump();
      await tester.tap(find.byKey(inoSendButtonKey));
      await tester.pump();
      await tester.pump();
      await tester.enterText(
        find.byKey(inoComposerFieldKey),
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
        tester.widget<FilledButton>(find.byKey(inoSendButtonKey)).onPressed,
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
        tester.widget<FilledButton>(find.byKey(inoSendButtonKey)).onPressed,
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
        tester.widget<FilledButton>(find.byKey(inoSendButtonKey)).onPressed,
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

      final field = tester.widget<TextField>(find.byKey(inoComposerFieldKey));
      expect(field.controller?.text, 'Keep this next draft');
      expect(field.focusNode?.hasFocus, isTrue);
      expect(find.text(prompt), findsOneWidget);
      expect(
        find.text('I can help you understand and work with this workspace.'),
        findsOneWidget,
      );
      expect(find.text('Response ready.'), findsOneWidget);
      expect(
        tester.widget<FilledButton>(find.byKey(inoSendButtonKey)).onPressed,
        isNotNull,
      );
    },
  );

  testWidgets('feed confirmation before the receipt does not lock chat', (
    tester,
  ) async {
    const prompt = 'What can you help me with in this workspace?';
    final receipt = Completer<ActionResult>();
    Future<ActionResult> submit(
      Object surface,
      String binding,
      Map<String, Object?> input,
    ) => receipt.future;

    await tester.pumpWidget(
      _host(SurfaceView(surface: _inoSurface(), onSubmitAction: submit)),
    );
    await tester.enterText(find.byKey(inoComposerFieldKey), prompt);
    await tester.pump();
    await tester.tap(find.byKey(inoSendButtonKey));
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
      const ActionResult(
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
    await tester.enterText(find.byKey(inoComposerFieldKey), 'Next question');
    await tester.pump();

    expect(find.byKey(inoSubmissionNoticeKey), findsNothing);
    expect(
      tester.widget<FilledButton>(find.byKey(inoSendButtonKey)).onPressed,
      isNotNull,
    );
  });

  testWidgets('definite preflight rejection restores the draft', (
    tester,
  ) async {
    const prompt = 'Keep this message';
    await tester.pumpWidget(
      _host(
        SurfaceView(
          surface: _inoSurface(),
          onSubmitAction: (surface, binding, input) async =>
              throw StateError('stale action'),
        ),
      ),
    );
    await tester.enterText(find.byKey(inoComposerFieldKey), prompt);
    await tester.pump();
    await tester.tap(find.byKey(inoSendButtonKey));
    await tester.pump();
    await tester.pump();

    final field = tester.widget<TextField>(find.byKey(inoComposerFieldKey));
    expect(field.controller?.text, prompt);
    expect(find.text('Checking delivery'), findsNothing);
    expect(
      find.text('That message wasn\'t sent. Please try again.'),
      findsOneWidget,
    );
    expect(
      tester.widget<FilledButton>(find.byKey(inoSendButtonKey)).onPressed,
      isNotNull,
    );
  });

  testWidgets('keeps the draft and disables Send while reconnecting', (
    tester,
  ) async {
    Future<ActionResult> submit(
      Object surface,
      String binding,
      Map<String, Object?> input,
    ) async => const ActionResult(
      operationId: 'operation-a',
      idempotencyKey: 'idempotency-a',
    );
    final surface = _inoSurface();

    await tester.pumpWidget(
      _host(SurfaceView(surface: surface, onSubmitAction: submit)),
    );
    await tester.enterText(
      find.byKey(inoComposerFieldKey),
      'Keep this draft offline',
    );
    await tester.pumpWidget(
      _host(
        SurfaceView(
          surface: surface,
          onSubmitAction: submit,
          actionEnabled: false,
          reconnecting: true,
        ),
      ),
    );
    await tester.pump();

    expect(find.byKey(inoReconnectBannerKey), findsOneWidget);
    expect(
      tester
          .widget<TextField>(find.byKey(inoComposerFieldKey))
          .controller
          ?.text,
      'Keep this draft offline',
    );
    expect(
      tester.widget<FilledButton>(find.byKey(inoSendButtonKey)).onPressed,
      isNull,
    );

    await tester.pumpWidget(
      _host(SurfaceView(surface: surface, onSubmitAction: submit)),
    );
    await tester.pump();
    expect(find.byKey(inoReconnectBannerKey), findsNothing);
    expect(
      tester
          .widget<TextField>(find.byKey(inoComposerFieldKey))
          .controller
          ?.text,
      'Keep this draft offline',
    );
  });

  testWidgets('shows Retry only for an explicitly retryable failure', (
    tester,
  ) async {
    var submissions = 0;
    Future<ActionResult> submit(
      Object surface,
      String binding,
      Map<String, Object?> input,
    ) async {
      submissions++;
      expect(input, {'prompt': 'Please try this'});
      return const ActionResult(
        operationId: 'operation-a',
        idempotencyKey: 'idempotency-a',
      );
    }

    await tester.pumpWidget(
      _host(
        SurfaceView(
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
    expect(find.byKey(inoRetryButtonKey), findsNothing);

    await tester.pumpWidget(
      _host(
        SurfaceView(
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
    await tester.tap(find.byKey(inoRetryButtonKey));
    await tester.pump();
    await tester.pump();
    expect(submissions, 1);
    expect(find.text('Please try this'), findsOneWidget);

    await tester.pumpWidget(
      _host(
        SurfaceView(
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
    await tester.enterText(find.byKey(inoComposerFieldKey), 'Another prompt');
    await tester.pump();
    expect(
      tester.widget<FilledButton>(find.byKey(inoSendButtonKey)).onPressed,
      isNotNull,
    );
  });

  testWidgets('renders a grounded sender email address verbatim', (
    tester,
  ) async {
    const response =
        'The latest incoming email was sent by Ada Lovelace <ada@example.com>.';
    await tester.pumpWidget(
      _host(
        SurfaceView(
          surface: _inoSurface(
            messages: [
              inoMessage(role: 'assistant', text: response, state: 'succeeded'),
            ],
            operation: inoOperation(state: 'succeeded'),
          ),
          onSubmitAction: _unexpectedAction,
        ),
      ),
    );

    expect(find.text(response), findsOneWidget);
    expect(find.byType(SelectableText), findsOneWidget);
  });

  testWidgets('shows the principal-scoped Google connection action', (
    tester,
  ) async {
    await tester.pumpWidget(
      _host(
        SurfaceView(
          surface: _inoSurface(
            messages: [
              inoMessage(
                role: 'assistant',
                text: 'Connect your Google account to let INO read your Gmail.',
                state: 'succeeded',
              ),
            ],
            operation: inoOperation(
              state: 'succeeded',
              action: googleConnectionAction(),
            ),
          ),
          onSubmitAction: (_, _, _) async => const ActionResult(
            operationId: 'unused',
            idempotencyKey: 'unused',
          ),
        ),
      ),
    );

    expect(find.byKey(inoConnectButtonKey), findsOneWidget);
    expect(find.text('Connect Google'), findsOneWidget);
    expect(find.textContaining('Connect your Google account'), findsOneWidget);
  });

  testWidgets('awaiting authorization renders a connection-required state', (
    tester,
  ) async {
    await tester.pumpWidget(
      _host(
        SurfaceView(
          surface: _inoSurface(
            messages: [
              inoMessage(
                role: 'assistant',
                text: 'Connect Google to continue.',
                state: 'awaiting-authorization',
              ),
            ],
            operation: inoOperation(
              state: 'awaiting-authorization',
              action: googleConnectionAction(),
            ),
          ),
          onSubmitAction: _unexpectedAction,
        ),
      ),
    );

    expect(find.text('INO is waiting for you to connect.'), findsOneWidget);
    expect(find.text('Connection required'), findsOneWidget);
    expect(find.byKey(inoConnectButtonKey), findsOneWidget);
    expect(
      tester.widget<FilledButton>(find.byKey(inoSendButtonKey)).onPressed,
      isNull,
    );
  });

  testWidgets('terminal state is announced as a live status', (tester) async {
    await tester.pumpWidget(
      _host(
        SurfaceView(
          surface: _inoSurface(operation: inoOperation(state: 'succeeded')),
          onSubmitAction: _unexpectedAction,
        ),
      ),
    );

    final semantics = tester.getSemantics(find.byKey(inoOperationStatusKey));
    expect(semantics.flagsCollection.isLiveRegion, isTrue);
  });

  testWidgets('new and delete conversation actions submit empty typed input', (
    tester,
  ) async {
    final calls = <(String, Map<String, Object?>)>[];
    final createCompletion = Completer<ActionResult>();
    var submissions = 0;
    Future<ActionResult> submit(
      Object surface,
      String binding,
      Map<String, Object?> input,
    ) {
      calls.add((binding, input));
      submissions++;
      if (submissions == 1) return createCompletion.future;
      return Future.value(
        const ActionResult(
          operationId: 'delete-operation',
          idempotencyKey: 'delete-idempotency',
        ),
      );
    }

    await tester.pumpWidget(
      _host(
        SurfaceView(
          surface: _inoSurface(includeLifecycleActions: true),
          onSubmitAction: submit,
        ),
      ),
    );

    await tester.tap(find.byKey(inoNewConversationButtonKey));
    await tester.pump();
    expect(calls, hasLength(1));
    expect(calls.single.$1, 'ino.new');
    expect(calls.single.$2, isEmpty);
    expect(
      tester
          .widget<OutlinedButton>(find.byKey(inoNewConversationButtonKey))
          .onPressed,
      isNull,
    );
    expect(
      tester
          .widget<IconButton>(find.byKey(inoDeleteConversationButtonKey))
          .onPressed,
      isNull,
    );

    createCompletion.complete(
      const ActionResult(
        operationId: 'create-operation',
        idempotencyKey: 'create-idempotency',
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(inoDeleteConversationButtonKey));
    await tester.pumpAndSettle();
    expect(find.text('Delete this conversation?'), findsOneWidget);
    await tester.tap(find.byKey(inoDeleteConversationConfirmKey));
    await tester.pumpAndSettle();

    expect(calls.last.$1, 'ino.delete');
    expect(calls.last.$2, isEmpty);
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

      Future<ActionResult> submit(
        Object surface,
        String binding,
        Map<String, Object?> input,
      ) async => const ActionResult(
        operationId: 'operation-a',
        idempotencyKey: 'idempotency-a',
      );

      await tester.pumpWidget(
        _host(
          SurfaceView(
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
          .widget<ListView>(find.byKey(inoTranscriptKey))
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
    Future<ActionResult> submit(
      Object surface,
      String binding,
      Map<String, Object?> input,
    ) async => const ActionResult(
      operationId: 'operation-a',
      idempotencyKey: 'idempotency-a',
    );
    Widget scoped(int epoch) => _host(
      KeyedSubtree(
        key: ValueKey<int>(epoch),
        child: SurfaceView(surface: _inoSurface(), onSubmitAction: submit),
      ),
    );

    await tester.pumpWidget(scoped(1));
    await tester.enterText(
      find.byKey(inoComposerFieldKey),
      'Private draft from the old scope',
    );
    await tester.pumpWidget(scoped(2));
    await tester.pump();

    expect(
      tester
          .widget<TextField>(find.byKey(inoComposerFieldKey))
          .controller
          ?.text,
      isEmpty,
    );
    expect(find.text('Private draft from the old scope'), findsNothing);
  });

  testWidgets('renders a native runtime surface and submits its action', (
    tester,
  ) async {
    final surface = testSurface(actions: [testActionJson()]);
    var submitted = false;

    await tester.pumpWidget(
      _host(
        SurfaceView(
          surface: surface,
          onSubmitAction: (surface, binding, input) async {
            submitted = true;
            expect(binding, 'refresh-binding');
            return const ActionResult(
              operationId: 'operation-a',
              idempotencyKey: 'idempotency-a',
            );
          },
        ),
      ),
    );

    expect(find.text('Runtime ready'), findsOneWidget);
    expect(find.text('Authenticated surface'), findsOneWidget);
    await tester.tap(find.text('Continue'));
    await tester.pump();
    expect(submitted, isTrue);
  });

  testWidgets('renders a text RFW surface through the fixed dictionary', (
    tester,
  ) async {
    final surface = testSurface(
      payload: {
        'kind': 'rfw',
        'rootWidget': 'root',
        'data': <String, Object?>{},
        'libraryText': '''
import digitalbrain;
widget root = Text(text: "RFW runtime ready");
''',
      },
    );

    await tester.pumpWidget(
      _host(SurfaceView(surface: surface, onSubmitAction: _unexpectedAction)),
    );
    await tester.pump();

    expect(find.text('RFW runtime ready'), findsOneWidget);
  });
}

SurfaceEnvelope _inoSurface({
  int sequence = 1,
  int revision = 1,
  List<Map<String, Object?>> messages = const [],
  Map<String, Object?>? operation,
  bool includeLifecycleActions = false,
}) => testSurface(
  sequence: sequence,
  revision: revision,
  payload: inoConversationPayload(messages: messages, operation: operation),
  actions: [
    testInoActionJson(surfaceRevision: revision),
    if (includeLifecycleActions)
      testActionJson(
        bindingId: 'ino.new',
        actionType: 'ino.conversation.new',
        actionToken: 'signed-new-conversation-action-token',
        surfaceRevision: revision,
      ),
    if (includeLifecycleActions)
      testActionJson(
        bindingId: 'ino.delete',
        actionType: 'ino.conversation.delete',
        actionToken: 'signed-delete-conversation-action-token',
        surfaceRevision: revision,
      ),
  ],
);

Future<void> _pumpInoRevision(
  WidgetTester tester, {
  required SurfaceEnvelope surface,
  required Future<ActionResult> Function(
    Object surface,
    String binding,
    Map<String, Object?> input,
  )
  submit,
}) async {
  await tester.pumpWidget(
    _host(SurfaceView(surface: surface, onSubmitAction: submit)),
  );
  await tester.pump();
}

Widget _host(Widget child, {ThemeData? theme}) => MaterialApp(
  theme: theme,
  home: FTheme(
    data: FThemes.neutral.light.touch,
    child: Scaffold(body: SizedBox.expand(child: child)),
  ),
);

Future<ActionResult> _unexpectedAction(
  Object surface,
  String binding,
  Map<String, Object?> input,
) => throw StateError('Unexpected action.');
