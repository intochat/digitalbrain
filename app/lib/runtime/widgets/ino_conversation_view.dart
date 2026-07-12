import 'dart:async';

import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import '../protocol/surface_protocol.dart';
import '../runtime.dart';
import 'ino_composer.dart';

const Key inoConversationKey = Key('v2-ino-conversation');
const Key inoIntroKey = Key('v2-ino-intro');
const Key inoTranscriptKey = Key('v2-ino-transcript');
const Key inoEmptyTranscriptKey = Key('v2-ino-empty-transcript');
const Key inoOperationStatusKey = Key('v2-ino-operation-status');
const Key inoRetryButtonKey = Key('v2-ino-retry-button');
const Key inoConnectButtonKey = Key('v2-ino-connect-button');
const Key inoReconnectBannerKey = Key('v2-ino-reconnect-banner');
const Key inoConnectionUnavailableBannerKey = Key(
  'v2-ino-connection-unavailable-banner',
);
const Key inoSubmissionNoticeKey = Key('v2-ino-submission-notice');
const Key inoNewConversationButtonKey = Key('v2-ino-new-conversation');
const Key inoDeleteConversationButtonKey = Key('v2-ino-delete-conversation');
const Key inoDeleteConversationConfirmKey = Key(
  'v2-ino-delete-conversation-confirm',
);

typedef InoActionSubmit =
    Future<ActionResult> Function(
      SurfaceEnvelope surface,
      String bindingId,
      Map<String, Object?> input,
    );

class InoConversationView extends StatefulWidget {
  const InoConversationView({
    super.key,
    required this.surface,
    required this.payload,
    required this.onSubmitAction,
    required this.actionEnabled,
    required this.reconnecting,
    this.connectionUnavailable = false,
  });

  final SurfaceEnvelope surface;
  final InoConversationSurfacePayload payload;
  final InoActionSubmit onSubmitAction;
  final bool actionEnabled;
  final bool reconnecting;
  final bool connectionUnavailable;

  @override
  State<InoConversationView> createState() => _InoConversationViewState();
}

class _InoConversationViewState extends State<InoConversationView> {
  final TextEditingController _composer = TextEditingController();
  final FocusNode _composerFocus = FocusNode(debugLabel: 'INO composer');
  final ScrollController _transcriptScroll = ScrollController();

  bool _submitting = false;
  String? _optimisticPrompt;
  String? _pendingPrompt;
  int _pendingPromptBaseline = 0;
  int _pendingSurfaceRevision = 0;
  bool _pendingWasRetry = false;
  bool _serverConfirmedCurrentSubmission = false;
  bool _currentSubmissionAccepted = false;
  bool _awaitingServerConfirmation = false;
  bool _submissionUncertain = false;
  String? _submissionNotice;
  bool _openingConnection = false;
  bool _changingConversation = false;
  ActionResult? _lastAcceptedReceipt;

  @override
  void initState() {
    super.initState();
    _composer.addListener(_onDraftChanged);
    if (widget.payload.messages.isNotEmpty) _scheduleScrollToEnd();
  }

  @override
  void didUpdateWidget(covariant InoConversationView oldWidget) {
    super.didUpdateWidget(oldWidget);
    final followTranscript = _isNearTranscriptEnd;
    final transcriptChanged = _transcriptChanged(
      oldWidget.payload.messages,
      widget.payload.messages,
    );
    _reconcilePendingSubmission();
    if (transcriptChanged && followTranscript) {
      _scheduleScrollToEnd();
    }
    final previousOperation = oldWidget.payload.operation?.state;
    final currentOperation = widget.payload.operation?.state;
    if (currentOperation?.isTerminal == true &&
        currentOperation != previousOperation) {
      _scheduleComposerFocus();
    }
  }

  @override
  void dispose() {
    _composer.removeListener(_onDraftChanged);
    _composer.dispose();
    _composerFocus.dispose();
    _transcriptScroll.dispose();
    super.dispose();
  }

  UiActionRef? get _sendAction {
    final action = widget.surface.actionByBindingId('ino.send');
    return action?.actionType == 'ino.interact' ? action : null;
  }

  UiActionRef? get _newConversationAction {
    final action = widget.surface.actionByBindingId('ino.new');
    return action?.actionType == 'ino.conversation.new' ? action : null;
  }

  UiActionRef? get _deleteConversationAction {
    final action = widget.surface.actionByBindingId('ino.delete');
    return action?.actionType == 'ino.conversation.delete' ? action : null;
  }

