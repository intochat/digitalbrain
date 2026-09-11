import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flyer_chat_text_message/flyer_chat_text_message.dart';
import 'package:flyer_chat_text_stream_message/flyer_chat_text_stream_message.dart';
import 'package:path_provider/path_provider.dart';
import 'package:provider/provider.dart';
import 'package:record/record.dart';
import 'package:uuid/uuid.dart';

import 'brain_chat_composer.dart';
import 'chat_contracts.dart';
import 'stream_state_store.dart';
import 'voice_file_io.dart'
    if (dart.library.html) 'voice_file_web.dart'
    as voice_file;

part 'brain_chat_presentation.dart';

/// Both presentations share the journal, active requests, and composer draft.
enum BrainChatPresentation { full, compact }

final class _PendingChatSend {
  _PendingChatSend({required this.id, required this.text})
    : createdAt = DateTime.now().toUtc();

  final String id;
  final String text;
  final DateTime createdAt;
  String? turnId;
  String? commandId;
  String? streamId;

  TextMessage get userMessage => TextMessage(
    id: id,
    authorId: ownerUserId,
    createdAt: createdAt,
    text: text,
  );
}

final class BrainChatScreen extends StatefulWidget {
  const BrainChatScreen({
    super.key,
    required this.chatName,
    required this.turns,
    this.onSend,
    this.onSendVoice,
    this.onAttachmentTap,
    this.onCancelTurn,
    this.onReadChart,
    this.onReadImageBytes,
    this.onReadSpreadsheet,
    this.onReadGraph,
    this.presentation = BrainChatPresentation.full,
    this.compactReplyMaxHeight = 180,
    this.activityMode = false,
    this.activityCorrelationId,
    this.activityCommandId,
    this.activityLocalId,
    this.onActivityStarted,
    this.onActivityAccepted,
    this.activityTurns = const [],
  });

  final String chatName;
  final List<ChatTurnEvent> turns;
  final SendMessage? onSend;
  final SendVoice? onSendVoice;
  final VoidCallback? onAttachmentTap;
  final CancelChatTurn? onCancelTurn;
  final ReadChart? onReadChart;
  final ReadImageBytes? onReadImageBytes;
  final ReadSpreadsheet? onReadSpreadsheet;
  final ReadGraph? onReadGraph;
  final BrainChatPresentation presentation;
  final double compactReplyMaxHeight;
  final bool activityMode;
  final String? activityCorrelationId, activityCommandId, activityLocalId;
  final void Function(String id, String text)? onActivityStarted;
  final void Function(String id, String commandId)? onActivityAccepted;
  final List<ChatTurnEvent> activityTurns;

  @override
  State<BrainChatScreen> createState() => _BrainChatScreenState();
}

final class _BrainChatScreenState extends State<BrainChatScreen> {
  static const _owner = User(id: ownerUserId, name: 'you');
  static const _assistant = User(id: assistantUserId, name: 'Ino');
  static const _uuid = Uuid();
  static const _voicePlaceholder = '🎤 …';

  final _controller = InMemoryChatController();
  final _streamStates = StreamStateStore();
  final _voice = VoiceComposerController();
  final _appliedSequences = <String>{};
  final _renderSequences = <String, int>{};
  final _recorder = AudioRecorder();
  final _pendingSends = <_PendingChatSend>[];
  String? _failure;
  _PendingChatSend? _failedSend;
  final _historyPortal = OverlayPortalController();
  bool _historyOpen = false;

  @override
  void initState() {
    super.initState();
    unawaited(_syncJournal(widget.turns));
  }

