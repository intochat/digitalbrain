import 'package:fixnum/fixnum.dart';
import 'package:flutter/foundation.dart';

import '../../runtime/runtime_errors.dart';
import 'feature_release_gateway.dart';
import 'feature_release_models.dart';

enum FeatureReleaseStatus {
  idle,
  loading,
  ready,
  loadFailed,
  rollingBack,
  rollbackFailed,
  restored,
}

class FeatureReleaseFailure {
  const FeatureReleaseFailure({
    required this.message,
    required this.retryable,
    this.reloadable = false,
  });

  final String message;
  final bool retryable;
  final bool reloadable;
}

typedef FeatureReleaseIdFactory = String Function();

class FeatureReleaseController extends ChangeNotifier {
  FeatureReleaseController({
    required this.featureId,
    this.expectedReleaseDigest,
    required FeatureReleaseGateway gateway,
    FeatureReleaseIdFactory? idFactory,
  }) : _gateway = gateway,
       _idFactory = idFactory ?? _nextRollbackId;

  final String featureId;
  final String? expectedReleaseDigest;
  final FeatureReleaseGateway _gateway;
  final FeatureReleaseIdFactory _idFactory;

  FeatureReleaseStatus _status = FeatureReleaseStatus.idle;
  FeatureReleaseDetails? _details;
  FeatureReleaseFailure? _failure;
  _RollbackIntent? _rollbackIntent;

  FeatureReleaseStatus get status => _status;
  FeatureReleaseDetails? get details => _details;
  FeatureReleaseFailure? get failure => _failure;
  bool get isLoading =>
      _status == FeatureReleaseStatus.loading ||
      _status == FeatureReleaseStatus.rollingBack;
  bool get canRollback =>
      _details?.previousVersion != null &&
      _details?.paused == false &&
      _status != FeatureReleaseStatus.rollingBack &&
      !(_status == FeatureReleaseStatus.rollbackFailed &&
          _failure?.retryable != true);

  Future<void> load() async {
    if (_status == FeatureReleaseStatus.loading ||
        _status == FeatureReleaseStatus.rollingBack) {
      return;
    }
    _status = FeatureReleaseStatus.loading;
    _failure = null;
    notifyListeners();
    try {
      final expectedDigest = expectedReleaseDigest;
      if (expectedDigest != null &&
          !isCanonicalFeatureReleaseDigest(expectedDigest)) {
        throw ArgumentError.value(
          expectedDigest,
          'expectedReleaseDigest',
          'Invalid Feature Version digest.',
        );
      }
      final loaded = await _gateway.loadFeature(
        featureId,
        expectedActiveDigest: expectedDigest,
      );
      if (expectedDigest != null &&
          loaded.activeVersion.digest != expectedDigest) {
        throw const ProtocolException(
          'The active Feature Version does not match the requested Version.',
        );
      }
      _details = loaded;
      _rollbackIntent = null;
      _status = FeatureReleaseStatus.ready;
    } on Object catch (error) {
      _failure = _loadFailure(error);
      _status = FeatureReleaseStatus.loadFailed;
    }
    notifyListeners();
  }

  Future<void> retry() {
    if (_status == FeatureReleaseStatus.loadFailed) return load();
    if (_status == FeatureReleaseStatus.rollbackFailed) {
      if (_failure?.reloadable == true) return _reloadAfterTerminalRollback();
      if (_failure?.retryable == true) return rollback();
    }
    return Future.value();
  }

  Future<void> _reloadAfterTerminalRollback() {
    _rollbackIntent = null;
    _details = null;
    return load();
  }

  Future<void> rollback() async {
    if (!canRollback) return;
    final current = _details;
    final target = current?.previousVersion;
    if (current == null || target == null) return;
    final intent = _rollbackIntent ??= _RollbackIntent(
      current: current,
      target: target,
      idempotencyId: _idFactory(),
    );
    _status = FeatureReleaseStatus.rollingBack;
    _failure = null;
    notifyListeners();
    try {
      final restored = await _gateway.rollbackFeature(
        current: intent.current,
        idempotencyId: intent.idempotencyId,
      );
      final revisionAdvancedExactlyOnce =
          intent.current.revision != Int64.MAX_VALUE &&
          restored.revision == intent.current.revision + 1;
      if (restored.featureId != featureId ||
          restored.installationId != intent.current.installationId ||
          restored.originatingRequest.operationId !=
              intent.current.originatingRequest.operationId ||
          restored.originatingRequest.conversationId !=
              intent.current.originatingRequest.conversationId ||
          restored.originatingRequest.text !=
              intent.current.originatingRequest.text ||
          !revisionAdvancedExactlyOnce ||
          restored.paused ||
          restored.previousVersion != null ||
          !restored.activeVersion.exactlyMatches(intent.target)) {
        _failure = const FeatureReleaseFailure(
          message: 'The restored Version could not be verified.',
          retryable: false,
          reloadable: true,
        );
        _status = FeatureReleaseStatus.rollbackFailed;
        notifyListeners();
        return;
      }
      _details = restored;
      _rollbackIntent = null;
      _status = FeatureReleaseStatus.restored;
    } on Object catch (error) {
      _failure = _rollbackFailure(error);
      _status = FeatureReleaseStatus.rollbackFailed;
    }
    notifyListeners();
  }
}

class _RollbackIntent {
  const _RollbackIntent({
    required this.current,
    required this.target,
    required this.idempotencyId,
  });

  final FeatureReleaseDetails current;
  final FeatureReleaseVersion target;
  final String idempotencyId;
}

FeatureReleaseFailure _loadFailure(Object error) {
  if (error is TransportException && error.isTerminal) {
    final message = switch (error.code) {
      TransportErrorCode.notFound => 'This Feature is no longer available.',
      TransportErrorCode.permissionDenied =>
        'You no longer have access to this Feature.',
      TransportErrorCode.protocol =>
        'The Feature response could not be verified.',
      _ => 'This Feature cannot be loaded.',
    };
    return FeatureReleaseFailure(message: message, retryable: false);
  }
  return const FeatureReleaseFailure(
    message: "We couldn't load this Feature. Try again.",
    retryable: true,
  );
}

FeatureReleaseFailure _rollbackFailure(Object error) {
  if (error is TransportException) {
    if (error.code
        case TransportErrorCode.aborted ||
            TransportErrorCode.failedPrecondition ||
            TransportErrorCode.protocol) {
      final message = switch (error.code) {
        TransportErrorCode.protocol =>
          'The rollback response could not be verified. Reload the Feature.',
        _ => 'This rollback is no longer valid. Reload the Feature.',
      };
      return FeatureReleaseFailure(
        message: message,
        retryable: false,
        reloadable: true,
      );
    }
    if (error.isTerminal) {
      final message = switch (error.code) {
        TransportErrorCode.permissionDenied =>
          'You no longer have permission to roll back this Feature.',
        _ => 'This Feature cannot be rolled back.',
      };
      return FeatureReleaseFailure(message: message, retryable: false);
    }
  }
  return const FeatureReleaseFailure(
    message: 'The rollback did not complete. Try again.',
    retryable: true,
  );
}

var _rollbackSequence = 0;

String _nextRollbackId() =>
    'feature-rollback-${DateTime.now().microsecondsSinceEpoch}-${++_rollbackSequence}';
