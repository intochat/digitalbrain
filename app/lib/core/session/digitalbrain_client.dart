import '../../grpc/ui.pb.dart' as wire;
import '../../runtime/runtime_errors.dart';
import '../../runtime/session_state.dart';

typedef AuthenticationRequiredCallback = Future<void> Function();

abstract interface class DigitalBrainTransport implements SessionTransport {
  Future<wire.FeatureDraftReply> getFeatureDraft({
    required String accessToken,
    required wire.GetFeatureDraftRequest request,
  });

  Future<wire.FeatureDraftReply> resetFeatureDraftInstallation({
    required String accessToken,
    required wire.ResetFeatureDraftInstallationRequest request,
  });

  Future<wire.FeatureDraftReply> reviseFeatureDraft({
    required String accessToken,
    required wire.ReviseFeatureDraftRequest request,
  });

  Future<wire.FeatureDraftPatchReply> suggestFeatureChange({
    required String accessToken,
    required wire.SuggestFeatureChangeRequest request,
  });

  Future<wire.FeatureReleaseReviewReply> verifyFeatureDraft({
    required String accessToken,
    required wire.VerifyFeatureDraftRequest request,
  });

  Future<wire.FeatureAccessReviewReply> reviewFeatureAccess({
    required String accessToken,
    required wire.ReviewFeatureAccessRequest request,
  });

  Future<wire.FeatureInstallReply> installFeatureVersion({
    required String accessToken,
    required wire.InstallFeatureVersionRequest request,
  });

  Future<wire.ResumeOriginatingRequestReply> resumeOriginatingRequest({
    required String accessToken,
    required wire.ResumeOriginatingRequestRequest request,
  });

  Future<wire.FeatureReply> getFeature({
    required String accessToken,
    required wire.GetFeatureRequest request,
  });

  Future<wire.FeatureReleaseSourceReply> getFeatureReleaseSource({
    required String accessToken,
    required wire.GetFeatureReleaseSourceRequest request,
  });

  Future<wire.FeatureReply> rollbackFeatureVersion({
    required String accessToken,
    required wire.RollbackFeatureVersionRequest request,
  });

  Future<wire.ListActivityReply> listActivity({
    required String accessToken,
    required wire.ListActivityRequest request,
  });

  Future<wire.RunReply> getRun({
    required String accessToken,
    required wire.GetRunRequest request,
  });
}

abstract interface class FeatureAuthoringClient {
  Future<wire.FeatureDraftReply> getFeatureDraft(
    wire.GetFeatureDraftRequest request,
  );

  Future<wire.FeatureDraftReply> resetFeatureDraftInstallation(
    wire.ResetFeatureDraftInstallationRequest request,
  );

  Future<wire.FeatureDraftReply> reviseFeatureDraft(
    wire.ReviseFeatureDraftRequest request,
  );

  Future<wire.FeatureDraftPatchReply> suggestFeatureChange(
    wire.SuggestFeatureChangeRequest request,
  );

  Future<wire.FeatureReleaseReviewReply> verifyFeatureDraft(
    wire.VerifyFeatureDraftRequest request,
  );

  Future<wire.FeatureAccessReviewReply> reviewFeatureAccess(
    wire.ReviewFeatureAccessRequest request,
  );

  Future<wire.FeatureInstallReply> installFeatureVersion(
    wire.InstallFeatureVersionRequest request,
  );

  Future<wire.FeatureReply> getFeature(wire.GetFeatureRequest request);

  Future<wire.FeatureReleaseSourceReply> getFeatureReleaseSource(
    wire.GetFeatureReleaseSourceRequest request,
  );

  Future<wire.FeatureReply> rollbackFeatureVersion(
    wire.RollbackFeatureVersionRequest request,
  );
}

abstract interface class ActivityClient {
  Future<wire.ListActivityReply> listActivity(wire.ListActivityRequest request);

  Future<wire.RunReply> getRun(wire.GetRunRequest request);
}

class DigitalBrainClient implements FeatureAuthoringClient, ActivityClient {
  const DigitalBrainClient({
    required SessionController session,
    required DigitalBrainTransport transport,
    AuthenticationRequiredCallback? onAuthenticationRequired,
  }) : _session = session,
       _transport = transport,
       _onAuthenticationRequired = onAuthenticationRequired;

  final SessionController _session;
  final DigitalBrainTransport _transport;
  final AuthenticationRequiredCallback? _onAuthenticationRequired;

  @override
  Future<wire.FeatureDraftReply> getFeatureDraft(
    wire.GetFeatureDraftRequest request,
  ) => _authorized(
    (accessToken) =>
        _transport.getFeatureDraft(accessToken: accessToken, request: request),
  );

  @override
  Future<wire.FeatureDraftReply> resetFeatureDraftInstallation(
    wire.ResetFeatureDraftInstallationRequest request,
  ) => _authorized(
    (accessToken) => _transport.resetFeatureDraftInstallation(
      accessToken: accessToken,
      request: request,
    ),
  );

  @override
  Future<wire.FeatureDraftReply> reviseFeatureDraft(
    wire.ReviseFeatureDraftRequest request,
  ) => _authorized(
    (accessToken) => _transport.reviseFeatureDraft(
      accessToken: accessToken,
      request: request,
    ),
  );

