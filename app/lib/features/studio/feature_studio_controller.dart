import 'dart:async';
import 'dart:math';

import 'package:fixnum/fixnum.dart';
import 'package:flutter/foundation.dart';

import '../../runtime/runtime_errors.dart';
import 'feature_studio_gateway.dart';
import 'feature_studio_models.dart';
import 'feature_studio_validation.dart';

typedef FeatureStudioDelay = Future<void> Function(Duration duration);
typedef FeatureStudioIdFactory = String Function();

enum FeatureStudioLoadPhase {
  idle,
  loading,
  ready,
  notFound,
  authenticationRequired,
  retryableFailure,
  terminalFailure,
}

enum FeatureStudioSavePhase {
  saved,
  debouncing,
  saving,
  invalid,
  retryableFailure,
  conflict,
  failed,
}

enum FeatureStudioSuggestionPhase {
  idle,
  requesting,
  ready,
  stale,
  deciding,
  retryableFailure,
  failed,
}

enum FeatureStudioVerificationPhase {
  idle,
  verifying,
  passed,
  stale,
  failedTests,
  retryableFailure,
  failed,
}

class FeatureStudioController extends ChangeNotifier {
  FeatureStudioController({
    required String draftId,
    required FeatureStudioGateway gateway,
    Duration autosaveDebounce = const Duration(milliseconds: 500),
    FeatureStudioDelay? delay,
    FeatureStudioIdFactory? idFactory,
  }) : _draftId = draftId,
       _gateway = gateway,
       _autosaveDebounce = autosaveDebounce,
       _delay = delay ?? Future<void>.delayed,
       _idFactory = idFactory ?? _nextDefaultId;

  final String _draftId;
  final FeatureStudioGateway _gateway;
  final Duration _autosaveDebounce;
  final FeatureStudioDelay _delay;
  final FeatureStudioIdFactory _idFactory;

  FeatureStudioDraft? _confirmedDraft;
  FeatureStudioBehavior? _behavior;
  FeatureStudioSource? _source;
  FeatureStudioLoadPhase _loadPhase = FeatureStudioLoadPhase.idle;
  FeatureStudioSavePhase _savePhase = FeatureStudioSavePhase.saved;
  FeatureStudioSuggestionPhase _suggestionPhase =
      FeatureStudioSuggestionPhase.idle;
  FeatureStudioVerificationPhase _verificationPhase =
      FeatureStudioVerificationPhase.idle;
  FeatureStudioSuggestion? _suggestion;
  FeatureStudioSuggestionDiff? _suggestionDiff;
  Object? _suggestionError;
  FeatureStudioVerification? _verification;
  Object? _verificationError;
  _SuggestionIntent? _retryableSuggestionIntent;
  _SuggestionDecisionIntent? _retryableSuggestionDecision;
  _VerificationIntent? _retryableVerificationIntent;
  _SuggestionDecisionIntent? _uncertainSuggestionDecision;
  _VerificationIntent? _uncertainVerificationIntent;
  bool _suggestionRequestInFlight = false;
  Object? _loadError;
  Object? _saveError;
  int _behaviorEpoch = 0;
  int _sourceEpoch = 0;
  int _debounceGeneration = 0;
  bool _mutationInFlight = false;
  bool _disposed = false;
  bool _conflictRecoveryInFlight = false;
  Future<void>? _laneFuture;
  _SaveMutation? _retryableSave;
  _ConflictOperation? _conflictOperation;
  _SuggestionDecisionIntent? _conflictSuggestionDecision;
  _VerificationIntent? _conflictVerification;
  bool _restoreReadySuggestionAfterNetZero = false;
  bool _restorePassedVerificationAfterNetZero = false;

  FeatureStudioDraft? get confirmedDraft => _confirmedDraft;
  FeatureStudioBehavior? get behavior => _behavior;
  FeatureStudioSource? get source => _source;
  FeatureStudioLoadPhase get loadPhase => _loadPhase;
  FeatureStudioSavePhase get savePhase => _savePhase;
  FeatureStudioSuggestionPhase get suggestionPhase => _suggestionPhase;
  FeatureStudioVerificationPhase get verificationPhase => _verificationPhase;
  Object? get loadError => _loadError;
  Object? get saveError => _saveError;
  FeatureStudioSuggestion? get suggestion => _suggestion;
  FeatureStudioSuggestionDiff? get suggestionDiff => _suggestionDiff;
  Object? get suggestionError => _suggestionError;
  FeatureStudioVerification? get verification => _verification;
  Object? get verificationError => _verificationError;
  bool get behaviorDirty =>
      !_sameBehavior(_behavior, _confirmedDraft?.behavior);
  bool get sourceDirty => !_sameSource(_source, _confirmedDraft?.source);
  bool get isDirty => behaviorDirty || sourceDirty;
  bool get mutationInFlight => _mutationInFlight;
  bool get hasConflict => _savePhase == FeatureStudioSavePhase.conflict;
  bool get conflictRecoveryInFlight => _conflictRecoveryInFlight;
  bool get isMutableDraft =>
      _confirmedDraft?.status == FeatureStudioDraftStatus.draft;
  bool get hasRetryableSuggestionDecision =>
      _retryableSuggestionDecision != null;
  bool get hasUnresolvedMutation =>
      _laneFuture != null ||
      _mutationInFlight ||
      _retryableSave != null ||
      _retryableSuggestionDecision != null ||
      _retryableVerificationIntent != null ||
      _uncertainSuggestionDecision != null ||
      _uncertainVerificationIntent != null;
  List<String> get behaviorErrors => _behavior == null
      ? const ['Behavior is unavailable.']
      : validateFeatureStudioBehavior(_behavior!);
  List<String> get sourceErrors => _source == null
      ? const ['Code is unavailable.']
      : validateFeatureStudioSource(_source!);
  bool get canVerify =>
      _loadPhase == FeatureStudioLoadPhase.ready &&
      _confirmedDraft?.status == FeatureStudioDraftStatus.draft &&
      !isDirty &&
      !_mutationInFlight &&
      !_suggestionRequestInFlight &&
      _verificationPhase != FeatureStudioVerificationPhase.verifying &&
      _savePhase == FeatureStudioSavePhase.saved &&
      _retryableVerificationIntent == null &&
      behaviorErrors.isEmpty &&
      sourceErrors.isEmpty;
  bool get canRequestSuggestion =>
      _loadPhase == FeatureStudioLoadPhase.ready &&
      isMutableDraft &&
      !isDirty &&
      !_mutationInFlight &&
      !_suggestionRequestInFlight &&
      _savePhase == FeatureStudioSavePhase.saved &&
      _retryableSuggestionIntent == null &&
      _retryableSuggestionDecision == null &&
      behaviorErrors.isEmpty &&
      sourceErrors.isEmpty;
  bool get canAcceptSuggestion =>
      canRequestSuggestion &&
      _suggestionPhase == FeatureStudioSuggestionPhase.ready &&
      _suggestion != null &&
      _suggestion!.baseRevision == _confirmedDraft?.revision;
  bool get canRejectSuggestion => canAcceptSuggestion;

