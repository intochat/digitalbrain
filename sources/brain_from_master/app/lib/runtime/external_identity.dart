import 'external_identity_contract.dart';
import 'external_identity_stub.dart'
    if (dart.library.js_interop) 'external_identity_web.dart'
    as platform;

export 'external_identity_contract.dart';

ExternalIdentityTokenSource createExternalIdentityTokenSource(
  ExternalIdentityConfiguration configuration,
) => platform.createExternalIdentityTokenSource(configuration);
