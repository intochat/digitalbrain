import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flyer_chat_text_message/flyer_chat_text_message.dart';
import 'package:flyer_chat_text_stream_message/flyer_chat_text_stream_message.dart';
import 'package:path_provider/path_provider.dart';
import 'package:provider/provider.dart';
import 'package:record/record.dart';
import 'package:uuid/uuid.dart';

import '../brain_theme.dart';
import '../user_actions/chat_login_action.dart';
import '../user_actions/gmail_login_card.dart';
import '../user_actions/salesforce_login_card.dart';
import 'brain_chat_composer.dart';
import 'chat_contracts.dart';
import 'stream_state_store.dart';
import 'voice_file_io.dart'
    if (dart.library.html) 'voice_file_web.dart'
    as voice_file;

final class _PendingChatSend {
  _PendingChatSend({
    required this.id,
    required this.text,
    required this.afterSequence,
    required this.expectsAcceptance,
  }) : createdAt = DateTime.now().toUtc();

  final String id;
  final String text;
  final int afterSequence;
  final bool expectsAcceptance;
  final DateTime createdAt;
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
    this.onStream,
    this.onStreamVoice,
    this.onAttachmentTap,
    this.onOpenSignIn,
    this.kernelBaseUri,
    this.onCancelTurn,
    this.onReadChart,
    this.onReadImageBytes,
    this.onReadSpreadsheet,
    this.onReadGraph,
  });

  final String chatName;
  final List<ChatTurnEvent> turns;
  final SendMessage? onSend;
  final StreamMessage? onStream;
  final StreamVoice? onStreamVoice;
  final VoidCallback? onAttachmentTap;
  final OpenUrl? onOpenSignIn;
  final Uri? kernelBaseUri;
  final CancelChatTurn? onCancelTurn;
  final ReadChart? onReadChart;
  final ReadImageBytes? onReadImageBytes;
  final ReadSpreadsheet? onReadSpreadsheet;
  final ReadGraph? onReadGraph;

  @override
  State<BrainChatScreen> createState() => _BrainChatScreenState();
}

final class _BrainChatScreenState extends State<BrainChatScreen> {
  static const _owner = User(id: ownerUserId, name: 'you');
  static const _assistant = User(id: assistantUserId, name: 'brain');
  static const _uuid = Uuid();
  static const _voicePlaceholder = '🎤 …';

  final _controller = InMemoryChatController();
  final _streamStates = StreamStateStore();
  final _voice = VoiceComposerController();
  final _appliedSequences = <int>{};
  final _recorder = AudioRecorder();
  Map<String, ChatLoginAction> _loginActions = const {};
  final _pendingSends = <_PendingChatSend>[];
  final _streams = <StreamIterator<ChatDelta>>{};
  String? _failure;
  _PendingChatSend? _failedSend;

  @override
  void initState() {
    super.initState();
    unawaited(_syncJournal(widget.turns));
  }