  Future<void> load() async {
    _loadPhase = FeatureStudioLoadPhase.loading;
    _loadError = null;
    _notify();
    try {
      final draft = await _gateway.loadDraft(_draftId);
      if (_disposed) return;
      _replaceWithDraft(draft);
      _conflictOperation = null;
      _conflictSuggestionDecision = null;
      _conflictVerification = null;
      _loadPhase = FeatureStudioLoadPhase.ready;
      _savePhase = FeatureStudioSavePhase.saved;
    } on TransportException catch (error) {
      if (_disposed) return;
      _loadError = error;
      _loadPhase = switch (error.code) {
        TransportErrorCode.notFound => FeatureStudioLoadPhase.notFound,
        TransportErrorCode.unauthenticated =>
          FeatureStudioLoadPhase.authenticationRequired,
        _ when error.isTerminal => FeatureStudioLoadPhase.terminalFailure,
        _ => FeatureStudioLoadPhase.retryableFailure,
      };
    } catch (error) {
      if (_disposed) return;
      _loadError = error;
      _loadPhase = FeatureStudioLoadPhase.terminalFailure;
    }
    _notify();
  }

  void reviseBehavior(FeatureStudioBehavior behavior) {
    if (_disposed ||
        _loadPhase != FeatureStudioLoadPhase.ready ||
        _conflictRecoveryInFlight ||
        !isMutableDraft) {
      return;
    }
    if (_sameBehavior(_behavior, behavior)) return;
    final wasDirty = isDirty;
    _behavior = behavior;
    _behaviorEpoch++;
    _captureFreshnessBeforeEdit(wasDirty);
    if (!isDirty) {
      _restoreAfterNetZeroEdit();
      return;
    }
    _markDependentWorkStale();
    _queueAutosave();
  }

  void reviseSource(FeatureStudioSource source) {
    if (_disposed ||
        _loadPhase != FeatureStudioLoadPhase.ready ||
        _conflictRecoveryInFlight ||
        !isMutableDraft) {
      return;
    }
    if (_sameSource(_source, source)) return;
    final wasDirty = isDirty;
    _source = source;
    _sourceEpoch++;
    _captureFreshnessBeforeEdit(wasDirty);
    if (!isDirty) {
      _restoreAfterNetZeroEdit();
      return;
    }
    _markDependentWorkStale();
    _queueAutosave();
  }

  Future<void> saveNow() {
    _debounceGeneration++;
    final activeLane = _laneFuture;
    if (activeLane != null) return activeLane;
    if (_retryableSave != null) return retrySave();
    if (!_canEnterSaveLane) return Future<void>.value();
    return _startSaveLane();
  }

  Future<void> retrySave() {
    final command = _retryableSave;
    if (command == null || _mutationInFlight || hasConflict) {
      return Future<void>.value();
    }
    return _startSaveLane(first: command);
  }

