import 'dart:async';

import 'package:digitalbrain_flutter/features/studio/feature_studio_controller.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_gateway.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_models.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_page.dart';
import 'package:digitalbrain_flutter/runtime/runtime_errors.dart';
import 'package:fixnum/fixnum.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
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
          onBackToChat: () {},
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
          onBackToChat: () {},
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
          onBackToChat: () {},
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
          onBackToChat: () {},
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
          onBackToChat: () {},
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
          onBackToChat: () {},
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
          onBackToChat: () {},
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
          onBackToChat: () {},
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
          onBackToChat: () {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Sign-in required'), findsOneWidget);
    expect(find.text('Try again'), findsNothing);
  });

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
          onBackToChat: () {},
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
      ..pendingVerification = Completer<FeatureStudioDraft>();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _Ids().call,
    );
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: () {},
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
    gateway.pendingVerification!.complete(
      _draft(
        revision: Int64(5),
        behavior: gateway.draft.behavior,
        source: gateway.draft.source,
        verification: FeatureStudioVerification(
          releaseDigest: 'a' * 64,
          total: 1,
          passed: 1,
          failed: 0,
          skipped: 0,
          verifiedAt: DateTime.utc(2026, 7, 15, 10, 1),
        ),
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
          onBackToChat: () {},
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
    );
    await tester.pumpWidget(
      MaterialApp(
        home: FeatureStudioPage(
          draftId: 'draft-a',
          controller: controller,
          onBackToChat: () {},
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
            onBackToChat: () {},
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
          onBackToChat: () => exits++,
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
          onBackToChat: () => exits++,
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
          onBackToChat: () => exits++,
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
          onBackToChat: () => exits++,
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
            onBackToChat: () => exits++,
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
          onBackToChat: () {},
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
            onBackToChat: () {},
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
  Object? loadError;
  bool abortBehaviorSave = false;
  bool controlBehaviorSaves = false;
  bool failNextAccept = false;
  bool failNextReject = false;
  int behaviorSaves = 0;
  int verifications = 0;
  int acceptCalls = 0;
  int rejectCalls = 0;
  final List<_PageBehaviorSaveCall> behaviorSaveCalls = [];
  String? lastGuidance;
  Completer<FeatureStudioSuggestion>? pendingSuggestion;
  Completer<FeatureStudioDraft>? pendingVerification;

  @override
  Future<FeatureStudioDraft> loadDraft(String draftId) async {
    if (loadError case final error?) throw error;
    if (pendingLoad case final pending?) return pending.future;
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
  Future<FeatureStudioDraft> verifyDraft({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioBehavior expectedBehavior,
    required FeatureStudioSource expectedSource,
  }) async {
    verifications++;
    if (pendingVerification case final pending?) return pending.future;
    draft = _draft(
      revision: expectedRevision + Int64.ONE,
      behavior: draft.behavior,
      source: draft.source,
      verification: FeatureStudioVerification(
        releaseDigest: 'a' * 64,
        total: 1,
        passed: 1,
        failed: 0,
        skipped: 0,
        verifiedAt: DateTime.utc(2026, 7, 15, 10, 1),
      ),
    );
    return draft;
  }
}

FeatureStudioDraft _draft({
  Int64? revision,
  FeatureStudioBehavior? behavior,
  FeatureStudioSource? source,
  FeatureStudioVerification? verification,
  FeatureStudioDraftStatus status = FeatureStudioDraftStatus.draft,
}) => FeatureStudioDraft(
  draftId: 'draft-a',
  originatingRequest: const FeatureStudioOriginatingRequest(
    operationId: 'operation-a',
    conversationId: 'conversation-a',
    text: 'Research Acme',
  ),
  goal: 'Create a concise company brief',
  status: status,
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
);

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
