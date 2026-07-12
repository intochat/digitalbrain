import 'dart:async';

import 'package:flutter/material.dart';

import '../protocol/surface_protocol.dart';
import '../runtime_configuration.dart';
import '../grpc_ui_transport.dart';
import '../runtime.dart';
import '../runtime_session_owner.dart';
import 'surface_view.dart';

typedef TransportFactory = UiTransport Function(Uri endpoint);

DateTime _utcNow() => DateTime.now().toUtc();

const Key runtimeLoadingKey = Key('v2-runtime-loading');
const Key runtimeSignInKey = Key('v2-runtime-sign-in');
const Key runtimeSecretFieldKey = Key('v2-runtime-secret-field');
const Key runtimeSignInButtonKey = Key('v2-runtime-sign-in-button');
const Key runtimeSignOutButtonKey = Key('v2-runtime-sign-out-button');
const Key runtimeSurfaceKey = Key('v2-runtime-surface');
const Key runtimeTerminalErrorKey = Key('v2-runtime-terminal-error');

class RuntimeShell extends StatefulWidget {
  const RuntimeShell({
    super.key,
    this.configuration,
    this.controller,
    this.transportFactory = GrpcUiTransport.connect,
    this.externalIdentityTokenSourceFactory,
    this.autoStart = true,
    this.now = _utcNow,
  });

  final RuntimeConfiguration? configuration;
  final RuntimeController? controller;
  final TransportFactory transportFactory;
  final ExternalIdentityTokenSourceFactory? externalIdentityTokenSourceFactory;
  final bool autoStart;
  final DateTime Function() now;

  @override
  State<RuntimeShell> createState() => _RuntimeShellState();
}

class _RuntimeShellState extends State<RuntimeShell> {
  final TextEditingController _secret = TextEditingController();
  late final RuntimeSessionOwner _session;
  Timer? _surfaceExpiryTimer;
  bool _firstSurfaceFrameReported = false;
  bool _firstSurfaceFrameScheduled = false;

  @override
  void initState() {
    super.initState();
    _session = RuntimeSessionOwner(
      configuration: widget.configuration,
      controller: widget.controller,
      transportFactory: widget.transportFactory,
      externalIdentityTokenSourceFactory:
          widget.externalIdentityTokenSourceFactory,
      autoStart: widget.autoStart,
    )..addListener(_onSessionChanged);
    scheduleMicrotask(_session.initialize);
  }

  void _onSessionChanged() {
    if (!mounted) return;
    _scheduleSurfaceExpiry();
    setState(() {});
    final hasSurface = _renderableSurface(_session.controller) != null;
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

  @override
  void dispose() {
    _surfaceExpiryTimer?.cancel();
    _secret.dispose();
    _session.removeListener(_onSessionChanged);
    unawaited(_session.close());
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final initializationError = _session.initializationError;
    if (initializationError != null) {
      return _errorScaffold(
        'DigitalBrain could not start. Please try again.',
        key: runtimeTerminalErrorKey,
      );
    }
    final controller = _session.controller;
    if (controller == null) {
      return const Scaffold(
        body: Center(child: CircularProgressIndicator(key: runtimeLoadingKey)),
      );
    }

    if (controller.status == RuntimeStatus.awaitingSignIn) {
      return _buildSignIn(controller);
    }
    final surface = _renderableSurface(controller);
    if (controller.status == RuntimeStatus.terminalError && surface == null) {
      return _errorScaffold(
        'DigitalBrain is unavailable right now. Please try again.',
        key: runtimeTerminalErrorKey,
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
              child: SurfaceView(
                key: runtimeSurfaceKey,
                surface: surface,
                onSubmitAction: controller.submitAction,
                actionEnabled: controller.canSubmitActionsFrom(surface),
                reconnecting: controller.status == RuntimeStatus.reconnecting,
                connectionUnavailable:
                    controller.status == RuntimeStatus.terminalError,
              ),
            ),
          if (controller.session.isAuthenticated)
            Positioned(
              top: 12,
              right: 12,
              child: Tooltip(
                message: 'Sign out',
                child: IconButton.filledTonal(
                  key: runtimeSignOutButtonKey,
                  onPressed: _session.signOut,
                  icon: const Icon(Icons.logout),
                ),
              ),
            ),
        ],
      ),
    );
  }

  SurfaceEnvelope? _renderableSurface(RuntimeController? controller) {
    final surface = controller?.latestSurface;
    if (surface == null || surface.isExpired(widget.now().toUtc())) {
      return null;
    }
    return surface;
  }

  void _scheduleSurfaceExpiry() {
    _surfaceExpiryTimer?.cancel();
    final expiresAt = _session.controller?.latestSurface?.expiresAt;
    if (expiresAt == null) return;
    final remaining = expiresAt.difference(widget.now().toUtc());
    if (remaining <= Duration.zero) return;
    _surfaceExpiryTimer = Timer(remaining, () {
      if (mounted) setState(() {});
    });
  }

  Widget _buildWaiting(RuntimeController controller) {
    final message = switch (controller.status) {
      RuntimeStatus.authenticating => 'Signing you in…',
      RuntimeStatus.connecting => 'Opening your workspace…',
      RuntimeStatus.reconnecting => 'Reconnecting…',
      _ => 'Preparing your workspace…',
    };
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const CircularProgressIndicator(key: runtimeLoadingKey),
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

  Widget _buildSignIn(RuntimeController controller) {
    final externalIdentity = _session.hasExternalIdentity;
    return Scaffold(
      key: runtimeSignInKey,
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
                  Text(
                    externalIdentity
                        ? 'Continue with your organization identity.'
                        : 'Enter the sign-in code supplied by your '
                              'DigitalBrain administrator.',
                  ),
                  const SizedBox(height: 16),
                  if (!externalIdentity) ...[
                    TextField(
                      key: runtimeSecretFieldKey,
                      controller: _secret,
                      obscureText: true,
                      enableSuggestions: false,
                      autocorrect: false,
                      onSubmitted: (_) => _authenticate(),
                      decoration: const InputDecoration(
                        labelText: 'Sign-in code',
                      ),
                    ),
                    const SizedBox(height: 16),
                  ],
                  FilledButton(
                    key: runtimeSignInButtonKey,
                    onPressed: controller.status == RuntimeStatus.authenticating
                        ? null
                        : _authenticate,
                    child: Text(externalIdentity ? 'Continue' : 'Sign in'),
                  ),
                  if (controller.transientError != null) ...[
                    const SizedBox(height: 12),
                    Text(
                      externalIdentity
                          ? 'Sign-in was not accepted. Please try again.'
                          : 'That sign-in code wasn\'t accepted. '
                                'Please try again.',
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

  void _authenticate() {
    if (_session.hasExternalIdentity) {
      _session.authenticateWithExternalIdentity();
      return;
    }
    final value = _secret.text;
    _secret.clear();
    _session.authenticateWithBootstrap(value);
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
