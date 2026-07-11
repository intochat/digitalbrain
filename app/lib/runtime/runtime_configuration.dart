import 'package:flutter/foundation.dart' show kIsWeb;

import '../telemetry/platform_env.dart';

const String digitalBrainUiAudience = 'digitalbrain-v2-ui';

class RuntimeConfiguration {
  const RuntimeConfiguration({
    required this.endpoint,
    this.bootstrapSecret,
    this.salesforceOAuthStartOrigin,
  });

  final Uri endpoint;

  /// A local, scope-limited bootstrap exchange credential supplied to the
  /// desktop process by Aspire. It is not an access token and is never accepted
  /// from a URL or compiled into the Flutter application.
  final String? bootstrapSecret;
  final Uri? salesforceOAuthStartOrigin;

  factory RuntimeConfiguration.fromEnvironment() {
    const compiledEndpoint = String.fromEnvironment(
      'DIGITALBRAIN_V2_UI_ENDPOINT',
    );
    const compiledSalesforceCallback = String.fromEnvironment(
      'DIGITALBRAIN_SALESFORCE_OAUTH_CALLBACK',
    );
    final configured = getEnv('DIGITALBRAIN_V2_UI_ENDPOINT');
    final source = configured?.trim().isNotEmpty == true
        ? configured!.trim()
        : compiledEndpoint.trim();
    if (source.isEmpty) {
      throw StateError('DigitalBrain requires DIGITALBRAIN_V2_UI_ENDPOINT.');
    }
    final configuredSalesforceCallback = _nonEmpty(
      getEnv('DIGITALBRAIN_SALESFORCE_OAUTH_CALLBACK'),
    );
    final salesforceCallback =
        configuredSalesforceCallback ?? _nonEmpty(compiledSalesforceCallback);
    return RuntimeConfiguration(
      endpoint: parseUiEndpoint(source),
      bootstrapSecret: kIsWeb
          ? null
          : _nonEmpty(getEnv('DIGITALBRAIN_V2_UI_BOOTSTRAP_SECRET')),
      salesforceOAuthStartOrigin: salesforceCallback == null
          ? null
          : parseSalesforceOAuthStartOrigin(salesforceCallback),
    );
  }
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

Uri parseSalesforceOAuthStartOrigin(String source) {
  final callback = Uri.tryParse(source);
  if (callback == null ||
      !callback.isAbsolute ||
      callback.host.isEmpty ||
      callback.userInfo.isNotEmpty ||
      callback.hasQuery ||
      callback.hasFragment ||
      callback.path != '/oauth/callback/salesforce' ||
      (callback.scheme != 'https' &&
          !(callback.scheme == 'http' && _isLoopbackHost(callback.host)))) {
    throw FormatException(
      'DIGITALBRAIN_SALESFORCE_OAUTH_CALLBACK must be an HTTPS callback origin or an HTTP loopback callback.',
    );
  }
  return callback.replace(path: '', query: null, fragment: null);
}

bool _isLoopbackHost(String host) {
  final normalized = host.toLowerCase();
  return normalized == 'localhost' ||
      normalized == '127.0.0.1' ||
      normalized == '::1';
}

String? _nonEmpty(String? value) {
  final trimmed = value?.trim();
  return trimmed == null || trimmed.isEmpty ? null : trimmed;
}
