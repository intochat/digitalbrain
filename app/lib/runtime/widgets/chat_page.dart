import 'dart:async';

import 'package:fixnum/fixnum.dart';
import 'package:flutter/material.dart';

import '../../core/session/app_session_scope.dart';
import '../../core/session/digitalbrain_client.dart';
import '../../grpc/ui.pb.dart' as wire;
import '../protocol/surface_protocol.dart';
import '../runtime.dart';
import 'surface_view.dart';

DateTime _utcNow() => DateTime.now().toUtc();

const Key runtimeLoadingKey = Key('v2-runtime-loading');
const Key runtimeSurfaceKey = Key('v2-runtime-surface');
const Key runtimeTerminalErrorKey = Key('v2-runtime-terminal-error');
const Key runtimeResumeErrorKey = Key('v2-runtime-resume-error');
const Key runtimeResumeRetryKey = Key('v2-runtime-resume-retry');

class ResumeOriginatingRequestIntent {
  ResumeOriginatingRequestIntent({
    required this.draftId,
    required this.expectedRevision,
    required this.idempotencyId,
  });

  final String draftId;
  final Int64 expectedRevision;
  final String idempotencyId;

  ({String draftId, Int64 expectedRevision, String idempotencyId})
  get identity => (
    draftId: draftId,
    expectedRevision: expectedRevision,
    idempotencyId: idempotencyId,
  );
}

class ChatPage extends StatefulWidget {
  const ChatPage({
    super.key,
    this.now = _utcNow,
    this.resumeIntent,
    this.invalidResumeIntent = false,
  });

  final DateTime Function() now;
  final ResumeOriginatingRequestIntent? resumeIntent;
  final bool invalidResumeIntent;

  @override
  State<ChatPage> createState() => _ChatPageState();
}

class _ChatPageState extends State<ChatPage> {
  static final RegExp _canonicalOperationId = RegExp(
    r'^runtime-op-[0-9a-f]{64}$',
  );
  Timer? _surfaceExpiryTimer;
  bool _firstSurfaceFrameReported = false;
  bool _firstSurfaceFrameScheduled = false;
  final Set<({String draftId, Int64 expectedRevision, String idempotencyId})>
  _attemptedResumeIntents = {};
  ({String draftId, Int64 expectedRevision, String idempotencyId})?
  _failedResumeIntent;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final controller = AppSessionScope.of(context).controller;
    _scheduleSurfaceExpiry(controller);
    _scheduleFirstSurfaceFrame(controller);
    _scheduleResumeIntent();
  }

  @override
  void didUpdateWidget(covariant ChatPage oldWidget) {
    super.didUpdateWidget(oldWidget);
    _scheduleResumeIntent();
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
          if (_resumeFailed) _buildResumeFailure(context),
        ],
      ),
    );
  }

  bool get _resumeFailed {
    if (widget.invalidResumeIntent) return true;
    final intent = widget.resumeIntent;
    return intent != null && _failedResumeIntent == intent.identity;
  }

  void _scheduleResumeIntent() {
    final intent = widget.resumeIntent;
    if (widget.invalidResumeIntent || intent == null) return;
    final owner = AppSessionScope.of(context);
    if (owner.controller?.session.isAuthenticated != true) return;
    final client = owner.digitalBrainClient;
    if (client == null || !_attemptedResumeIntents.add(intent.identity)) return;
    unawaited(_resume(client, intent));
  }

  Future<void> _resume(
    DigitalBrainClient client,
    ResumeOriginatingRequestIntent intent,
  ) async {
    try {
      final reply = await client.resumeOriginatingRequest(
        wire.ResumeOriginatingRequestRequest(
          draftId: intent.draftId,
          expectedRevision: intent.expectedRevision,
          idempotencyId: intent.idempotencyId,
        ),
      );
      if (!_validResumeReply(reply, intent)) {
        throw StateError('Invalid resume reply.');
      }
    } catch (_) {
      if (!mounted || widget.resumeIntent?.identity != intent.identity) return;
      setState(() => _failedResumeIntent = intent.identity);
    }
  }

  bool _validResumeReply(
    wire.ResumeOriginatingRequestReply reply,
    ResumeOriginatingRequestIntent intent,
  ) =>
      reply.commandId == intent.idempotencyId &&
      _canonicalOperationId.hasMatch(reply.operationId) &&
      reply.phase == 'Accepted' &&
      reply.version > Int64.ZERO;

  void _retryResume() {
    final intent = widget.resumeIntent;
    if (intent == null || widget.invalidResumeIntent) return;
    setState(() {
      _failedResumeIntent = null;
      _attemptedResumeIntents.remove(intent.identity);
    });
    _scheduleResumeIntent();
  }

  Widget _buildResumeFailure(BuildContext context) => Align(
    alignment: Alignment.topCenter,
    child: SafeArea(
      child: Material(
        key: runtimeResumeErrorKey,
        color: Theme.of(context).colorScheme.errorContainer,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Flexible(
                child: Text(
                  'The original request could not be run safely. Try again.',
                ),
              ),
              if (!widget.invalidResumeIntent) ...[
                const SizedBox(width: 12),
                TextButton(
                  key: runtimeResumeRetryKey,
                  onPressed: _retryResume,
                  child: const Text('Try again'),
                ),
              ],
            ],
          ),
        ),
      ),
    ),
  );

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