  Future<void> resolveConflictKeepingLocalChanges() async {
    if (!hasConflict || _mutationInFlight || _conflictRecoveryInFlight) return;
    _conflictRecoveryInFlight = true;
    _notify();
    final conflictOperation = _conflictOperation ?? _ConflictOperation.save;
    final conflictDecision = _conflictSuggestionDecision;
    final conflictVerification = _conflictVerification;
    final localBehavior = _behavior;
    final localSource = _source;
    var invalidatesVerification = false;
    final keepBehavior = switch (conflictOperation) {
      _ConflictOperation.save => behaviorDirty,
      _ConflictOperation.acceptSuggestion ||
      _ConflictOperation.rejectSuggestion =>
        conflictDecision != null &&
            _behaviorEpoch != conflictDecision.behaviorEpoch,
      _ConflictOperation.verify =>
        conflictVerification != null &&
            _behaviorEpoch != conflictVerification.behaviorEpoch,
    };
    final keepSource = switch (conflictOperation) {
      _ConflictOperation.save => sourceDirty,
      _ConflictOperation.acceptSuggestion ||
      _ConflictOperation.rejectSuggestion =>
        conflictDecision != null &&
            _sourceEpoch != conflictDecision.sourceEpoch,
      _ConflictOperation.verify =>
        conflictVerification != null &&
            _sourceEpoch != conflictVerification.sourceEpoch,
    };
    var retryVerification = false;
    try {
      final server = await _gateway.loadDraft(_draftId);
      if (_disposed) return;
      if (_confirmedDraft != null &&
          server.revision < _confirmedDraft!.revision) {
        throw const ProtocolException('Draft response regressed.');
      }
      if (conflictOperation == _ConflictOperation.save &&
          server.status == FeatureStudioDraftStatus.draft) {
        _confirmedDraft = server;
        if (!keepBehavior) {
          _behavior = server.behavior;
        } else {
          _behavior = localBehavior;
        }
        if (!keepSource) {
          _source = server.source;
        } else {
          _source = localSource;
        }
        _resolveUncertainMutationIntents();
      } else {
        _replaceWithDraft(server);
      }
      _retryableSave = null;
      _saveError = null;
      _conflictOperation = null;
      _conflictSuggestionDecision = null;
      _conflictVerification = null;
      if (conflictOperation == _ConflictOperation.acceptSuggestion &&
          conflictDecision != null &&
          isMutableDraft) {
        if (keepBehavior) {
          _behavior = localBehavior;
          invalidatesVerification = true;
        } else if (!_sameBehavior(
          _behavior,
          conflictDecision.suggestion.replacementBehavior,
        )) {
          _behavior = conflictDecision.suggestion.replacementBehavior;
          _behaviorEpoch++;
          invalidatesVerification = true;
        }
        if (keepSource) {
          _source = localSource;
          invalidatesVerification = true;
        } else if (!_sameSource(
          _source,
          conflictDecision.suggestion.replacementSource,
        )) {
          _source = conflictDecision.suggestion.replacementSource;
          _sourceEpoch++;
          invalidatesVerification = true;
        }
        if (keepBehavior || keepSource) {
          _suggestionPhase = FeatureStudioSuggestionPhase.stale;
        } else {
          _suggestion = null;
          _suggestionDiff = null;
          _suggestionPhase = FeatureStudioSuggestionPhase.idle;
        }
      } else if (conflictOperation == _ConflictOperation.rejectSuggestion) {
        if (keepBehavior) {
          _behavior = localBehavior;
          invalidatesVerification = true;
        }
        if (keepSource) {
          _source = localSource;
          invalidatesVerification = true;
        }
        if (keepBehavior || keepSource) {
          _suggestionPhase = FeatureStudioSuggestionPhase.stale;
        } else {
          _suggestion = null;
          _suggestionDiff = null;
          _suggestionPhase = FeatureStudioSuggestionPhase.idle;
        }
      } else if (conflictOperation == _ConflictOperation.verify) {
        if (keepBehavior) {
          _behavior = localBehavior;
          invalidatesVerification = true;
        }
        if (keepSource) {
          _source = localSource;
          invalidatesVerification = true;
        }
        if (keepBehavior || keepSource) {
          _markVerificationStale();
        } else {
          retryVerification = true;
        }
      }
      if (invalidatesVerification) _markVerificationStale();
      _savePhase = _hasValidationErrors
          ? FeatureStudioSavePhase.invalid
          : isDirty
          ? FeatureStudioSavePhase.debouncing
          : FeatureStudioSavePhase.saved;
      _notify();
      if (isDirty && !_hasValidationErrors) {
        await _startSaveLane();
      } else if (retryVerification) {
        await verify();
      }
    } catch (error) {
      if (_disposed) return;
      _saveError = error;
      _savePhase = FeatureStudioSavePhase.conflict;
      _notify();
    } finally {
      _conflictRecoveryInFlight = false;
      _notify();
    }
  }

  Future<void> resolveConflictUsingServerDraft() async {
    if (!hasConflict || _mutationInFlight || _conflictRecoveryInFlight) return;
    _conflictRecoveryInFlight = true;
    _notify();
    try {
      final server = await _gateway.loadDraft(_draftId);
      if (_disposed) return;
      if (_confirmedDraft != null &&
          server.revision < _confirmedDraft!.revision) {
        throw const ProtocolException('Draft response regressed.');
      }
      _replaceWithDraft(server);
      _retryableSave = null;
      _retryableSuggestionIntent = null;
      _retryableSuggestionDecision = null;
      _retryableVerificationIntent = null;
      _suggestion = null;
      _suggestionDiff = null;
      _suggestionPhase = FeatureStudioSuggestionPhase.idle;
      _conflictOperation = null;
      _conflictSuggestionDecision = null;
      _conflictVerification = null;
      _saveError = null;
      _savePhase = FeatureStudioSavePhase.saved;
      _notify();
    } catch (error) {
      if (_disposed) return;
      _saveError = error;
      _savePhase = FeatureStudioSavePhase.conflict;
      _notify();
    } finally {
      _conflictRecoveryInFlight = false;
      _notify();
    }
  }

  Future<void> requestSuggestedChange(String guidance) {
    final normalizedGuidance = guidance.trim();
    if (!canRequestSuggestion || !_isCanonicalGuidance(normalizedGuidance)) {
      return Future<void>.value();
    }
    final draft = _confirmedDraft!;
    final intent = _SuggestionIntent(
      expectedRevision: draft.revision,
      guidance: normalizedGuidance,
      suggestionId: _idFactory(),
      behavior: _behavior!,
      source: _source!,
      behaviorEpoch: _behaviorEpoch,
      sourceEpoch: _sourceEpoch,
    );
    return _runSuggestionRequest(intent);
  }

  Future<void> retrySuggestedChangeRequest() {
    final intent = _retryableSuggestionIntent;
    if (intent == null ||
        _suggestionRequestInFlight ||
        _mutationInFlight ||
        isDirty) {
      return Future<void>.value();
    }
    return _runSuggestionRequest(intent);
  }

  Future<void> retrySuggestedChange() => hasRetryableSuggestionDecision
      ? retrySuggestedChangeDecision()
      : retrySuggestedChangeRequest();

