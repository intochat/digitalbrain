import 'dart:async';

import 'package:digitalbrain_flutter/features/studio/feature_studio_controller.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_gateway.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_models.dart';
import 'package:digitalbrain_flutter/runtime/runtime_errors.dart';
import 'package:fixnum/fixnum.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test(
    'autosaves the latest Behavior once after the injected 500ms debounce',
    () async {
      final delay = _ManualDelay();
      final gateway = _ControlledGateway()..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        delay: delay.call,
        idFactory: () => 'save-a',
      );
      await controller.load();

      controller.reviseBehavior(_behavior('First edit'));
      controller.reviseBehavior(_behavior('Latest edit'));

      expect(gateway.behaviorCalls, isEmpty);
      expect(delay.durations, everyElement(const Duration(milliseconds: 500)));
      delay.completeAll();
      await pumpEventQueue();

      expect(gateway.behaviorCalls, hasLength(1));
      expect(
        gateway.behaviorCalls.single.behavior.scenarios.single.name,
        'Latest edit',
      );
      expect(gateway.behaviorCalls.single.expectedRevision, Int64(4));
      expect(gateway.behaviorCalls.single.idempotencyId, 'save-a');
      gateway.behaviorCalls.single.completer.complete(
        _draft(revision: Int64(5), behavior: _behavior('Latest edit')),
      );
      await pumpEventQueue();
      expect(controller.savePhase, FeatureStudioSavePhase.saved);
    },
  );

  test(
    'serializes Behavior before Source and chains reply revisions',
    () async {
      final delay = _ManualDelay();
      final gateway = _ControlledGateway()..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        delay: delay.call,
        idFactory: _SequenceIds().call,
      );
      await controller.load();

      final editedSource = _source('source edit');
      controller.reviseSource(editedSource);
      controller.reviseBehavior(_behavior('Behavior edit'));
      delay.completeAll();
      await pumpEventQueue();

      expect(gateway.behaviorCalls, hasLength(1));
      expect(gateway.sourceCalls, isEmpty);
      expect(
        gateway.behaviorCalls.single.expectedSource.files.last.content,
        'source',
      );
      gateway.behaviorCalls.single.completer.complete(
        _draft(revision: Int64(5), behavior: _behavior('Behavior edit')),
      );
      await pumpEventQueue();

      expect(gateway.sourceCalls, hasLength(1));
      expect(gateway.sourceCalls.single.expectedRevision, Int64(5));
      expect(
        gateway.sourceCalls.single.expectedBehavior.scenarios.single.name,
        'Behavior edit',
      );
      expect(
        gateway.sourceCalls.single.source.files.last.content,
        'source edit',
      );
      expect(
        gateway.sourceCalls.single.idempotencyId,
        isNot(gateway.behaviorCalls.single.idempotencyId),
      );
      gateway.sourceCalls.single.completer.complete(
        _draft(
          revision: Int64(6),
          behavior: _behavior('Behavior edit'),
          source: editedSource,
        ),
      );
      await pumpEventQueue();

      expect(controller.confirmedDraft?.revision, Int64(6));
      expect(controller.savePhase, FeatureStudioSavePhase.saved);
      expect(controller.isDirty, isFalse);
    },
  );

  test(
    'coalesces edits during a save and always drains Behavior first',
    () async {
      final delay = _ManualDelay();
      final gateway = _ControlledGateway()..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        delay: delay.call,
        idFactory: _SequenceIds().call,
      );
      await controller.load();

      controller.reviseBehavior(_behavior('First in flight'));
      delay.completeAll();
      await pumpEventQueue();
      controller.reviseSource(_source('queued source'));
      controller.reviseBehavior(_behavior('Latest queued Behavior'));

      gateway.behaviorCalls.first.completer.complete(
        _draft(revision: Int64(5), behavior: _behavior('First in flight')),
      );
      await pumpEventQueue();

      expect(gateway.behaviorCalls, hasLength(2));
      expect(
        gateway.behaviorCalls.last.behavior.scenarios.single.name,
        'Latest queued Behavior',
      );
      expect(gateway.behaviorCalls.last.expectedRevision, Int64(5));
      expect(gateway.sourceCalls, isEmpty);
      gateway.behaviorCalls.last.completer.complete(
        _draft(
          revision: Int64(6),
          behavior: _behavior('Latest queued Behavior'),
        ),
      );
      await pumpEventQueue();

      expect(gateway.sourceCalls.single.expectedRevision, Int64(6));
      gateway.sourceCalls.single.completer.complete(
        _draft(
          revision: Int64(7),
          behavior: _behavior('Latest queued Behavior'),
          source: _source('queued source'),
        ),
      );
      await pumpEventQueue();
    },
  );

  test('retry reuses the exact failed mutation identity and payload', () async {
    final delay = _ManualDelay();
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      delay: delay.call,
      idFactory: _SequenceIds().call,
    );
    await controller.load();

    controller.reviseBehavior(_behavior('Retry me'));
    delay.completeAll();
    await pumpEventQueue();
    final first = gateway.behaviorCalls.single;
    first.completer.completeError(
      const TransportException(
        TransportErrorCode.unavailable,
        'Studio is temporarily unavailable.',
      ),
    );
    await pumpEventQueue();

    expect(controller.savePhase, FeatureStudioSavePhase.retryableFailure);
    final retry = controller.retrySave();
    await pumpEventQueue();
    final second = gateway.behaviorCalls.last;
    expect(second.expectedRevision, first.expectedRevision);
    expect(second.idempotencyId, first.idempotencyId);
    expect(second.behavior, same(first.behavior));
    second.completer.complete(
      _draft(revision: Int64(5), behavior: _behavior('Retry me')),
    );
    await retry;

    expect(controller.savePhase, FeatureStudioSavePhase.saved);
  });

  test('Aborted preserves edits until explicit keep-local recovery', () async {
    final delay = _ManualDelay();
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      delay: delay.call,
      idFactory: _SequenceIds().call,
    );
    await controller.load();

    controller.reviseBehavior(_behavior('Keep this edit'));
    delay.completeAll();
    await pumpEventQueue();
    final first = gateway.behaviorCalls.single;
    first.completer.completeError(
      const TransportException(
        TransportErrorCode.aborted,
        'Draft changed on the server.',
      ),
    );
    await pumpEventQueue();

    expect(controller.savePhase, FeatureStudioSavePhase.conflict);
    expect(controller.behavior?.scenarios.single.name, 'Keep this edit');
    expect(controller.canVerify, isFalse);
    gateway.loadedDraft = _draft(revision: Int64(8));
    final recovery = controller.resolveConflictKeepingLocalChanges();
    await pumpEventQueue();

    expect(gateway.behaviorCalls, hasLength(2));
    expect(gateway.behaviorCalls.last.expectedRevision, Int64(8));
    expect(
      gateway.behaviorCalls.last.idempotencyId,
      isNot(first.idempotencyId),
    );
    gateway.behaviorCalls.last.completer.complete(
      _draft(revision: Int64(9), behavior: _behavior('Keep this edit')),
    );
    await recovery;

    expect(controller.confirmedDraft?.revision, Int64(9));
    expect(controller.savePhase, FeatureStudioSavePhase.saved);
  });

  test('invalid local aggregates never enter the mutation lane', () async {
    final delay = _ManualDelay();
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      delay: delay.call,
    );
    await controller.load();

    controller.reviseBehavior(FeatureStudioBehavior(scenarios: const []));
    controller.reviseSource(
      FeatureStudioSource(
        implementationProjectPath: 'missing.csproj',
        scenarioProjectPath: 'missing.tests.csproj',
        files: const [],
      ),
    );
    delay.completeAll();
    await pumpEventQueue();

    expect(controller.savePhase, FeatureStudioSavePhase.invalid);
    expect(controller.behaviorErrors, isNotEmpty);
    expect(controller.sourceErrors, isNotEmpty);
    expect(gateway.behaviorCalls, isEmpty);
    expect(gateway.sourceCalls, isEmpty);
  });

  test(
    'suggestions stay review-only and produce a local add/remove patch',
    () async {
      final gateway = _ControlledGateway()..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        idFactory: _SequenceIds().call,
      );
      await controller.load();

      final request = controller.requestSuggestedChange('Add source evidence');
      await pumpEventQueue();
      expect(gateway.suggestionCalls.single.expectedRevision, Int64(4));
      expect(gateway.suggestionCalls.single.guidance, 'Add source evidence');
      gateway.suggestionCalls.single.completer.complete(_suggestion());
      await request;

      expect(controller.suggestionPhase, FeatureStudioSuggestionPhase.ready);
      expect(
        controller.confirmedDraft?.behavior.scenarios.single.name,
        'Create a brief',
      );
      expect(controller.suggestionDiff?.entries, hasLength(6));
      expect(
        controller.suggestionDiff?.entries,
        contains(
          isA<FeatureStudioDiffEntry>()
              .having(
                (entry) => entry.kind,
                'kind',
                FeatureStudioDiffKind.removal,
              )
              .having(
                (entry) => entry.area,
                'area',
                FeatureStudioDiffArea.behavior,
              )
              .having((entry) => entry.identity, 'identity', 'brief'),
        ),
      );
      expect(
        controller.suggestionDiff?.entries,
        contains(
          isA<FeatureStudioDiffEntry>()
              .having(
                (entry) => entry.kind,
                'kind',
                FeatureStudioDiffKind.addition,
              )
              .having((entry) => entry.identity, 'identity', 'brief')
              .having(
                (entry) => entry.value,
                'complete scenario',
                'Scenario name: Create an evidence brief\n'
                    'Given: A company name\n'
                    'When: The Feature runs\n'
                    'Then: A concise brief with evidence is returned',
              ),
        ),
      );
      expect(
        controller.suggestionDiff?.entries,
        contains(
          isA<FeatureStudioDiffEntry>()
              .having(
                (entry) => entry.identity,
                'identity',
                'Feature/Evidence.cs',
              )
              .having(
                (entry) => entry.value,
                'complete source',
                'evidence source',
              ),
        ),
      );
      expect(
        controller.suggestionDiff?.entries,
        contains(
          isA<FeatureStudioDiffEntry>()
              .having(
                (entry) => entry.kind,
                'kind',
                FeatureStudioDiffKind.addition,
              )
              .having(
                (entry) => entry.area,
                'area',
                FeatureStudioDiffArea.source,
              )
              .having(
                (entry) => entry.identity,
                'identity',
                'Feature/Evidence.cs',
              ),
        ),
      );
    },
  );

  test(
    'suggestion diff exposes entrypoint, path-case, and aggregate-order changes',
    () async {
      Future<List<FeatureStudioDiffEntry>> diffFor(
        FeatureStudioSuggestion suggestion, {
        FeatureStudioDraft? draft,
      }) async {
        final gateway = _ControlledGateway()..loadedDraft = draft ?? _draft();
        final controller = FeatureStudioController(
          draftId: 'draft-a',
          gateway: gateway,
          idFactory: _SequenceIds().call,
        );
        await controller.load();
        final request = controller.requestSuggestedChange('Show every change');
        await pumpEventQueue();
        gateway.suggestionCalls.single.completer.complete(suggestion);
        await request;
        return controller.suggestionDiff!.entries;
      }

      final baselineSource = _source();
      final entrypointEntries = await diffFor(
        _exactSuggestion(
          source: FeatureStudioSource(
            implementationProjectPath: baselineSource.scenarioProjectPath,
            scenarioProjectPath: baselineSource.implementationProjectPath,
            files: baselineSource.files,
          ),
        ),
      );
      expect(entrypointEntries.map((entry) => entry.displayLabel).toSet(), {
        'Implementation project',
        'Scenario project',
      });
      expect(entrypointEntries, hasLength(4));

      final caseChangedFiles = baselineSource.files.toList();
      caseChangedFiles[2] = FeatureStudioSourceFile(
        path: 'Feature/FEATURE.cs',
        content: caseChangedFiles[2].content,
      );
      final caseEntries = await diffFor(
        _exactSuggestion(
          source: FeatureStudioSource(
            implementationProjectPath: baselineSource.implementationProjectPath,
            scenarioProjectPath: baselineSource.scenarioProjectPath,
            files: caseChangedFiles,
          ),
        ),
      );
      expect(caseEntries, hasLength(2));
      expect(caseEntries.map((entry) => entry.displayLabel), [
        'Feature/Feature.cs',
        'Feature/FEATURE.cs',
      ]);

      final first = _scenario('first', name: 'First');
      final second = _scenario('second', name: 'Second');
      final orderedBehavior = FeatureStudioBehavior(scenarios: [first, second]);
      final reorderedFiles = baselineSource.files.toList()
        ..[0] = baselineSource.files[1]
        ..[1] = baselineSource.files[0];
      final orderEntries = await diffFor(
        _exactSuggestion(
          behavior: FeatureStudioBehavior(scenarios: [second, first]),
          source: FeatureStudioSource(
            implementationProjectPath: baselineSource.implementationProjectPath,
            scenarioProjectPath: baselineSource.scenarioProjectPath,
            files: reorderedFiles,
          ),
        ),
        draft: _draft(behavior: orderedBehavior),
      );
      expect(orderEntries, hasLength(8));
      expect(
        orderEntries.where(
          (entry) => entry.area == FeatureStudioDiffArea.behavior,
        ),
        hasLength(4),
      );
      expect(
        orderEntries.where(
          (entry) => entry.area == FeatureStudioDiffArea.source,
        ),
        hasLength(4),
      );
    },
  );

  test('editing makes a suggestion stale and prevents acceptance', () async {
    final delay = _ManualDelay();
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      delay: delay.call,
      idFactory: _SequenceIds().call,
    );
    await controller.load();
    final request = controller.requestSuggestedChange('Add source evidence');
    await pumpEventQueue();
    gateway.suggestionCalls.single.completer.complete(_suggestion());
    await request;

    controller.reviseBehavior(_behavior('Local edit'));
    await controller.acceptSuggestedChange();

    expect(controller.suggestionPhase, FeatureStudioSuggestionPhase.stale);
    expect(controller.canAcceptSuggestion, isFalse);
    expect(gateway.acceptCalls, isEmpty);
  });

  test(
    'accept and reject use exact patch coordinates and revision semantics',
    () async {
      final acceptGateway = _ControlledGateway()..loadedDraft = _draft();
      final acceptController = FeatureStudioController(
        draftId: 'draft-a',
        gateway: acceptGateway,
        idFactory: _SequenceIds().call,
      );
      await acceptController.load();
      final acceptRequest = acceptController.requestSuggestedChange(
        'Improve it',
      );
      await pumpEventQueue();
      acceptGateway.suggestionCalls.single.completer.complete(_suggestion());
      await acceptRequest;

      final acceptance = acceptController.acceptSuggestedChange();
      await pumpEventQueue();
      expect(acceptGateway.acceptCalls.single.expectedRevision, Int64(4));
      expect(
        acceptGateway.acceptCalls.single.suggestion,
        same(_lastSuggestion),
      );
      acceptGateway.acceptCalls.single.completer.complete(
        _draft(
          revision: Int64(5),
          behavior: _lastSuggestion.replacementBehavior,
          source: _lastSuggestion.replacementSource,
        ),
      );
      await acceptance;
      expect(acceptController.confirmedDraft?.revision, Int64(5));
      expect(
        acceptController.suggestionPhase,
        FeatureStudioSuggestionPhase.idle,
      );

      final rejectGateway = _ControlledGateway()..loadedDraft = _draft();
      final rejectController = FeatureStudioController(
        draftId: 'draft-a',
        gateway: rejectGateway,
        idFactory: _SequenceIds().call,
      );
      await rejectController.load();
      final rejectRequest = rejectController.requestSuggestedChange(
        'Improve it',
      );
      await pumpEventQueue();
      rejectGateway.suggestionCalls.single.completer.complete(_suggestion());
      await rejectRequest;
      final rejection = rejectController.rejectSuggestedChange();
      await pumpEventQueue();
      expect(rejectGateway.rejectCalls.single.expectedRevision, Int64(4));
      expect(
        rejectGateway.rejectCalls.single.suggestion.baseRevision,
        Int64(4),
      );
      rejectGateway.rejectCalls.single.completer.complete(_draft());
      await rejection;
      expect(rejectController.confirmedDraft?.revision, Int64(4));
      expect(
        rejectController.suggestionPhase,
        FeatureStudioSuggestionPhase.idle,
      );
    },
  );

  test('Reject preserves Passed verification for an exact echo', () async {
    final verification = _verification();
    final gateway = _ControlledGateway()
      ..loadedDraft = _draft(verification: verification);
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _SequenceIds().call,
    );
    await controller.load();
    final request = controller.requestSuggestedChange('Improve it');
    await pumpEventQueue();
    gateway.suggestionCalls.single.completer.complete(_suggestion());
    await request;

    final rejection = controller.rejectSuggestedChange();
    await pumpEventQueue();
    gateway.rejectCalls.single.completer.complete(
      _draft(verification: verification),
    );
    await rejection;

    expect(controller.verificationPhase, FeatureStudioVerificationPhase.passed);
    expect(controller.verification, same(verification));
  });

  test('no-op Accept stales prior Passed verification', () async {
    final gateway = _ControlledGateway()
      ..loadedDraft = _draft(verification: _verification());
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _SequenceIds().call,
    );
    await controller.load();
    final request = controller.requestSuggestedChange('Keep the aggregates');
    await pumpEventQueue();
    gateway.suggestionCalls.single.completer.complete(_exactSuggestion());
    await request;

    final acceptance = controller.acceptSuggestedChange();
    await pumpEventQueue();
    gateway.acceptCalls.single.completer.complete(_draft(revision: Int64(5)));
    await acceptance;

    expect(controller.verificationPhase, FeatureStudioVerificationPhase.stale);
  });

  test(
    'suggestion retry keeps its intent identity but accepts a new result',
    () async {
      final gateway = _ControlledGateway()..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        idFactory: _SequenceIds().call,
      );
      await controller.load();

      final firstRequest = controller.requestSuggestedChange('Improve it');
      await pumpEventQueue();
      final first = gateway.suggestionCalls.single;
      first.completer.completeError(
        const TransportException(
          TransportErrorCode.unavailable,
          'Studio is temporarily unavailable.',
        ),
      );
      await firstRequest;
      expect(
        controller.suggestionPhase,
        FeatureStudioSuggestionPhase.retryableFailure,
      );
      expect(controller.canRequestSuggestion, isFalse);
      await controller.requestSuggestedChange('Start a new intent');
      expect(gateway.suggestionCalls, hasLength(1));

      final retry = controller.retrySuggestedChangeRequest();
      await pumpEventQueue();
      final second = gateway.suggestionCalls.last;
      expect(second.expectedRevision, first.expectedRevision);
      expect(second.guidance, first.guidance);
      expect(second.suggestionId, first.suggestionId);
      final differentResult = _suggestion(summary: 'A different model result');
      second.completer.complete(differentResult);
      await retry;

      expect(controller.suggestion, same(differentResult));
      expect(controller.suggestionPhase, FeatureStudioSuggestionPhase.ready);
    },
  );

  test('Verify is enabled only for a clean mutable valid Draft', () async {
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
    );
    await controller.load();
    expect(controller.canVerify, isTrue);

    final suggestionRequest = controller.requestSuggestedChange('Improve it');
    await pumpEventQueue();
    expect(controller.canVerify, isFalse);
    gateway.suggestionCalls.single.completer.complete(_suggestion());
    await suggestionRequest;

    final installedGateway = _ControlledGateway()
      ..loadedDraft = _draft(status: FeatureStudioDraftStatus.installed);
    final installedController = FeatureStudioController(
      draftId: 'draft-a',
      gateway: installedGateway,
    );
    await installedController.load();
    expect(installedController.canVerify, isFalse);

    final invalidGateway = _ControlledGateway()
      ..loadedDraft = _draft(
        source: FeatureStudioSource(
          implementationProjectPath: 'missing.csproj',
          scenarioProjectPath: 'missing.tests.csproj',
          files: const [],
        ),
      );
    final invalidController = FeatureStudioController(
      draftId: 'draft-a',
      gateway: invalidGateway,
    );
    await invalidController.load();
    expect(invalidController.canVerify, isFalse);
  });

  test('installed Drafts reject every authoring mutation', () async {
    final delay = _ManualDelay();
    final gateway = _ControlledGateway()
      ..loadedDraft = _draft(status: FeatureStudioDraftStatus.installed);
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      delay: delay.call,
    );
    await controller.load();

    controller.reviseBehavior(_behavior('Forbidden edit'));
    controller.reviseSource(_source('forbidden source'));
    await controller.requestSuggestedChange('Forbidden suggestion');
    delay.completeAll();
    await pumpEventQueue();

    expect(controller.isMutableDraft, isFalse);
    expect(controller.isDirty, isFalse);
    expect(controller.canRequestSuggestion, isFalse);
    expect(gateway.behaviorCalls, isEmpty);
    expect(gateway.sourceCalls, isEmpty);
    expect(gateway.suggestionCalls, isEmpty);
  });

  test('suggestion guidance is trimmed and rejects C1 controls', () async {
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _SequenceIds().call,
    );
    await controller.load();

    final request = controller.requestSuggestedChange('  Improve it  ');
    await pumpEventQueue();
    expect(gateway.suggestionCalls.single.guidance, 'Improve it');
    gateway.suggestionCalls.single.completer.complete(_suggestion());
    await request;

    await controller.requestSuggestedChange('Bad\u0085guidance');
    expect(gateway.suggestionCalls, hasLength(1));
  });

  test('terminal transport failures are permanent in every workflow', () async {
    const denied = TransportException(
      TransportErrorCode.permissionDenied,
      'This action is not permitted.',
    );

    final saveDelay = _ManualDelay();
    final saveGateway = _ControlledGateway()..loadedDraft = _draft();
    final saveController = FeatureStudioController(
      draftId: 'draft-a',
      gateway: saveGateway,
      delay: saveDelay.call,
    );
    await saveController.load();
    saveController.reviseBehavior(_behavior('Denied edit'));
    saveDelay.completeAll();
    await pumpEventQueue();
    saveGateway.behaviorCalls.single.completer.completeError(denied);
    await pumpEventQueue();
    expect(saveController.savePhase, FeatureStudioSavePhase.failed);
    await saveController.retrySave();
    expect(saveGateway.behaviorCalls, hasLength(1));

    final suggestionGateway = _ControlledGateway()..loadedDraft = _draft();
    final suggestionController = FeatureStudioController(
      draftId: 'draft-a',
      gateway: suggestionGateway,
    );
    await suggestionController.load();
    final suggestion = suggestionController.requestSuggestedChange('Improve');
    await pumpEventQueue();
    suggestionGateway.suggestionCalls.single.completer.completeError(denied);
    await suggestion;
    expect(
      suggestionController.suggestionPhase,
      FeatureStudioSuggestionPhase.failed,
    );
    await suggestionController.retrySuggestedChangeRequest();
    expect(suggestionGateway.suggestionCalls, hasLength(1));

    final decisionGateway = _ControlledGateway()..loadedDraft = _draft();
    final decisionController = FeatureStudioController(
      draftId: 'draft-a',
      gateway: decisionGateway,
    );
    await decisionController.load();
    final decisionSuggestion = decisionController.requestSuggestedChange(
      'Improve',
    );
    await pumpEventQueue();
    decisionGateway.suggestionCalls.single.completer.complete(_suggestion());
    await decisionSuggestion;
    final decision = decisionController.acceptSuggestedChange();
    await pumpEventQueue();
    decisionGateway.acceptCalls.single.completer.completeError(denied);
    await decision;
    expect(
      decisionController.suggestionPhase,
      FeatureStudioSuggestionPhase.failed,
    );
    await decisionController.retrySuggestedChangeDecision();
    expect(decisionGateway.acceptCalls, hasLength(1));

    final verifyGateway = _ControlledGateway()..loadedDraft = _draft();
    final verifyController = FeatureStudioController(
      draftId: 'draft-a',
      gateway: verifyGateway,
    );
    await verifyController.load();
    final verification = verifyController.verify();
    await pumpEventQueue();
    verifyGateway.verifyCalls.single.completer.completeError(denied);
    await verification;
    expect(
      verifyController.verificationPhase,
      FeatureStudioVerificationPhase.failed,
    );
    await verifyController.retryVerification();
    expect(verifyGateway.verifyCalls, hasLength(1));
  });

  test('Aborted suggestion requests become stale without a retry', () async {
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
    );
    await controller.load();

    final request = controller.requestSuggestedChange('Improve');
    await pumpEventQueue();
    gateway.suggestionCalls.single.completer.completeError(
      const TransportException(
        TransportErrorCode.aborted,
        'Draft changed on the server.',
      ),
    );
    await request;

    expect(controller.suggestionPhase, FeatureStudioSuggestionPhase.stale);
    await controller.retrySuggestedChangeRequest();
    expect(gateway.suggestionCalls, hasLength(1));
  });

  test(
    'Aborted Accept reapplies the accepted replacement after reload',
    () async {
      final gateway = _ControlledGateway()..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        idFactory: _SequenceIds().call,
      );
      await controller.load();
      final request = controller.requestSuggestedChange('Improve');
      await pumpEventQueue();
      gateway.suggestionCalls.single.completer.complete(_suggestion());
      await request;

      final acceptance = controller.acceptSuggestedChange();
      await pumpEventQueue();
      gateway.acceptCalls.single.completer.completeError(
        const TransportException(
          TransportErrorCode.aborted,
          'Draft changed on the server.',
        ),
      );
      await acceptance;
      expect(controller.hasConflict, isTrue);

      gateway.loadedDraft = _draft(revision: Int64(8));
      final recovery = controller.resolveConflictKeepingLocalChanges();
      await pumpEventQueue();
      expect(gateway.behaviorCalls, hasLength(1));
      gateway.behaviorCalls.single.completer.complete(
        _draft(
          revision: Int64(9),
          behavior: _lastSuggestion.replacementBehavior,
        ),
      );
      await pumpEventQueue();
      expect(gateway.sourceCalls, hasLength(1));
      gateway.sourceCalls.single.completer.complete(
        _draft(
          revision: Int64(10),
          behavior: _lastSuggestion.replacementBehavior,
          source: _lastSuggestion.replacementSource,
        ),
      );
      await recovery;

      expect(controller.hasConflict, isFalse);
      expect(controller.savePhase, FeatureStudioSavePhase.saved);
      expect(controller.behavior, _lastSuggestion.replacementBehavior);
      expect(controller.source, _lastSuggestion.replacementSource);
    },
  );

  test('Aborted Verify reloads and verifies the current revision', () async {
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _SequenceIds().call,
    );
    await controller.load();

    final firstVerification = controller.verify();
    await pumpEventQueue();
    gateway.verifyCalls.single.completer.completeError(
      const TransportException(
        TransportErrorCode.aborted,
        'Draft changed on the server.',
      ),
    );
    await firstVerification;
    expect(controller.hasConflict, isTrue);

    gateway.loadedDraft = _draft(revision: Int64(8));
    final recovery = controller.resolveConflictKeepingLocalChanges();
    await pumpEventQueue();
    expect(gateway.verifyCalls, hasLength(2));
    expect(gateway.verifyCalls.last.expectedRevision, Int64(8));
    gateway.verifyCalls.last.completer.complete(
      _draft(revision: Int64(9), verification: _verification()),
    );
    await recovery;

    expect(controller.hasConflict, isFalse);
    expect(controller.verificationPhase, FeatureStudioVerificationPhase.passed);
  });

  test(
    'Verify advances the Draft and keeps only the safe aggregate result',
    () async {
      final gateway = _ControlledGateway()..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        idFactory: _SequenceIds().call,
      );
      await controller.load();

      final verification = controller.verify();
      await pumpEventQueue();
      expect(gateway.verifyCalls.single.expectedRevision, Int64(4));
      expect(gateway.verifyCalls.single.idempotencyId, 'studio-id-1');
      gateway.verifyCalls.single.completer.complete(
        _draft(revision: Int64(5), verification: _verification()),
      );
      await verification;

      expect(controller.confirmedDraft?.revision, Int64(5));
      expect(
        controller.verificationPhase,
        FeatureStudioVerificationPhase.passed,
      );
      expect(controller.verification?.passed, 1);
    },
  );

  test('FailedPrecondition is a test failure, never a save conflict', () async {
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
    );
    await controller.load();

    final verification = controller.verify();
    await pumpEventQueue();
    gateway.verifyCalls.single.completer.completeError(
      const PreconditionException('Verification did not pass.'),
    );
    await verification;

    expect(
      controller.verificationPhase,
      FeatureStudioVerificationPhase.failedTests,
    );
    expect(controller.savePhase, FeatureStudioSavePhase.saved);
    expect(controller.hasConflict, isFalse);
  });

  test(
    'edits during Verify stale its result and save from the reply revision',
    () async {
      final delay = _ManualDelay();
      final gateway = _ControlledGateway()..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        delay: delay.call,
        idFactory: _SequenceIds().call,
      );
      await controller.load();

      final verification = controller.verify();
      await pumpEventQueue();
      final editedSource = _source('edited during Verify');
      controller.reviseSource(editedSource);
      expect(gateway.sourceCalls, isEmpty);
      gateway.verifyCalls.single.completer.complete(
        _draft(revision: Int64(5), verification: _verification()),
      );
      await pumpEventQueue();

      expect(
        controller.verificationPhase,
        FeatureStudioVerificationPhase.stale,
      );
      expect(controller.source?.files.last.content, 'edited during Verify');
      expect(gateway.sourceCalls.single.expectedRevision, Int64(5));
      gateway.sourceCalls.single.completer.complete(
        _draft(revision: Int64(6), source: editedSource),
      );
      await verification;

      expect(controller.confirmedDraft?.revision, Int64(6));
      expect(controller.savePhase, FeatureStudioSavePhase.saved);
    },
  );

  test(
    'Verify retry preserves its exact expected revision and identity',
    () async {
      final gateway = _ControlledGateway()..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        idFactory: _SequenceIds().call,
      );
      await controller.load();

      final firstVerification = controller.verify();
      await pumpEventQueue();
      final first = gateway.verifyCalls.single;
      first.completer.completeError(
        const TransportException(
          TransportErrorCode.unavailable,
          'Studio is temporarily unavailable.',
        ),
      );
      await firstVerification;
      expect(controller.canVerify, isFalse);
      await controller.verify();
      expect(gateway.verifyCalls, hasLength(1));
      final retry = controller.retryVerification();
      await pumpEventQueue();
      final second = gateway.verifyCalls.last;
      expect(second.expectedRevision, first.expectedRevision);
      expect(second.idempotencyId, first.idempotencyId);
      second.completer.complete(
        _draft(revision: Int64(5), verification: _verification()),
      );
      await retry;

      expect(
        controller.verificationPhase,
        FeatureStudioVerificationPhase.passed,
      );
    },
  );

  test('load failures distinguish retry, terminal, and shared auth', () async {
    Future<FeatureStudioLoadPhase> phaseFor(TransportException error) async {
      final gateway = _ControlledGateway()
        ..loadError = error
        ..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
      );
      await controller.load();
      return controller.loadPhase;
    }

    expect(
      await phaseFor(
        const TransportException(
          TransportErrorCode.unavailable,
          'Temporarily unavailable.',
        ),
      ),
      FeatureStudioLoadPhase.retryableFailure,
    );
    expect(
      await phaseFor(
        const TransportException(
          TransportErrorCode.permissionDenied,
          'Not permitted.',
        ),
      ),
      FeatureStudioLoadPhase.terminalFailure,
    );
    expect(
      await phaseFor(const AuthenticationException()),
      FeatureStudioLoadPhase.authenticationRequired,
    );
  });

  test(
    'conflict recovery serializes reload and ignores edits while pending',
    () async {
      final delay = _ManualDelay();
      final gateway = _ControlledGateway()..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        delay: delay.call,
        idFactory: _SequenceIds().call,
      );
      await controller.load();
      controller.reviseBehavior(_behavior('Keep this edit'));
      delay.completeAll();
      await pumpEventQueue();
      gateway.behaviorCalls.single.completer.completeError(
        const TransportException(
          TransportErrorCode.aborted,
          'Draft changed on the server.',
        ),
      );
      await pumpEventQueue();

      gateway.pendingLoad = Completer<FeatureStudioDraft>();
      final recovery = controller.resolveConflictKeepingLocalChanges();
      await pumpEventQueue();
      expect(controller.conflictRecoveryInFlight, isTrue);
      expect(gateway.loadCalls, 2);
      controller.reviseBehavior(_behavior('Must not race'));
      controller.reviseSource(_source('Must not race'));
      await controller.resolveConflictKeepingLocalChanges();
      expect(gateway.loadCalls, 2);
      expect(controller.behavior?.scenarios.single.name, 'Keep this edit');
      expect(controller.source?.files.last.content, 'source');

      gateway.pendingLoad!.complete(_draft(revision: Int64(8)));
      await pumpEventQueue();
      gateway.pendingLoad = null;
      expect(gateway.behaviorCalls, hasLength(2));
      gateway.behaviorCalls.last.completer.complete(
        _draft(revision: Int64(9), behavior: _behavior('Keep this edit')),
      );
      await recovery;

      expect(controller.conflictRecoveryInFlight, isFalse);
      expect(controller.behavior?.scenarios.single.name, 'Keep this edit');
      expect(controller.source?.files.last.content, 'source');
    },
  );

  test('local edits invalidate every retry intent before autosave', () async {
    final generationDelay = _ManualDelay();
    final generationGateway = _ControlledGateway()..loadedDraft = _draft();
    final generationController = FeatureStudioController(
      draftId: 'draft-a',
      gateway: generationGateway,
      delay: generationDelay.call,
    );
    await generationController.load();
    final generation = generationController.requestSuggestedChange('Improve');
    await pumpEventQueue();
    generationGateway.suggestionCalls.single.completer.completeError(
      const TransportException(
        TransportErrorCode.unavailable,
        'Temporarily unavailable.',
      ),
    );
    await generation;
    generationController.reviseBehavior(_behavior('Edited after failure'));
    expect(
      generationController.suggestionPhase,
      FeatureStudioSuggestionPhase.stale,
    );
    await generationController.retrySuggestedChange();
    expect(generationGateway.suggestionCalls, hasLength(1));

    final decisionDelay = _ManualDelay();
    final decisionGateway = _ControlledGateway()..loadedDraft = _draft();
    final decisionController = FeatureStudioController(
      draftId: 'draft-a',
      gateway: decisionGateway,
      delay: decisionDelay.call,
    );
    await decisionController.load();
    final suggestion = decisionController.requestSuggestedChange('Improve');
    await pumpEventQueue();
    decisionGateway.suggestionCalls.single.completer.complete(_suggestion());
    await suggestion;
    final decision = decisionController.acceptSuggestedChange();
    await pumpEventQueue();
    decisionGateway.acceptCalls.single.completer.completeError(
      const TransportException(
        TransportErrorCode.unavailable,
        'Temporarily unavailable.',
      ),
    );
    await decision;
    decisionController.reviseBehavior(_behavior('Edited after decision'));
    expect(
      decisionController.suggestionPhase,
      FeatureStudioSuggestionPhase.stale,
    );
    await decisionController.retrySuggestedChange();
    expect(decisionGateway.acceptCalls, hasLength(1));

    final verifyDelay = _ManualDelay();
    final verifyGateway = _ControlledGateway()..loadedDraft = _draft();
    final verifyController = FeatureStudioController(
      draftId: 'draft-a',
      gateway: verifyGateway,
      delay: verifyDelay.call,
    );
    await verifyController.load();
    final verification = verifyController.verify();
    await pumpEventQueue();
    verifyGateway.verifyCalls.single.completer.completeError(
      const TransportException(
        TransportErrorCode.unavailable,
        'Temporarily unavailable.',
      ),
    );
    await verification;
    verifyController.reviseSource(_source('Edited after Verify'));
    expect(
      verifyController.verificationPhase,
      FeatureStudioVerificationPhase.stale,
    );
    await verifyController.retryVerification();
    expect(verifyGateway.verifyCalls, hasLength(1));
  });

  test(
    'uncertain save replays its exact intent before net-zero compensation',
    () async {
      final delay = _ManualDelay();
      final gateway = _ControlledGateway()..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        delay: delay.call,
        idFactory: _SequenceIds().call,
      );
      await controller.load();
      final uncertainBehavior = _behavior('Possibly stored');
      final originalBehavior = _behavior('Create a brief');

      controller.reviseBehavior(uncertainBehavior);
      delay.completeAll();
      await pumpEventQueue();
      final first = gateway.behaviorCalls.single;
      first.completer.completeError(
        const TransportException(
          TransportErrorCode.unavailable,
          'The save outcome is unknown.',
        ),
      );
      await pumpEventQueue();

      controller.reviseBehavior(originalBehavior);
      expect(controller.isDirty, isFalse);
      expect(controller.savePhase, FeatureStudioSavePhase.retryableFailure);

      final recovery = controller.retrySave();
      await pumpEventQueue();
      final replay = gateway.behaviorCalls[1];
      expect(replay.expectedRevision, first.expectedRevision);
      expect(replay.idempotencyId, first.idempotencyId);
      expect(replay.behavior.scenarios.single.name, 'Possibly stored');
      replay.completer.complete(
        _draft(revision: Int64(5), behavior: uncertainBehavior),
      );
      await pumpEventQueue();

      final compensation = gateway.behaviorCalls[2];
      expect(compensation.expectedRevision, Int64(5));
      expect(compensation.idempotencyId, isNot(first.idempotencyId));
      expect(compensation.behavior.scenarios.single.name, 'Create a brief');
      compensation.completer.complete(
        _draft(revision: Int64(6), behavior: originalBehavior),
      );
      await recovery;

      expect(controller.confirmedDraft?.revision, Int64(6));
      expect(controller.isDirty, isFalse);
      expect(controller.savePhase, FeatureStudioSavePhase.saved);
    },
  );

  test('Aborted Verify recovery preserves edits made after request', () async {
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _SequenceIds().call,
    );
    await controller.load();

    final verification = controller.verify();
    await pumpEventQueue();
    final editedSource = _source('edited while Verify ran');
    controller.reviseSource(editedSource);
    gateway.verifyCalls.single.completer.completeError(
      const TransportException(
        TransportErrorCode.aborted,
        'Draft changed on the server.',
      ),
    );
    await verification;

    gateway.loadedDraft = _draft(revision: Int64(8), source: _source('server'));
    final recovery = controller.resolveConflictKeepingLocalChanges();
    await pumpEventQueue();

    expect(controller.source?.files.last.content, 'edited while Verify ran');
    expect(gateway.verifyCalls, hasLength(1));
    expect(gateway.sourceCalls, hasLength(1));
    expect(gateway.sourceCalls.single.expectedRevision, Int64(8));
    gateway.sourceCalls.single.completer.complete(
      _draft(revision: Int64(9), source: editedSource),
    );
    await recovery;

    expect(controller.verificationPhase, FeatureStudioVerificationPhase.stale);
    expect(controller.savePhase, FeatureStudioSavePhase.saved);
  });

  test('Aborted Accept recovery preserves edits made after decision', () async {
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _SequenceIds().call,
    );
    await controller.load();
    final request = controller.requestSuggestedChange('Improve');
    await pumpEventQueue();
    gateway.suggestionCalls.single.completer.complete(_suggestion());
    await request;

    final acceptance = controller.acceptSuggestedChange();
    await pumpEventQueue();
    final editedBehavior = _behavior('Edited while Accept ran');
    controller.reviseBehavior(editedBehavior);
    gateway.acceptCalls.single.completer.completeError(
      const TransportException(
        TransportErrorCode.aborted,
        'Draft changed on the server.',
      ),
    );
    await acceptance;

    gateway.loadedDraft = _draft(
      revision: Int64(8),
      verification: _verification(),
    );
    final recovery = controller.resolveConflictKeepingLocalChanges();
    await pumpEventQueue();

    expect(gateway.behaviorCalls, hasLength(1));
    expect(
      gateway.behaviorCalls.single.behavior.scenarios.single.name,
      'Edited while Accept ran',
    );
    gateway.behaviorCalls.single.completer.complete(
      _draft(revision: Int64(9), behavior: editedBehavior),
    );
    await pumpEventQueue();
    expect(gateway.sourceCalls, hasLength(1));
    expect(
      gateway.sourceCalls.single.source,
      _lastSuggestion.replacementSource,
    );
    gateway.sourceCalls.single.completer.complete(
      _draft(
        revision: Int64(10),
        behavior: editedBehavior,
        source: _lastSuggestion.replacementSource,
      ),
    );
    await recovery;

    expect(
      controller.behavior?.scenarios.single.name,
      'Edited while Accept ran',
    );
    expect(controller.source, _lastSuggestion.replacementSource);
    expect(controller.suggestionPhase, FeatureStudioSuggestionPhase.stale);
    expect(controller.verificationPhase, FeatureStudioVerificationPhase.stale);
  });

  test('Aborted Reject recovery preserves edits made after decision', () async {
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _SequenceIds().call,
    );
    await controller.load();
    final request = controller.requestSuggestedChange('Improve');
    await pumpEventQueue();
    gateway.suggestionCalls.single.completer.complete(_suggestion());
    await request;

    final rejection = controller.rejectSuggestedChange();
    await pumpEventQueue();
    final editedSource = _source('edited while Reject ran');
    controller.reviseSource(editedSource);
    gateway.rejectCalls.single.completer.completeError(
      const TransportException(
        TransportErrorCode.aborted,
        'Draft changed on the server.',
      ),
    );
    await rejection;

    gateway.loadedDraft = _draft(
      revision: Int64(8),
      verification: _verification(),
    );
    final recovery = controller.resolveConflictKeepingLocalChanges();
    await pumpEventQueue();

    expect(controller.source?.files.last.content, 'edited while Reject ran');
    expect(gateway.sourceCalls, hasLength(1));
    gateway.sourceCalls.single.completer.complete(
      _draft(revision: Int64(9), source: editedSource),
    );
    await recovery;

    expect(controller.suggestionPhase, FeatureStudioSuggestionPhase.stale);
    expect(controller.savePhase, FeatureStudioSavePhase.saved);
    expect(controller.verificationPhase, FeatureStudioVerificationPhase.stale);
  });

  test(
    'transient Accept failure drains edits and clears stale retry',
    () async {
      final gateway = _ControlledGateway()..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        idFactory: _SequenceIds().call,
      );
      await controller.load();
      final request = controller.requestSuggestedChange('Improve');
      await pumpEventQueue();
      gateway.suggestionCalls.single.completer.complete(_suggestion());
      await request;

      final acceptance = controller.acceptSuggestedChange();
      await pumpEventQueue();
      final editedBehavior = _behavior('Edited after Accept started');
      controller.reviseBehavior(editedBehavior);
      gateway.acceptCalls.single.completer.completeError(
        const TransportException(
          TransportErrorCode.unavailable,
          'Studio is temporarily unavailable.',
        ),
      );
      await pumpEventQueue();

      expect(controller.hasRetryableSuggestionDecision, isFalse);
      expect(controller.suggestionPhase, FeatureStudioSuggestionPhase.stale);
      expect(gateway.behaviorCalls, hasLength(1));
      gateway.behaviorCalls.single.completer.complete(
        _draft(revision: Int64(5), behavior: editedBehavior),
      );
      await acceptance;
      await controller.retrySuggestedChangeDecision();

      expect(gateway.acceptCalls, hasLength(1));
      expect(controller.savePhase, FeatureStudioSavePhase.saved);
    },
  );

  test(
    'transient Reject failure drains edits and clears stale retry',
    () async {
      final gateway = _ControlledGateway()..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        idFactory: _SequenceIds().call,
      );
      await controller.load();
      final request = controller.requestSuggestedChange('Improve');
      await pumpEventQueue();
      gateway.suggestionCalls.single.completer.complete(_suggestion());
      await request;

      final rejection = controller.rejectSuggestedChange();
      await pumpEventQueue();
      final editedSource = _source('Edited after Reject started');
      controller.reviseSource(editedSource);
      gateway.rejectCalls.single.completer.completeError(
        const TransportException(
          TransportErrorCode.unavailable,
          'Studio is temporarily unavailable.',
        ),
      );
      await pumpEventQueue();

      expect(controller.hasRetryableSuggestionDecision, isFalse);
      expect(controller.suggestionPhase, FeatureStudioSuggestionPhase.stale);
      expect(gateway.sourceCalls, hasLength(1));
      gateway.sourceCalls.single.completer.complete(
        _draft(revision: Int64(5), source: editedSource),
      );
      await rejection;
      await controller.retrySuggestedChangeDecision();

      expect(gateway.rejectCalls, hasLength(1));
      expect(controller.savePhase, FeatureStudioSavePhase.saved);
    },
  );

  test(
    'transient Verify failure drains edits and clears stale retry',
    () async {
      final gateway = _ControlledGateway()..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        idFactory: _SequenceIds().call,
      );
      await controller.load();

      final verification = controller.verify();
      await pumpEventQueue();
      final editedSource = _source('Edited after Verify started');
      controller.reviseSource(editedSource);
      gateway.verifyCalls.single.completer.completeError(
        const TransportException(
          TransportErrorCode.unavailable,
          'Studio is temporarily unavailable.',
        ),
      );
      await pumpEventQueue();

      expect(
        controller.verificationPhase,
        FeatureStudioVerificationPhase.stale,
      );
      expect(gateway.sourceCalls, hasLength(1));
      gateway.sourceCalls.single.completer.complete(
        _draft(revision: Int64(5), source: editedSource),
      );
      await verification;
      await controller.retryVerification();

      expect(gateway.verifyCalls, hasLength(1));
      expect(controller.savePhase, FeatureStudioSavePhase.saved);
    },
  );

  test(
    'transient Accept after edit and revert replays exactly then compensates',
    () async {
      final gateway = _ControlledGateway()..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        idFactory: _SequenceIds().call,
      );
      await controller.load();
      final request = controller.requestSuggestedChange('Improve');
      await pumpEventQueue();
      gateway.suggestionCalls.single.completer.complete(_suggestion());
      await request;

      final acceptance = controller.acceptSuggestedChange();
      await pumpEventQueue();
      final first = gateway.acceptCalls.single;
      controller.reviseBehavior(_behavior('Temporary Accept edit'));
      controller.reviseBehavior(_behavior('Create a brief'));
      expect(controller.isDirty, isFalse);
      first.completer.completeError(
        const TransportException(
          TransportErrorCode.unavailable,
          'The Accept outcome is unknown.',
        ),
      );
      await acceptance;

      expect(controller.hasRetryableSuggestionDecision, isTrue);
      expect(
        controller.suggestionPhase,
        FeatureStudioSuggestionPhase.retryableFailure,
      );
      final retry = controller.retrySuggestedChangeDecision();
      await pumpEventQueue();
      final replay = gateway.acceptCalls.last;
      expect(replay.expectedRevision, first.expectedRevision);
      expect(replay.idempotencyId, first.idempotencyId);
      replay.completer.complete(
        _draft(
          revision: Int64(5),
          behavior: _lastSuggestion.replacementBehavior,
          source: _lastSuggestion.replacementSource,
        ),
      );
      await pumpEventQueue();

      expect(gateway.acceptCalls, hasLength(2));
      expect(controller.confirmedDraft?.revision, Int64(5));
      expect(controller.behaviorDirty, isTrue);
      expect(controller.sourceDirty, isFalse);
      expect(controller.behaviorErrors, isEmpty);
      expect(controller.sourceErrors, isEmpty);
      expect(controller.savePhase, FeatureStudioSavePhase.saving);
      await pumpEventQueue();
      expect(gateway.behaviorCalls, hasLength(1));
      gateway.behaviorCalls.single.completer.complete(
        _draft(
          revision: Int64(6),
          behavior: _behavior('Create a brief'),
          source: _lastSuggestion.replacementSource,
        ),
      );
      await retry;

      expect(controller.confirmedDraft?.revision, Int64(6));
      expect(controller.behavior?.scenarios.single.name, 'Create a brief');
      expect(controller.source, _lastSuggestion.replacementSource);
      expect(gateway.sourceCalls, isEmpty);
      expect(controller.savePhase, FeatureStudioSavePhase.saved);
      expect(controller.hasRetryableSuggestionDecision, isFalse);
    },
  );

  test('transient Reject after edit and revert replays exactly', () async {
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _SequenceIds().call,
    );
    await controller.load();
    final request = controller.requestSuggestedChange('Improve');
    await pumpEventQueue();
    gateway.suggestionCalls.single.completer.complete(_suggestion());
    await request;

    final rejection = controller.rejectSuggestedChange();
    await pumpEventQueue();
    final first = gateway.rejectCalls.single;
    controller.reviseSource(_source('Temporary Reject edit'));
    controller.reviseSource(_source());
    expect(controller.isDirty, isFalse);
    first.completer.completeError(
      const TransportException(
        TransportErrorCode.unavailable,
        'The Reject outcome is unknown.',
      ),
    );
    await rejection;

    expect(controller.hasRetryableSuggestionDecision, isTrue);
    expect(
      controller.suggestionPhase,
      FeatureStudioSuggestionPhase.retryableFailure,
    );
    final retry = controller.retrySuggestedChangeDecision();
    await pumpEventQueue();
    final replay = gateway.rejectCalls.last;
    expect(replay.expectedRevision, first.expectedRevision);
    expect(replay.idempotencyId, first.idempotencyId);
    replay.completer.complete(_draft(revision: Int64(5)));
    await retry;

    expect(controller.confirmedDraft?.revision, Int64(5));
    expect(gateway.behaviorCalls, isEmpty);
    expect(gateway.sourceCalls, isEmpty);
    expect(controller.hasRetryableSuggestionDecision, isFalse);
  });

  test('transient Verify after edit and revert replays exactly', () async {
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _SequenceIds().call,
    );
    await controller.load();

    final verification = controller.verify();
    await pumpEventQueue();
    final first = gateway.verifyCalls.single;
    controller.reviseSource(_source('Temporary Verify edit'));
    controller.reviseSource(_source());
    expect(controller.isDirty, isFalse);
    first.completer.completeError(
      const TransportException(
        TransportErrorCode.unavailable,
        'The Verify outcome is unknown.',
      ),
    );
    await verification;

    expect(
      controller.verificationPhase,
      FeatureStudioVerificationPhase.retryableFailure,
    );
    final retry = controller.retryVerification();
    await pumpEventQueue();
    final replay = gateway.verifyCalls.last;
    expect(replay.expectedRevision, first.expectedRevision);
    expect(replay.idempotencyId, first.idempotencyId);
    replay.completer.complete(
      _draft(revision: Int64(5), verification: _verification()),
    );
    await retry;

    expect(controller.confirmedDraft?.revision, Int64(5));
    expect(controller.verificationPhase, FeatureStudioVerificationPhase.stale);
    expect(gateway.behaviorCalls, isEmpty);
    expect(gateway.sourceCalls, isEmpty);
  });

  test('FailedPrecondition after an edit is stale while save drains', () async {
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      idFactory: _SequenceIds().call,
    );
    await controller.load();

    final verification = controller.verify();
    await pumpEventQueue();
    final editedSource = _source('Edited before failed tests returned');
    controller.reviseSource(editedSource);
    gateway.verifyCalls.single.completer.completeError(
      const PreconditionException('Verification did not pass.'),
    );
    await pumpEventQueue();

    expect(controller.verificationPhase, FeatureStudioVerificationPhase.stale);
    expect(gateway.sourceCalls, hasLength(1));
    gateway.sourceCalls.single.completer.complete(
      _draft(revision: Int64(5), source: editedSource),
    );
    await verification;

    expect(controller.savePhase, FeatureStudioSavePhase.saved);
    expect(controller.verificationPhase, FeatureStudioVerificationPhase.stale);
  });

  test('transient Accept response survives a later edit and revert', () async {
    final delay = _ManualDelay();
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      delay: delay.call,
      idFactory: _SequenceIds().call,
    );
    await controller.load();
    final request = controller.requestSuggestedChange('Improve');
    await pumpEventQueue();
    gateway.suggestionCalls.single.completer.complete(_suggestion());
    await request;

    final acceptance = controller.acceptSuggestedChange();
    await pumpEventQueue();
    final first = gateway.acceptCalls.single;
    first.completer.completeError(
      const TransportException(
        TransportErrorCode.unavailable,
        'The Accept outcome is unknown.',
      ),
    );
    await acceptance;
    controller.reviseBehavior(_behavior('Later Accept edit'));
    controller.reviseBehavior(_behavior('Create a brief'));

    expect(controller.isDirty, isFalse);
    expect(controller.hasRetryableSuggestionDecision, isTrue);
    expect(controller.hasUnresolvedMutation, isTrue);
    expect(gateway.behaviorCalls, isEmpty);
    final retry = controller.retrySuggestedChangeDecision();
    await pumpEventQueue();
    final replay = gateway.acceptCalls.last;
    expect(gateway.acceptCalls, hasLength(2));
    expect(replay.expectedRevision, first.expectedRevision);
    expect(replay.idempotencyId, first.idempotencyId);
    replay.completer.complete(
      _draft(
        revision: Int64(5),
        behavior: _lastSuggestion.replacementBehavior,
        source: _lastSuggestion.replacementSource,
      ),
    );
    await pumpEventQueue();
    expect(gateway.behaviorCalls, hasLength(1));
    gateway.behaviorCalls.single.completer.complete(
      _draft(
        revision: Int64(6),
        behavior: _behavior('Create a brief'),
        source: _lastSuggestion.replacementSource,
      ),
    );
    await retry;

    expect(controller.hasUnresolvedMutation, isFalse);
    expect(controller.confirmedDraft?.revision, Int64(6));
  });

  test('transient Reject response survives a later edit and revert', () async {
    final delay = _ManualDelay();
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      delay: delay.call,
      idFactory: _SequenceIds().call,
    );
    await controller.load();
    final request = controller.requestSuggestedChange('Improve');
    await pumpEventQueue();
    gateway.suggestionCalls.single.completer.complete(_suggestion());
    await request;

    final rejection = controller.rejectSuggestedChange();
    await pumpEventQueue();
    final first = gateway.rejectCalls.single;
    first.completer.completeError(
      const TransportException(
        TransportErrorCode.unavailable,
        'The Reject outcome is unknown.',
      ),
    );
    await rejection;
    controller.reviseSource(_source('Later Reject edit'));
    controller.reviseSource(_source());

    expect(controller.isDirty, isFalse);
    expect(controller.hasRetryableSuggestionDecision, isTrue);
    expect(controller.hasUnresolvedMutation, isTrue);
    expect(gateway.sourceCalls, isEmpty);
    final retry = controller.retrySuggestedChangeDecision();
    await pumpEventQueue();
    final replay = gateway.rejectCalls.last;
    expect(gateway.rejectCalls, hasLength(2));
    expect(replay.expectedRevision, first.expectedRevision);
    expect(replay.idempotencyId, first.idempotencyId);
    replay.completer.complete(_draft(revision: Int64(5)));
    await retry;

    expect(controller.hasUnresolvedMutation, isFalse);
    expect(controller.confirmedDraft?.revision, Int64(5));
  });

  test('transient Verify response survives a later edit and revert', () async {
    final delay = _ManualDelay();
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      delay: delay.call,
      idFactory: _SequenceIds().call,
    );
    await controller.load();

    final verification = controller.verify();
    await pumpEventQueue();
    final first = gateway.verifyCalls.single;
    first.completer.completeError(
      const TransportException(
        TransportErrorCode.unavailable,
        'The Verify outcome is unknown.',
      ),
    );
    await verification;
    controller.reviseSource(_source('Later Verify edit'));
    controller.reviseSource(_source());

    expect(controller.isDirty, isFalse);
    expect(
      controller.verificationPhase,
      FeatureStudioVerificationPhase.retryableFailure,
    );
    expect(controller.hasUnresolvedMutation, isTrue);
    expect(gateway.sourceCalls, isEmpty);
    final retry = controller.retryVerification();
    await pumpEventQueue();
    final replay = gateway.verifyCalls.last;
    expect(gateway.verifyCalls, hasLength(2));
    expect(replay.expectedRevision, first.expectedRevision);
    expect(replay.idempotencyId, first.idempotencyId);
    replay.completer.complete(
      _draft(revision: Int64(5), verification: _verification()),
    );
    await retry;

    expect(controller.hasUnresolvedMutation, isFalse);
    expect(controller.confirmedDraft?.revision, Int64(5));
    expect(controller.verificationPhase, FeatureStudioVerificationPhase.stale);
  });

  test(
    'matching conflict reload reconciles a private suggestion decision',
    () async {
      final delay = _ManualDelay();
      final gateway = _ControlledGateway()..loadedDraft = _draft();
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: gateway,
        delay: delay.call,
        idFactory: _SequenceIds().call,
      );
      await controller.load();
      final request = controller.requestSuggestedChange('Improve');
      await pumpEventQueue();
      gateway.suggestionCalls.single.completer.complete(_suggestion());
      await request;

      final acceptance = controller.acceptSuggestedChange();
      await pumpEventQueue();
      gateway.acceptCalls.single.completer.completeError(
        const TransportException(
          TransportErrorCode.unavailable,
          'The Accept outcome is unknown.',
        ),
      );
      await acceptance;

      final matchedBehavior = _behavior('Already stored by the server');
      controller.reviseBehavior(matchedBehavior);
      delay.completeAll();
      await pumpEventQueue();
      gateway.behaviorCalls.single.completer.completeError(
        const TransportException(
          TransportErrorCode.aborted,
          'The save revision conflicted.',
        ),
      );
      await pumpEventQueue();
      expect(controller.hasConflict, isTrue);
      expect(controller.hasUnresolvedMutation, isTrue);

      gateway.loadedDraft = _draft(
        revision: Int64(8),
        behavior: matchedBehavior,
      );
      await controller.resolveConflictKeepingLocalChanges();

      expect(controller.hasConflict, isFalse);
      expect(controller.isDirty, isFalse);
      expect(controller.hasUnresolvedMutation, isFalse);
      expect(gateway.behaviorCalls, hasLength(1));
    },
  );

  test('Behavior edit then revert is net-zero before debounce', () async {
    final delay = _ManualDelay();
    final gateway = _ControlledGateway()..loadedDraft = _draft();
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      delay: delay.call,
    );
    await controller.load();
    final suggestion = controller.requestSuggestedChange('Improve');
    await pumpEventQueue();
    gateway.suggestionCalls.single.completer.complete(_suggestion());
    await suggestion;

    controller.reviseBehavior(_behavior('Temporary edit'));
    controller.reviseBehavior(_behavior('Create a brief'));
    delay.completeAll();
    await pumpEventQueue();

    expect(controller.isDirty, isFalse);
    expect(controller.savePhase, FeatureStudioSavePhase.saved);
    expect(gateway.behaviorCalls, isEmpty);
    expect(controller.suggestionPhase, FeatureStudioSuggestionPhase.ready);
    expect(controller.confirmedDraft?.revision, Int64(4));
  });

  test('Source edit then revert is net-zero before debounce', () async {
    final delay = _ManualDelay();
    final gateway = _ControlledGateway()
      ..loadedDraft = _draft(verification: _verification());
    final controller = FeatureStudioController(
      draftId: 'draft-a',
      gateway: gateway,
      delay: delay.call,
    );
    await controller.load();

    controller.reviseSource(_source('Temporary edit'));
    controller.reviseSource(_source());
    delay.completeAll();
    await pumpEventQueue();

    expect(controller.isDirty, isFalse);
    expect(controller.savePhase, FeatureStudioSavePhase.saved);
    expect(gateway.sourceCalls, isEmpty);
    expect(controller.verificationPhase, FeatureStudioVerificationPhase.passed);
    expect(controller.confirmedDraft?.revision, Int64(4));
  });
}