  @override
  void didUpdateWidget(covariant BrainChatScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!_sameJournal(oldWidget.turns, widget.turns)) {
      unawaited(_syncJournal(widget.turns));
    }
  }

  Future<void> _syncJournal(
    List<ChatTurnEvent> turns, {
    bool force = false,
  }) async {
    final sequences = {for (final turn in turns) turn.sequence};
    if (!force &&
        sequences.length == _appliedSequences.length &&
        sequences.every(_appliedSequences.contains)) {
      return;
    }

    for (final turn in turns) {
      if (!turn.fromUser || _appliedSequences.contains(turn.sequence)) continue;
      for (final pending in _pendingSends) {
        if (!pending.expectsAcceptance &&
            pending.commandId == null &&
            turn.sequence > pending.afterSequence &&
            (turn.text == pending.text || pending.text == _voicePlaceholder)) {
          pending.commandId = turn.commandId;
          break;
        }
      }
    }
    for (final pending in _pendingSends.toList()) {
      if (pending.commandId == null) continue;
      final responded = turns.any(
        (turn) =>
            turn.commandId == pending.commandId && turn.signal == 'Responded',
      );
      final terminal =
          responded ||
          turns.any(
            (turn) =>
                turn.commandId == pending.commandId &&
                turn.signal == 'TurnLifecycle' &&
                (turn.status == 'Failed' || turn.status == 'Cancelled'),
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

    _loginActions = ChatLoginAction.project(turns);
    final actionCommands = {
      for (final login in _loginActions.values) login.offer.commandId,
    };
    final actionsBySequence = {
      for (final login in _loginActions.values) login.offer.sequence: login,
    };
    final messages = <Message>[
      for (final turn in turns) ...[
        // The inline action card presents this command's lifecycle state.
        if (turn.signal != 'TurnLifecycle' ||
            (!actionCommands.contains(turn.commandId) &&
                (turn.status == 'Failed' || turn.status == 'Cancelled')))
          ...KitMessageFactory.messagesForTurn(
            sequence: turn.sequence,
            fromUser: turn.fromUser,
            text: turn.signal == 'TurnLifecycle'
                ? (turn.status == 'Cancelled'
                      ? 'Request cancelled.'
                      : 'Request failed. See Activity for details.')
                : turn.text,
            createdAt: turn.timestamp,
            parts: turn.kitParts,
          ),
        if (actionsBySequence[turn.sequence] case final login?)
          CustomMessage(
            id: 'user_action_${login.key}',
            authorId: assistantUserId,
            createdAt: turn.timestamp,
            metadata: {
              'kind': 'user-action',
              'actionKey': login.key,
              'status': login.status.name,
              'turnId': login.turnId,
            },
          ),
      ],
    ];

    for (final pending in _pendingSends) {
      if (!turns.any(
        (turn) => turn.fromUser && turn.commandId == pending.commandId,
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
    if (trimmed.isEmpty) {
      return;
    }

    _showFailure(null);

    final pending = _newPendingSend(
      trimmed,
      expectsAcceptance: widget.onStream != null,
    );
    await _controller.insertMessage(pending.userMessage);
    if (!mounted) return;

    final stream = widget.onStream;
    if (stream != null) {
      await _drainStream(pending, stream(trimmed));
      return;
    }

    final send = widget.onSend;
    if (send == null) {
      return;
    }

    try {
      await send(trimmed);
    } on Object catch (error) {
      if (mounted) {
        _showFailure('$error', pending: pending);
      }
    }
  }

  _PendingChatSend _newPendingSend(
    String text, {
    required bool expectsAcceptance,
  }) {
    final pending = _PendingChatSend(
      id: _uuid.v4(),
      text: text,
      expectsAcceptance: expectsAcceptance,
      afterSequence: widget.turns.fold(
        0,
        (value, turn) => turn.sequence > value ? turn.sequence : value,
      ),
    );
    _pendingSends.add(pending);
    return pending;
  }

  void _showFailure(String? message, {_PendingChatSend? pending}) {
    setState(() {
      _failure = message;
      _failedSend = pending;
    });
  }

  Future<void> _drainStream(
    _PendingChatSend pending,
    Stream<ChatDelta> deltas,
  ) async {
    final streamId = _uuid.v4();
    pending.streamId = streamId;
    final streamMessage = TextStreamMessage(
      id: streamId,
      authorId: assistantUserId,
      createdAt: DateTime.now().toUtc(),
      streamId: streamId,
    );
    await _controller.insertMessage(streamMessage);
    if (!mounted) return;
    _streamStates.start(streamId);

    final buffer = StringBuffer();
    final iterator = StreamIterator(deltas);
    _streams.add(iterator);
    try {
      while (await iterator.moveNext()) {
        if (!mounted) return;
        if (iterator.current.isAcceptance) {
          pending.commandId = iterator.current.commandId;
          await _syncJournal(widget.turns, force: true);
          continue;
        }
        buffer.write(iterator.current.text);
        if (_pendingSends.contains(pending)) {
          _streamStates.streaming(streamId, buffer.toString());
        }
      }
      if (!mounted || !_pendingSends.contains(pending)) return;
      if (buffer.isEmpty) {
        throw StateError(
          'The assistant connection ended without a response. Please try again.',
        );
      }
      _streamStates.complete(streamId, buffer.toString());
    } on Object catch (error) {
      if (mounted && _pendingSends.contains(pending)) {
        _streamStates.error(streamId, '$error');
        _showFailure('$error', pending: pending);
      }
    } finally {
      _streams.remove(iterator);
      await iterator.cancel();
    }
  }

  Future<void> _toggleVoice() async {
    final streamVoice = widget.onStreamVoice;
    if (streamVoice == null || _voice.busy) {
      return;
    }

    if (_voice.recording) {
      await _stopAndSendVoice(streamVoice);
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

  Future<void> _stopAndSendVoice(StreamVoice streamVoice) async {
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
      final pending = _newPendingSend(
        _voicePlaceholder,
        expectsAcceptance: true,
      );
      await _controller.insertMessage(pending.userMessage);
      if (!mounted) return;

      _voice.update(busy: false);
      await _drainStream(pending, streamVoice(bytes, fileName: 'voice.wav'));
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
    for (final stream in _streams.toList()) {
      unawaited(stream.cancel());
    }
    _streams.clear();
    unawaited(_recorder.dispose());
    _controller.dispose();
    _streamStates.dispose();
    _voice.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final canSend = widget.onSend != null || widget.onStream != null;
    final canVoice = widget.onStreamVoice != null;

    return ColoredBox(
      color: BrainPalette.surface,
      child: Column(
        children: [
          ListenableBuilder(
            listenable: _voice,
            builder: (context, _) {
              if (!_voice.recording && !_voice.busy) {
                return const SizedBox.shrink();
              }
              return Material(
                color: _voice.recording
                    ? BrainPalette.signal.withValues(alpha: 0.12)
                    : BrainPalette.surfaceRaised,
                child: Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 16,
                    vertical: 10,
                  ),
                  child: Row(
                    children: [
                      Icon(
                        _voice.recording ? Icons.mic : Icons.hourglass_top,
                        size: 18,
                        color: _voice.recording
                            ? BrainPalette.signal
                            : BrainPalette.textMuted,
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          _voice.recording
                              ? 'Recording… tap the mic to stop and send'
                              : 'Sending voice…',
                          style: BrainType.meta.copyWith(
                            color: _voice.recording
                                ? BrainPalette.signal
                                : BrainPalette.textMuted,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              );
            },
          ),
          Expanded(
            child: ChangeNotifierProvider.value(
              value: _streamStates,
              child: ChangeNotifierProvider.value(
                value: _voice,
                child: KitChat(
                  key: const Key('chat_surface'),
                  chatController: _controller,
                  currentUserId: ownerUserId,
                  resolveUser: (id) async => switch (id) {
                    ownerUserId => _owner,
                    assistantUserId => _assistant,
                    _ => null,
                  },
                  onMessageSend: canSend ? _handleSend : null,
                  onAttachmentTap: widget.onAttachmentTap,
                  builders: Builders(
                    composerBuilder: (context) => BrainChatComposer(
                      canVoice: canVoice,
                      onVoiceTap: () => unawaited(_toggleVoice()),
                    ),
                    textMessageBuilder:
                        (
                          context,
                          message,
                          index, {
                          required bool isSentByMe,
                          MessageGroupStatus? groupStatus,
                        }) => FlyerChatTextMessage(
                          message: message,
                          index: index,
                          showTime: false,
                          showStatus: false,
                        ),
                    textStreamMessageBuilder:
                        (
                          context,
                          message,
                          index, {
                          required bool isSentByMe,
                          MessageGroupStatus? groupStatus,
                        }) {
                          final streamState = context
                              .watch<StreamStateStore>()
                              .stateFor(message.streamId);
                          return FlyerChatTextStreamMessage(
                            message: message,
                            index: index,
                            streamState: streamState,
                            showTime: false,
                            showStatus: false,
                          );
                        },
                    // Flyer Chat: CustomMessage + customMessageBuilder
                    // https://pub.dev/packages/flutter_chat_ui
                    customMessageBuilder:
                        (
                          context,
                          message,
                          index, {
                          required bool isSentByMe,
                          MessageGroupStatus? groupStatus,
                        }) {
                          if (message.metadata?['kind'] == 'user-action') {
                            final login =
                                _loginActions[message.metadata?['actionKey']];
                            if (login == null) return const SizedBox.shrink();
                            return switch (login.action.provider) {
                              'salesforce' => SalesforceLoginCard(
                                key: ValueKey(login.key),
                                login: login,
                                kernelBaseUri: widget.kernelBaseUri,
                                onOpenSignIn: widget.onOpenSignIn,
                                onCancelTurn: widget.onCancelTurn,
                              ),
                              'gmail' => GmailLoginCard(
                                key: ValueKey(login.key),
                                login: login,
                                kernelBaseUri: widget.kernelBaseUri,
                                onOpenSignIn: widget.onOpenSignIn,
                                onCancelTurn: widget.onCancelTurn,
                              ),
                              _ => const SizedBox.shrink(),
                            };
                          }
                          return KitChatBuilders.customMessageBuilder(
                            context,
                            message,
                            index,
                            isSentByMe: isSentByMe,
                            groupStatus: groupStatus,
                            onReadChart: widget.onReadChart,
                            onReadImageBytes: widget.onReadImageBytes,
                            onReadSpreadsheet: widget.onReadSpreadsheet,
                            onReadGraph: widget.onReadGraph,
                          );
                        },
                  ),
                ),
              ),
            ),
          ),
          if (_failure != null)
            Padding(
              padding: const EdgeInsets.all(12),
              child: Text(_failure!, style: BrainType.bodyMuted),
            ),
        ],
      ),
    );
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
          a[i].text != b[i].text ||
          a[i].buttons.length != b[i].buttons.length ||
          a[i].charts.length != b[i].charts.length ||
          a[i].status != b[i].status ||
          a[i].turnId != b[i].turnId ||
          a[i].userAction?.id != b[i].userAction?.id) {
        return false;
      }
    }
    return true;
  }
}
