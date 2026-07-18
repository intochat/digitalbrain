import 'runtime_errors.dart';

enum SessionStatus {
  signedOut,
  authenticating,
  authenticated,
  refreshing,
  expiring,
  signingOut,
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

class SessionAccessLease {
  const SessionAccessLease._({
    required this.accessToken,
    required SessionController session,
    required SessionBundle bundle,
    required int bundleVersion,
    required int authenticationGeneration,
  }) : _session = session,
       _bundle = bundle,
       _bundleVersion = bundleVersion,
       _authenticationGeneration = authenticationGeneration;

  final String accessToken;
  final SessionController _session;
  final SessionBundle _bundle;
  final int _bundleVersion;
  final int _authenticationGeneration;

  @override
  String toString() => 'SessionAccessLease([REDACTED])';
}

abstract interface class SessionTransport {
  Future<SessionBundle> login({
    required String username,
    required String password,
  });

  Future<SessionBundle> refreshSession({required String refreshToken});

  Future<void> logout({required String refreshToken});
}

abstract interface class ExternalSessionTransport {
  Future<SessionBundle> loginExternal(String identityToken);
}

abstract interface class SessionProductCallCancellation {
  Future<void> cancelProductCalls();
}

class SessionController {
  SessionController({DateTime Function()? now})
    : _now = now ?? (() => DateTime.now().toUtc());

  final DateTime Function() _now;
  SessionBundle? _bundle;
  int _bundleVersion = 0;
  int _authenticationGeneration = 0;
  Future<String>? _refreshInFlight;
  int? _refreshBundleVersion;

  SessionStatus status = SessionStatus.signedOut;
  Object? lastError;

  SessionIdentity? get identity => _bundle?.identity;
  String? get sessionId => identity?.sessionId;
  String? get ownerId => identity?.ownerId;
  String? get actorId => identity?.actorId;
  bool get isAuthenticated =>
      (status == SessionStatus.authenticated ||
          status == SessionStatus.refreshing) &&
      _bundle != null;

  void begin() {
    lastError = null;
    status = SessionStatus.authenticating;
  }

  void establish(SessionBundle bundle) {
    _validate(bundle);
    _authenticationGeneration++;
    _bundle = bundle;
    _bundleVersion++;
    lastError = null;
    status = SessionStatus.authenticated;
  }

  Future<bool> login(
    SessionTransport transport, {
    required String username,
    required String password,
  }) async {
    if (username.trim().isEmpty) {
      throw ArgumentError.value(
        username,
        'username',
        'A username is required.',
      );
    }
    if (password.isEmpty) {
      throw ArgumentError.value(
        password,
        'password',
        'A password is required.',
      );
    }
    return _establish(
      () => transport.login(username: username, password: password),
    );
  }

  Future<bool> loginExternal(
    ExternalSessionTransport transport,
    String identityToken,
  ) => _establish(() => transport.loginExternal(identityToken));