  Future<void> _runSuggestionRequest(_SuggestionIntent intent) async {
    _suggestionRequestInFlight = true;
    _suggestionPhase = FeatureStudioSuggestionPhase.requesting;
    _suggestionError = null;
    _suggestion = null;
    _suggestionDiff = null;
    _notify();
    try {
      final result = await _gateway.suggestChange(
        draftId: _draftId,
        expectedRevision: intent.expectedRevision,
        guidance: intent.guidance,
        suggestionId: intent.suggestionId,
      );
      if (_disposed) return;
      _suggestion = result;
      _suggestionDiff = _buildSuggestionDiff(
        behavior: intent.behavior,
        source: intent.source,
        suggestion: result,
      );
      _retryableSuggestionIntent = null;
      _suggestionPhase =
          _confirmedDraft?.revision == intent.expectedRevision &&
              _behaviorEpoch == intent.behaviorEpoch &&
              _sourceEpoch == intent.sourceEpoch &&
              !isDirty
          ? FeatureStudioSuggestionPhase.ready
          : FeatureStudioSuggestionPhase.stale;
    } on TransportException catch (error) {
      if (_disposed) return;
      _suggestionError = error;
      if (error.code == TransportErrorCode.aborted) {
        _retryableSuggestionIntent = null;
        _suggestionPhase = FeatureStudioSuggestionPhase.stale;
      } else if (error.isTerminal) {
        _retryableSuggestionIntent = null;
        _suggestionPhase = FeatureStudioSuggestionPhase.failed;
      } else {
        _retryableSuggestionIntent = intent;
        _suggestionPhase = FeatureStudioSuggestionPhase.retryableFailure;
      }
    } catch (error) {
      if (_disposed) return;
      _suggestionError = error;
      _retryableSuggestionIntent = null;
      _suggestionPhase = FeatureStudioSuggestionPhase.failed;
    } finally {
      _suggestionRequestInFlight = false;
      _notify();
    }
  }

  Future<void> acceptSuggestedChange() {
    if (!canAcceptSuggestion) return Future<void>.value();
    return _runSuggestionDecision(
      _SuggestionDecisionIntent(
        accept: true,
        expectedRevision: _confirmedDraft!.revision,
        idempotencyId: _idFactory(),
        suggestion: _suggestion!,
        behavior: _behavior!,
        source: _source!,
        verification: _confirmedDraft!.verification,
        behaviorEpoch: _behaviorEpoch,
        sourceEpoch: _sourceEpoch,
      ),
    );
  }

  Future<void> rejectSuggestedChange() {
    if (!canRejectSuggestion) return Future<void>.value();
    return _runSuggestionDecision(
      _SuggestionDecisionIntent(
        accept: false,
        expectedRevision: _confirmedDraft!.revision,
        idempotencyId: _idFactory(),
        suggestion: _suggestion!,
        behavior: _behavior!,
        source: _source!,
        verification: _confirmedDraft!.verification,
        behaviorEpoch: _behaviorEpoch,
        sourceEpoch: _sourceEpoch,
      ),
    );
  }

  Future<void> retrySuggestedChangeDecision() {
    final intent = _retryableSuggestionDecision;
    if (intent == null || _mutationInFlight || isDirty || !isMutableDraft) {
      return Future<void>.value();
    }
    return _runSuggestionDecision(intent);
  }

  Future<void> _runSuggestionDecision(_SuggestionDecisionIntent intent) async {
    if (_mutationInFlight) return;
    _mutationInFlight = true;
    _suggestionPhase = FeatureStudioSuggestionPhase.deciding;
    _suggestionError = null;
    _notify();
    var queueSave = false;
    try {
      final reply = intent.accept
          ? await _gateway.acceptSuggestedChange(
              draftId: _draftId,
              expectedRevision: intent.expectedRevision,
              idempotencyId: intent.idempotencyId,
              suggestion: intent.suggestion,
            )
          : await _gateway.rejectSuggestedChange(
              draftId: _draftId,
              expectedRevision: intent.expectedRevision,
              idempotencyId: intent.idempotencyId,
              suggestion: intent.suggestion,
              expectedBehavior: intent.behavior,
              expectedSource: intent.source,
              expectedVerification: intent.verification,
            );
      if (_disposed) return;
      final current = _confirmedDraft;
      if (current == null || reply.revision < current.revision) {
        throw const ProtocolException('Draft response regressed.');
      }
      _confirmedDraft = reply;
      _uncertainSuggestionDecision = null;
      if (_behaviorEpoch == intent.behaviorEpoch) {
        _behavior = reply.behavior;
      }
      if (_sourceEpoch == intent.sourceEpoch) {
        _source = reply.source;
      }
      _retryableSuggestionDecision = null;
      if (_behaviorEpoch == intent.behaviorEpoch &&
          _sourceEpoch == intent.sourceEpoch) {
        _suggestion = null;
        _suggestionDiff = null;
        _suggestionPhase = FeatureStudioSuggestionPhase.idle;
      } else {
        _suggestionPhase = FeatureStudioSuggestionPhase.stale;
      }
      if (_verificationPhase == FeatureStudioVerificationPhase.passed &&
          intent.accept) {
        _verificationPhase = FeatureStudioVerificationPhase.stale;
      }
      queueSave = isDirty && !_hasValidationErrors;
    } on TransportException catch (error) {
      if (_disposed) return;
      _suggestionError = error;
      if (error.code == TransportErrorCode.aborted) {
        _retryableSuggestionDecision = null;
        _uncertainSuggestionDecision = null;
        _suggestionPhase = FeatureStudioSuggestionPhase.stale;
        _saveError = error;
        _savePhase = FeatureStudioSavePhase.conflict;
        _conflictOperation = intent.accept
            ? _ConflictOperation.acceptSuggestion
            : _ConflictOperation.rejectSuggestion;
        _conflictSuggestionDecision = intent;
        _conflictVerification = null;
      } else if (error.isTerminal) {
        _retryableSuggestionDecision = null;
        _uncertainSuggestionDecision = null;
        _suggestionPhase = FeatureStudioSuggestionPhase.failed;
        queueSave = isDirty && !_hasValidationErrors;
      } else if ((_behaviorEpoch == intent.behaviorEpoch &&
              _sourceEpoch == intent.sourceEpoch) ||
          !isDirty) {
        _retryableSuggestionDecision = intent;
        _uncertainSuggestionDecision = null;
        _suggestionPhase = FeatureStudioSuggestionPhase.retryableFailure;
      } else {
        _retryableSuggestionDecision = null;
        _uncertainSuggestionDecision = intent;
        _suggestionPhase = FeatureStudioSuggestionPhase.stale;
        queueSave = isDirty && !_hasValidationErrors;
      }
    } catch (error) {
      if (_disposed) return;
      _suggestionError = error;
      _retryableSuggestionDecision = null;
      _uncertainSuggestionDecision = null;
      _suggestionPhase = FeatureStudioSuggestionPhase.failed;
      queueSave = isDirty && !_hasValidationErrors;
    } finally {
      _mutationInFlight = false;
      _notify();
    }
    if (queueSave) await _startSaveLane();
  }

