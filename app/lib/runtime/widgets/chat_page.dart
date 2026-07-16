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

  String get semanticsLabel => [
    'Opened from Activity.',
    if (conversationId case final value?) 'Conversation $value.',
    if (requestId case final value?) 'Request $value.',
  ].join(' ');

  String get detailLabel => [
    if (conversationId case final value?) 'Conversation $value',
    if (requestId case final value?) 'Request $value',
  ].join(' · ');
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
  ({String conversationId, String requestId})? _loadingActivityContext;
  ({String conversationId, String requestId})? _loadedActivityContext;
  ({String conversationId, String requestId})? _failedActivityContext;
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
    return Scaffold(
      body: Column(
        children: [
          _ChatActivityContextBanner(
            reference: reference,
            contextReply: _loadedActivityContext == _activityIdentity(reference)
                ? _activityContext
                : null,
            failed: _failedActivityContext == _activityIdentity(reference),
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
    final identity = _activityIdentity(widget.activityReference);
    if (identity == null ||
        _loadedActivityContext == identity ||
        _loadingActivityContext == identity) {
      return;
    }
    final owner = AppSessionScope.of(context);
    if (owner.controller?.session.isAuthenticated != true) return;
    final client = owner.digitalBrainClient;
    if (client == null) return;
    _loadingActivityContext = identity;
    unawaited(_loadActivityContext(client, identity));
  }

  Future<void> _loadActivityContext(
    DigitalBrainClient client,
    ({String conversationId, String requestId}) identity,
  ) async {
    try {
      final reply = await client.getConversationContext(
        wire.GetConversationContextRequest(
          conversationId: identity.conversationId,
          requestId: identity.requestId,
        ),
      );
      if (reply.conversationId != identity.conversationId ||
          reply.requestId != identity.requestId) {
        throw StateError('Invalid conversation context reply.');
      }
      if (!mounted || _activityIdentity(widget.activityReference) != identity) {
        return;
      }
      setState(() {
        _activityContext = reply;
        _loadedActivityContext = identity;
        _failedActivityContext = null;
      });
    } catch (_) {
      if (!mounted || _activityIdentity(widget.activityReference) != identity) {
        return;
      }
      setState(() {
        _activityContext = null;
        _loadedActivityContext = null;
        _failedActivityContext = identity;
      });
    } finally {
      if (_loadingActivityContext == identity) {
        _loadingActivityContext = null;
      }
    }
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

class _ChatActivityContextBanner extends StatelessWidget {
  const _ChatActivityContextBanner({
    required this.reference,
    required this.contextReply,
    required this.failed,
  });

  final ChatActivityReference reference;
  final wire.GetConversationContextReply? contextReply;
  final bool failed;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return Semantics(
      key: chatActivityContextKey,
      container: true,
      label: [
        reference.semanticsLabel,
        if (contextReply case final context?) context.requestText,
      ].join(' '),
      child: ExcludeSemantics(
        child: Material(
          color: colors.secondaryContainer,
          child: SafeArea(
            bottom: false,
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
              child: Row(
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
                        if (contextReply case final context?) ...[
                          Text(
                            'Conversation ${context.conversationId}',
                            style: TextStyle(
                              color: colors.onSecondaryContainer,
                            ),
                          ),
                          Text(
                            context.requestText,
                            style: TextStyle(
                              color: colors.onSecondaryContainer,
                            ),
                          ),
                        ] else
                          Text(
                            failed
                                ? 'The originating request is unavailable.'
                                : reference.detailLabel,
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              color: colors.onSecondaryContainer,
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