  @override
  void didUpdateWidget(covariant BrainChatScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.presentation != oldWidget.presentation && _historyOpen) {
      _historyPortal.hide();
      _historyOpen = false;
    }
    final scopeChanged =
        oldWidget.activityCorrelationId != widget.activityCorrelationId ||
        oldWidget.activityCommandId != widget.activityCommandId ||
        oldWidget.activityLocalId != widget.activityLocalId ||
        oldWidget.activityMode != widget.activityMode;
    final resultsChanged = !_sameJournal(
      oldWidget.activityTurns,
      widget.activityTurns,
    );
    if (scopeChanged ||
        resultsChanged ||
        !_sameJournal(oldWidget.turns, widget.turns)) {
      unawaited(_syncJournal(widget.turns, force: true));
    }
  }

  Future<void> _syncJournal(
    List<ChatTurnEvent> turns, {
    bool force = false,
  }) async {
    // Activity results use TurnRequested, the journal TurnAccepted: deduplicate by command and direction, not signal name.
    final resultCommands = {
      for (final turn in widget.activityTurns)
        '${turn.commandId}:${turn.fromUser}',
    };
    turns = {
      for (final turn in turns)
        if (!resultCommands.contains('${turn.commandId}:${turn.fromUser}'))
          _turnIdentity(turn): turn,
      for (final turn in widget.activityTurns) _turnIdentity(turn): turn,
    }.values.toList()..sort((a, b) => a.timestamp.compareTo(b.timestamp));
    final sequences = {for (final turn in turns) _turnIdentity(turn)};
    if (!force &&
        sequences.length == _appliedSequences.length &&
        sequences.every(_appliedSequences.contains)) {
      return;
    }

    for (final pending in _pendingSends.toList()) {
      if (pending.turnId == null) continue;
      final responded = turns.any(
        (turn) => turn.turnId == pending.turnId && turn.signal == 'Responded',
      );
      final terminal =
          responded ||
          turns.any(
            (turn) =>
                turn.turnId == pending.turnId && turn.signal == 'TurnFailed',
          );
      if (terminal) {
        _pendingSends.remove(pending);
        if (responded && identical(_failedSend, pending)) {
          _failure = null;
          _failedSend = null;
        }
        if (pending.streamId case final streamId?) {
          _streamStates.forget(streamId);
        }
      }
    }

    final visibleTurns = turns.where(_turnVisible).toList();
    final messages = <Message>[
      for (final turn in visibleTurns) ...[
        ...KitMessageFactory.messagesForTurn(
          sequence: widget.activityMode
              ? _renderSequences.putIfAbsent(
                  _turnIdentity(turn),
                  () => -_renderSequences.length - 1,
                )
              : turn.sequence,
          fromUser: turn.fromUser,
          text: turn.signal == 'TurnFailed'
              ? turn.status == 'Cancelled'
                    ? 'Request cancelled.'
                    : 'Request failed. See Activity for details.'
              : turn.text,
          createdAt: turn.timestamp,
          parts: turn.kitParts,
        ),
      ],
    ];

    for (final pending in _pendingSends) {
      if (!_pendingVisible(pending)) continue;
      if (!turns.any(
        (turn) => turn.fromUser && turn.turnId == pending.turnId,
      )) {
        messages.add(pending.userMessage);
      }
      if (pending.streamId case final streamId?) {
        messages.add(
          TextStreamMessage(
            id: streamId,
            authorId: assistantUserId,
            createdAt: pending.createdAt,
            streamId: streamId,
          ),
        );
      }
    }

    _appliedSequences
      ..clear()
      ..addAll(sequences);

    await _controller.setMessages(messages, animated: false);
    if (mounted) {
      setState(() {});
    }
  }

  Future<void> _handleSend(String text) async {
    final trimmed = text.trim();
    if (trimmed.isEmpty) return;
    _showFailure(null);
    final send = widget.onSend;
    if (send == null) return;
    final pending = _newPendingSend(trimmed);
    await _submit(pending, send(trimmed));
  }

  _PendingChatSend _newPendingSend(String text) {
    final pending = _PendingChatSend(id: _uuid.v4(), text: text);
    _pendingSends.add(pending);
    widget.onActivityStarted?.call(
      pending.id,
      text == _voicePlaceholder ? 'Voice message' : text,
    );
    return pending;
  }

  void _showFailure(String? message, {_PendingChatSend? pending}) {
    setState(() {
      _failure = message;
      _failedSend = pending;
    });
  }

  Future<void> _submit(
    _PendingChatSend pending,
    Future<ChatSendReceipt> accepted,
  ) async {
    final streamId = _uuid.v4();
    pending.streamId = streamId;
    // The send is already in flight, so give it an error handler before awaiting anything else.
    final settled = accepted.then<Object>(
      (receipt) => receipt,
      onError: (Object error) => error,
    );
    await _controller.insertMessage(pending.userMessage);
    if (!mounted) return;
    await _controller.insertMessage(
      TextStreamMessage(
        id: streamId,
        authorId: assistantUserId,
        createdAt: pending.createdAt,
        streamId: streamId,
      ),
    );
    if (!mounted) return;
    _streamStates.start(streamId);
    final receipt = await settled;
    if (!mounted || !_pendingSends.contains(pending)) return;
    if (receipt is! ChatSendReceipt) {
      _streamStates.error(streamId, '$receipt');
      _showFailure('$receipt', pending: pending);
      return;
    }
    pending.turnId = receipt.turnId;
    pending.commandId = receipt.commandId;
    if (receipt.commandId case final commandId?) {
      widget.onActivityAccepted?.call(pending.id, commandId);
    }
    await _syncJournal(widget.turns, force: true);
  }

  Future<void> _toggleVoice() async {
    final sendVoice = widget.onSendVoice;
    if (sendVoice == null || _voice.busy) {
      return;
    }

    if (_voice.recording) {
      await _stopAndSendVoice(sendVoice);
      return;
    }

    await _startRecording();
  }

  Future<void> _startRecording() async {
    try {
      if (await _recorder.isRecording()) {
        await _recorder.stop();
      }
      if (!await _recorder.hasPermission()) {
        if (mounted) {
          _showFailure('Microphone permission denied.');
        }
        return;
      }

      // Whisper expects a container (WAV); raw PCM is refused server-side quality.
      if (!await _recorder.isEncoderSupported(AudioEncoder.wav)) {
        if (mounted) {
          _showFailure('WAV recording is not supported on this device.');
        }
        return;
      }

      final dir = await getTemporaryDirectory();
      final path = voice_file.joinVoicePath(
        dir.path,
        'voice_${_uuid.v4()}.wav',
      );

      await _recorder.start(
        const RecordConfig(
          encoder: AudioEncoder.wav,
          sampleRate: 16000,
          numChannels: 1,
        ),
        path: path,
      );

      if (mounted) {
        _voice.update(recording: true);
        _showFailure(null);
      }
    } on Object catch (error) {
      if (mounted) {
        _voice.update(recording: false, busy: false);
        _showFailure('Record failed: $error');
      }
    }
  }

  Future<void> _stopAndSendVoice(SendVoice sendVoice) async {
    _voice.update(recording: false, busy: true);
    if (mounted) {
      _showFailure(null);
    }

    String? path;
    try {
      if (await _recorder.isRecording()) {
        path = await _recorder.stop();
      }
      if (path == null || path.isEmpty) {
        throw StateError('Recording produced no audio file.');
      }

      final bytes = await voice_file.readVoiceBytes(path);
      if (bytes.isEmpty) {
        throw StateError('Recording is empty.');
      }

      if (!mounted) return;
      final pending = _newPendingSend(_voicePlaceholder);
      _voice.update(busy: false);
      await _submit(pending, sendVoice(bytes, fileName: 'voice.wav'));
    } on Object catch (error) {
      if (mounted) {
        _showFailure('$error');
      }
    } finally {
      if (path != null) {
        try {
          await voice_file.deleteVoiceFile(path);
        } on Object {
          // best-effort temp cleanup
        }
      }
      if (mounted) _voice.update(recording: false, busy: false);
    }
  }

  @override
  void dispose() {
    unawaited(_recorder.dispose());
    _controller.dispose();
    _streamStates.dispose();
    _voice.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => _buildPresentation(context);

  void _openHistory() {
    setState(() => _historyOpen = true);
    _historyPortal.show();
  }

  void _closeHistory() {
    _historyPortal.hide();
    setState(() => _historyOpen = false);
  }

  bool _sameJournal(List<ChatTurnEvent> a, List<ChatTurnEvent> b) {
    if (identical(a, b)) {
      return true;
    }
    if (a.length != b.length) {
      return false;
    }
    for (var i = 0; i < a.length; i++) {
      if (a[i].sequence != b[i].sequence ||
          a[i].eventId != b[i].eventId ||
          a[i].text != b[i].text ||
          a[i].cards.length != b[i].cards.length ||
          a[i].status != b[i].status ||
          a[i].turnId != b[i].turnId) {
        return false;
      }
    }
    return true;
  }

  String _turnIdentity(ChatTurnEvent turn) =>
      turn.eventId ??
      '${turn.correlationId}:${turn.neuronId}:${turn.sequence}:${turn.signal}';

  bool _turnVisible(ChatTurnEvent turn) =>
      !widget.activityMode ||
      (widget.activityCorrelationId != null &&
          turn.correlationId == widget.activityCorrelationId) ||
      (widget.activityCommandId != null &&
          turn.commandId == widget.activityCommandId);

  bool _pendingVisible(_PendingChatSend pending) =>
      !widget.activityMode ||
      pending.id == widget.activityLocalId ||
      (pending.commandId != null &&
          (pending.commandId == widget.activityCommandId ||
              widget.turns.any(
                (turn) =>
                    turn.commandId == pending.commandId && _turnVisible(turn),
              )));
}