class _ManualDelay {
  final List<Duration> durations = [];
  final List<Completer<void>> _pending = [];

  Future<void> call(Duration duration) {
    durations.add(duration);
    final completer = Completer<void>();
    _pending.add(completer);
    return completer.future;
  }

  void completeAll() {
    for (final completer in _pending) {
      if (!completer.isCompleted) completer.complete();
    }
  }
}

class _BehaviorCall {
  _BehaviorCall({
    required this.expectedRevision,
    required this.idempotencyId,
    required this.behavior,
    required this.expectedSource,
    required this.completer,
  });

  final Int64 expectedRevision;
  final String idempotencyId;
  final FeatureStudioBehavior behavior;
  final FeatureStudioSource expectedSource;
  final Completer<FeatureStudioDraft> completer;
}

class _SourceCall {
  _SourceCall({
    required this.expectedRevision,
    required this.idempotencyId,
    required this.source,
    required this.expectedBehavior,
    required this.completer,
  });

  final Int64 expectedRevision;
  final String idempotencyId;
  final FeatureStudioSource source;
  final FeatureStudioBehavior expectedBehavior;
  final Completer<FeatureStudioDraft> completer;
}

class _SuggestionCall {
  _SuggestionCall({
    required this.expectedRevision,
    required this.guidance,
    required this.suggestionId,
    required this.completer,
  });

