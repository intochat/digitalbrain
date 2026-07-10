import 'protocol/surface_protocol.dart';

enum V2SessionStatus { signedOut, authenticating, authenticated, expired }

class V2SessionController {
  V2SessionStatus status = V2SessionStatus.signedOut;
  String? sessionId;
  String? tenantId;
  String? workspaceId;

  void begin() => status = V2SessionStatus.authenticating;
  void establish({
    required String session,
    required String tenant,
    required String workspace,
  }) {
    sessionId = session;
    tenantId = tenant;
    workspaceId = workspace;
    status = V2SessionStatus.authenticated;
  }

  void expire() {
    sessionId = null;
    status = V2SessionStatus.expired;
  }

  void signOut() {
    sessionId = null;
    tenantId = null;
    workspaceId = null;
    status = V2SessionStatus.signedOut;
  }
}

sealed class V2FeedMessage {
  const V2FeedMessage();
}

class V2FeedSurface extends V2FeedMessage {
  const V2FeedSurface(this.envelope);
  final SurfaceEnvelope envelope;
}

class V2FeedReset extends V2FeedMessage {
  const V2FeedReset(this.reason);
  final String reason;
}

class V2FeedController {
  int lastSequence = 0;
  bool needsReset = false;
  final Map<String, SurfaceEnvelope> _surfaces = {};
  Iterable<SurfaceEnvelope> get surfaces => _surfaces.values;

  V2FeedMessage accept(SurfaceEnvelope envelope) {
    if (envelope.feedSequence <= lastSequence) return V2FeedReset('duplicate');
    if (lastSequence != 0 && envelope.feedSequence != lastSequence + 1) {
      needsReset = true;
      return const V2FeedReset('sequence-gap');
    }
    lastSequence = envelope.feedSequence;
    final current = _surfaces[envelope.surfaceId];
    if (current == null || envelope.revision > current.revision) {
      _surfaces[envelope.surfaceId] = envelope;
    }
    return V2FeedSurface(envelope);
  }

  void reset() {
    lastSequence = 0;
    needsReset = false;
    _surfaces.clear();
  }

  void acknowledge(int sequence) {
    if (sequence > lastSequence) {
      throw StateError('Cannot acknowledge an unseen feed sequence.');
    }
  }
}

abstract interface class V2ActionTransport {
  Future<String> submit(UiActionRef action, Map<String, Object?> input);
}

class V2ActionClient {
  const V2ActionClient(this.transport);
  final V2ActionTransport transport;
  Future<String> invoke(
    SurfaceEnvelope surface,
    String bindingId,
    Map<String, Object?> input, {
    DateTime? now,
  }) {
    final action = surface.actionByBindingId(bindingId);
    if (action == null) {
      throw StateError('Unknown V2 action binding.');
    }
    if (action.isExpired(now ?? DateTime.now().toUtc()) ||
        surface.isExpired(now ?? DateTime.now().toUtc())) {
        throw StateError('V2 action expired.');
      }
    return transport.submit(action, Map.unmodifiable(input));
  }
}
