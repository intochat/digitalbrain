import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:digitalbrain_flutter/telemetry/platform_env.dart';

(String host, int port, bool secure) resolveKernelEndpoint() {
  final base = Uri.base;

  if (kIsWeb) {
    final portParam = base.queryParameters['port'] ?? getEnv('KERNEL_PORT');
    if (portParam != null && portParam.isNotEmpty) {
      final p = int.tryParse(portParam);
      if (p != null) {
        return (base.host, p, base.scheme == 'https');
      }
    }
  }

  const configured = String.fromEnvironment('KERNEL_ENDPOINT');
  final aspireUrl = kIsWeb
      ? null
      : resolveAspireKernelUrl(
          grpcUrl: getEnv('services__kernel__grpc__0'),
          httpsUrl: getEnv('services__kernel__https__0'),
          httpUrl: getEnv('services__kernel__http__0'),
          webUrl: getEnv('services__kernel__web__0'),
        );

  return resolveEndpointFrom(
    isWeb: kIsWeb,
    base: base,
    kernelEndpoint: configured.isEmpty ? null : configured,
    aspireKernelUrl: aspireUrl,
  );
}

/// Endpoint for ordinary HTTP requests such as multipart upload.
///
/// Desktop Aspire exposes the kernel twice: a gRPC HTTP/2-only endpoint and a
/// web endpoint. Multipart upload is HTTP/1.x, so it must prefer the web URL.
(String host, int port, bool secure) resolveKernelUploadEndpoint() {
  final base = Uri.base;

  const configured = String.fromEnvironment('KERNEL_UPLOAD_ENDPOINT');
  final aspireUrl = kIsWeb
      ? null
      : resolveAspireKernelUploadUrl(
          webUrl: getEnv('services__kernel__web__0'),
          httpUrl: getEnv('services__kernel__http__0'),
          httpsUrl: getEnv('services__kernel__https__0'),
          grpcUrl: getEnv('services__kernel__grpc__0'),
        );

  return resolveEndpointFrom(
    isWeb: kIsWeb,
    base: base,
    kernelEndpoint: configured.isEmpty ? null : configured,
    aspireKernelUrl: aspireUrl,
  );
}

String? resolveAspireKernelUrl({
  String? grpcUrl,
  String? httpsUrl,
  String? httpUrl,
  String? webUrl,
}) {
  String? nonEmpty(String? value) {
    if (value == null || value.isEmpty) return null;
    return value;
  }

  return nonEmpty(grpcUrl) ??
      nonEmpty(httpsUrl) ??
      nonEmpty(httpUrl) ??
      nonEmpty(webUrl);
}

String? resolveAspireKernelUploadUrl({
  String? webUrl,
  String? httpUrl,
  String? httpsUrl,
  String? grpcUrl,
}) {
  String? nonEmpty(String? value) {
    if (value == null || value.isEmpty) return null;
    return value;
  }

  return nonEmpty(webUrl) ??
      nonEmpty(httpUrl) ??
      nonEmpty(httpsUrl) ??
      nonEmpty(grpcUrl);
}

(String host, int port, bool secure) resolveEndpointFrom({
  required bool isWeb,
  required Uri base,
  String? kernelEndpoint,
  String? aspireKernelUrl,
}) {
  if (kernelEndpoint != null && kernelEndpoint.isNotEmpty) {
    final u = Uri.parse(kernelEndpoint);
    if (u.host.isEmpty) {
      throw StateError(
        'KERNEL_ENDPOINT="$kernelEndpoint" has no host. Expected an absolute '
        'URL, e.g. https://api.digitalbrain.tech.',
      );
    }
    final port = u.hasPort ? u.port : (u.scheme == 'https' ? 443 : 80);
    return (u.host, port, u.scheme == 'https');
  }

  if (isWeb) {
    final port = base.hasPort ? base.port : (base.scheme == 'https' ? 443 : 80);
    return (base.host, port, base.scheme == 'https');
  }

  if (aspireKernelUrl == null || aspireKernelUrl.isEmpty) {
    throw StateError(
      'DigitalBrain desktop client requires services__kernel__https__0 '
      '(or --dart-define=KERNEL_ENDPOINT). Set it, e.g. '
      r"flutter run -d windows --dart-define=KERNEL_ENDPOINT='https://localhost:59066'.",
    );
  }
  final u = Uri.parse(aspireKernelUrl);
  return (u.host, u.port, u.scheme == 'https');
}