  final Int64 expectedRevision;
  final String guidance;
  final String suggestionId;
  final Completer<FeatureStudioSuggestion> completer;
}

class _DecisionCall {
  _DecisionCall({
    required this.expectedRevision,
    required this.idempotencyId,
    required this.suggestion,
    required this.completer,
  });

  final Int64 expectedRevision;
  final String idempotencyId;
  final FeatureStudioSuggestion suggestion;
  final Completer<FeatureStudioDraft> completer;
}

class _VerifyCall {
  _VerifyCall({
    required this.expectedRevision,
    required this.idempotencyId,
    required this.completer,
  });

  final Int64 expectedRevision;
  final String idempotencyId;
  final Completer<FeatureStudioDraft> completer;
}

class _SequenceIds {
  int _next = 0;

  String call() => 'studio-id-${++_next}';
}

class _ControlledGateway implements FeatureStudioGateway {
  late FeatureStudioDraft loadedDraft;
  Completer<FeatureStudioDraft>? pendingLoad;
  Object? loadError;
  int loadCalls = 0;
  final List<_BehaviorCall> behaviorCalls = [];
  final List<_SourceCall> sourceCalls = [];
  final List<_SuggestionCall> suggestionCalls = [];
  final List<_DecisionCall> acceptCalls = [];
  final List<_DecisionCall> rejectCalls = [];
  final List<_VerifyCall> verifyCalls = [];

