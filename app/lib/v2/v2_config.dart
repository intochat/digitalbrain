import 'package:flutter/foundation.dart' show kIsWeb;

import '../telemetry/platform_env.dart';

const String digitalBrainV2UiAudience = 'digitalbrain-v2-ui';

bool isV2Runtime({String? runtime}) {
  const compiled = String.fromEnvironment('DIGITALBRAIN_RUNTIME');
  final value = runtime ?? getEnv('DIGITALBRAIN_RUNTIME') ?? compiled;
  return value.trim().toUpperCase() == 'V2';
}

class V2RuntimeConfiguration {
  const V2RuntimeConfiguration({required this.endpoint, this.bootstrapSecret});

  final Uri endpoint;

  /// A local, scope-limited bootstrap exchange credential supplied to the
  /// desktop process by Aspire. It is not an access token and is never accepted
  /// from a URL or compiled into the Flutter application.
  final String? bootstrapSecret;

  factory V2RuntimeConfiguration.fromEnvironment() {
    const compiledEndpoint = String.fromEnvironment(
      'DIGITALBRAIN_V2_UI_ENDPOINT',
    );
    final configured = getEnv('DIGITALBRAIN_V2_UI_ENDPOINT');
    final source = configured?.trim().isNotEmpty == true
        ? configured!.trim()
        : compiledEndpoint.trim();
    if (source.isEmpty) {
      throw StateError('Runtime V2 requires DIGITALBRAIN_V2_UI_ENDPOINT.');
    }
    return V2RuntimeConfiguration(
      endpoint: parseV2UiEndpoint(source),
      bootstrapSecret: kIsWeb
          ? null
          : _nonEmpty(getEnv('DIGITALBRAIN_V2_UI_BOOTSTRAP_SECRET')),
    );
  }
}

Uri parseV2UiEndpoint(String source) {
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
