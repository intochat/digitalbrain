import 'dart:async';

import 'package:digitalbrain_flutter/features/studio/feature_studio_controller.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_gateway.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_models.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_page.dart';
import 'package:digitalbrain_flutter/features/studio/widgets/access_review_panel.dart';
import 'package:digitalbrain_flutter/runtime/runtime_errors.dart';
import 'package:fixnum/fixnum.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('exposes a requested installation target to its owned controller', () {
    final page = FeatureStudioPage(
      draftId: 'draft-a',
      requestedInstallationId: 'installation-existing',
      gateway: _PageGateway(),
      onBackToChat: (_, _) {},
    );

    expect(page.requestedInstallationId, 'installation-existing');
  });

  testWidgets('wide Studio presents the complete trusted Draft canvas', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(1440, 1000);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final gateway = _PageGateway();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
    );

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          key: UniqueKey(),
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Draft'), findsWidgets);
    expect(find.text('Revision 4'), findsNothing);
    expect(find.text('Research Acme'), findsOneWidget);
    expect(find.text('Behavior'), findsWidgets);
    expect(find.text('Suggested changes'), findsWidgets);
    expect(find.text('Code & changes'), findsWidgets);
    expect(find.text('Test results'), findsWidgets);
    expect(find.byKey(featureStudioVerifyButtonKey), findsOneWidget);
    expect(
      tester
          .widget<ButtonStyleButton>(find.byKey(featureStudioVerifyButtonKey))
          .onPressed,
      isNotNull,
    );
    expect(
      find.textContaining(RegExp('proposal', caseSensitive: false)),
      findsNothing,
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets('compact Studio moves assistant and Code into disclosures', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(320, 900);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: _PageGateway(),
    );

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          key: UniqueKey(),
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(tester.getSize(find.byType(SafeArea).first).width, 320);
    await tester.drag(find.byType(ListView).last, const Offset(0, -600));
    await tester.pumpAndSettle();
    expect(find.byKey(featureStudioOpenSuggestionsKey), findsOneWidget);
    expect(find.byKey(featureStudioOpenCodeKey), findsOneWidget);
    expect(find.byKey(featureStudioSuggestionsPanelKey), findsNothing);
    await tester.tap(find.byKey(featureStudioOpenSuggestionsKey));
    await tester.pumpAndSettle();
    expect(find.byKey(featureStudioSuggestionsPanelKey), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('wide section controls scroll to and focus their targets', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(1440, 560);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: _PageGateway(),
    );

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();

    final testResultsControl = find.byKey(featureStudioNavigateTestResultsKey);
    expect(
      tester
          .widget<TextButton>(
            find.descendant(
              of: testResultsControl,
              matching: find.byType(TextButton),
            ),
          )
          .onPressed,
      isNotNull,
    );
    await tester.tap(testResultsControl);
    await tester.pumpAndSettle();

    final testResultsTarget = find.byKey(featureStudioTestResultsSectionKey);
    expect(Focus.of(tester.element(testResultsTarget)).hasPrimaryFocus, isTrue);
    expect(tester.getTopLeft(testResultsTarget).dy, greaterThanOrEqualTo(0));
    expect(tester.getBottomLeft(testResultsTarget).dy, lessThanOrEqualTo(560));

    await tester.tap(find.byKey(featureStudioNavigateSuggestedChangesKey));
    await tester.pumpAndSettle();
    final suggestionsTarget = find.byKey(featureStudioSuggestionsSectionKey);
    expect(Focus.of(tester.element(suggestionsTarget)).hasPrimaryFocus, isTrue);
  });

  testWidgets('Ctrl and Command shortcuts save and Verify immediately', (
    tester,
  ) async {
    final gateway = _PageGateway();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      delay: (_) => Future<void>.value(),
      idFactory: _Ids().call,
    );
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(const ValueKey('scenario-brief-name')),
      'Updated brief',
    );
    expect(FocusManager.instance.primaryFocus?.hasFocus, isTrue);
    await tester.sendKeyDownEvent(LogicalKeyboardKey.controlLeft);
    await tester.sendKeyEvent(LogicalKeyboardKey.keyS);
    await tester.sendKeyUpEvent(LogicalKeyboardKey.controlLeft);
    await tester.pumpAndSettle();
    expect(gateway.behaviorSaves, 1);

    await tester.sendKeyDownEvent(LogicalKeyboardKey.controlLeft);
    await tester.sendKeyEvent(LogicalKeyboardKey.enter);
    await tester.sendKeyUpEvent(LogicalKeyboardKey.controlLeft);
    await tester.pumpAndSettle();
    expect(gateway.verifications, 1);

    await tester.enterText(
      find.byKey(const ValueKey('scenario-brief-name')),
      'Updated by Command shortcut',
    );
    await tester.sendKeyDownEvent(LogicalKeyboardKey.metaLeft);
    await tester.sendKeyEvent(LogicalKeyboardKey.keyS);
    await tester.sendKeyUpEvent(LogicalKeyboardKey.metaLeft);
    await tester.pumpAndSettle();
    expect(gateway.behaviorSaves, 2);

    await tester.sendKeyDownEvent(LogicalKeyboardKey.metaLeft);
    await tester.sendKeyEvent(LogicalKeyboardKey.enter);
    await tester.sendKeyUpEvent(LogicalKeyboardKey.metaLeft);
    await tester.pumpAndSettle();
    expect(gateway.verifications, 2);

    await tester.sendKeyEvent(LogicalKeyboardKey.escape);
    await tester.pump();
    expect(
      FocusManager.instance.primaryFocus?.context?.widget,
      isNot(isA<EditableText>()),
    );
  });

  testWidgets('loading and safe load failures have intentional recovery', (
    tester,
  ) async {
    final pendingGateway = _PageGateway()
      ..pendingLoad = Completer<FeatureStudioDraft>();
    final pendingController = FeatureStudioController(
      draftId: 'draft-a',
      gateway: pendingGateway,
    );
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: pendingController,
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pump();
    expect(find.byKey(featureStudioLoadingKey), findsOneWidget);
    pendingGateway.pendingLoad!.complete(_draft());
    await tester.pumpAndSettle();
    expect(find.byKey(featureStudioDraftIdKey), findsOneWidget);

    final missingGateway = _PageGateway()
      ..loadError = const TransportException(
        TransportErrorCode.notFound,
        'secret server detail',
      );
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          key: const ValueKey('missing-draft-page'),
          draftId: 'draft-a',
          controller: FeatureStudioController(
            draftId: 'draft-a',
            gateway: missingGateway,
          ),
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Draft not found'), findsOneWidget);
    expect(find.textContaining('secret server detail'), findsNothing);
    expect(find.byKey(featureStudioBackToChatButtonKey), findsOneWidget);

    final failedGateway = _PageGateway()
      ..loadError = StateError('database password must not escape');
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          key: const ValueKey('failed-draft-page'),
          draftId: 'draft-a',
          controller: FeatureStudioController(
            draftId: 'draft-a',
            gateway: failedGateway,
          ),
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Draft could not be opened'), findsOneWidget);
    expect(find.textContaining('database password'), findsNothing);
    expect(find.text('Try again'), findsNothing);

    final transientGateway = _PageGateway()
      ..loadError = const TransportException(
        TransportErrorCode.unavailable,
        'safe transient',
      );
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          key: const ValueKey('transient-draft-page'),
          draftId: 'draft-a',
          controller: FeatureStudioController(
            draftId: 'draft-a',
            gateway: transientGateway,
          ),
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Try again'), findsOneWidget);

    final authGateway = _PageGateway()
      ..loadError = const AuthenticationException();
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          key: const ValueKey('auth-draft-page'),
          draftId: 'draft-a',
          controller: FeatureStudioController(
            draftId: 'draft-a',
            gateway: authGateway,
          ),
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Sign-in required'), findsOneWidget);
    expect(find.text('Try again'), findsNothing);
  });

  testWidgets('pending install reset requires explicit informed confirmation', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(1440, 1000);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final gateway = _PageGateway()
      ..draft = _draft(verification: _passingVerification());
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _Ids().call,
    );
    final exitCoordinator = FeatureStudioExitCoordinator();
    await controller.load();
    gateway.loadError = const PreconditionException('The plan is stale.');
    await controller.load();
    gateway
      ..loadError = null
      ..pendingReset = Completer<FeatureStudioDraft>();

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          exitCoordinator: exitCoordinator,
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(gateway.pendingInstallResets, 0);
    expect(
      find.byKey(featureStudioResetPendingInstallButtonKey),
      findsOneWidget,
    );
    await tester.tap(find.byKey(featureStudioResetPendingInstallButtonKey));
    await tester.pumpAndSettle();
    expect(
      find.byKey(featureStudioResetPendingInstallDialogKey),
      findsOneWidget,
    );
    expect(
      find.text(
        'Resetting supersedes the prior access decision. You must Verify '
        'this Draft and review access again before installing.',
      ),
      findsOneWidget,
    );
    await tester.tap(
      find.byKey(featureStudioCancelPendingInstallResetButtonKey),
    );
    await tester.pumpAndSettle();
    expect(gateway.pendingInstallResets, 0);

    await tester.tap(find.byKey(featureStudioResetPendingInstallButtonKey));
    await tester.pumpAndSettle();
    await tester.tap(
      find.byKey(featureStudioConfirmPendingInstallResetButtonKey),
    );
    await tester.pump();

    expect(gateway.pendingInstallResets, 1);
    expect(
      tester
          .widget<TextButton>(find.byKey(featureStudioBackToChatButtonKey))
          .onPressed,
      isNull,
    );
    expect(await exitCoordinator.requestExit(), isFalse);
    expect(find.byKey(featureStudioLeaveDialogKey), findsNothing);
    expect(
      tester
          .widget<FilledButton>(
            find.byKey(featureStudioResetPendingInstallButtonKey),
          )
          .onPressed,
      isNull,
    );
    await tester.tap(
      find.byKey(featureStudioResetPendingInstallButtonKey),
      warnIfMissed: false,
    );
    await tester.pump();
    expect(gateway.pendingInstallResets, 1);

    gateway.pendingReset!.complete(_draft(revision: Int64(5)));
    await tester.pumpAndSettle();

    expect(controller.verification, isNull);
    expect(controller.version, isNull);
    expect(controller.accessReview, isNull);
    expect(controller.installSuccess, isNull);
    expect(controller.canVerify, isTrue);
    expect(controller.canReviewAccess, isFalse);
    expect(
      tester
          .widget<FilledButton>(find.byKey(featureStudioVerifyButtonKey))
          .onPressed,
      isNotNull,
    );
    expect(
      find.byKey(featureStudioReviewAccessButtonKey, skipOffstage: false),
      findsNothing,
    );
  });

  testWidgets('pending install reset is hidden for every other load error', (
    tester,
  ) async {
    final errors = <(String, Object)>[
      ('protocol', const ProtocolException('Invalid reply.')),
      (
        'permission',
        const TransportException(
          TransportErrorCode.permissionDenied,
          'Not permitted.',
        ),
      ),
      (
        'not-found',
        const TransportException(TransportErrorCode.notFound, 'Not found.'),
      ),
      (
        'network',
        const TransportException(
          TransportErrorCode.unavailable,
          'Unavailable.',
        ),
      ),
    ];

    for (final (name, error) in errors) {
      final gateway = _PageGateway()..loadError = error;
      await tester.pumpWidget(
        MaterialApp(
          home: FeatureStudioPage(
            key: ValueKey(name),
            draftId: 'draft-a',
            controller: FeatureStudioController(
              draftId: 'draft-a',
              gateway: gateway,
            ),
            onBackToChat: (_, _) {},
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(
        find.byKey(featureStudioResetPendingInstallButtonKey),
        findsNothing,
        reason: name,
      );
      expect(gateway.pendingInstallResets, 0, reason: name);
    }
  });

  testWidgets(
    'pending install reset is semantic and fits compact 200 percent text',
    (tester) async {
      tester.view.devicePixelRatio = 1;
      tester.view.physicalSize = const Size(320, 900);
      addTearDown(tester.view.resetDevicePixelRatio);
      addTearDown(tester.view.resetPhysicalSize);
      final semantics = tester.ensureSemantics();
      final gateway = _PageGateway()
        ..loadError = const PreconditionException('The plan is stale.');

      await tester.pumpWidget(
        MaterialApp(
          home: MediaQuery(
            data: MediaQueryData.fromView(
              tester.view,
            ).copyWith(textScaler: const TextScaler.linear(2)),
            child: FeatureStudioPage(
              draftId: 'draft-a',
              controller: FeatureStudioController(
                draftId: 'draft-a',
                gateway: gateway,
              ),
              onBackToChat: (_, _) {},
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.bySemanticsLabel('Reset pending install'), findsOneWidget);
      expect(tester.takeException(), isNull);
      final resetButton = find.byKey(featureStudioResetPendingInstallButtonKey);
      await tester.ensureVisible(resetButton);
      await tester.pumpAndSettle();
      await tester.tap(resetButton);
      await tester.pumpAndSettle();
      expect(
        find.byKey(featureStudioResetPendingInstallDialogKey),
        findsOneWidget,
      );
      expect(find.bySemanticsLabel('Reset pending install?'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.tap(
        find.byKey(featureStudioCancelPendingInstallResetButtonKey),
      );
      await tester.pumpAndSettle();
      semantics.dispose();
    },
  );

  testWidgets('conflict recovery is explicit and save state is announced', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();
    final gateway = _PageGateway()..abortBehaviorSave = true;
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      delay: (_) => Future<void>.value(),
    );
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.bySemanticsLabel(RegExp('Save status: Saved')), findsOneWidget);

    await tester.enterText(
      find.byKey(const ValueKey('scenario-brief-name')),
      'Conflicting edit',
    );
    await tester.pumpAndSettle();

    expect(find.byKey(featureStudioConflictKey), findsOneWidget);
    expect(find.text('Use server version'), findsOneWidget);
    expect(find.text('Retry my changes'), findsOneWidget);
    expect(
      find.bySemanticsLabel(RegExp('Save status: Changes need review')),
      findsOneWidget,
    );
    semantics.dispose();
  });

  testWidgets('suggestion and verification state changes are live regions', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(1440, 1000);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final semantics = tester.ensureSemantics();
    final gateway = _PageGateway()
      ..pendingSuggestion = Completer<FeatureStudioSuggestion>()
      ..pendingVerification = Completer<FeatureStudioVerificationResult>();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _Ids().call,
      requestedInstallationId: 'installation-existing',
    );
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    Finder liveRegion(String label) => find.byWidgetPredicate(
      (widget) =>
          widget is Semantics &&
          widget.properties.liveRegion == true &&
          widget.properties.label == label,
      skipOffstage: false,
    );

    final suggestion = controller.requestSuggestedChange('Clarify it');
    await tester.pump();
    expect(liveRegion('Preparing Suggested changes.'), findsOneWidget);
    gateway.pendingSuggestion!.complete(
      await (_PageGateway()..draft = gateway.draft).suggestChange(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        guidance: 'Clarify it',
        suggestionId: 'suggestion-live',
      ),
    );
    await suggestion;
    await tester.pump();
    expect(
      liveRegion('Suggested changes are ready for review.'),
      findsOneWidget,
    );

    final verification = controller.verify();
    await tester.pump();
    expect(liveRegion('Verification is running.'), findsOneWidget);
    final evidence = FeatureStudioVerification(
      releaseDigest: 'a' * 64,
      total: 1,
      passed: 1,
      failed: 0,
      skipped: 0,
      verifiedAt: DateTime.utc(2026, 7, 15, 10, 1),
      sourceReference:
          'sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
      scenarios: const [
        FeatureStudioVerificationScenario(
          scenarioId: 'brief',
          name: 'Create a brief',
          outcome: FeatureStudioScenarioOutcome.passed,
          safeFailure: null,
          durationMilliseconds: 14,
        ),
      ],
    );
    final verifiedDraft = _draft(
      revision: Int64(5),
      behavior: gateway.draft.behavior,
      source: gateway.draft.source,
      verification: evidence,
    );
    gateway.pendingVerification!.complete(
      FeatureStudioVerificationResult(
        draft: verifiedDraft,
        verification: evidence,
        version: _candidateVersion(verifiedDraft.source),
      ),
    );
    await verification;
    await tester.pump();
    expect(
      liveRegion('Verification passed. 1 of 1 tests passed.'),
      findsOneWidget,
    );
    semantics.dispose();
  });

  testWidgets('exact suggestion review refreshes accepted Behavior and Code', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(1440, 1000);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final gateway = _PageGateway();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
    );
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.ensureVisible(find.text('Feature/Feature.cs'));
    await tester.tap(find.text('Feature/Feature.cs'));
    await tester.pumpAndSettle();
    expect(
      find.byKey(const ValueKey('source-Feature/Feature.cs')),
      findsOneWidget,
    );

    await tester.enterText(
      find.byKey(featureStudioSuggestionGuidanceKey),
      '  Add evidence  ',
    );
    await tester.pump();
    expect(controller.canRequestSuggestion, isTrue);
    expect(
      tester
          .widget<TextField>(find.byKey(featureStudioSuggestionGuidanceKey))
          .controller
          ?.text,
      '  Add evidence  ',
    );
    final suggestButton = find.widgetWithText(
      OutlinedButton,
      'Suggest changes',
    );
    await tester.ensureVisible(suggestButton);
    expect(tester.widget<OutlinedButton>(suggestButton).onPressed, isNotNull);
    await tester.tap(suggestButton);
    await tester.pumpAndSettle();
    expect(gateway.lastGuidance, 'Add evidence');

    final completeBehavior = find.byKey(
      const ValueKey('suggestion-diff-addition-behavior-brief'),
    );
    await tester.ensureVisible(completeBehavior);
    await tester.tap(completeBehavior);
    await tester.pumpAndSettle();
    expect(
      find.text(
        'Scenario name: Create an evidence brief\n'
        'Given: A company name and research focus\n'
        'When: The Feature runs\n'
        'Then: A concise brief with evidence is returned',
      ),
      findsOneWidget,
    );

    await tester.ensureVisible(find.widgetWithText(OutlinedButton, 'Accept'));
    await tester.tap(find.widgetWithText(OutlinedButton, 'Accept'));
    await tester.pumpAndSettle();
    expect(
      tester
          .widget<TextFormField>(
            find.byKey(const ValueKey('scenario-brief-name')),
          )
          .controller
          ?.text,
      'Create an evidence brief',
    );
    expect(
      tester
          .widget<TextFormField>(
            find.byKey(const ValueKey('source-Feature/Feature.cs')),
          )
          .controller
          ?.text,
      'updated source',
    );
  });

  testWidgets('installed Draft renders every authoring control read-only', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(1440, 1000);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final gateway = _PageGateway()
      ..draft = _draft(status: FeatureStudioDraftStatus.installed);
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      requestedInstallationId: 'installation-a',
    );
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(
      tester
          .widget<TextFormField>(
            find.byKey(const ValueKey('scenario-brief-name')),
          )
          .enabled,
      isFalse,
    );
    expect(
      tester
          .widget<TextField>(find.byKey(featureStudioSuggestionGuidanceKey))
          .enabled,
      isFalse,
    );
    expect(
      tester
          .widget<ButtonStyleButton>(find.byKey(featureStudioVerifyButtonKey))
          .onPressed,
      isNull,
    );
    final addScenario = find.widgetWithText(OutlinedButton, 'Add Scenario');
    await tester.ensureVisible(addScenario);
    expect(tester.widget<OutlinedButton>(addScenario).onPressed, isNull);
  });

  testWidgets('Accept and Reject retry the exact failed decision intent', (
    tester,
  ) async {
    Future<void> exercise({required bool accept}) async {
      final gateway = _PageGateway();
      if (accept) {
        gateway.failNextAccept = true;
      } else {
        gateway.failNextReject = true;
      }
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        idFactory: _Ids().call,
      );
      await tester.pumpWidget(
        MaterialApp(
          home: FeatureStudioPage(
            key: UniqueKey(),
            draftId: 'draft-a',
            controller: controller,
            onBackToChat: (_, _) {},
          ),
        ),
      );
      await tester.pumpAndSettle();
      await controller.requestSuggestedChange('Improve it');
      await tester.pumpAndSettle();
      final decision = find.widgetWithText(
        OutlinedButton,
        accept ? 'Accept' : 'Reject',
      );
      await tester.ensureVisible(decision);
      await tester.tap(decision);
      await tester.pumpAndSettle();
      expect(find.text('Try again'), findsOneWidget);
      expect(controller.canRequestSuggestion, isFalse);
      final retry = find.widgetWithText(OutlinedButton, 'Try again');
      await tester.ensureVisible(retry);
      await tester.tap(retry);
      await tester.pumpAndSettle();
      expect(accept ? gateway.acceptCalls : gateway.rejectCalls, 2);
      expect(controller.suggestionPhase, FeatureStudioSuggestionPhase.idle);
    }

    await exercise(accept: true);
    await exercise(accept: false);
  });

  testWidgets('Back flushes valid edits before leaving', (tester) async {
    final gateway = _PageGateway();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
    );
    var exits = 0;
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: (_, _) => exits++,
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const ValueKey('scenario-brief-name')),
      'Saved before leaving',
    );
    await tester.tap(find.byKey(featureStudioBackToChatButtonKey));
    await tester.pumpAndSettle();

    expect(gateway.behaviorSaves, 1);
    expect(exits, 1);
    await tester.pump(const Duration(milliseconds: 500));
  });

  testWidgets('system Back flushes valid edits before leaving', (tester) async {
    final gateway = _PageGateway();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
    );
    var exits = 0;
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: (_, _) => exits++,
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const ValueKey('scenario-brief-name')),
      'Saved by system Back',
    );
    await tester.binding.handlePopRoute();
    await tester.pumpAndSettle();

    expect(gateway.behaviorSaves, 1);
    expect(exits, 1);
    await tester.pump(const Duration(milliseconds: 500));
  });

  testWidgets('Back reconciles an uncertain net-zero save before leaving', (
    tester,
  ) async {
    final gateway = _PageGateway()..controlBehaviorSaves = true;
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _Ids().call,
    );
    var exits = 0;
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: (_, _) => exits++,
        ),
      ),
    );
    await tester.pumpAndSettle();

    final uncertainBehavior = _behavior('Possibly stored');
    controller.reviseBehavior(uncertainBehavior);
    final firstAttempt = controller.saveNow();
    await tester.pump();
    final first = gateway.behaviorSaveCalls.single;
    first.completer.completeError(
      const TransportException(
        TransportErrorCode.unavailable,
        'The save outcome is unknown.',
      ),
    );
    await firstAttempt;
    controller.reviseBehavior(_behavior('Create a brief'));
    expect(controller.isDirty, isFalse);

    await tester.tap(find.byKey(featureStudioBackToChatButtonKey));
    await tester.pump();

    expect(exits, 0);
    expect(gateway.behaviorSaveCalls, hasLength(2));
    final replay = gateway.behaviorSaveCalls[1];
    expect(replay.expectedRevision, first.expectedRevision);
    expect(replay.idempotencyId, first.idempotencyId);
    replay.completer.complete(
      _draft(revision: Int64(5), behavior: uncertainBehavior),
    );
    await tester.pump();

    expect(gateway.behaviorSaveCalls, hasLength(3));
    final compensation = gateway.behaviorSaveCalls[2];
    expect(compensation.expectedRevision, Int64(5));
    compensation.completer.complete(_draft(revision: Int64(6)));
    await tester.pumpAndSettle();

    expect(exits, 1);
    expect(find.byKey(featureStudioLeaveDialogKey), findsNothing);
    await tester.pump(const Duration(milliseconds: 500));
  });

  testWidgets('shell exit waits for an active net-zero save and compensation', (
    tester,
  ) async {
    final gateway = _PageGateway()..controlBehaviorSaves = true;
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _Ids().call,
    );
    final coordinator = FeatureStudioExitCoordinator();
    var exits = 0;
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          exitCoordinator: coordinator,
          onBackToChat: (_, _) => exits++,
        ),
      ),
    );
    await tester.pumpAndSettle();

    final inFlightBehavior = _behavior('In flight');
    controller.reviseBehavior(inFlightBehavior);
    final activeSave = controller.saveNow();
    await tester.pump();
    controller.reviseBehavior(_behavior('Create a brief'));
    expect(controller.isDirty, isFalse);

    final exit = coordinator.requestExit();
    await tester.pump();
    expect(exits, 0);

    gateway.behaviorSaveCalls.single.completer.complete(
      _draft(revision: Int64(5), behavior: inFlightBehavior),
    );
    await tester.pump();
    expect(gateway.behaviorSaveCalls, hasLength(2));
    expect(exits, 0);
    gateway.behaviorSaveCalls.last.completer.complete(
      _draft(revision: Int64(6)),
    );
    await activeSave;
    await tester.pump();
    expect(await exit, isTrue);
    await tester.pumpAndSettle();

    expect(exits, 1);
    expect(find.byKey(featureStudioLeaveDialogKey), findsNothing);
    await tester.pump(const Duration(milliseconds: 500));
  });

  testWidgets('invalid and conflicted edits require explicit discard', (
    tester,
  ) async {
    Future<void> exercise({required bool conflict}) async {
      final gateway = _PageGateway()..abortBehaviorSave = conflict;
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        delay: (_) => Future<void>.value(),
      );
      var exits = 0;
      await tester.pumpWidget(
        MaterialApp(
          home: FeatureStudioPage(
            key: UniqueKey(),
            draftId: 'draft-a',
            controller: controller,
            onBackToChat: (_, _) => exits++,
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const ValueKey('scenario-brief-name')),
        conflict ? 'Conflicting edit' : '',
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(featureStudioBackToChatButtonKey));
      await tester.pumpAndSettle();
      expect(find.byKey(featureStudioLeaveDialogKey), findsOneWidget);
      expect(exits, 0);
      await tester.tap(find.byKey(featureStudioStayButtonKey));
      await tester.pumpAndSettle();
      expect(exits, 0);
      await tester.binding.handlePopRoute();
      await tester.pumpAndSettle();
      expect(find.byKey(featureStudioLeaveDialogKey), findsOneWidget);
      await tester.tap(find.byKey(featureStudioDiscardButtonKey));
      await tester.pumpAndSettle();
      expect(exits, 1);
    }

    await exercise(conflict: false);
    await exercise(conflict: true);
  });

  testWidgets('Escape closes compact disclosures and restores launcher focus', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(320, 900);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: _PageGateway(),
    );
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.drag(find.byType(ListView).last, const Offset(0, -600));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(featureStudioOpenSuggestionsKey));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(featureStudioSuggestionGuidanceKey));
    await tester.sendKeyEvent(LogicalKeyboardKey.escape);
    await tester.pumpAndSettle();
    expect(find.byKey(featureStudioSuggestionsPanelKey), findsNothing);
    expect(
      tester
          .widget<OutlinedButton>(find.byKey(featureStudioOpenSuggestionsKey))
          .focusNode
          ?.hasFocus,
      isTrue,
    );

    await tester.tap(find.byKey(featureStudioOpenCodeKey));
    await tester.pumpAndSettle();
    await tester.sendKeyEvent(LogicalKeyboardKey.escape);
    await tester.pumpAndSettle();
    expect(find.byType(Dialog), findsNothing);
    expect(
      tester
          .widget<OutlinedButton>(find.byKey(featureStudioOpenCodeKey))
          .focusNode
          ?.hasFocus,
      isTrue,
    );
  });

  testWidgets('compact Studio remains usable at 200 percent text scaling', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(320, 900);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: _PageGateway(),
    );
    await tester.pumpWidget(
      MaterialApp(
        home: MediaQuery(
          data: MediaQueryData.fromView(
            tester.view,
          ).copyWith(textScaler: const TextScaler.linear(2)),
          child: FeatureStudioPage(
            draftId: 'draft-a',
            controller: controller,
            onBackToChat: (_, _) {},
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    final header = find
        .ancestor(
          of: find.byKey(featureStudioDraftIdKey),
          matching: find.byType(Material),
        )
        .first;
    expect(tester.getSize(header).height, lessThan(300));
    expect(find.text('Behavior'), findsWidgets);

    final mainList = find.byType(ListView).last;
    for (
      var attempt = 0;
      attempt < 8 &&
          find.byKey(featureStudioOpenSuggestionsKey).evaluate().isEmpty;
      attempt++
    ) {
      await tester.drag(mainList, const Offset(0, -300));
      await tester.pumpAndSettle();
    }
    expect(find.byKey(featureStudioOpenSuggestionsKey), findsOneWidget);
    await tester.tap(find.byKey(featureStudioOpenSuggestionsKey));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(featureStudioSuggestionGuidanceKey),
      'Clarify the result',
    );
    await tester.pump();
    final suggest = find.widgetWithText(OutlinedButton, 'Suggest changes');
    await tester.ensureVisible(suggest);
    await controller.requestSuggestedChange('Clarify the result');
    await tester.pumpAndSettle();
    final fullDiff = find.byKey(
      const ValueKey('suggestion-diff-addition-behavior-brief'),
      skipOffstage: false,
    );
    await tester.ensureVisible(fullDiff);
    await tester.tap(fullDiff);
    await tester.pumpAndSettle();
    expect(
      find.textContaining('Then: A concise brief with evidence is returned'),
      findsOneWidget,
    );

    await tester.sendKeyEvent(LogicalKeyboardKey.escape);
    await tester.pumpAndSettle();
    if (find.byKey(featureStudioOpenCodeKey).evaluate().isEmpty) {
      await tester.drag(mainList, const Offset(0, -180));
      await tester.pumpAndSettle();
    }
    await tester.tap(find.byKey(featureStudioOpenCodeKey));
    await tester.pumpAndSettle();
    final sourceTile = find.text('Feature/Feature.cs', skipOffstage: false);
    await tester.ensureVisible(sourceTile);
    await tester.tap(sourceTile);
    await tester.pumpAndSettle();
    expect(
      find.byKey(const ValueKey('source-Feature/Feature.cs')),
      findsOneWidget,
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets('failed verification keeps ordered safe evidence inspectable', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(1440, 1000);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final gateway = _PageGateway()..verificationShouldFail = true;
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _Ids().call,
    );
    await controller.load();
    await controller.verify();

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('0 passed · 1 failed · 1 skipped'), findsOneWidget);
    expect(find.text('Create a brief safely'), findsOneWidget);
    expect(
      find.text('The provider returned no trusted evidence.'),
      findsOneWidget,
    );
    expect(find.text('verification-report.json'), findsOneWidget);
    expect(
      find.text(
        'sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc',
      ),
      findsOneWidget,
    );
    expect(find.byKey(featureStudioReviewAccessButtonKey), findsNothing);

    await tester.enterText(
      find.byKey(const ValueKey('scenario-brief-name')),
      '',
    );
    await tester.pump();

    expect(controller.behavior?.scenarios.single.name, isEmpty);
    expect(controller.verification, isNull);
    expect(find.text('0 passed · 1 failed · 1 skipped'), findsNothing);
    expect(find.text('verification-report.json'), findsNothing);
  });

  testWidgets('exact access review survives a safe install retry and shows success', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(1440, 1200);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    var runNow = 0;
    FeatureStudioOriginatingRequest? returnedOrigin;
    String? returnedDraftId;
    String? runDraftId;
    Int64? runRevision;
    final gateway = _PageGateway()..failNextInstall = true;
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _Ids().call,
      requestedInstallationId: 'installation-existing',
    );
    await controller.load();
    await controller.verify();

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: (origin, draftId) {
            returnedOrigin = origin;
            returnedDraftId = draftId;
          },
          onRunNow: (draftId, expectedRevision) {
            runNow++;
            runDraftId = draftId;
            runRevision = expectedRevision;
          },
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('2 passed · 0 failed · 0 skipped'), findsOneWidget);
    expect(
      find.text(
        'sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
      ),
      findsWidgets,
    );
    final versionPanel = find.byKey(featureStudioVersionPanelKey);
    expect(
      find.descendant(of: versionPanel, matching: find.text('Version digest')),
      findsOneWidget,
    );
    expect(
      find.descendant(
        of: versionPanel,
        matching: find.text('Current source digest'),
      ),
      findsOneWidget,
    );
    expect(
      find.text('Previous Version comparison is not loaded.'),
      findsOneWidget,
    );
    final reviewAccess = find.byKey(featureStudioReviewAccessButtonKey);
    await tester.ensureVisible(reviewAccess);
    await tester.tap(reviewAccess);
    await tester.pumpAndSettle();

    expect(gateway.accessReviews, 1);
    expect(find.text('Changed · Feature/Feature.cs'), findsNWidgets(2));
    expect(find.text('Removed · Feature/Legacy.cs'), findsNWidgets(2));
    expect(
      find.text('digitalbrain.integration.email.read · v1'),
      findsOneWidget,
    );
    expect(find.text('Access needed'), findsWidgets);
    expect(find.text('Automations'), findsOneWidget);
    expect(find.text('Update existing installation'), findsOneWidget);
    expect(find.text('Target installation ID'), findsOneWidget);
    expect(find.text(controller.accessReview!.installationId), findsOneWidget);
    expect(find.text('Candidate release digest'), findsOneWidget);
    expect(find.text('Installed release digest'), findsOneWidget);
    expect(find.text('d' * 64), findsOneWidget);
    expect(find.text('Installed source digest'), findsOneWidget);
    expect(
      find.text(
        'sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd',
      ),
      findsOneWidget,
    );
    expect(find.text('Google · connection-acme'), findsOneWidget);
    expect(find.text('Read up to 25 inbox messages'), findsOneWidget);
    expect(find.text('Manual'), findsOneWidget);
    expect(find.byKey(featureStudioApproveInstallButtonKey), findsOneWidget);
    final accessPanel = find.byKey(featureStudioAccessReviewPanelKey);
    expect(
      find.descendant(
        of: accessPanel,
        matching: find.text('Current source digest'),
      ),
      findsOneWidget,
    );
    expect(
      find.descendant(
        of: accessPanel,
        matching: find.text(
          'sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
        ),
      ),
      findsOneWidget,
    );
    expect(
      find.descendant(
        of: accessPanel,
        matching: find.text('Changed · Feature/Feature.cs'),
      ),
      findsOneWidget,
    );

    final approve = find.byKey(featureStudioApproveInstallButtonKey);
    await tester.ensureVisible(approve);
    await tester.tap(approve);
    await tester.pumpAndSettle();

    expect(gateway.installCalls, 1);
    expect(
      find.text(
        'The approval is unchanged. Retrying is safe and will not duplicate access or install a second Version.',
      ),
      findsOneWidget,
    );
    final retry = find.byKey(featureStudioRetryInstallButtonKey);
    await tester.ensureVisible(retry);
    await tester.tap(retry);
    await tester.pumpAndSettle();

    expect(gateway.accessReviews, 1);
    expect(gateway.installCalls, 2);
    expect(controller.confirmedDraft?.revision, Int64(6));
    expect(find.text('Feature installed'), findsOneWidget);
    expect(find.text('Version identity'), findsOneWidget);
    expect(find.text('Rollback available'), findsOneWidget);
    expect(find.text('Research Acme'), findsWidgets);
    final returnAction = find.widgetWithText(OutlinedButton, 'Return to Chat');
    await tester.ensureVisible(returnAction);
    await tester.tap(returnAction);
    await tester.pump();
    expect(returnedOrigin?.operationId, 'operation-a');
    expect(returnedOrigin?.conversationId, 'conversation-a');
    expect(returnedDraftId, 'draft-a');
    final runNowAction = find.byKey(featureStudioReturnRunNowButtonKey);
    await tester.ensureVisible(runNowAction);
    await tester.tap(runNowAction);
    expect(runNow, 1);
    expect(runDraftId, 'draft-a');
    expect(runRevision, Int64(6));
  });

  testWidgets(
    'access review does not infer a new installation when no prior release is returned',
    (tester) async {
      final gateway = _PageGateway()..includePreviousVersion = false;
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        idFactory: _Ids().call,
        requestedInstallationId: 'installation-requested',
      );
      await controller.load();
      await controller.verify();
      await controller.reviewAccess();

      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: SingleChildScrollView(
              child: AccessReviewPanel(controller: controller),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Installation target'), findsOneWidget);
      expect(find.text('New installation'), findsNothing);
      expect(find.text('Update existing installation'), findsNothing);
      expect(find.text('Target installation ID'), findsOneWidget);
      expect(find.text('installation-requested'), findsOneWidget);
      expect(find.text('Candidate release digest'), findsOneWidget);
      expect(
        find.text(
          'Confirm this exact target and candidate Version before approving.',
        ),
        findsOneWidget,
      );
      expect(find.text('No installed Version will be replaced.'), findsNothing);
      expect(find.text('Installed release digest'), findsNothing);
      expect(find.text('Installed source digest'), findsNothing);
      expect(find.byKey(featureStudioApproveInstallButtonKey), findsOneWidget);
    },
  );

  testWidgets(
    'installed recovery reopens complete success and exact origin handoff',
    (tester) async {
      tester.view.devicePixelRatio = 1;
      tester.view.physicalSize = const Size(1440, 1200);
      addTearDown(tester.view.resetDevicePixelRatio);
      addTearDown(tester.view.resetPhysicalSize);
      FeatureStudioOriginatingRequest? returnedOrigin;
      String? returnedDraftId;
      String? runDraftId;
      Int64? runRevision;
      final recovery = _installedRecovery();
      final gateway = _PageGateway()
        ..draft = _draft(
          revision: Int64(6),
          status: FeatureStudioDraftStatus.installed,
          verification: recovery.verification,
          installationRecovery: recovery,
        );
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
      );
      await controller.load();
      expect(controller.loadPhase, FeatureStudioLoadPhase.ready);
      expect(controller.installPhase, FeatureStudioInstallPhase.succeeded);
      expect(controller.installSuccess, isNotNull);

      await tester.pumpWidget(
        MaterialApp(
          home: FeatureStudioPage(
            draftId: 'draft-a',
            controller: controller,
            onBackToChat: (origin, draftId) {
              returnedOrigin = origin;
              returnedDraftId = draftId;
            },
            onRunNow: (draftId, expectedRevision) {
              runDraftId = draftId;
              runRevision = expectedRevision;
            },
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(controller.installSuccess, isNotNull);

      expect(find.text('Feature installed'), findsOneWidget);
      expect(find.text('Version identity'), findsOneWidget);
      expect(find.text(recovery.version.digest), findsNWidgets(2));
      expect(find.text('Rollback available'), findsOneWidget);
      final returnAction = find.widgetWithText(
        OutlinedButton,
        'Return to Chat',
      );
      await tester.ensureVisible(returnAction);
      await tester.tap(returnAction);
      await tester.pump();
      expect(
        returnedOrigin,
        same(controller.confirmedDraft!.originatingRequest),
      );
      expect(returnedOrigin?.operationId, 'operation-a');
      expect(returnedOrigin?.conversationId, 'conversation-a');
      expect(returnedDraftId, 'draft-a');
      final runAction = find.byKey(featureStudioReturnRunNowButtonKey);
      await tester.ensureVisible(runAction);
      await tester.tap(runAction);
      expect(runDraftId, 'draft-a');
      expect(runRevision, Int64(6));
    },
  );

  testWidgets('zero-authority review explains that no access is requested', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(1440, 1200);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final gateway = _PageGateway()..emptyAccessGrants = true;
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _Ids().call,
    );
    await controller.load();
    await controller.verify();
    await controller.reviewAccess();

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('No access needed.'), findsOneWidget);
  });

  testWidgets('terminal authority failures expose an explicit reset action', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(1440, 1200);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final accessGateway = _PageGateway()..terminalAccessReview = true;
    final accessController = FeatureStudioController(
      draftId: 'draft-a',
      gateway: accessGateway,
      idFactory: _Ids().call,
    );
    await accessController.load();
    await accessController.verify();
    await accessController.reviewAccess();

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: accessController,
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(
      find.byKey(featureStudioResetAuthorityReviewButtonKey),
      findsOneWidget,
    );
    expect(find.bySemanticsLabel('Review access again'), findsOneWidget);
    final accessReset = find.byKey(featureStudioResetAuthorityReviewButtonKey);
    await tester.ensureVisible(accessReset);
    await tester.tap(accessReset);
    await tester.pumpAndSettle();
    expect(
      accessController.accessReviewPhase,
      FeatureStudioAccessReviewPhase.idle,
    );
    expect(accessController.version, isNotNull);

    final installGateway = _PageGateway()..terminalInstall = true;
    final installController = FeatureStudioController(
      draftId: 'draft-a',
      gateway: installGateway,
      idFactory: _Ids().call,
    );
    await installController.load();
    await installController.verify();
    await installController.reviewAccess();
    await installController.approveAndInstall();

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          key: UniqueKey(),
          draftId: 'draft-a',
          controller: installController,
          onBackToChat: (_, _) {},
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(
      find.byKey(featureStudioResetAuthorityReviewButtonKey),
      findsOneWidget,
    );
    final installReset = find.byKey(featureStudioResetAuthorityReviewButtonKey);
    await tester.ensureVisible(installReset);
    await tester.tap(installReset);
    await tester.pumpAndSettle();
    expect(installController.installPhase, FeatureStudioInstallPhase.idle);
    expect(installController.accessReview, isNull);
    expect(installController.version, isNotNull);
  });

  testWidgets(
    'compact Version and Review access restore their launcher focus',
    (tester) async {
      tester.view.devicePixelRatio = 1;
      tester.view.physicalSize = const Size(320, 900);
      addTearDown(tester.view.resetDevicePixelRatio);
      addTearDown(tester.view.resetPhysicalSize);
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: _PageGateway(),
        idFactory: _Ids().call,
      );
      await controller.load();
      await controller.verify();
      await tester.pumpWidget(
        MaterialApp(
          home: FeatureStudioPage(
            draftId: 'draft-a',
            controller: controller,
            onBackToChat: (_, _) {},
          ),
        ),
      );
      await tester.pumpAndSettle();

      final list = find.byType(ListView).last;
      for (
        var attempt = 0;
        attempt < 10 &&
            find.byKey(featureStudioOpenVersionKey).evaluate().isEmpty;
        attempt++
      ) {
        await tester.drag(list, const Offset(0, -300));
        await tester.pumpAndSettle();
      }
      final openVersion = find.byKey(featureStudioOpenVersionKey);
      await tester.ensureVisible(openVersion);
      await tester.tap(openVersion);
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(featureStudioReviewAccessButtonKey));
      await tester.pumpAndSettle();
      await tester.sendKeyEvent(LogicalKeyboardKey.escape);
      await tester.pumpAndSettle();
      expect(
        tester.widget<OutlinedButton>(openVersion).focusNode?.hasFocus,
        isTrue,
      );

      final openAccess = find.byKey(featureStudioOpenAccessReviewKey);
      await tester.ensureVisible(openAccess);
      await tester.tap(openAccess);
      await tester.pumpAndSettle();
      expect(find.byKey(featureStudioApproveInstallButtonKey), findsOneWidget);
      await tester.sendKeyEvent(LogicalKeyboardKey.escape);
      await tester.pumpAndSettle();
      expect(
        tester.widget<OutlinedButton>(openAccess).focusNode?.hasFocus,
        isTrue,
      );
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'compact governance is usable at 200 percent by semantics and keyboard only',
    (tester) async {
      tester.view.devicePixelRatio = 1;
      tester.view.physicalSize = const Size(320, 900);
      addTearDown(tester.view.resetDevicePixelRatio);
      addTearDown(tester.view.resetPhysicalSize);
      final semantics = tester.ensureSemantics();
      var runNow = 0;
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: _PageGateway(),
        idFactory: _Ids().call,
      );
      await controller.load();
      await controller.verify();
      await controller.reviewAccess();
      expect(
        controller.accessReviewPhase,
        FeatureStudioAccessReviewPhase.ready,
      );

      await tester.pumpWidget(
        MaterialApp(
          home: MediaQuery(
            data: MediaQueryData.fromView(
              tester.view,
            ).copyWith(textScaler: const TextScaler.linear(2)),
            child: FeatureStudioPage(
              draftId: 'draft-a',
              controller: controller,
              onBackToChat: (_, _) {},
              onRunNow: (_, _) => runNow++,
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      final openAccess = find.byKey(
        featureStudioOpenAccessReviewKey,
        skipOffstage: false,
      );
      final compactList = find.byWidgetPredicate(
        (widget) =>
            widget is ListView && widget.padding == const EdgeInsets.all(12),
      );
      await tester.scrollUntilVisible(
        openAccess,
        400,
        scrollable: find
            .descendant(
              of: compactList.first,
              matching: find.byType(Scrollable),
            )
            .first,
        maxScrolls: 50,
      );
      expect(openAccess, findsOneWidget);
      await _activateByKeyboard(tester, openAccess);
      await tester.pumpAndSettle();

      expect(find.bySemanticsLabel('Review access'), findsWidgets);
      expect(find.bySemanticsLabel('Approve & install'), findsOneWidget);
      expect(find.text('Google · connection-acme'), findsOneWidget);
      expect(find.text('Read up to 25 inbox messages'), findsOneWidget);
      expect(tester.takeException(), isNull);

      await _activateByKeyboard(
        tester,
        find.byKey(featureStudioApproveInstallButtonKey),
      );
      await tester.pumpAndSettle();

      expect(controller.installPhase, FeatureStudioInstallPhase.succeeded);
      expect(
        find.bySemanticsLabel(RegExp('Feature installed')),
        findsOneWidget,
      );
      expect(
        find.bySemanticsLabel(RegExp('Return to Chat · Run now')),
        findsOneWidget,
      );
      expect(tester.takeException(), isNull);
      await _activateByKeyboard(
        tester,
        find.byKey(featureStudioReturnRunNowButtonKey).hitTestable(),
      );
      await tester.pump();
      expect(runNow, 1);
      semantics.dispose();
    },
  );
}

Future<void> _activateByKeyboard(WidgetTester tester, Finder target) async {
  await tester.ensureVisible(target);
  for (var attempt = 0; attempt < 40; attempt++) {
    await tester.sendKeyEvent(LogicalKeyboardKey.tab);
    await tester.pump();
    final context = FocusManager.instance.primaryFocus?.context;
    if (context != null &&
        find
            .ancestor(of: find.byWidget(context.widget), matching: target)
            .evaluate()
            .isNotEmpty) {
      await tester.sendKeyEvent(LogicalKeyboardKey.enter);
      return;
    }
  }
  fail('Keyboard focus never reached ${target.describeMatch(Plurality.one)}.');
}

class _Ids {
  int value = 0;
  String call() => 'page-id-${++value}';
}

class _PageBehaviorSaveCall {
  _PageBehaviorSaveCall({
    required this.expectedRevision,
    required this.idempotencyId,
    required this.behavior,
    required this.completer,
  });

  final Int64 expectedRevision;
  final String idempotencyId;
  final FeatureStudioBehavior behavior;
  final Completer<FeatureStudioDraft> completer;
}

class _PageGateway implements FeatureStudioGateway {
  FeatureStudioDraft draft = _draft();
  Completer<FeatureStudioDraft>? pendingLoad;
  Completer<FeatureStudioDraft>? pendingReset;
  Object? loadError;
  bool abortBehaviorSave = false;
  bool controlBehaviorSaves = false;
  bool failNextAccept = false;
  bool failNextReject = false;
  bool failNextInstall = false;
  bool terminalAccessReview = false;
  bool terminalInstall = false;
  bool emptyAccessGrants = false;
  bool includePreviousVersion = true;
  bool verificationShouldFail = false;
  int behaviorSaves = 0;
  int verifications = 0;
  int acceptCalls = 0;
  int rejectCalls = 0;
  int accessReviews = 0;
  int installCalls = 0;
  int pendingInstallResets = 0;
  String? pendingInstallResetId;
  final List<_PageBehaviorSaveCall> behaviorSaveCalls = [];
  String? lastGuidance;
  Completer<FeatureStudioSuggestion>? pendingSuggestion;
  Completer<FeatureStudioVerificationResult>? pendingVerification;

  @override
  Future<FeatureStudioDraft> loadDraft(String draftId) async {
    if (loadError case final error?) throw error;
    if (pendingLoad case final pending?) return pending.future;
    return draft;
  }

  @override
  Future<FeatureStudioDraft> resetPendingInstall({
    required String draftId,
    required String idempotencyId,
  }) async {
    pendingInstallResets++;
    pendingInstallResetId = idempotencyId;
    if (pendingReset case final pending?) return pending.future;
    return draft;
  }

  @override
  Future<FeatureStudioDraft> reviseBehavior({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioBehavior behavior,
    required FeatureStudioSource expectedSource,
  }) async {
    if (abortBehaviorSave) {
      throw const TransportException(
        TransportErrorCode.aborted,
        'Draft changed on the server.',
      );
    }
    behaviorSaves++;
    if (controlBehaviorSaves) {
      final completer = Completer<FeatureStudioDraft>();
      behaviorSaveCalls.add(
        _PageBehaviorSaveCall(
          expectedRevision: expectedRevision,
          idempotencyId: idempotencyId,
          behavior: behavior,
          completer: completer,
        ),
      );
      return completer.future;
    }
    draft = _draft(revision: expectedRevision + Int64.ONE, behavior: behavior);
    return draft;
  }

  @override
  Future<FeatureStudioDraft> reviseSource({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioSource source,
    required FeatureStudioBehavior expectedBehavior,
  }) async {
    draft = _draft(revision: expectedRevision + Int64.ONE, source: source);
    return draft;
  }

  @override
  Future<FeatureStudioDraft> acceptSuggestedChange({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioSuggestion suggestion,
  }) async {
    acceptCalls++;
    if (failNextAccept) {
      failNextAccept = false;
      throw const TransportException(
        TransportErrorCode.unavailable,
        'Temporarily unavailable.',
      );
    }
    draft = _draft(
      revision: expectedRevision + Int64.ONE,
      behavior: suggestion.replacementBehavior,
      source: suggestion.replacementSource,
    );
    return draft;
  }

  @override
  Future<FeatureStudioDraft> rejectSuggestedChange({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioSuggestion suggestion,
    required FeatureStudioBehavior expectedBehavior,
    required FeatureStudioSource expectedSource,
    required FeatureStudioVerification? expectedVerification,
  }) async {
    rejectCalls++;
    if (failNextReject) {
      failNextReject = false;
      throw const TransportException(
        TransportErrorCode.unavailable,
        'Temporarily unavailable.',
      );
    }
    return draft;
  }

  @override
  Future<FeatureStudioSuggestion> suggestChange({
    required String draftId,
    required Int64 expectedRevision,
    required String guidance,
    required String suggestionId,
  }) async {
    lastGuidance = guidance;
    if (pendingSuggestion case final pending?) return pending.future;
    return FeatureStudioSuggestion(
      patchId: 'patch-a',
      draftId: draftId,
      baseRevision: expectedRevision,
      summary: 'Improve the outcome',
      replacementBehavior: FeatureStudioBehavior(
        scenarios: const [
          FeatureStudioScenario(
            scenarioId: 'brief',
            name: 'Create an evidence brief',
            given: 'A company name and research focus',
            when: 'The Feature runs',
            then: 'A concise brief with evidence is returned',
          ),
        ],
      ),
      replacementSource: FeatureStudioSource(
        implementationProjectPath: draft.source.implementationProjectPath,
        scenarioProjectPath: draft.source.scenarioProjectPath,
        files: [
          ...draft.source.files.take(2),
          const FeatureStudioSourceFile(
            path: 'Feature/Feature.cs',
            content: 'updated source',
          ),
        ],
      ),
    );
  }

  @override
  Future<FeatureStudioVerificationResult> verifyDraft({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioBehavior expectedBehavior,
    required FeatureStudioSource expectedSource,
  }) async {
    verifications++;
    if (pendingVerification case final pending?) return pending.future;
    if (verificationShouldFail) {
      final verification = _failedVerification();
      draft = _draft(
        revision: expectedRevision,
        behavior: draft.behavior,
        source: draft.source,
        verification: verification,
      );
      return FeatureStudioVerificationResult(
        draft: draft,
        verification: verification,
        version: null,
      );
    }
    final verification = _passingVerification();
    draft = _draft(
      revision: expectedRevision + Int64.ONE,
      behavior: draft.behavior,
      source: draft.source,
      verification: verification,
    );
    return FeatureStudioVerificationResult(
      draft: draft,
      verification: verification,
      version: _candidateVersion(draft.source),
    );
  }

  @override
  Future<FeatureStudioAccessReview> reviewAccess({
    required String draftId,
    required Int64 expectedRevision,
    required FeatureStudioDraft expectedDraft,
    required String installationId,
    required FeatureStudioVersion version,
    required FeatureStudioVerification expectedVerification,
    required FeatureStudioBehavior expectedBehavior,
    required FeatureStudioSource expectedSource,
  }) async {
    accessReviews++;
    if (terminalAccessReview) {
      throw const TransportException(
        TransportErrorCode.permissionDenied,
        'Access review is not permitted.',
      );
    }
    return FeatureStudioAccessReview(
      draft: draft,
      version: version,
      installationId: installationId,
      grants: emptyAccessGrants
          ? const []
          : const [
              FeatureStudioGrant(
                capabilityId: 'digitalbrain.integration.email.read',
                capabilityVersion: 1,
                provider: 'Google',
                connectionId: 'connection-acme',
                constraintsJson: '{"mailbox":"inbox","limit":25}',
                constraintSummary: 'Read up to 25 inbox messages',
              ),
              FeatureStudioGrant(
                capabilityId: 'digitalbrain.model.generate',
                capabilityVersion: 1,
                provider: null,
                connectionId: null,
                constraintsJson: '{"allowedToolIds":[]}',
                constraintSummary: 'Generate the requested brief',
              ),
            ],
      subscriptions: const ['manual'],
      previousVersion: includePreviousVersion ? _previousVersion() : null,
    );
  }

  @override
  Future<FeatureStudioInstallSuccess> installVersion({
    required FeatureStudioAccessReview review,
    required Int64 expectedRevision,
    required String decisionId,
    required String idempotencyId,
    required FeatureStudioBehavior expectedBehavior,
    required FeatureStudioSource expectedSource,
  }) async {
    installCalls++;
    if (terminalInstall) {
      throw const TransportException(
        TransportErrorCode.permissionDenied,
        'Installation is not permitted.',
      );
    }
    if (failNextInstall) {
      failNextInstall = false;
      throw const TransportException(
        TransportErrorCode.unavailable,
        'Installation status is temporarily unavailable.',
      );
    }
    draft = _draft(
      revision: expectedRevision + Int64.ONE,
      behavior: expectedBehavior,
      source: expectedSource,
      verification: _passingVerification(),
      status: FeatureStudioDraftStatus.installed,
      installationId: review.installationId,
    );
    return FeatureStudioInstallSuccess(
      draft: draft,
      version: review.version,
      installationId: review.installationId,
      activeGrants: review.grants,
      subscriptions: review.subscriptions,
      rollbackAvailable: true,
    );
  }
}

FeatureStudioDraft _draft({
  Int64? revision,
  FeatureStudioBehavior? behavior,
  FeatureStudioSource? source,
  FeatureStudioVerification? verification,
  FeatureStudioDraftStatus status = FeatureStudioDraftStatus.draft,
  String? installationId,
  FeatureStudioInstallationRecovery? installationRecovery,
}) => FeatureStudioDraft(
  draftId: 'draft-a',
  originatingRequest: const FeatureStudioOriginatingRequest(
    operationId: 'operation-a',
    conversationId: 'conversation-a',
    text: 'Research Acme',
  ),
  goal: 'Create a concise company brief',
  status: status,
  installationId:
      installationId ??
      (status == FeatureStudioDraftStatus.installed ? 'installation-a' : null),
  behavior:
      behavior ??
      FeatureStudioBehavior(
        scenarios: const [
          FeatureStudioScenario(
            scenarioId: 'brief',
            name: 'Create a brief',
            given: 'A company name',
            when: 'The Feature runs',
            then: 'A concise brief is returned',
          ),
        ],
      ),
  source:
      source ??
      FeatureStudioSource(
        implementationProjectPath: 'Feature/Feature.csproj',
        scenarioProjectPath: 'Feature.Tests/Feature.Tests.csproj',
        files: const [
          FeatureStudioSourceFile(
            path: 'Feature/Feature.csproj',
            content: '<Project Sdk="Microsoft.NET.Sdk" />',
          ),
          FeatureStudioSourceFile(
            path: 'Feature.Tests/Feature.Tests.csproj',
            content: '<Project Sdk="Microsoft.NET.Sdk" />',
          ),
          FeatureStudioSourceFile(
            path: 'Feature/Feature.cs',
            content: 'source',
          ),
        ],
      ),
  verification: verification,
  revision: revision ?? Int64(4),
  createdAt: DateTime.utc(2026, 7, 15, 10),
  updatedAt: DateTime.utc(2026, 7, 15, 10, 1),
  installationRecovery: installationRecovery,
);

FeatureStudioInstallationRecovery _installedRecovery() {
  final source = _draft().source;
  return FeatureStudioInstallationRecovery(
    installed: true,
    verification: _passingVerification(),
    version: _candidateVersion(source),
    installationId: 'installation-a',
    grants: const [
      FeatureStudioGrant(
        capabilityId: 'digitalbrain.integration.email.read',
        capabilityVersion: 1,
        provider: 'Google',
        connectionId: 'connection-acme',
        constraintsJson:
            '{"allowedToolIds":["digitalbrain.integration.email.read"]}',
        constraintSummary: 'Only digitalbrain.integration.email.read',
      ),
      FeatureStudioGrant(
        capabilityId: 'digitalbrain.model.generate',
        capabilityVersion: 1,
        provider: null,
        connectionId: null,
        constraintsJson: '{"allowedToolIds":["digitalbrain.model.generate"]}',
        constraintSummary: 'Only digitalbrain.model.generate',
      ),
    ],
    subscriptions: const ['manual'],
    previousVersion: _previousVersion(),
    decisionId: null,
    idempotencyId: null,
    rollbackAvailable: true,
    paused: false,
    pauseReason: null,
  );
}

FeatureStudioBehavior _behavior(String name) => FeatureStudioBehavior(
  scenarios: [
    FeatureStudioScenario(
      scenarioId: 'brief',
      name: name,
      given: 'A company name',
      when: 'The Feature runs',
      then: 'A concise brief is returned',
    ),
  ],
);

FeatureStudioVerification _passingVerification() => FeatureStudioVerification(
  releaseDigest: 'a' * 64,
  total: 2,
  passed: 2,
  failed: 0,
  skipped: 0,
  verifiedAt: DateTime.utc(2026, 7, 15, 10, 1),
  sourceReference:
      'sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
  scenarios: const [
    FeatureStudioVerificationScenario(
      scenarioId: 'brief',
      name: 'Create a brief',
      outcome: FeatureStudioScenarioOutcome.passed,
      safeFailure: null,
      durationMilliseconds: 14,
    ),
    FeatureStudioVerificationScenario(
      scenarioId: 'missing-evidence',
      name: 'Explain missing evidence',
      outcome: FeatureStudioScenarioOutcome.passed,
      safeFailure: null,
      durationMilliseconds: 9,
    ),
  ],
  artifacts: [
    FeatureStudioVerificationArtifact(
      name: 'verification-report.json',
      mediaType: 'application/json',
      sizeBytes: 384,
      digest: 'b' * 64,
    ),
  ],
);

FeatureStudioVerification _failedVerification() => FeatureStudioVerification(
  releaseDigest: null,
  total: 2,
  passed: 0,
  failed: 1,
  skipped: 1,
  verifiedAt: DateTime.utc(2026, 7, 15, 10, 1),
  sourceReference:
      'sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc',
  scenarios: const [
    FeatureStudioVerificationScenario(
      scenarioId: 'brief',
      name: 'Create a brief safely',
      outcome: FeatureStudioScenarioOutcome.failed,
      safeFailure: 'The provider returned no trusted evidence.',
      durationMilliseconds: 20,
    ),
    FeatureStudioVerificationScenario(
      scenarioId: 'follow-up',
      name: 'Send a follow-up',
      outcome: FeatureStudioScenarioOutcome.skipped,
      safeFailure: null,
      durationMilliseconds: 0,
    ),
  ],
  artifacts: [
    FeatureStudioVerificationArtifact(
      name: 'verification-report.json',
      mediaType: 'application/json',
      sizeBytes: 512,
      digest: 'c' * 64,
    ),
  ],
);

FeatureStudioVersion _candidateVersion(
  FeatureStudioSource source,
) => FeatureStudioVersion(
  digest: 'a' * 64,
  sourceReference:
      'sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
  requestedCapabilityIds: const [
    'digitalbrain.integration.email.read',
    'digitalbrain.model.generate',
  ],
  dependencies: const ['Google connection'],
  source: source,
);

FeatureStudioVersion _previousVersion() => FeatureStudioVersion(
  digest: 'd' * 64,
  sourceReference:
      'sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd',
  requestedCapabilityIds: const ['digitalbrain.integration.email.read'],
  dependencies: const ['Google connection'],
  source: FeatureStudioSource(
    implementationProjectPath: 'Feature/Feature.csproj',
    scenarioProjectPath: 'Feature.Tests/Feature.Tests.csproj',
    files: const [
      FeatureStudioSourceFile(
        path: 'Feature/Feature.csproj',
        content: '<Project Sdk="Microsoft.NET.Sdk" />',
      ),
      FeatureStudioSourceFile(
        path: 'Feature.Tests/Feature.Tests.csproj',
        content: '<Project Sdk="Microsoft.NET.Sdk" />',
      ),
      FeatureStudioSourceFile(
        path: 'Feature/Feature.cs',
        content: 'previous source',
      ),
      FeatureStudioSourceFile(
        path: 'Feature/Legacy.cs',
        content: 'legacy source',
      ),
    ],
  ),
);