  @override
  Future<FeatureStudioDraft> loadDraft(String draftId) async {
    loadCalls++;
    if (loadError case final error?) throw error;
    if (pendingLoad case final pending?) return pending.future;
    return loadedDraft;
  }

  @override
  Future<FeatureStudioDraft> reviseBehavior({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioBehavior behavior,
    required FeatureStudioSource expectedSource,
  }) {
    final completer = Completer<FeatureStudioDraft>();
    behaviorCalls.add(
      _BehaviorCall(
        expectedRevision: expectedRevision,
        idempotencyId: idempotencyId,
        behavior: behavior,
        expectedSource: expectedSource,
        completer: completer,
      ),
    );
    return completer.future;
  }

  @override
  Future<FeatureStudioDraft> reviseSource({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioSource source,
    required FeatureStudioBehavior expectedBehavior,
  }) {
    final completer = Completer<FeatureStudioDraft>();
    sourceCalls.add(
      _SourceCall(
        expectedRevision: expectedRevision,
        idempotencyId: idempotencyId,
        source: source,
        expectedBehavior: expectedBehavior,
        completer: completer,
      ),
    );
    return completer.future;
  }

  @override
  Future<FeatureStudioDraft> acceptSuggestedChange({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioSuggestion suggestion,
  }) {
    final completer = Completer<FeatureStudioDraft>();
    acceptCalls.add(
      _DecisionCall(
        expectedRevision: expectedRevision,
        idempotencyId: idempotencyId,
        suggestion: suggestion,
        completer: completer,
      ),
    );
    return completer.future;
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
  }) {
    final completer = Completer<FeatureStudioDraft>();
    rejectCalls.add(
      _DecisionCall(
        expectedRevision: expectedRevision,
        idempotencyId: idempotencyId,
        suggestion: suggestion,
        completer: completer,
      ),
    );
    return completer.future;
  }

  @override
  Future<FeatureStudioSuggestion> suggestChange({
    required String draftId,
    required Int64 expectedRevision,
    required String guidance,
    required String suggestionId,
  }) {
    final completer = Completer<FeatureStudioSuggestion>();
    suggestionCalls.add(
      _SuggestionCall(
        expectedRevision: expectedRevision,
        guidance: guidance,
        suggestionId: suggestionId,
        completer: completer,
      ),
    );
    return completer.future;
  }

  @override
  Future<FeatureStudioDraft> verifyDraft({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioBehavior expectedBehavior,
    required FeatureStudioSource expectedSource,
  }) {
    final completer = Completer<FeatureStudioDraft>();
    verifyCalls.add(
      _VerifyCall(
        expectedRevision: expectedRevision,
        idempotencyId: idempotencyId,
        completer: completer,
      ),
    );
    return completer.future;
  }
}