  bool get _canChangeConversation =>
      widget.actionEnabled &&
      !widget.reconnecting &&
      !widget.connectionUnavailable &&
      !_submitting &&
      !_changingConversation &&
      !_awaitingServerConfirmation &&
      !_submissionUncertain;

  bool get _canSend =>
      widget.actionEnabled &&
      !widget.reconnecting &&
      !widget.connectionUnavailable &&
      !_submitting &&
      !_changingConversation &&
      !_awaitingServerConfirmation &&
      !_submissionUncertain &&
      (widget.payload.operation?.state.isTerminal ?? true) &&
      _sendAction != null &&
      _composer.text.trim().isNotEmpty &&
      _composer.text.trim().length <= inoMaximumPromptLength;

  bool get _isNearTranscriptEnd {
    if (!_transcriptScroll.hasClients) return true;
    final position = _transcriptScroll.position;
    return position.maxScrollExtent - position.pixels <= 80;
  }

  void _onDraftChanged() {
    if (mounted) setState(() {});
  }

  void _reconcilePendingSubmission() {
    final prompt = _pendingPrompt;
    if (prompt == null) return;
    final matchingTurns = widget.payload.messages
        .where(
          (message) =>
              message.role == InoConversationRole.user &&
              message.text == prompt,
        )
        .length;
    final retryAdvanced =
        _pendingWasRetry &&
        widget.surface.revision > _pendingSurfaceRevision &&
        matchingTurns >= _pendingPromptBaseline &&
        widget.payload.operation?.state != InoConversationOperationState.failed;
    if (matchingTurns <= _pendingPromptBaseline && !retryAdvanced) return;
    _serverConfirmedCurrentSubmission = true;
    _awaitingServerConfirmation = false;
    _optimisticPrompt = null;
    _pendingPrompt = null;
    _pendingWasRetry = false;
    _currentSubmissionAccepted = false;
    _submissionUncertain = false;
    _submissionNotice = null;
  }

  Future<void> _sendDraft() async {
    if (!_canSend) return;
    final prompt = _composer.text.trim();
    _composer.clear();
    await _submit(prompt, showOptimisticTurn: true);
  }

  Future<void> _retry() async {
    if (_submitting ||
        _awaitingServerConfirmation ||
        _submissionUncertain ||
        !widget.actionEnabled ||
        widget.reconnecting ||
        widget.connectionUnavailable) {
      return;
    }
    InoConversationMessage? lastUserTurn;
    for (final message in widget.payload.messages.reversed) {
      if (message.role == InoConversationRole.user) {
        lastUserTurn = message;
        break;
      }
    }
    if (lastUserTurn == null) return;
    await _submit(lastUserTurn.text, showOptimisticTurn: false);
  }

  Future<void> _openConnection(InoConversationAction action) async {
    if (_openingConnection ||
        widget.reconnecting ||
        widget.connectionUnavailable) {
      return;
    }
    setState(() {
      _openingConnection = true;
      _submissionNotice = null;
    });
    try {
      final opened = await launchUrl(
        action.target,
        mode: LaunchMode.externalApplication,
      );
      if (!mounted) return;
      setState(() {
        _openingConnection = false;
        if (!opened) {
          _submissionNotice =
              'Connection sign-in couldn\'t be opened. Please try again.';
        }
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _openingConnection = false;
        _submissionNotice =
            'Connection sign-in couldn\'t be opened. Please try again.';
      });
    }
  }

  Future<void> _startNewConversation() async {
    final action = _newConversationAction;
    if (action == null || !_canChangeConversation) return;
    await _submitConversationLifecycle(action);
  }

