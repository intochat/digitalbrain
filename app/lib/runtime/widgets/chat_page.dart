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
const Key chatActivityContextKey = Key('chat-activity-context');
const Key chatActivityContextRetryKey = Key('chat-activity-context-retry');
const Key chatActivityRequestScrollKey = Key('chat-activity-request-scroll');

class ChatActivityReference {
  const ChatActivityReference._({this.conversationId, this.requestId});

  static ChatActivityReference? tryCreate({
    String? conversationId,
    String? requestId,
  }) {
    if (conversationId == null && requestId == null) return null;
    if (!_isSafeActivityCoordinate(conversationId) ||
        !_isSafeActivityCoordinate(requestId)) {
      return null;
    }
    return ChatActivityReference._(
      conversationId: conversationId,
      requestId: requestId,
    );
  }

  final String? conversationId;
  final String? requestId;
}

bool _isSafeActivityCoordinate(String? value) =>
    value == null ||
    value.isNotEmpty &&
        value.length <= 256 &&
        value.trim() == value &&
        !value.runes.any(
          (character) => character < 32 || character >= 127 && character <= 159,
        );

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
    this.activityReference,
  });

  final DateTime Function() now;
  final ResumeOriginatingRequestIntent? resumeIntent;
  final bool invalidResumeIntent;
  final ChatActivityReference? activityReference;

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
  ({String conversationId, String requestId, int scopeEpoch})?
  _loadingActivityContext;
  ({String conversationId, String requestId, int scopeEpoch})?
  _loadedActivityContext;
  ({String conversationId, String requestId, int scopeEpoch})?
  _failedActivityContext;
  wire.GetConversationContextReply? _activityContext;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final controller = AppSessionScope.of(context).controller;
    _scheduleSurfaceExpiry(controller);
    _scheduleFirstSurfaceFrame(controller);
    _scheduleResumeIntent();
    _scheduleActivityContext();
  }

  @override
  void didUpdateWidget(covariant ChatPage oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (_activityIdentity(oldWidget.activityReference) !=
        _activityIdentity(widget.activityReference)) {
      _loadingActivityContext = null;
      _loadedActivityContext = null;
      _failedActivityContext = null;
      _activityContext = null;
    }
    _scheduleResumeIntent();
    _scheduleActivityContext();
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

    return _buildScaffold(
      Stack(
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

  Widget _buildScaffold(Widget body) {
    final reference = widget.activityReference;
    if (reference == null) return Scaffold(body: body);
    final owner = AppSessionScope.of(context);
    final attempt = _activityAttempt(reference, owner.controller?.scopeEpoch);
    return Scaffold(
      body: Column(
        children: [
          _ChatActivityContextBanner(
            contextReply: _loadedActivityContext == attempt
                ? _activityContext
                : null,
            canRetry: attempt != null && _failedActivityContext == attempt,
            onRetry: _retryActivityContext,
          ),
          Expanded(child: body),
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

  void _scheduleActivityContext() {
    final owner = AppSessionScope.of(context);
    if (owner.controller?.session.isAuthenticated != true) return;
    final attempt = _activityAttempt(
      widget.activityReference,
      owner.controller?.scopeEpoch,
    );
    if (attempt == null ||
        _loadedActivityContext == attempt ||
        _loadingActivityContext == attempt ||
        _failedActivityContext == attempt) {
      return;
    }
    final client = owner.digitalBrainClient;
    if (client == null) return;
    _loadingActivityContext = attempt;
    unawaited(_loadActivityContext(client, attempt));
  }

  Future<void> _loadActivityContext(
    DigitalBrainClient client,
    ({String conversationId, String requestId, int scopeEpoch}) attempt,
  ) async {
    try {
      final reply = await client.getConversationContext(
        wire.GetConversationContextRequest(
          conversationId: attempt.conversationId,
          requestId: attempt.requestId,
        ),
      );
      if (reply.conversationId != attempt.conversationId ||
          reply.requestId != attempt.requestId) {
        throw StateError('Invalid conversation context reply.');
      }
      if (!mounted || _currentActivityAttempt != attempt) {
        return;
      }
      setState(() {
        _activityContext = reply;
        _loadedActivityContext = attempt;
        _failedActivityContext = null;
      });
    } catch (_) {
      if (!mounted || _currentActivityAttempt != attempt) {
        return;
      }
      setState(() {
        _activityContext = null;
        _loadedActivityContext = null;
        _failedActivityContext = attempt;
      });
    } finally {
      if (_loadingActivityContext == attempt) {
        _loadingActivityContext = null;
      }
    }
  }

  ({String conversationId, String requestId, int scopeEpoch})?
  get _currentActivityAttempt {
    final controller = AppSessionScope.of(context).controller;
    return _activityAttempt(widget.activityReference, controller?.scopeEpoch);
  }

  ({String conversationId, String requestId, int scopeEpoch})? _activityAttempt(
    ChatActivityReference? reference,
    int? scopeEpoch,
  ) {
    final identity = _activityIdentity(reference);
    if (identity == null || scopeEpoch == null) return null;
    return (
      conversationId: identity.conversationId,
      requestId: identity.requestId,
      scopeEpoch: scopeEpoch,
    );
  }

  void _retryActivityContext() {
    final attempt = _currentActivityAttempt;
    if (attempt == null || _failedActivityContext != attempt) return;
    setState(() => _failedActivityContext = null);
    _scheduleActivityContext();
  }

  ({String conversationId, String requestId})? _activityIdentity(
    ChatActivityReference? reference,
  ) {
    final conversationId = reference?.conversationId;
    final requestId = reference?.requestId;
    if (conversationId == null || requestId == null) return null;
    return (conversationId: conversationId, requestId: requestId);
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

  Widget _errorScaffold(String message, {required Key key}) => _buildScaffold(
    Center(
      child: Padding(
        key: key,
        padding: const EdgeInsets.all(24),
        child: Text(message, textAlign: TextAlign.center),
      ),
    ),
  );
}

class _ChatActivityContextBanner extends StatefulWidget {
  const _ChatActivityContextBanner({
    required this.contextReply,
    required this.canRetry,
    required this.onRetry,
  });

  final wire.GetConversationContextReply? contextReply;
  final bool canRetry;
  final VoidCallback onRetry;

  @override
  State<_ChatActivityContextBanner> createState() =>
      _ChatActivityContextBannerState();
}

class _ChatActivityContextBannerState
    extends State<_ChatActivityContextBanner> {
  final ScrollController _requestScrollController = ScrollController();

  @override
  void dispose() {
    _requestScrollController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final contextReply = widget.contextReply;
    if (contextReply == null) {
      return Semantics(
        key: chatActivityContextKey,
        container: true,
        label: 'Historical Chat context is unavailable.',
        child: ExcludeSemantics(
          child: Material(
            color: colors.secondaryContainer,
            child: SafeArea(
              bottom: false,
              child: Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: 16,
                  vertical: 10,
                ),
                child: Row(
                  children: [
                    Icon(Icons.history, color: colors.onSecondaryContainer),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Text(
                        'Historical Chat context is unavailable.',
                        style: TextStyle(color: colors.onSecondaryContainer),
                      ),
                    ),
                    if (widget.canRetry)
                      TextButton(
                        key: chatActivityContextRetryKey,
                        onPressed: widget.onRetry,
                        child: const Text('Retry'),
                      ),
                  ],
                ),
              ),
            ),
          ),
        ),
      );
    }
    return Semantics(
      key: chatActivityContextKey,
      container: true,
      label: 'Opened from Activity',
      child: Material(
        color: colors.secondaryContainer,
        child: SafeArea(
          bottom: false,
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxHeight: 200),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Icon(Icons.history, color: colors.onSecondaryContainer),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Text(
                          'Opened from Activity',
                          style: Theme.of(context).textTheme.labelLarge
                              ?.copyWith(color: colors.onSecondaryContainer),
                        ),
                        Text(
                          'Conversation ${contextReply.conversationId}',
                          style: TextStyle(color: colors.onSecondaryContainer),
                        ),
                        Text(
                          'Request ${contextReply.requestId}',
                          style: TextStyle(color: colors.onSecondaryContainer),
                        ),
                        Flexible(
                          child: Semantics(
                            label: 'Originating request',
                            child: Scrollbar(
                              controller: _requestScrollController,
                              thumbVisibility: true,
                              child: SingleChildScrollView(
                                key: chatActivityRequestScrollKey,
                                controller: _requestScrollController,
                                child: Text(
                                  contextReply.requestText,
                                  style: TextStyle(
                                    color: colors.onSecondaryContainer,
                                  ),
                                ),
                              ),
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