FeatureStudioDraft _draft({
  Int64? revision,
  FeatureStudioBehavior? behavior,
  FeatureStudioSource? source,
  FeatureStudioDraftStatus status = FeatureStudioDraftStatus.draft,
  FeatureStudioVerification? verification,
}) => FeatureStudioDraft(
  draftId: 'draft-a',
  originatingRequest: const FeatureStudioOriginatingRequest(
    operationId: 'operation-a',
    conversationId: 'conversation-a',
    text: 'Research Acme',
  ),
  goal: 'Create a concise company brief',
  status: status,
  behavior: behavior ?? _behavior('Create a brief'),
  source: source ?? _source(),
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

FeatureStudioScenario _scenario(String id, {required String name}) =>
    FeatureStudioScenario(
      scenarioId: id,
      name: name,
      given: 'A company name',
      when: 'The Feature runs',
      then: 'A concise brief is returned',
    );

FeatureStudioSource _source([String implementation = 'source']) =>
    FeatureStudioSource(
      implementationProjectPath: 'Feature/Feature.csproj',
      scenarioProjectPath: 'Feature.Tests/Feature.Tests.csproj',
      files: [
        const FeatureStudioSourceFile(
          path: 'Feature/Feature.csproj',
          content: '<Project Sdk="Microsoft.NET.Sdk" />',
        ),
        const FeatureStudioSourceFile(
          path: 'Feature.Tests/Feature.Tests.csproj',
          content: '<Project Sdk="Microsoft.NET.Sdk" />',
        ),
        FeatureStudioSourceFile(
          path: 'Feature/Feature.cs',
          content: implementation,
        ),
      ],
    );

FeatureStudioVerification _verification() => FeatureStudioVerification(
  releaseDigest: 'a' * 64,
  total: 1,
  passed: 1,
  failed: 0,
  skipped: 0,
  verifiedAt: DateTime.utc(2026, 7, 15, 10, 1),
);

late FeatureStudioSuggestion _lastSuggestion;

FeatureStudioSuggestion _suggestion({String summary = 'Add evidence'}) {
  _lastSuggestion = FeatureStudioSuggestion(
    patchId: 'patch-a',
    draftId: 'draft-a',
    baseRevision: Int64(4),
    summary: summary,
    replacementBehavior: FeatureStudioBehavior(
      scenarios: [
        const FeatureStudioScenario(
          scenarioId: 'brief',
          name: 'Create an evidence brief',
          given: 'A company name',
          when: 'The Feature runs',
          then: 'A concise brief with evidence is returned',
        ),
        const FeatureStudioScenario(
          scenarioId: 'sources',
          name: 'Include sources',
          given: 'Research evidence',
          when: 'The brief is created',
          then: 'Sources are included',
        ),
      ],
    ),
    replacementSource: FeatureStudioSource(
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
          content: 'updated source',
        ),
        FeatureStudioSourceFile(
          path: 'Feature/Evidence.cs',
          content: 'evidence source',
        ),
      ],
    ),
  );
  return _lastSuggestion;
}

FeatureStudioSuggestion _exactSuggestion({
  FeatureStudioBehavior? behavior,
  FeatureStudioSource? source,
}) => FeatureStudioSuggestion(
  patchId: 'patch-exact',
  draftId: 'draft-a',
  baseRevision: Int64(4),
  summary: 'Exact replacement',
  replacementBehavior: behavior ?? _behavior('Create a brief'),
  replacementSource: source ?? _source(),
);