  Future<void> _deleteConversation() async {
    final action = _deleteConversationAction;
    if (action == null || !_canChangeConversation) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Delete this conversation?'),
        content: const Text(
          'This removes this conversation and cancels any work or connection request still waiting in it.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            key: inoDeleteConversationConfirmKey,
            onPressed: () => Navigator.of(context).pop(true),
            child: const Text('Delete'),
          ),
        ],
      ),
    );
    if (confirmed == true && mounted) {
      await _submitConversationLifecycle(action);
    }
  }

  Future<void> _submitConversationLifecycle(UiActionRef action) async {
    setState(() {
      _changingConversation = true;
      _submissionNotice = null;
    });
    try {
      await widget.onSubmitAction(
        widget.surface,
        action.bindingId,
        const <String, Object?>{},
      );
      if (!mounted) return;
      setState(() => _changingConversation = false);
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _changingConversation = false;
        _submissionNotice =
            'The conversation couldn\'t be changed. Please try again.';
      });
    }
  }

  Future<void> _submit(
    String prompt, {
    required bool showOptimisticTurn,
  }) async {
    final action = _sendAction;
    if (action == null || _submitting) return;
    final matchingTurns = widget.payload.messages
        .where(
          (message) =>
              message.role == InoConversationRole.user &&
              message.text == prompt,
        )
        .length;
    setState(() {
      _submitting = true;
      _pendingPrompt = prompt;
      _pendingPromptBaseline = matchingTurns;
      _pendingSurfaceRevision = widget.surface.revision;
      _pendingWasRetry = !showOptimisticTurn;
      _serverConfirmedCurrentSubmission = false;
      _currentSubmissionAccepted = false;
      _awaitingServerConfirmation = true;
      _submissionUncertain = false;
      _submissionNotice = null;
      if (showOptimisticTurn) _optimisticPrompt = prompt;
    });
    if (showOptimisticTurn) _scheduleScrollToEnd();

    try {
      final receipt = await widget.onSubmitAction(
        widget.surface,
        action.bindingId,
        <String, Object?>{'prompt': prompt},
      );
      if (!mounted) return;
      setState(() {
        _lastAcceptedReceipt = receipt;
        _currentSubmissionAccepted = true;
        _submitting = false;
        if (_serverConfirmedCurrentSubmission) {
          _awaitingServerConfirmation = false;
          _pendingPrompt = null;
          _pendingWasRetry = false;
          _currentSubmissionAccepted = false;
        }
      });
      _reconcilePendingSubmission();
      _scheduleComposerFocus();
    } catch (error) {
      if (!mounted) return;
      if (_serverConfirmedCurrentSubmission) {
        setState(() {
          _submitting = false;
          _awaitingServerConfirmation = false;
          _pendingPrompt = null;
          _pendingWasRetry = false;
          _currentSubmissionAccepted = false;
        });
        _scheduleComposerFocus();
        return;
      }
      if (_definitelyNotSubmitted(error)) {
        if (_composer.text.isEmpty) {
          _composer.value = TextEditingValue(
            text: prompt,
            selection: TextSelection.collapsed(offset: prompt.length),
          );
        }
        setState(() {
          _submitting = false;
          _awaitingServerConfirmation = false;
          _pendingPrompt = null;
          _pendingWasRetry = false;
          _currentSubmissionAccepted = false;
          _optimisticPrompt = null;
          _submissionUncertain = false;
          _submissionNotice = 'That message wasn\'t sent. Please try again.';
        });
        _scheduleComposerFocus();
        return;
      }
      setState(() {
        _submitting = false;
        _currentSubmissionAccepted = false;
        _submissionUncertain = true;
        _submissionNotice =
            'We couldn\'t confirm that INO received this message. '
            'Your conversation will update when the connection recovers.';
      });
      _scheduleComposerFocus();
    }
  }

  static bool _transcriptChanged(
    List<InoConversationMessage> previous,
    List<InoConversationMessage> current,
  ) {
    if (previous.length != current.length) return true;
    if (previous.isEmpty) return false;
    final before = previous.last;
    final after = current.last;
    return before.turnKey != after.turnKey ||
        before.role != after.role ||
        before.text != after.text ||
        before.state != after.state;
  }

  static bool _definitelyNotSubmitted(Object error) {
    if (error is StateError) return true;
    return error is TransportException &&
        (error.code == TransportErrorCode.unauthenticated ||
            error.code == TransportErrorCode.permissionDenied ||
            error.code == TransportErrorCode.invalidArgument);
  }

  void _scheduleScrollToEnd() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted || !_transcriptScroll.hasClients) return;
      unawaited(
        _transcriptScroll.animateTo(
          _transcriptScroll.position.maxScrollExtent,
          duration: const Duration(milliseconds: 180),
          curve: Curves.easeOut,
        ),
      );
    });
  }

  void _scheduleComposerFocus() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted && _composerFocus.canRequestFocus) {
        _composerFocus.requestFocus();
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final operation = widget.payload.operation;
    final optimisticPrompt = _optimisticPrompt;
    final messages = <_PresentedMessage>[
      for (final message in widget.payload.messages)
        _PresentedMessage.fromServer(message),
      if (optimisticPrompt != null)
        _PresentedMessage(
          turnKey: 'turn-optimistic',
          role: InoConversationRole.user,
          text: optimisticPrompt,
          state: !_currentSubmissionAccepted || _lastAcceptedReceipt == null
              ? InoConversationTurnState.sending
              : InoConversationTurnState.queued,
          uncertain: _submissionUncertain,
        ),
    ];

    return Semantics(
      key: inoConversationKey,
      container: true,
      label: 'INO conversation',
      child: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 920),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(20, 18, 20, 16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          'Ask INO',
                          style: Theme.of(context).textTheme.headlineMedium,
                        ),
                      ),
                      OutlinedButton.icon(
                        key: inoNewConversationButtonKey,
                        onPressed:
                            _canChangeConversation &&
                                _newConversationAction != null
                            ? _startNewConversation
                            : null,
                        icon: const Icon(Icons.add_comment_outlined),
                        label: const Text('New'),
                      ),
                      const SizedBox(width: 8),
                      IconButton(
                        key: inoDeleteConversationButtonKey,
                        tooltip: 'Delete conversation',
                        onPressed:
                            _canChangeConversation &&
                                _deleteConversationAction != null
                            ? _deleteConversation
                            : null,
                        icon: const Icon(Icons.delete_outline),
                      ),
                    ],
                  ),
                  const SizedBox(height: 6),
                  Text(
                    widget.payload.intro,
                    key: inoIntroKey,
                    style: Theme.of(context).textTheme.bodyLarge,
                  ),
                  if (widget.reconnecting) ...[
                    const SizedBox(height: 12),
                    const _ReconnectingBanner(),
                  ] else if (widget.connectionUnavailable) ...[
                    const SizedBox(height: 12),
                    const _ConnectionUnavailableBanner(),
                  ],
                  const SizedBox(height: 14),
                  Expanded(
                    child: messages.isEmpty
                        ? const Center(
                            key: inoEmptyTranscriptKey,
                            child: Text(
                              'Start with a question about this workspace.',
                              textAlign: TextAlign.center,
                            ),
                          )
                        : ListView.separated(
                            key: inoTranscriptKey,
                            controller: _transcriptScroll,
                            padding: const EdgeInsets.symmetric(vertical: 4),
                            itemCount: messages.length,
                            separatorBuilder: (_, _) =>
                                const SizedBox(height: 10),
                            itemBuilder: (context, index) => _ConversationTurn(
                              key: ValueKey(
                                'v2-ino-turn-${messages[index].turnKey}',
                              ),
                              message: messages[index],
                            ),
                          ),
                  ),
                  if (_submissionNotice case final notice?) ...[
                    const SizedBox(height: 10),
                    _SafeNotice(message: notice),
                  ],
                  if (operation != null) ...[
                    const SizedBox(height: 10),
                    _OperationStatus(
                      operation: operation,
                      retryEnabled:
                          operation.state ==
                              InoConversationOperationState.failed &&
                          operation.retryable &&
                          widget.actionEnabled &&
                          !widget.reconnecting &&
                          !widget.connectionUnavailable &&
                          !_submitting &&
                          !_awaitingServerConfirmation &&
                          !_submissionUncertain &&
                          _sendAction != null,
                      onRetry: _retry,
                      connectionEnabled:
                          !_openingConnection &&
                          !widget.reconnecting &&
                          !widget.connectionUnavailable,
                      onConnect: _openConnection,
                    ),
                  ],
                  const SizedBox(height: 12),
                  InoComposer(
                    controller: _composer,
                    focusNode: _composerFocus,
                    canSend: _canSend,
                    onSend: _sendDraft,
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

class _PresentedMessage {
  const _PresentedMessage({
    required this.turnKey,
    required this.role,
    required this.text,
    required this.state,
    this.uncertain = false,
  });

  factory _PresentedMessage.fromServer(InoConversationMessage message) =>
      _PresentedMessage(
        turnKey: message.turnKey,
        role: message.role,
        text: message.text,
        state: message.state,
      );

  final String turnKey;
  final InoConversationRole role;
  final String text;
  final InoConversationTurnState state;
  final bool uncertain;
}

class _ConversationTurn extends StatelessWidget {
  const _ConversationTurn({super.key, required this.message});

  final _PresentedMessage message;

  @override
  Widget build(BuildContext context) {
    final isUser = message.role == InoConversationRole.user;
    final author = isUser ? 'You' : 'INO';
    final status = message.uncertain
        ? 'Checking delivery'
        : _turnStatus(message.state);
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    final background = isUser
        ? colors.primaryContainer
        : colors.surfaceContainerHigh;
    final foreground = isUser ? colors.onPrimaryContainer : colors.onSurface;
    return Semantics(
      container: true,
      label: '$author, $status',
      child: Align(
        alignment: isUser ? Alignment.centerRight : Alignment.centerLeft,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 680),
          child: Material(
            color: background,
            borderRadius: BorderRadius.circular(16),
            child: Padding(
              padding: const EdgeInsets.all(14),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    author,
                    key: ValueKey('v2-ino-turn-${message.turnKey}-author'),
                    style: theme.textTheme.labelLarge?.copyWith(
                      color: foreground,
                    ),
                  ),
                  const SizedBox(height: 5),
                  SelectableText(
                    message.text,
                    style: theme.textTheme.bodyMedium?.copyWith(
                      color: foreground,
                    ),
                  ),
                  const SizedBox(height: 6),
                  Text(
                    status,
                    key: ValueKey('v2-ino-turn-${message.turnKey}-status'),
                    style: theme.textTheme.labelSmall?.copyWith(
                      color: foreground,
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

class _OperationStatus extends StatelessWidget {
  const _OperationStatus({
    required this.operation,
    required this.retryEnabled,
    required this.onRetry,
    required this.connectionEnabled,
    required this.onConnect,
  });

  final InoConversationOperation operation;
  final bool retryEnabled;
  final VoidCallback onRetry;
  final bool connectionEnabled;
  final Future<void> Function(InoConversationAction action) onConnect;

  @override
  Widget build(BuildContext context) {
    final isActive = !operation.state.isTerminal;
    final message = switch (operation.state) {
      InoConversationOperationState.queued => 'Your message is queued.',
      InoConversationOperationState.running => 'INO is working on it.',
      InoConversationOperationState.responding => 'INO is writing a response.',
      InoConversationOperationState.succeeded => 'Response ready.',
      InoConversationOperationState.failed =>
        operation.safeReason ?? 'INO couldn\'t complete that request.',
    };
    return Semantics(
      container: true,
      liveRegion: true,
      label: message,
      child: Material(
        key: inoOperationStatusKey,
        color: Theme.of(context).colorScheme.surfaceContainer,
        borderRadius: BorderRadius.circular(12),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
          child: Row(
            children: [
              if (isActive) ...[
                const SizedBox.square(
                  dimension: 18,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
                const SizedBox(width: 10),
              ],
              Expanded(child: Text(message)),
              if (operation.state == InoConversationOperationState.failed &&
                  operation.retryable) ...[
                const SizedBox(width: 10),
                OutlinedButton(
                  key: inoRetryButtonKey,
                  onPressed: retryEnabled ? onRetry : null,
                  child: const Text('Retry'),
                ),
              ],
              if (operation.action case final action?) ...[
                const SizedBox(width: 10),
                FilledButton(
                  key: inoConnectButtonKey,
                  onPressed: connectionEnabled
                      ? () => unawaited(onConnect(action))
                      : null,
                  child: Text(action.label),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _ReconnectingBanner extends StatelessWidget {
  const _ReconnectingBanner();

  @override
  Widget build(BuildContext context) => Semantics(
    liveRegion: true,
    label: 'Reconnecting',
    child: Material(
      key: inoReconnectBannerKey,
      color: Theme.of(context).colorScheme.secondaryContainer,
      borderRadius: BorderRadius.circular(12),
      child: const Padding(
        padding: EdgeInsets.symmetric(horizontal: 12, vertical: 10),
        child: Text(
          'Connection interrupted. Your draft is safe while we reconnect.',
        ),
      ),
    ),
  );
}

class _ConnectionUnavailableBanner extends StatelessWidget {
  const _ConnectionUnavailableBanner();

  @override
  Widget build(BuildContext context) => Semantics(
    liveRegion: true,
    label: 'Connection unavailable',
    child: Material(
      key: inoConnectionUnavailableBannerKey,
      color: Theme.of(context).colorScheme.errorContainer,
      borderRadius: BorderRadius.circular(12),
      child: const Padding(
        padding: EdgeInsets.symmetric(horizontal: 12, vertical: 10),
        child: Text(
          'Connection unavailable. Your draft is saved. '
          'Reopen the workspace to continue.',
        ),
      ),
    ),
  );
}

class _SafeNotice extends StatelessWidget {
  const _SafeNotice({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) => Material(
    key: inoSubmissionNoticeKey,
    color: Theme.of(context).colorScheme.errorContainer,
    borderRadius: BorderRadius.circular(12),
    child: Padding(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
      child: Text(message),
    ),
  );
}

String _turnStatus(InoConversationTurnState state) => switch (state) {
  InoConversationTurnState.sending => 'Sending',
  InoConversationTurnState.queued => 'Queued',
  InoConversationTurnState.running => 'Working',
  InoConversationTurnState.responding => 'Responding',
  InoConversationTurnState.succeeded => 'Complete',
  InoConversationTurnState.failed => 'Not completed',
};
