import 'package:openid_client/openid_client_browser.dart' as oidc;

import 'external_identity_contract.dart';

ExternalIdentityTokenSource createExternalIdentityTokenSource(
  ExternalIdentityConfiguration configuration,
) => _BrowserExternalIdentityTokenSource(configuration);

class _BrowserExternalIdentityTokenSource
    implements ExternalIdentityTokenSource {
  _BrowserExternalIdentityTokenSource(this._configuration);

  final ExternalIdentityConfiguration _configuration;
  Future<oidc.Authenticator>? _pendingAuthenticator;

  Future<oidc.Authenticator> _authenticator() =>
      _pendingAuthenticator ??= _createAuthenticator();

  Future<oidc.Authenticator> _createAuthenticator() async {
    final issuer = await oidc.Issuer.discover(_configuration.issuer);
    final client = oidc.Client(issuer, _configuration.clientId);
    return oidc.Authenticator(client, scopes: _configuration.scopes);
  }

  @override
  Future<String?> restoreIdentityToken() async {
    final credential = await (await _authenticator()).credential;
    if (credential == null) return null;
    final violations = await credential.validateToken().toList();
    if (violations.isNotEmpty) {
      throw StateError('The external identity token was rejected.');
    }
    final token = credential.idToken.toCompactSerialization();
    if (token.length < 32 ||
        token.length > 8 * 1024 - 7 ||
        token.trim() != token ||
        !RegExp(
          r'^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$',
        ).hasMatch(token)) {
      throw StateError('The external identity token was rejected.');
    }
    return token;
  }

  @override
  Future<void> beginAuthentication() async {
    (await _authenticator()).authorize();
  }
}
