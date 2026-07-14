import 'runtime_errors.dart';

enum SessionStatus {
  signedOut,
  authenticating,
  authenticated,
  refreshing,
  expired,
}

class SessionIdentity {
  const SessionIdentity({
    required this.sessionId,
    required this.ownerId,
    required this.actorId,
  });

  final String sessionId;
  final String ownerId;
  final String actorId;

  @override
  String toString() => 'SessionIdentity([private])';
}

class SessionCredentials {
  const SessionCredentials({
    required this.accessToken,
    required this.refreshToken,
    required this.accessExpiresAt,
    required this.refreshExpiresAt,
  });

  final String accessToken;
  final String refreshToken;
  final DateTime accessExpiresAt;
  final DateTime refreshExpiresAt;

  @override
  String toString() => 'SessionCredentials([REDACTED])';
}

class SessionBundle {
  const SessionBundle({required this.identity, required this.credentials});

  final SessionIdentity identity;
  final SessionCredentials credentials;

  @override
  String toString() => 'SessionBundle([private])';
}

abstract interface class SessionTransport {
  Future<SessionBundle> bootstrapSession(String bootstrapSecret);

  Future<SessionBundle> refreshSession({required String refreshToken});

  Future<void> logout({required String refreshToken});
}

abstract interface class ExternalSessionTransport {
  Future<SessionBundle> bootstrapExternalSession(String identityToken);
}

class SessionController {
  SessionController({DateTime Function()? now})
    : _now = now ?? (() => DateTime.now().toUtc());

  final DateTime Function() _now;
  SessionBundle? _bundle;
  int _bundleVersion = 0;
  int _bootstrapGeneration = 0;
  Future<String>? _refreshInFlight;
  int? _refreshBundleVersion;

  SessionStatus status = SessionStatus.signedOut;
  Object? lastError;

  SessionIdentity? get identity => _bundle?.identity;
  String? get sessionId => identity?.sessionId;
  String? get ownerId => identity?.ownerId;
  String? get actorId => identity?.actorId;
  bool get isAuthenticated =>
      status == SessionStatus.authenticated && _bundle != null;

  void begin() {
    lastError = null;
    status = SessionStatus.authenticating;
  }

  void establish(SessionBundle bundle) {
    _validate(bundle);
    _bootstrapGeneration++;
    _bundle = bundle;
    _bundleVersion++;
    lastError = null;
    status = SessionStatus.authenticated;
  }

  Future<bool> bootstrap(
    SessionTransport transport,
    String bootstrapSecret,
  ) async {
    if (bootstrapSecret.trim().isEmpty) {
      throw ArgumentError.value(
        bootstrapSecret,
        'bootstrapSecret',
        'A bootstrap secret is required.',
      );
    }
    return _bootstrap(() => transport.bootstrapSession(bootstrapSecret));
  }

  Future<bool> bootstrapExternal(
    ExternalSessionTransport transport,
    String identityToken,
  ) => _bootstrap(() => transport.bootstrapExternalSession(identityToken));

  Future<bool> _bootstrap(Future<SessionBundle> Function() establish) async {
    final generation = ++_bootstrapGeneration;
    _bundle = null;
    _bundleVersion++;
    begin();
    try {
      final established = await establish();
      if (generation != _bootstrapGeneration) return false;
      this.establish(established);
      return true;
    } catch (error) {
      if (generation != _bootstrapGeneration) return false;
      _bundle = null;
      _bundleVersion++;
      lastError = error;
      status = SessionStatus.signedOut;
      rethrow;
    }
  }

  Future<String> accessToken(
    SessionTransport transport, {
    Duration refreshSkew = const Duration(seconds: 30),
  }) async {
    var bundle = _bundle;
    if (bundle == null) throw const AuthenticationException();
    final refreshInFlight = _currentRefresh;
    if (refreshInFlight != null) return refreshInFlight;
    final now = _now().toUtc();
    if (bundle.credentials.refreshExpiresAt.isBefore(now) ||
        bundle.credentials.refreshExpiresAt.isAtSameMomentAs(now)) {
      expire();
      throw const AuthenticationException('Runtime session refresh expired.');
    }
    if (bundle.credentials.accessExpiresAt.isAfter(now.add(refreshSkew))) {
      return bundle.credentials.accessToken;
    }

    return refreshAccessToken(transport);
  }

