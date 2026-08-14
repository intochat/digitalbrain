import 'process_environment_stub.dart'
    if (dart.library.io) 'process_environment_io.dart' as process_environment;

abstract final class DigitalBrainHostEnvironment {
  static const productBaseVariable = 'DIGITALBRAIN_PRODUCT_BASE';
  static const shellVariable = 'DIGITALBRAIN_SHELL';
  static const defaultShell = 'corev2';

  static Uri requireProductBase({
    String fromDefine = const String.fromEnvironment(productBaseVariable),
    Map<String, String>? processEnvironment,
  }) {
    final raw = fromDefine.isNotEmpty
        ? fromDefine
        : (processEnvironment ?? process_environment.readProcessEnvironment())[
                  productBaseVariable
              ] ??
              '';
    if (raw.isEmpty) {
      throw StateError(
        '$productBaseVariable is required and is injected by Aspire.',
      );
    }

    final uri = Uri.tryParse(raw);
    if (uri == null || !uri.hasScheme || uri.host.isEmpty) {
      throw FormatException('$productBaseVariable must be an absolute URI.', raw);
    }

    return uri;
  }

  static String resolveShell({
    String fromDefine = const String.fromEnvironment(shellVariable),
    Map<String, String>? processEnvironment,
  }) {
    if (fromDefine.isNotEmpty) {
      return fromDefine;
    }

    final raw = (processEnvironment ??
            process_environment.readProcessEnvironment())[shellVariable] ??
        '';
    return raw.isEmpty ? defaultShell : raw;
  }
}