  void dismissStaleSuggestedChange() {
    if (_suggestionPhase != FeatureStudioSuggestionPhase.stale) return;
    _suggestion = null;
    _suggestionDiff = null;
    _suggestionPhase = FeatureStudioSuggestionPhase.idle;
    _notify();
  }

  Future<void> verify() {
    if (!canVerify) return Future<void>.value();
    return _runVerification(
      _VerificationIntent(
        expectedRevision: _confirmedDraft!.revision,
        idempotencyId: _idFactory(),
        behaviorEpoch: _behaviorEpoch,
        sourceEpoch: _sourceEpoch,
        behavior: _behavior!,
        source: _source!,
      ),
    );
  }

  Future<void> retryVerification() {
    final intent = _retryableVerificationIntent;
    if (intent == null || _mutationInFlight || isDirty) {
      return Future<void>.value();
    }
    return _runVerification(intent);
  }

  Future<void> _runVerification(_VerificationIntent intent) async {
    if (_mutationInFlight) return;
    _mutationInFlight = true;
    _verificationPhase = FeatureStudioVerificationPhase.verifying;
    _verificationError = null;
    _notify();
    var queueSave = false;
    try {
      final reply = await _gateway.verifyDraft(
        draftId: _draftId,
        expectedRevision: intent.expectedRevision,
        idempotencyId: intent.idempotencyId,
        expectedBehavior: intent.behavior,
        expectedSource: intent.source,
      );
      if (_disposed) return;
      final current = _confirmedDraft;
      if (current == null || reply.revision < current.revision) {
        throw const ProtocolException('Draft response regressed.');
      }
      _confirmedDraft = reply;
      if (_behaviorEpoch == intent.behaviorEpoch) {
        _behavior = reply.behavior;
      }
      if (_sourceEpoch == intent.sourceEpoch) {
        _source = reply.source;
      }
      _verification = reply.verification;
      _retryableVerificationIntent = null;
      _uncertainVerificationIntent = null;
      _verificationPhase =
          _behaviorEpoch == intent.behaviorEpoch &&
              _sourceEpoch == intent.sourceEpoch
          ? FeatureStudioVerificationPhase.passed
          : FeatureStudioVerificationPhase.stale;
      if (_suggestionPhase == FeatureStudioSuggestionPhase.ready) {
        _suggestionPhase = FeatureStudioSuggestionPhase.stale;
      }
      queueSave = isDirty && !_hasValidationErrors;
    } on PreconditionException {
      if (_disposed) return;
      _retryableVerificationIntent = null;
      if (isDirty) {
        _markVerificationStale();
      } else {
        _verificationError = const PreconditionException(
          'Verification did not pass.',
        );
        _verificationPhase = FeatureStudioVerificationPhase.failedTests;
      }
      queueSave = isDirty && !_hasValidationErrors;
    } on TransportException catch (error) {
      if (_disposed) return;
      _verificationError = error;
      if (error.code == TransportErrorCode.aborted) {
        _retryableVerificationIntent = null;
        _uncertainVerificationIntent = null;
        _verificationPhase = FeatureStudioVerificationPhase.failed;
        _saveError = error;
        _savePhase = FeatureStudioSavePhase.conflict;
        _conflictOperation = _ConflictOperation.verify;
        _conflictSuggestionDecision = null;
        _conflictVerification = intent;
      } else if (error.isTerminal) {
        _retryableVerificationIntent = null;
        _uncertainVerificationIntent = null;
        _verificationPhase = FeatureStudioVerificationPhase.failed;
        queueSave = isDirty && !_hasValidationErrors;
      } else if ((_behaviorEpoch == intent.behaviorEpoch &&
              _sourceEpoch == intent.sourceEpoch) ||
          !isDirty) {
        _retryableVerificationIntent = intent;
        _uncertainVerificationIntent = null;
        _verificationPhase = FeatureStudioVerificationPhase.retryableFailure;
      } else {
        _retryableVerificationIntent = null;
        _uncertainVerificationIntent = intent;
        _verificationPhase = FeatureStudioVerificationPhase.stale;
        queueSave = isDirty && !_hasValidationErrors;
      }
    } catch (error) {
      if (_disposed) return;
      _verificationError = error;
      _retryableVerificationIntent = null;
      _uncertainVerificationIntent = null;
      _verificationPhase = FeatureStudioVerificationPhase.failed;
      queueSave = isDirty && !_hasValidationErrors;
    } finally {
      _mutationInFlight = false;
      _notify();
    }
    if (queueSave && !hasConflict) await _startSaveLane();
  }

  bool get _hasValidationErrors =>
      behaviorErrors.isNotEmpty || sourceErrors.isNotEmpty;

  bool get _canEnterSaveLane =>
      !_disposed &&
      !_mutationInFlight &&
      !hasConflict &&
      isMutableDraft &&
      _savePhase != FeatureStudioSavePhase.failed &&
      isDirty &&
      !_hasValidationErrors;

  void _queueAutosave() {
    _retryableSave ??= null;
    if (_hasValidationErrors) {
      _debounceGeneration++;
      _savePhase = FeatureStudioSavePhase.invalid;
      _notify();
      return;
    }
    if (hasConflict || _retryableSave != null) {
      _notify();
      return;
    }
    if (_mutationInFlight) {
      _savePhase = FeatureStudioSavePhase.saving;
      _notify();
      return;
    }
    _savePhase = FeatureStudioSavePhase.debouncing;
    final generation = ++_debounceGeneration;
    _notify();
    unawaited(
      _delay(_autosaveDebounce).then<void>((_) async {
        if (_disposed || generation != _debounceGeneration) return;
        await _startSaveLane();
      }),
    );
  }