  @override
  Future<wire.FeatureDraftPatchReply> suggestFeatureChange(
    wire.SuggestFeatureChangeRequest request,
  ) => _authorized(
    (accessToken) => _transport.suggestFeatureChange(
      accessToken: accessToken,
      request: request,
    ),
  );

  @override
  Future<wire.FeatureReleaseReviewReply> verifyFeatureDraft(
    wire.VerifyFeatureDraftRequest request,
  ) => _authorized(
    (accessToken) => _transport.verifyFeatureDraft(
      accessToken: accessToken,
      request: request,
    ),
  );

  @override
  Future<wire.FeatureAccessReviewReply> reviewFeatureAccess(
    wire.ReviewFeatureAccessRequest request,
  ) => _authorized(
    (accessToken) => _transport.reviewFeatureAccess(
      accessToken: accessToken,
      request: request,
    ),
  );

  @override
  Future<wire.FeatureInstallReply> installFeatureVersion(
    wire.InstallFeatureVersionRequest request,
  ) => _authorized(
    (accessToken) => _transport.installFeatureVersion(
      accessToken: accessToken,
      request: request,
    ),
  );

  Future<wire.ResumeOriginatingRequestReply> resumeOriginatingRequest(
    wire.ResumeOriginatingRequestRequest request,
  ) => _authorized(
    (accessToken) => _transport.resumeOriginatingRequest(
      accessToken: accessToken,
      request: request,
    ),
  );

  @override
  Future<wire.FeatureReply> getFeature(wire.GetFeatureRequest request) =>
      _authorized(
        (accessToken) =>
            _transport.getFeature(accessToken: accessToken, request: request),
      );

  @override
  Future<wire.FeatureReleaseSourceReply> getFeatureReleaseSource(
    wire.GetFeatureReleaseSourceRequest request,
  ) => _authorized(
    (accessToken) => _transport.getFeatureReleaseSource(
      accessToken: accessToken,
      request: request,
    ),
  );

  @override
  Future<wire.FeatureReply> rollbackFeatureVersion(
    wire.RollbackFeatureVersionRequest request,
  ) => _authorized(
    (accessToken) => _transport.rollbackFeatureVersion(
      accessToken: accessToken,
      request: request,
    ),
  );

  @override
  Future<wire.ListActivityReply> listActivity(
    wire.ListActivityRequest request,
  ) => _authorized(
    (accessToken) =>
        _transport.listActivity(accessToken: accessToken, request: request),
  );

  @override
  Future<wire.RunReply> getRun(wire.GetRunRequest request) => _authorized(
    (accessToken) =>
        _transport.getRun(accessToken: accessToken, request: request),
  );

  Future<T> _authorized<T>(Future<T> Function(String accessToken) send) async {
    SessionAccessLease? admissionLease;
    late final SessionAccessLease lease;
    try {
      admissionLease = _session.currentAccessLease();
      lease = await _session.accessLease(_transport);
      _session.validateAccessLease(lease);
    } on AuthenticationException {
      await _requireAuthenticationUnlessFenced(admissionLease);
      rethrow;
    } catch (_) {
      if (_session.status == SessionStatus.expired) {
        await _requireAuthentication();
      }
      rethrow;
    }
    try {
      return await send(lease.accessToken);
    } on AuthenticationException {
      late final SessionAccessLease retryLease;
      try {
        retryLease = await _refreshRejectedToken(lease.accessToken);
        _session.validateAccessLease(retryLease);
      } on AuthenticationException {
        await _requireAuthenticationUnlessFenced(lease);
        rethrow;
      } catch (_) {
        if (_session.status == SessionStatus.expired) {
          await _requireAuthentication();
        }
        rethrow;
      }
      try {
        return await send(retryLease.accessToken);
      } on AuthenticationException {
        await _requireAuthenticationUnlessFenced(retryLease);
        rethrow;
      }
    }
  }

  Future<void> _requireAuthenticationUnlessFenced(
    SessionAccessLease? originatingLease,
  ) async {
    if (_session.status == SessionStatus.signedOut ||
        _session.status == SessionStatus.authenticating ||
        _session.status == SessionStatus.expiring ||
        _session.status == SessionStatus.signingOut) {
      return;
    }
    if (originatingLease != null &&
        (_session.status == SessionStatus.authenticated ||
            _session.status == SessionStatus.refreshing) &&
        !_session.isAccessLeaseCurrent(originatingLease)) {
      return;
    }
    await _requireAuthentication();
  }

  Future<SessionAccessLease> _refreshRejectedToken(String rejectedToken) async {
    final currentLease = await _session.accessLease(_transport);
    if (currentLease.accessToken != rejectedToken) return currentLease;
    await _session.refreshAccessToken(_transport);
    return _session.accessLease(_transport);
  }

  Future<void> _requireAuthentication() async {
    var completedExpiration = _session.status == SessionStatus.expired;
    if (!completedExpiration) {
      completedExpiration = await _session.expireAfterCancellingProductCalls(
        _transport,
      );
    }
    if (completedExpiration && _session.status == SessionStatus.expired) {
      await _onAuthenticationRequired?.call();
    }
  }
}
