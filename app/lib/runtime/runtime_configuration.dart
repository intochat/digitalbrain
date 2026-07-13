import 'package:flutter/foundation.dart' show kIsWeb;

import '../telemetry/platform_env.dart';
import 'external_identity.dart';

const String digitalBrainUiAudience = 'digitalbrain-v2-ui';

class RuntimeConfiguration {
  const RuntimeConfiguration({
    required this.endpoint,
    this.bootstrapSecret,
    this.externalIdentity,
  });

  final Uri endpoint;

  /// A local, scope-limited bootstrap exchange credential supplied to the
  /// desktop process by Aspire. It is not an access token and is never accepted
  /// from a URL or compiled into the Flutter application.
  final String? bootstrapSecret;
  final ExternalIdentityConfiguration? externalIdentity;

  factory RuntimeConfiguration.fromEnvironment() {
    const compiledEndpoint = String.fromEnvironment(
      'DIGITALBRAIN_V2_UI_ENDPOINT',
    );
    final configured = getEnv('DIGITALBRAIN_V2_UI_ENDPOINT');
    final source = configured?.trim().isNotEmpty == true
        ? configured!.trim()
        : compiledEndpoint.trim();
    if (source.isEmpty) {
      throw StateError('DigitalBrain requires DIGITALBRAIN_V2_UI_ENDPOINT.');
    }
    const compiledIssuer = String.fromEnvironment('DIGITALBRAIN_OIDC_ISSUER');
    const compiledClientId = String.fromEnvironment(
      'DIGITALBRAIN_OIDC_CLIENT_ID',
    );
    const compiledScopes = String.fromEnvironment(
      'DIGITALBRAIN_OIDC_SCOPES',
      defaultValue: 'openid profile',
    );
    final externalIdentity = kIsWeb
        ? parseExternalIdentityConfiguration(
            compiledIssuer,
            compiledClientId,
            compiledScopes,
          )
        : null;
    return RuntimeConfiguration(
      endpoint: parseUiEndpoint(source),
      bootstrapSecret: kIsWeb
          ? null
          : _nonEmpty(getEnv('DIGITALBRAIN_V2_UI_BOOTSTRAP_SECRET')),
      externalIdentity: externalIdentity,
    );
  }
}

ExternalIdentityConfiguration parseExternalIdentityConfiguration(
  String issuerSource,
  String clientIdSource,
  String scopesSource,
) {
  final issuer = Uri.tryParse(issuerSource.trim());
  final clientId = clientIdSource.trim();
  final scopes = scopesSource
      .split(RegExp(r'[\s,;]+'))
      .map((scope) => scope.trim())
      .where((scope) => scope.isNotEmpty)
      .toSet();
  if (issuer == null ||
      !issuer.isAbsolute ||
      issuer.scheme != 'https' ||
      issuer.host.isEmpty ||
      issuer.userInfo.isNotEmpty ||
      issuer.hasQuery ||
      issuer.hasFragment ||
      issuerSource.length > 512 ||
      clientId.isEmpty ||
      clientId.length > 512 ||
      clientId.contains(RegExp(r'[\x00-\x1f\x7f]')) ||
      !scopes.contains('openid') ||
      scopes.length > 16 ||
      scopes.any(
        (scope) =>
            scope.length > 128 || scope.contains(RegExp(r'[\x00-\x20\x7f]')),
      )) {
    throw const FormatException(
      'DigitalBrain external identity configuration is invalid.',
    );
  }
  return ExternalIdentityConfiguration(
    issuer: issuer,
    clientId: clientId,
    scopes: scopes,
  );
}

Uri parseUiEndpoint(String source) {
  final endpoint = Uri.tryParse(source);
  if (endpoint == null ||
      !endpoint.isAbsolute ||
      endpoint.host.isEmpty ||
      endpoint.scheme != 'https' ||
      endpoint.userInfo.isNotEmpty ||
      endpoint.hasQuery ||
      endpoint.hasFragment) {
    throw FormatException(
      'DIGITALBRAIN_V2_UI_ENDPOINT must be an absolute HTTPS origin.',
    );
  }
  if (endpoint.path.isNotEmpty && endpoint.path != '/') {
    throw FormatException(
      'DIGITALBRAIN_V2_UI_ENDPOINT must not contain a path.',
    );
  }
  return endpoint.replace(path: '', query: null, fragment: null);
}

String? _nonEmpty(String? value) {
  final trimmed = value?.trim();
  return trimmed == null || trimmed.isEmpty ? null : trimmed;
}