  Future<void> _startSaveLane({_SaveMutation? first}) {
    if (_mutationInFlight) return _laneFuture ?? Future<void>.value();
    if (first == null && !_canEnterSaveLane) return Future<void>.value();
    _mutationInFlight = true;
    _restoreReadySuggestionAfterNetZero = false;
    _restorePassedVerificationAfterNetZero = false;
    _savePhase = FeatureStudioSavePhase.saving;
    _notify();
    final completer = Completer<void>();
    _laneFuture = completer.future;
    unawaited(
      _drainSaveLane(first).whenComplete(() {
        _mutationInFlight = false;
        _laneFuture = null;
        if (!_disposed && _savePhase == FeatureStudioSavePhase.saving) {
          _savePhase = _hasValidationErrors
              ? FeatureStudioSavePhase.invalid
              : isDirty
              ? FeatureStudioSavePhase.debouncing
              : FeatureStudioSavePhase.saved;
          _notify();
        }
        completer.complete();
      }),
    );
    return completer.future;
  }

  Future<void> _drainSaveLane(_SaveMutation? first) async {
    var command = first;
    while (!_disposed) {
      command ??= _nextSaveMutation();
      if (command == null) return;
      try {
        final reply = await command.execute(_gateway, _draftId);
        if (_disposed) return;
        _retryableSave = null;
        _saveError = null;
        _applySaveReply(command, reply);
        command = null;
      } on TransportException catch (error) {
        if (_disposed) return;
        _saveError = error;
        if (error.code == TransportErrorCode.aborted) {
          _retryableSave = null;
          _savePhase = FeatureStudioSavePhase.conflict;
          _conflictOperation = _ConflictOperation.save;
          _conflictSuggestionDecision = null;
          _conflictVerification = null;
        } else if (error.isTerminal) {
          _retryableSave = null;
          _savePhase = FeatureStudioSavePhase.failed;
        } else {
          _retryableSave = command;
          _savePhase = FeatureStudioSavePhase.retryableFailure;
        }
        _notify();
        return;
      } catch (error) {
        if (_disposed) return;
        _saveError = error;
        _retryableSave = null;
        _savePhase = FeatureStudioSavePhase.failed;
        _notify();
        return;
      }
    }
  }

  _SaveMutation? _nextSaveMutation() {
    final draft = _confirmedDraft;
    if (draft == null || !isMutableDraft || _hasValidationErrors) return null;
    if (behaviorDirty) {
      return _BehaviorSaveMutation(
        expectedRevision: draft.revision,
        idempotencyId: _idFactory(),
        epoch: _behaviorEpoch,
        behavior: _behavior!,
        expectedSource: draft.source,
      );
    }
    if (sourceDirty) {
      return _SourceSaveMutation(
        expectedRevision: draft.revision,
        idempotencyId: _idFactory(),
        epoch: _sourceEpoch,
        source: _source!,
        expectedBehavior: draft.behavior,
      );
    }
    return null;
  }

  void _applySaveReply(_SaveMutation command, FeatureStudioDraft reply) {
    final current = _confirmedDraft;
    if (current == null) return;
    if (reply.revision < current.revision) {
      return;
    }
    _confirmedDraft = reply;
    if (command is _BehaviorSaveMutation && _behaviorEpoch == command.epoch) {
      _behavior = reply.behavior;
    }
    if (command is _SourceSaveMutation && _sourceEpoch == command.epoch) {
      _source = reply.source;
    }
    if (!behaviorDirty) _behavior = reply.behavior;
    if (!sourceDirty) _source = reply.source;
    _resolveUncertainMutationIntents();
  }

  void _replaceWithDraft(FeatureStudioDraft draft) {
    _confirmedDraft = draft;
    _behavior = draft.behavior;
    _source = draft.source;
    _restoreReadySuggestionAfterNetZero = false;
    _restorePassedVerificationAfterNetZero = false;
    _verification = draft.verification;
    _verificationPhase = draft.verification == null
        ? FeatureStudioVerificationPhase.idle
        : FeatureStudioVerificationPhase.passed;
    _resolveUncertainMutationIntents();
  }

  void _markDependentWorkStale() {
    if (_suggestionPhase == FeatureStudioSuggestionPhase.ready ||
        _suggestionPhase == FeatureStudioSuggestionPhase.deciding ||
        _suggestionPhase == FeatureStudioSuggestionPhase.retryableFailure) {
      _suggestionPhase = FeatureStudioSuggestionPhase.stale;
    }
    _retryableSuggestionIntent = null;
    if (_retryableSuggestionDecision case final intent?) {
      _uncertainSuggestionDecision = intent;
    }
    _retryableSuggestionDecision = null;
    if (_verificationPhase == FeatureStudioVerificationPhase.passed ||
        _verificationPhase == FeatureStudioVerificationPhase.retryableFailure) {
      _verificationPhase = FeatureStudioVerificationPhase.stale;
    }
    if (_retryableVerificationIntent case final intent?) {
      _uncertainVerificationIntent = intent;
    }
    _retryableVerificationIntent = null;
  }

  void _markVerificationStale() {
    _verificationPhase = FeatureStudioVerificationPhase.stale;
    _verificationError = null;
    _retryableVerificationIntent = null;
    _uncertainVerificationIntent = null;
    _restorePassedVerificationAfterNetZero = false;
  }

  void _resolveUncertainMutationIntents() {
    final hadSuggestionDecision =
        _retryableSuggestionDecision != null ||
        _uncertainSuggestionDecision != null;
    final hadVerification =
        _retryableVerificationIntent != null ||
        _uncertainVerificationIntent != null;
    _retryableSuggestionDecision = null;
    _uncertainSuggestionDecision = null;
    _retryableVerificationIntent = null;
    _uncertainVerificationIntent = null;
    if (hadSuggestionDecision &&
        _suggestionPhase == FeatureStudioSuggestionPhase.retryableFailure) {
      _suggestionPhase = FeatureStudioSuggestionPhase.stale;
    }
    if (hadVerification &&
        _verificationPhase == FeatureStudioVerificationPhase.retryableFailure) {
      _verificationPhase = FeatureStudioVerificationPhase.stale;
    }
  }

