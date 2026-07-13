import 'dart:async';

import 'package:flutter/foundation.dart';

import 'external_identity.dart';
import 'protocol/surface_protocol.dart';
import 'runtime_configuration.dart';
import 'runtime.dart';

typedef UiTransportFactory = UiTransport Function(Uri endpoint);
typedef ExternalIdentityTokenSourceFactory =
    ExternalIdentityTokenSource Function(
      ExternalIdentityConfiguration configuration,
    );

/// Owns the non-visual lifecycle of a runtime shell session.
class RuntimeSessionOwner extends ChangeNotifier {
  RuntimeSessionOwner({
    RuntimeConfiguration? configuration,
    RuntimeController? controller,
    required UiTransportFactory transportFactory,
    ExternalIdentityTokenSourceFactory? externalIdentityTokenSourceFactory,
    bool autoStart = true,
  }) : _configuration = configuration,
       _providedController = controller,
       _transportFactory = transportFactory,
       _externalIdentityTokenSourceFactory =
           externalIdentityTokenSourceFactory ??
           createExternalIdentityTokenSource,
       _autoStart = autoStart;

  final RuntimeConfiguration? _configuration;
  final RuntimeController? _providedController;
  final UiTransportFactory _transportFactory;
  final ExternalIdentityTokenSourceFactory _externalIdentityTokenSourceFactory;
  final bool _autoStart;

  RuntimeController? _controller;
  Object? _initializationError;
  bool _initialized = false;
  bool _ownsController = false;
  bool _closing = false;
  Future<void>? _closeFuture;
  ExternalIdentityTokenSource? _externalIdentity;

  RuntimeController? get controller => _controller;
  Object? get initializationError => _initializationError;
  bool get hasExternalIdentity => _externalIdentity != null;

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
      final externalConfiguration = configuration.externalIdentity;
      if (externalConfiguration != null) {
        _externalIdentity = _externalIdentityTokenSourceFactory(
          externalConfiguration,
        );
      }
      controller.addListener(_onControllerChanged);
      notifyListeners();
      if (_autoStart) {
        unawaited(_run(_start(controller, configuration.bootstrapSecret)));
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
          oauthStartOrigin: configuration.endpoint,
        ),
      );

  Future<void> _start(
    RuntimeController controller,
    String? bootstrapSecret,
  ) async {
    if (bootstrapSecret != null && bootstrapSecret.trim().isNotEmpty) {
      await controller.start(bootstrapSecret: bootstrapSecret);
      return;
    }
    final externalIdentity = _externalIdentity;
    if (externalIdentity == null) {
      await controller.start();
      return;
    }
    try {
      final token = await externalIdentity.restoreIdentityToken();
      if (token == null) {
        await controller.start();
      } else {
        await controller.authenticateWithExternalIdentityToken(token);
      }
    } catch (_) {
      await controller.start();
      rethrow;
    }
  }

  void authenticateWithBootstrap(String bootstrapSecret) {
    if (_closing || bootstrapSecret.trim().isEmpty) return;
    final controller = _controller;
    if (controller == null) return;
    unawaited(_run(controller.authenticateWithBootstrap(bootstrapSecret)));
  }

  void authenticateWithExternalIdentity() {
    if (_closing) return;
    final controller = _controller;
    final externalIdentity = _externalIdentity;
    if (controller == null || externalIdentity == null) return;
    unawaited(
      _run(() async {
        final token = await externalIdentity.restoreIdentityToken();
        if (token != null) {
          await controller.authenticateWithExternalIdentityToken(token);
          return;
        }
        await externalIdentity.beginAuthentication();
      }()),
    );
  }

  void signOut() {
    if (_closing) return;
    final controller = _controller;
    if (controller == null) return;
    unawaited(_run(controller.signOut()));
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
