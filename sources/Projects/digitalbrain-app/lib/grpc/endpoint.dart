import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:digitalbrain_flutter/telemetry/platform_env.dart';

(String host, int port, bool secure) resolveKernelEndpoint() {
  if (kIsWeb) {
    final u = Uri.base;
    final portParam = u.queryParameters['port'];
    if (portParam != null) {
      final p = int.tryParse(portParam);
      if (p != null) {
        return (u.host, p, u.scheme == 'https');
      }
    }

    final jsPort = getEnv('KERNEL_PORT');
    if (jsPort != null && jsPort.isNotEmpty) {
      final p = int.tryParse(jsPort);
      if (p != null) {
        return (u.host, p, u.scheme == 'https');
      }
    }
  }

  const configured = String.fromEnvironment('KERNEL_ENDPOINT');
  if (configured.isNotEmpty) {
    final u = Uri.parse(configured);
    if (u.host.isEmpty) {
      throw StateError(
        'KERNEL_ENDPOINT="$configured" has no host. Expected an absolute URL, '
        'e.g. https://localhost:59066.',
      );
    }
    if (kIsWeb) {
      return (Uri.base.host, u.port, u.scheme == 'https');
    }
    return (u.host, u.port, u.scheme == 'https');
  }

  if (kIsWeb) {
    final u = Uri.base;
    return (u.host, u.port, u.scheme == 'https');
  }

  final aspireUrl = getEnv('services__kernel__https__0');
  if (aspireUrl == null || aspireUrl.isEmpty) {
    throw StateError(
      'DigitalBrain Windows client requires services__kernel__https__0 env var '
      '(or --dart-define=KERNEL_ENDPOINT). When launched via Aspire '
      '(flutter-windows resource) the kernel URL is injected as the '
      'KERNEL_ENDPOINT dart-define. For standalone `flutter run` set it, '
      r"e.g. flutter run -d windows --dart-define=KERNEL_ENDPOINT='https://localhost:59066'.",
    );
  }
  final u = Uri.parse(aspireUrl);
  return (u.host, u.port, u.scheme == 'https');
}