  Future<String> refreshAccessToken(SessionTransport transport) async {
    final refreshInFlight = _currentRefresh;
    if (refreshInFlight != null) return refreshInFlight;

    final bundle = _bundle;
    if (bundle == null) throw const AuthenticationException();
    final now = _now().toUtc();
    if (!bundle.credentials.refreshExpiresAt.isAfter(now)) {
      expire();
      throw const AuthenticationException('Runtime session refresh expired.');
    }

    status = SessionStatus.refreshing;
    final bundleVersion = _bundleVersion;
    late final Future<String> refresh;
    refresh = _refreshAccessToken(transport, bundle, bundleVersion)
        .whenComplete(() {
          if (identical(_refreshInFlight, refresh)) {
            _refreshInFlight = null;
            _refreshBundleVersion = null;
          }
        });
    _refreshInFlight = refresh;
    _refreshBundleVersion = bundleVersion;
    return refresh;
  }

  Future<String> _refreshAccessToken(
    SessionTransport transport,
    SessionBundle bundle,
    int bundleVersion,
  ) async {
    try {
      final refreshed = await transport.refreshSession(
        refreshToken: bundle.credentials.refreshToken,
      );
      if (!_sameIdentity(bundle.identity, refreshed.identity)) {
        throw const ProtocolException(
          'Session refresh changed the authenticated identity.',
        );
      }
      if (!_isCurrent(bundle, bundleVersion)) {
        final current = _bundle;
        if (current == null ||
            !_sameIdentity(bundle.identity, current.identity)) {
          throw const AuthenticationException();
        }
        return current.credentials.accessToken;
      }
      establish(refreshed);
      return refreshed.credentials.accessToken;
    } catch (error) {
      if (_isCurrent(bundle, bundleVersion)) {
        lastError = error;
        expire();
      }
      rethrow;
    }
  }

  Future<String>? get _currentRefresh =>
      _refreshBundleVersion == _bundleVersion ? _refreshInFlight : null;

  bool _isCurrent(SessionBundle bundle, int bundleVersion) =>
      _bundleVersion == bundleVersion && identical(_bundle, bundle);

  void expire() {
    _bootstrapGeneration++;
    _bundle = null;
    _bundleVersion++;
    status = SessionStatus.expired;
  }

  Future<void> signOut(SessionTransport transport) async {
    final bundle = _bundle;
    final refreshToken = bundle?.credentials.refreshToken;
    _bootstrapGeneration++;
    if (refreshToken == null) {
      _bundle = null;
      _bundleVersion++;
      lastError = null;
      status = SessionStatus.signedOut;
      return;
    }
    try {
      await transport.logout(refreshToken: refreshToken);
    } catch (error) {
      if (identical(_bundle, bundle)) {
        lastError = error;
        status = SessionStatus.authenticated;
      }
      rethrow;
    }
    if (identical(_bundle, bundle)) {
      _bundle = null;
      _bundleVersion++;
      lastError = null;
      status = SessionStatus.signedOut;
    }
  }

  void _validate(SessionBundle bundle) {
    final identity = bundle.identity;
    final credentials = bundle.credentials;
    if (identity.sessionId.trim().isEmpty ||
        identity.ownerId.trim().isEmpty ||
        identity.actorId.trim().isEmpty ||
        credentials.accessToken.trim().isEmpty ||
        credentials.refreshToken.trim().isEmpty) {
      throw const ProtocolException('Session response is incomplete.');
    }
    if (!credentials.accessExpiresAt.isAfter(_now().toUtc()) ||
        !credentials.refreshExpiresAt.isAfter(credentials.accessExpiresAt)) {
      throw const ProtocolException('Session response has invalid expiry.');
    }
  }

  static bool _sameIdentity(SessionIdentity left, SessionIdentity right) =>
      left.sessionId == right.sessionId &&
      left.ownerId == right.ownerId &&
      left.actorId == right.actorId;
}