  Future<bool> _establish(Future<SessionBundle> Function() establish) async {
    final generation = ++_authenticationGeneration;
    _bundle = null;
    _bundleVersion++;
    begin();
    try {
      final established = await establish();
      if (generation != _authenticationGeneration) return false;
      this.establish(established);
      return true;
    } catch (error) {
      if (generation != _authenticationGeneration) return false;
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
    if (status == SessionStatus.expiring ||
        status == SessionStatus.signingOut) {
      throw const AuthenticationException();
    }
    var bundle = _bundle;
    if (bundle == null) throw const AuthenticationException();
    final refreshInFlight = _currentRefresh;
    if (refreshInFlight != null) return refreshInFlight;
    final now = _now().toUtc();
    if (bundle.credentials.refreshExpiresAt.isBefore(now) ||
        bundle.credentials.refreshExpiresAt.isAtSameMomentAs(now)) {
      await expireAfterCancellingProductCalls(transport);
      throw const AuthenticationException('Runtime session refresh expired.');
    }
    if (bundle.credentials.accessExpiresAt.isAfter(now.add(refreshSkew))) {
      return bundle.credentials.accessToken;
    }

    return refreshAccessToken(transport);
  }

  Future<SessionAccessLease> accessLease(
    SessionTransport transport, {
    Duration refreshSkew = const Duration(seconds: 30),
  }) async {
    final accessToken = await this.accessToken(
      transport,
      refreshSkew: refreshSkew,
    );
    final lease = currentAccessLease();
    if (lease.accessToken != accessToken) {
      throw const AuthenticationException();
    }
    return lease;
  }

  SessionAccessLease currentAccessLease() {
    final bundle = _bundle;
    if (bundle == null ||
        (status != SessionStatus.authenticated &&
            status != SessionStatus.refreshing)) {
      throw const AuthenticationException();
    }
    return SessionAccessLease._(
      accessToken: bundle.credentials.accessToken,
      session: this,
      bundle: bundle,
      bundleVersion: _bundleVersion,
      authenticationGeneration: _authenticationGeneration,
    );
  }

  void validateAccessLease(SessionAccessLease lease) {
    if (!isAccessLeaseCurrent(lease)) {
      throw const AuthenticationException();
    }
  }

  bool isAccessLeaseCurrent(SessionAccessLease lease) =>
      identical(lease._session, this) &&
      identical(lease._bundle, _bundle) &&
      lease._bundleVersion == _bundleVersion &&
      lease._authenticationGeneration == _authenticationGeneration &&
      (status == SessionStatus.authenticated ||
          status == SessionStatus.refreshing) &&
      lease._bundle.credentials.accessToken == lease.accessToken;

  Future<String> refreshAccessToken(SessionTransport transport) async {
    if (status == SessionStatus.expiring ||
        status == SessionStatus.signingOut) {
      throw const AuthenticationException();
    }
    final refreshInFlight = _currentRefresh;
    if (refreshInFlight != null) return refreshInFlight;

    final bundle = _bundle;
    if (bundle == null) throw const AuthenticationException();
    final now = _now().toUtc();
    if (!bundle.credentials.refreshExpiresAt.isAfter(now)) {
      await expireAfterCancellingProductCalls(transport);
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
        if (status == SessionStatus.expiring ||
            status == SessionStatus.signingOut ||
            current == null ||
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
        if (error is AuthenticationException || error is ProtocolException) {
          await expireAfterCancellingProductCalls(transport);
        } else {
          status = SessionStatus.authenticated;
        }
      }
      rethrow;
    }
  }

  Future<String>? get _currentRefresh =>
      _refreshBundleVersion == _bundleVersion ? _refreshInFlight : null;

  bool _isCurrent(SessionBundle bundle, int bundleVersion) =>
      _bundleVersion == bundleVersion && identical(_bundle, bundle);

  void expire() {
    _authenticationGeneration++;
    _bundle = null;
    _bundleVersion++;
    status = SessionStatus.expired;
  }

  void beginExpiration() {
    if (status == SessionStatus.expired || status == SessionStatus.expiring) {
      return;
    }
    _authenticationGeneration++;
    _bundleVersion++;
    status = SessionStatus.expiring;
  }

  Future<void> signOut(SessionTransport transport) async {
    final bundle = _bundle;
    final refreshToken = bundle?.credentials.refreshToken;
    _authenticationGeneration++;
    _bundleVersion++;
    status = SessionStatus.signingOut;
    if (refreshToken == null) {
      _bundle = null;
      _bundleVersion++;
      lastError = null;
      status = SessionStatus.signedOut;
      return;
    }
    try {
      await _cancelProductCalls(transport);
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

  Future<bool> expireAfterCancellingProductCalls(
    SessionTransport transport,
  ) async {
    beginExpiration();
    final authenticationGeneration = _authenticationGeneration;
    final bundleVersion = _bundleVersion;
    var completed = false;
    try {
      await _cancelProductCalls(transport);
    } finally {
      if (status == SessionStatus.expiring &&
          _authenticationGeneration == authenticationGeneration &&
          _bundleVersion == bundleVersion) {
        expire();
        completed = true;
      }
    }
    return completed;
  }

  static Future<void> _cancelProductCalls(SessionTransport transport) async {
    if (transport case final SessionProductCallCancellation cancellation) {
      await cancellation.cancelProductCalls();
    }
  }
}
