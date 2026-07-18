class ExternalIdentityConfiguration {
  const ExternalIdentityConfiguration({
    required this.issuer,
    required this.clientId,
    this.scopes = const {'openid', 'profile'},
  });

  final Uri issuer;
  final String clientId;
  final Set<String> scopes;
}

abstract interface class ExternalIdentityTokenSource {
  Future<String?> restoreIdentityToken();
  Future<void> beginAuthentication();
}