  void _captureFreshnessBeforeEdit(bool wasDirty) {
    if (wasDirty) return;
    _restoreReadySuggestionAfterNetZero =
        _suggestionPhase == FeatureStudioSuggestionPhase.ready;
    _restorePassedVerificationAfterNetZero =
        _verificationPhase == FeatureStudioVerificationPhase.passed;
  }

  void _restoreAfterNetZeroEdit() {
    _debounceGeneration++;
    if (_retryableSave != null) {
      _restoreReadySuggestionAfterNetZero = false;
      _restorePassedVerificationAfterNetZero = false;
      _savePhase = FeatureStudioSavePhase.retryableFailure;
      _notify();
      return;
    }
    _retryableSave = null;
    _saveError = null;
    _savePhase = FeatureStudioSavePhase.saved;
    final uncertainSuggestionDecision = _uncertainSuggestionDecision;
    final uncertainVerification = _uncertainVerificationIntent;
    if (uncertainSuggestionDecision != null) {
      _retryableSuggestionDecision = uncertainSuggestionDecision;
      _uncertainSuggestionDecision = null;
      _suggestionPhase = FeatureStudioSuggestionPhase.retryableFailure;
    } else if (_restoreReadySuggestionAfterNetZero &&
        _suggestion != null &&
        _suggestion!.baseRevision == _confirmedDraft?.revision) {
      _suggestionPhase = FeatureStudioSuggestionPhase.ready;
    }
    if (uncertainVerification != null) {
      _retryableVerificationIntent = uncertainVerification;
      _uncertainVerificationIntent = null;
      _verificationPhase = FeatureStudioVerificationPhase.retryableFailure;
    } else if (_restorePassedVerificationAfterNetZero &&
        _verification != null) {
      _verificationPhase = FeatureStudioVerificationPhase.passed;
    }
    _restoreReadySuggestionAfterNetZero = false;
    _restorePassedVerificationAfterNetZero = false;
    _notify();
  }

  void _notify() {
    if (!_disposed) notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    _debounceGeneration++;
    super.dispose();
  }
}

sealed class _SaveMutation {
  const _SaveMutation({
    required this.expectedRevision,
    required this.idempotencyId,
    required this.epoch,
  });

  final Int64 expectedRevision;
  final String idempotencyId;
  final int epoch;

  Future<FeatureStudioDraft> execute(
    FeatureStudioGateway gateway,
    String draftId,
  );
}

class _BehaviorSaveMutation extends _SaveMutation {
  const _BehaviorSaveMutation({
    required super.expectedRevision,
    required super.idempotencyId,
    required super.epoch,
    required this.behavior,
    required this.expectedSource,
  });

  final FeatureStudioBehavior behavior;
  final FeatureStudioSource expectedSource;

  @override
  Future<FeatureStudioDraft> execute(
    FeatureStudioGateway gateway,
    String draftId,
  ) => gateway.reviseBehavior(
    draftId: draftId,
    expectedRevision: expectedRevision,
    idempotencyId: idempotencyId,
    behavior: behavior,
    expectedSource: expectedSource,
  );
}

class _SourceSaveMutation extends _SaveMutation {
  const _SourceSaveMutation({
    required super.expectedRevision,
    required super.idempotencyId,
    required super.epoch,
    required this.source,
    required this.expectedBehavior,
  });

  final FeatureStudioSource source;
  final FeatureStudioBehavior expectedBehavior;

  @override
  Future<FeatureStudioDraft> execute(
    FeatureStudioGateway gateway,
    String draftId,
  ) => gateway.reviseSource(
    draftId: draftId,
    expectedRevision: expectedRevision,
    idempotencyId: idempotencyId,
    source: source,
    expectedBehavior: expectedBehavior,
  );
}

class _SuggestionIntent {
  const _SuggestionIntent({
    required this.expectedRevision,
    required this.guidance,
    required this.suggestionId,
    required this.behavior,
    required this.source,
    required this.behaviorEpoch,
    required this.sourceEpoch,
  });

  final Int64 expectedRevision;
  final String guidance;
  final String suggestionId;
  final FeatureStudioBehavior behavior;
  final FeatureStudioSource source;
  final int behaviorEpoch;
  final int sourceEpoch;
}

class _SuggestionDecisionIntent {
  const _SuggestionDecisionIntent({
    required this.accept,
    required this.expectedRevision,
    required this.idempotencyId,
    required this.suggestion,
    required this.behavior,
    required this.source,
    required this.verification,
    required this.behaviorEpoch,
    required this.sourceEpoch,
  });

  final bool accept;
  final Int64 expectedRevision;
  final String idempotencyId;
  final FeatureStudioSuggestion suggestion;
  final FeatureStudioBehavior behavior;
  final FeatureStudioSource source;
  final FeatureStudioVerification? verification;
  final int behaviorEpoch;
  final int sourceEpoch;
}

class _VerificationIntent {
  const _VerificationIntent({
    required this.expectedRevision,
    required this.idempotencyId,
    required this.behaviorEpoch,
    required this.sourceEpoch,
    required this.behavior,
    required this.source,
  });

  final Int64 expectedRevision;
  final String idempotencyId;
  final int behaviorEpoch;
  final int sourceEpoch;
  final FeatureStudioBehavior behavior;
  final FeatureStudioSource source;
}

enum _ConflictOperation { save, acceptSuggestion, rejectSuggestion, verify }

