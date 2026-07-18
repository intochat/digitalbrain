import 'external_identity_contract.dart';

ExternalIdentityTokenSource createExternalIdentityTokenSource(
  ExternalIdentityConfiguration configuration,
) => _UnsupportedExternalIdentityTokenSource();

class _UnsupportedExternalIdentityTokenSource
    implements ExternalIdentityTokenSource {
  @override
  Future<String?> restoreIdentityToken() async => null;

  @override
  Future<void> beginAuthentication() async {
    throw UnsupportedError(
      'External browser identity is supported by the deployed web client.',
    );
  }
}
