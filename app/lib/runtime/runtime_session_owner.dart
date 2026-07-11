import 'dart:async';

import 'package:flutter/foundation.dart';

import 'protocol/surface_protocol.dart';
import 'runtime_configuration.dart';
import 'runtime.dart';

typedef UiTransportFactory = UiTransport Function(Uri endpoint);

/// Owns the non-visual lifecycle of a runtime shell session.
class RuntimeSessionOwner extends ChangeNotifier {
  RuntimeSessionOwner({
    RuntimeConfiguration? configuration,
    RuntimeController? controller,
    required UiTransportFactory transportFactory,
    bool autoStart = true,
  }) : _configuration = configuration,
       _providedController = controller,
       _transportFactory = transportFactory,
       _autoStart = autoStart;

  final RuntimeConfiguration? _configuration;
  final RuntimeController? _providedController;
  final UiTransportFactory _transportFactory;
  final bool _autoStart;

  RuntimeController? _controller;
  Object? _initializationError;
  bool _initialized = false;
  bool _ownsController = false;
  bool _closing = false;
  Future<void>? _closeFuture;

  RuntimeController? get controller => _controller;
  Object? get initializationError => _initializationError;

  void initialize() {
    if (_initialized || _closing) return;
    _initialized = true;
    try {
      final configuration =
          _configuration ?? RuntimeConfiguration.fromEnvironment();
      final controller =
          _providedController ?? _createController(configuration);
      _controller = controller;
      _ownsController = _providedController == null;
      controller.addListener(_onControllerChanged);
      notifyListeners();
      if (_autoStart) {
        unawaited(
          _run(
            controller.start(bootstrapSecret: configuration.bootstrapSecret),
          ),
        );
      }
    } catch (error) {
      _initializationError = error;
      notifyListeners();
    }
  }

  RuntimeController _createController(RuntimeConfiguration configuration) =>
      RuntimeController(
        transport: _transportFactory(configuration.endpoint),
        decoder: SurfaceEnvelopeDecoder(
          capabilities: const ClientCapabilities(supportsBinaryRfw: false),
          salesforceOAuthStartOrigin: configuration.salesforceOAuthStartOrigin,
        ),
      );

  void authenticateWithBootstrap(String bootstrapSecret) {
    if (_closing || bootstrapSecret.trim().isEmpty) return;
    final controller = _controller;
    if (controller == null) return;
    unawaited(_run(controller.authenticateWithBootstrap(bootstrapSecret)));
  }

  Future<void> _run(Future<void> operation) async {
    try {
      await operation;
    } catch (_) {
      if (!_closing) notifyListeners();
    }
  }

  void _onControllerChanged() {
    if (!_closing) notifyListeners();
  }

  Future<void> close() => _closeFuture ??= _close();

  Future<void> _close() async {
    _closing = true;
    final controller = _controller;
    controller?.removeListener(_onControllerChanged);
    if (_ownsController && controller != null) {
      try {
        await controller.stop();
      } catch (_) {
        // Shutdown is best-effort after the shell has left the widget tree.
      } finally {
        controller.dispose();
      }
    }
    super.dispose();
  }
}
