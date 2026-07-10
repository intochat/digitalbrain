import 'dart:async';

import 'package:flutter/material.dart';

import '../protocol/surface_protocol.dart';
import '../v2_config.dart';
import '../v2_grpc_transport.dart';
import '../v2_runtime.dart';
import 'v2_surface_view.dart';

typedef V2TransportFactory = V2UiTransport Function(Uri endpoint);

DateTime _utcNow() => DateTime.now().toUtc();

const Key v2RuntimeLoadingKey = Key('v2-runtime-loading');
const Key v2RuntimeSignInKey = Key('v2-runtime-sign-in');
const Key v2RuntimeSecretFieldKey = Key('v2-runtime-secret-field');
const Key v2RuntimeSignInButtonKey = Key('v2-runtime-sign-in-button');
const Key v2RuntimeSurfaceKey = Key('v2-runtime-surface');
const Key v2RuntimeTerminalErrorKey = Key('v2-runtime-terminal-error');

class V2RuntimeShell extends StatefulWidget {
  const V2RuntimeShell({
    super.key,
    this.configuration,
    this.controller,
    this.transportFactory = V2GrpcUiTransport.connect,
    this.autoStart = true,
    this.now = _utcNow,
  });

  final V2RuntimeConfiguration? configuration;
  final V2RuntimeController? controller;
  final V2TransportFactory transportFactory;
  final bool autoStart;
  final DateTime Function() now;

  @override
  State<V2RuntimeShell> createState() => _V2RuntimeShellState();
}

class _V2RuntimeShellState extends State<V2RuntimeShell> {
  final TextEditingController _secret = TextEditingController();
  V2RuntimeController? _controller;
  bool _ownsController = false;
  Object? _initializationError;
  Timer? _surfaceExpiryTimer;
  bool _firstSurfaceFrameReported = false;
  bool _firstSurfaceFrameScheduled = false;

  @override
  void initState() {
    super.initState();
    scheduleMicrotask(_initialize);
  }

  void _initialize() {
    if (!mounted) return;
    try {
      final configuration =
          widget.configuration ?? V2RuntimeConfiguration.fromEnvironment();
      final controller =
          widget.controller ??
          V2RuntimeController(
            transport: widget.transportFactory(configuration.endpoint),
          );
      _controller = controller;
      _ownsController = widget.controller == null;
      controller.addListener(_onControllerChanged);
      setState(() {});
      if (widget.autoStart) {
        unawaited(
          controller
              .start(bootstrapSecret: configuration.bootstrapSecret)
              .catchError(_onStartError),
        );
      }
    } catch (error) {
      setState(() => _initializationError = error);
    }
  }