FeatureStudioSuggestionDiff _buildSuggestionDiff({
  required FeatureStudioBehavior behavior,
  required FeatureStudioSource source,
  required FeatureStudioSuggestion suggestion,
}) {
  final entries = <FeatureStudioDiffEntry>[];
  final scenarioCount = max(
    behavior.scenarios.length,
    suggestion.replacementBehavior.scenarios.length,
  );
  for (var index = 0; index < scenarioCount; index++) {
    final current = index < behavior.scenarios.length
        ? behavior.scenarios[index]
        : null;
    final replacement = index < suggestion.replacementBehavior.scenarios.length
        ? suggestion.replacementBehavior.scenarios[index]
        : null;
    if (current != null &&
        (replacement == null || !_sameScenario(current, replacement))) {
      entries.add(
        FeatureStudioDiffEntry(
          kind: FeatureStudioDiffKind.removal,
          area: FeatureStudioDiffArea.behavior,
          identity: current.scenarioId,
          displayLabel: _scenarioDisplayLabel(index, current),
          value: _scenarioDiffText(current),
        ),
      );
    }
    if (replacement != null &&
        (current == null || !_sameScenario(current, replacement))) {
      entries.add(
        FeatureStudioDiffEntry(
          kind: FeatureStudioDiffKind.addition,
          area: FeatureStudioDiffArea.behavior,
          identity: replacement.scenarioId,
          displayLabel: _scenarioDisplayLabel(index, replacement),
          value: _scenarioDiffText(replacement),
        ),
      );
    }
  }
  _appendPathDiff(
    entries,
    identity: 'implementation-project-path',
    displayLabel: 'Implementation project',
    current: source.implementationProjectPath,
    replacement: suggestion.replacementSource.implementationProjectPath,
  );
  _appendPathDiff(
    entries,
    identity: 'scenario-project-path',
    displayLabel: 'Scenario project',
    current: source.scenarioProjectPath,
    replacement: suggestion.replacementSource.scenarioProjectPath,
  );
  final fileCount = max(
    source.files.length,
    suggestion.replacementSource.files.length,
  );
  for (var index = 0; index < fileCount; index++) {
    final current = index < source.files.length ? source.files[index] : null;
    final replacement = index < suggestion.replacementSource.files.length
        ? suggestion.replacementSource.files[index]
        : null;
    if (current != null &&
        (replacement == null || !_sameSourceFile(current, replacement))) {
      entries.add(
        FeatureStudioDiffEntry(
          kind: FeatureStudioDiffKind.removal,
          area: FeatureStudioDiffArea.source,
          identity: current.path,
          displayLabel: current.path,
          value: current.content,
        ),
      );
    }
    if (replacement != null &&
        (current == null || !_sameSourceFile(current, replacement))) {
      entries.add(
        FeatureStudioDiffEntry(
          kind: FeatureStudioDiffKind.addition,
          area: FeatureStudioDiffArea.source,
          identity: replacement.path,
          displayLabel: replacement.path,
          value: replacement.content,
        ),
      );
    }
  }
  return FeatureStudioSuggestionDiff(entries: entries);
}

void _appendPathDiff(
  List<FeatureStudioDiffEntry> entries, {
  required String identity,
  required String displayLabel,
  required String current,
  required String replacement,
}) {
  if (current == replacement) return;
  entries
    ..add(
      FeatureStudioDiffEntry(
        kind: FeatureStudioDiffKind.removal,
        area: FeatureStudioDiffArea.source,
        identity: identity,
        displayLabel: displayLabel,
        value: current,
      ),
    )
    ..add(
      FeatureStudioDiffEntry(
        kind: FeatureStudioDiffKind.addition,
        area: FeatureStudioDiffArea.source,
        identity: identity,
        displayLabel: displayLabel,
        value: replacement,
      ),
    );
}

bool _sameSourceFile(
  FeatureStudioSourceFile left,
  FeatureStudioSourceFile right,
) => left.path == right.path && left.content == right.content;

String _scenarioDisplayLabel(int index, FeatureStudioScenario scenario) =>
    'Scenario ${index + 1}: ${scenario.name}';

bool _sameScenario(FeatureStudioScenario left, FeatureStudioScenario right) =>
    left.scenarioId == right.scenarioId &&
    left.name == right.name &&
    left.given == right.given &&
    left.when == right.when &&
    left.then == right.then;

String _scenarioDiffText(FeatureStudioScenario scenario) =>
    'Scenario name: ${scenario.name}\n'
    'Given: ${scenario.given}\n'
    'When: ${scenario.when}\n'
    'Then: ${scenario.then}';

bool _isCanonicalGuidance(String value) =>
    value.isNotEmpty &&
    value.length <= 4096 &&
    value.trim() == value &&
    !value.runes.any(_isControl);

bool _isControl(int rune) => rune < 32 || (rune >= 127 && rune <= 159);

bool _sameBehavior(FeatureStudioBehavior? left, FeatureStudioBehavior? right) {
  if (identical(left, right)) return true;
  if (left == null ||
      right == null ||
      left.scenarios.length != right.scenarios.length) {
    return false;
  }
  for (var index = 0; index < left.scenarios.length; index++) {
    final a = left.scenarios[index];
    final b = right.scenarios[index];
    if (a.scenarioId != b.scenarioId ||
        a.name != b.name ||
        a.given != b.given ||
        a.when != b.when ||
        a.then != b.then) {
      return false;
    }
  }
  return true;
}

bool _sameSource(FeatureStudioSource? left, FeatureStudioSource? right) {
  if (identical(left, right)) return true;
  if (left == null ||
      right == null ||
      left.implementationProjectPath != right.implementationProjectPath ||
      left.scenarioProjectPath != right.scenarioProjectPath ||
      left.files.length != right.files.length) {
    return false;
  }
  for (var index = 0; index < left.files.length; index++) {
    final a = left.files[index];
    final b = right.files[index];
    if (a.path != b.path || a.content != b.content) return false;
  }
  return true;
}

int _defaultIdCounter = 0;

String _nextDefaultId() =>
    'studio-${DateTime.now().microsecondsSinceEpoch}-${_defaultIdCounter++}';
