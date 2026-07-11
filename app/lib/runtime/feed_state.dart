import 'protocol/surface_protocol.dart';
import 'runtime_errors.dart';
import 'session_state.dart';

enum FeedAudience { principal, workspace, public }

sealed class FeedEvent {
  const FeedEvent();
}

class FeedSurfaceJson extends FeedEvent {
  const FeedSurfaceJson(this.surfaceJson);
  final String surfaceJson;
}

class FeedResetEvent extends FeedEvent {
  const FeedResetEvent({
    required this.reason,
    required this.resumeSequence,
    this.snapshotJson = const [],
  });
  final String reason;
  final int resumeSequence;
  final List<String> snapshotJson;
}

abstract interface class FeedCall {
  Stream<FeedEvent> get events;
  Future<void> cancel();
}

abstract interface class FeedTransport {
  Future<FeedCall> watchSurfaceFeed({
    required String accessToken,
    required int afterSequence,
    required FeedAudience audience,
    required Set<String> clientCapabilities,
    required int maxBatchSize,
  });

  Future<void> acknowledgeSurfaceFeed({
    required String accessToken,
    required FeedAudience audience,
    required int sequence,
  });
}

class ActionResult {
  const ActionResult({required this.operationId, required this.idempotencyKey});

  final String operationId;
  final String idempotencyKey;

  @override
  String toString() => 'ActionResult(operation accepted)';
}

abstract interface class ActionTransport {
  Future<ActionResult> submitAction({
    required String accessToken,
    required UiActionRef action,
    required Map<String, Object?> input,
  });
}

abstract interface class UiTransport
    implements SessionTransport, FeedTransport, ActionTransport {
  Future<void> close();
}

sealed class FeedMessage {
  const FeedMessage();
}

class FeedSurface extends FeedMessage {
  const FeedSurface(this.envelope);
  final SurfaceEnvelope envelope;
}

class FeedDuplicate extends FeedMessage {
  const FeedDuplicate(this.reason);
  final String reason;
}

class FeedReset extends FeedMessage {
  const FeedReset(this.reason, {this.resumeSequence = 0});
  final String reason;
  final int resumeSequence;
}

class FeedController {
  FeedController({DateTime Function()? now})
    : _now = now ?? (() => DateTime.now().toUtc());

  final DateTime Function() _now;
  int lastSequence = 0;
  bool needsReset = false;
  SessionIdentity? _identity;
  final Map<String, SurfaceEnvelope> _surfaces = {};

  Iterable<SurfaceEnvelope> get surfaces {
    final result = _surfaces.values.toList()
      ..sort((left, right) => left.feedSequence.compareTo(right.feedSequence));
    return result;
  }

  SurfaceEnvelope? surface(String surfaceId) => _surfaces[surfaceId];

  bool bindIdentity(SessionIdentity identity) {
    final current = _identity;
    final scopeChanged = current != null && !_sameScope(current, identity);
    if (scopeChanged) {
      reset();
    }
    _identity = identity;
    return scopeChanged;
  }

  void clearIdentity() {
    _identity = null;
    reset();
  }

  FeedMessage accept(SurfaceEnvelope envelope) {
    _demandScope(envelope);
    _demandFresh(envelope);
    if (envelope.feedSequence <= lastSequence) {
      return const FeedDuplicate('duplicate-sequence');
    }
    if (needsReset ||
        (lastSequence != 0 && envelope.feedSequence != lastSequence + 1)) {
      needsReset = true;
      return const FeedReset('sequence-gap');
    }

    lastSequence = envelope.feedSequence;
    final current = _surfaces[envelope.surfaceId];
    if (current != null && envelope.revision <= current.revision) {
      return const FeedDuplicate('stale-revision');
    }
    _surfaces[envelope.surfaceId] = envelope;
    return FeedSurface(envelope);
  }

  void applyServerReset(
    FeedResetEvent resetEvent,
    Iterable<SurfaceEnvelope> snapshots,
  ) {
    if (resetEvent.resumeSequence < 0) {
      throw const ProtocolException('Feed reset sequence cannot be negative.');
    }
    final replacement = <String, SurfaceEnvelope>{};
    for (final envelope in snapshots) {
      _demandScope(envelope);
      _demandFresh(envelope);
      if (envelope.feedSequence > resetEvent.resumeSequence) {
        throw const ProtocolException(
          'Feed reset snapshot is newer than its resume sequence.',
        );
      }
      if (replacement.containsKey(envelope.surfaceId)) {
        throw const ProtocolException(
          'Feed reset snapshot contains a duplicate surface.',
        );
      }
      replacement[envelope.surfaceId] = envelope;
    }
    _surfaces
      ..clear()
      ..addAll(replacement);
    lastSequence = resetEvent.resumeSequence;
    needsReset = false;
  }

  void reset() {
    lastSequence = 0;
    needsReset = false;
    _surfaces.clear();
  }

  void acknowledge(int sequence) {
    if (sequence <= 0 || sequence > lastSequence) {
      throw StateError('Cannot acknowledge an unseen feed sequence.');
    }
  }

  void _demandScope(SurfaceEnvelope envelope) {
    final identity = _identity;
    if (identity == null) {
      throw const ScopeViolation('Feed identity is not established.');
    }
    if (envelope.tenantId != identity.tenantId ||
        envelope.workspaceId != identity.workspaceId) {
      throw const ScopeViolation('Surface is outside the signed workspace.');
    }
    final audience = envelope.audience;
    final validAudience = switch (audience.kind) {
      'principal' => audience.id == identity.principalId,
      'workspace' => audience.id == identity.workspaceId,
      'public' => audience.id.isEmpty,
      _ => false,
    };
    if (!validAudience) {
      throw const ScopeViolation('Surface audience does not match session.');
    }
  }

  void _demandFresh(SurfaceEnvelope envelope) {
    if (envelope.isExpired(_now().toUtc())) {
      throw const ProtocolException('Surface is expired.');
    }
  }

  static bool _sameScope(SessionIdentity left, SessionIdentity right) =>
      left.sessionId == right.sessionId &&
      left.tenantId == right.tenantId &&
      left.workspaceId == right.workspaceId &&
      left.principalId == right.principalId;
}
