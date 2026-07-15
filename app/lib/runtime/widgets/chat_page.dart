import 'dart:async';

import 'package:flutter/material.dart';

import '../../core/session/app_session_scope.dart';
import '../protocol/surface_protocol.dart';
import '../runtime.dart';
import 'surface_view.dart';

DateTime _utcNow() => DateTime.now().toUtc();

const Key runtimeLoadingKey = Key('v2-runtime-loading');
const Key runtimeSurfaceKey = Key('v2-runtime-surface');
const Key runtimeTerminalErrorKey = Key('v2-runtime-terminal-error');

class ChatPage extends StatefulWidget {
  const ChatPage({super.key, this.now = _utcNow});

  final DateTime Function() now;

  @override
  State<ChatPage> createState() => _ChatPageState();
}

class _ChatPageState extends State<ChatPage> {
  Timer? _surfaceExpiryTimer;
  bool _firstSurfaceFrameReported = false;
  bool _firstSurfaceFrameScheduled = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final controller = AppSessionScope.of(context).controller;
    _scheduleSurfaceExpiry(controller);
    _scheduleFirstSurfaceFrame(controller);
  }

  @override
  void dispose() {
    _surfaceExpiryTimer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final controller = AppSessionScope.of(context).controller!;
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

  void _scheduleSurfaceExpiry(RuntimeController? controller) {
    _surfaceExpiryTimer?.cancel();
    final expiresAt = controller?.latestSurface?.expiresAt;
    if (expiresAt == null) return;
    final remaining = expiresAt.difference(widget.now().toUtc());
    if (remaining <= Duration.zero) return;
    _surfaceExpiryTimer = Timer(remaining, () {
      if (mounted) setState(() {});
    });
  }

  void _scheduleFirstSurfaceFrame(RuntimeController? controller) {
    final hasSurface = _renderableSurface(controller) != null;
    if (!hasSurface ||
        _firstSurfaceFrameReported ||
        _firstSurfaceFrameScheduled) {
      return;
    }
    _firstSurfaceFrameScheduled = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      _firstSurfaceFrameScheduled = false;
      if (!mounted || _firstSurfaceFrameReported) return;
      _firstSurfaceFrameReported = true;
      debugPrint('DigitalBrain rendered the first authenticated view');
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