  void _onControllerChanged() {
    if (!mounted) return;
    _scheduleSurfaceExpiry();
    setState(() {});
    final hasSurface = _renderableSurface(_controller) != null;
    if (hasSurface &&
        !_firstSurfaceFrameReported &&
        !_firstSurfaceFrameScheduled) {
      _firstSurfaceFrameScheduled = true;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        _firstSurfaceFrameScheduled = false;
        if (!mounted || _firstSurfaceFrameReported) return;
        _firstSurfaceFrameReported = true;
        debugPrint('DigitalBrain rendered the first authenticated view');
      });
    }
  }

  void _onStartError(Object error) {
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    _surfaceExpiryTimer?.cancel();
    _secret.dispose();
    final controller = _controller;
    controller?.removeListener(_onControllerChanged);
    if (_ownsController && controller != null) controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final initializationError = _initializationError;
    if (initializationError != null) {
      return _errorScaffold(
        'DigitalBrain could not start. Please try again.',
        key: v2RuntimeTerminalErrorKey,
      );
    }
    final controller = _controller;
    if (controller == null) {
      return const Scaffold(
        body: Center(
          child: CircularProgressIndicator(key: v2RuntimeLoadingKey),
        ),
      );
    }

    if (controller.status == V2RuntimeStatus.awaitingSignIn) {
      return _buildSignIn(controller);
    }
    final surface = _renderableSurface(controller);
    if (controller.status == V2RuntimeStatus.terminalError && surface == null) {
      return _errorScaffold(
        'DigitalBrain is unavailable right now. Please try again.',
        key: v2RuntimeTerminalErrorKey,
      );
    }

    return Scaffold(
      body: Stack(
        fit: StackFit.expand,
        children: [
          if (surface == null)
            _buildWaiting(controller)
          else
            KeyedSubtree(
              key: ValueKey<int>(controller.scopeEpoch),
              child: V2SurfaceView(
                key: v2RuntimeSurfaceKey,
                surface: surface,
                onSubmitAction: controller.submitAction,
                actionEnabled: controller.canSubmitActionsFrom(surface),
                reconnecting: controller.status == V2RuntimeStatus.reconnecting,
                connectionUnavailable:
                    controller.status == V2RuntimeStatus.terminalError,
              ),
            ),
        ],
      ),
    );
  }

  SurfaceEnvelope? _renderableSurface(V2RuntimeController? controller) {
    final surface = controller?.latestSurface;
    if (surface == null || surface.isExpired(widget.now().toUtc())) {
      return null;
    }
    return surface;
  }

  void _scheduleSurfaceExpiry() {
    _surfaceExpiryTimer?.cancel();
    final expiresAt = _controller?.latestSurface?.expiresAt;
    if (expiresAt == null) return;
    final remaining = expiresAt.difference(widget.now().toUtc());
    if (remaining <= Duration.zero) return;
    _surfaceExpiryTimer = Timer(remaining, () {
      if (mounted) setState(() {});
    });
  }

  Widget _buildWaiting(V2RuntimeController controller) {
    final message = switch (controller.status) {
      V2RuntimeStatus.authenticating => 'Signing you in…',
      V2RuntimeStatus.connecting => 'Opening your workspace…',
      V2RuntimeStatus.reconnecting => 'Reconnecting…',
      _ => 'Preparing your workspace…',
    };
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const CircularProgressIndicator(key: v2RuntimeLoadingKey),
          const SizedBox(height: 16),
          Text(message),
          if (controller.transientError != null) ...[
            const SizedBox(height: 8),
            const Text(
              'DigitalBrain is taking longer than expected. We\'ll keep trying.',
              textAlign: TextAlign.center,
            ),
          ],
        ],
      ),
    );
  }

  Widget _buildSignIn(V2RuntimeController controller) {
    return Scaffold(
      key: v2RuntimeSignInKey,
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 420),
          child: Card(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    'Sign in to DigitalBrain',
                    style: Theme.of(context).textTheme.headlineSmall,
                  ),
                  const SizedBox(height: 12),
                  const Text(
                    'Enter the sign-in code supplied by your '
                    'DigitalBrain administrator.',
                  ),
                  const SizedBox(height: 16),
                  TextField(
                    key: v2RuntimeSecretFieldKey,
                    controller: _secret,
                    obscureText: true,
                    enableSuggestions: false,
                    autocorrect: false,
                    onSubmitted: (_) => _authenticate(controller),
                    decoration: const InputDecoration(
                      labelText: 'Sign-in code',
                    ),
                  ),
                  const SizedBox(height: 16),
                  FilledButton(
                    key: v2RuntimeSignInButtonKey,
                    onPressed:
                        controller.status == V2RuntimeStatus.authenticating
                        ? null
                        : () => _authenticate(controller),
                    child: const Text('Sign in'),
                  ),
                  if (controller.transientError != null) ...[
                    const SizedBox(height: 12),
                    Text(
                      'That sign-in code wasn\'t accepted. Please try again.',
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                  ],
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  void _authenticate(V2RuntimeController controller) {
    final value = _secret.text;
    _secret.clear();
    if (value.trim().isEmpty) return;
    unawaited(
      controller.authenticateWithBootstrap(value).catchError(_onStartError),
    );
  }

  Widget _errorScaffold(String message, {required Key key}) => Scaffold(
    body: Center(
      child: Padding(
        key: key,
        padding: const EdgeInsets.all(24),
        child: Text(message, textAlign: TextAlign.center),
      ),
    ),
  );
}
