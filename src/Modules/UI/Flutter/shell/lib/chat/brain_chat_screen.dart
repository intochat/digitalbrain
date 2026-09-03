import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter_chat_ui/flutter_chat_ui.dart';
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
    this.onActivateButton,
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
  final ActivateChatButton? onActivateButton;
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
  String? _pendingUserMessageId;
  String? _pendingUserText;
  String? _activeStreamId;
  String? _failure;

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

  Future<void> _syncJournal(List<ChatTurnEvent> turns) async {
    final sequences = {for (final turn in turns) turn.sequence};
    if (sequences.length == _appliedSequences.length &&
        sequences.every(_appliedSequences.contains)) {
      return;
    }

    if (sequences.isEmpty &&
        (_pendingUserMessageId != null || _activeStreamId != null)) {
      return;
    }

    if (_pendingUserMessageId != null) {
      final matchedExact = turns.any(
        (turn) => turn.fromUser && turn.text == _pendingUserText,
      );
      // Voice path: server text arrives only after STT; clear on any new user turn.
      final matchedNewUser = turns.any(
        (turn) => turn.fromUser && !_appliedSequences.contains(turn.sequence),
      );
      if (matchedExact || matchedNewUser) {
        _pendingUserMessageId = null;
        _pendingUserText = null;
      }
    }

    final journalHasAssistant = turns.any((turn) => !turn.fromUser);
    if (_activeStreamId != null && journalHasAssistant) {
      _streamStates.forget(_activeStreamId!);
      _activeStreamId = null;
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
        if (turn.synapse != 'TurnLifecycle' ||
            !actionCommands.contains(turn.commandId))
          ...KitMessageFactory.messagesForTurn(
            sequence: turn.sequence,
            fromUser: turn.fromUser,
            text: turn.text,
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

    if (_pendingUserMessageId != null && _pendingUserText != null) {
      messages.add(
        TextMessage(
          id: _pendingUserMessageId!,
          authorId: ownerUserId,
          createdAt: DateTime.now().toUtc(),
          text: _pendingUserText!,
        ),
      );
    }

    if (_activeStreamId != null) {
      messages.add(
        TextStreamMessage(
          id: _activeStreamId!,
          authorId: assistantUserId,
          createdAt: DateTime.now().toUtc(),
          streamId: _activeStreamId!,
        ),
      );
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

    setState(() => _failure = null);

    final localId = _uuid.v4();
    _pendingUserMessageId = localId;
    _pendingUserText = trimmed;
    await _controller.insertMessage(
      TextMessage(
        id: localId,
        authorId: ownerUserId,
        createdAt: DateTime.now().toUtc(),
        text: trimmed,
      ),
    );

    final stream = widget.onStream;
    if (stream != null) {
      await _drainStream(stream(trimmed));
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
        setState(() => _failure = '$error');
      }
    }
  }

  Future<void> _drainStream(Stream<ChatDelta> deltas) async {
    final streamId = _uuid.v4();
    _activeStreamId = streamId;
    final streamMessage = TextStreamMessage(
      id: streamId,
      authorId: assistantUserId,
      createdAt: DateTime.now().toUtc(),
      streamId: streamId,
    );
    await _controller.insertMessage(streamMessage);
    _streamStates.start(streamId);

    final buffer = StringBuffer();
    try {
      await for (final delta in deltas) {
        buffer.write(delta.text);
        _streamStates.streaming(streamId, buffer.toString());
      }
      _streamStates.complete(streamId, buffer.toString());
    } on Object catch (error) {
      _streamStates.error(streamId, '$error');
      if (mounted) {
        setState(() => _failure = '$error');
      }
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
          setState(() => _failure = 'Microphone permission denied.');
        }
        return;
      }

      // Whisper expects a container (WAV); raw PCM is refused server-side quality.
      if (!await _recorder.isEncoderSupported(AudioEncoder.wav)) {
        if (mounted) {
          setState(
            () => _failure = 'WAV recording is not supported on this device.',
          );
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
        setState(() => _failure = null);
      }
    } on Object catch (error) {
      _voice.update(recording: false, busy: false);
      if (mounted) {
        setState(() => _failure = 'Record failed: $error');
      }
    }
  }

  Future<void> _stopAndSendVoice(StreamVoice streamVoice) async {
    _voice.update(recording: false, busy: true);
    if (mounted) {
      setState(() => _failure = null);
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

      final localId = _uuid.v4();
      _pendingUserMessageId = localId;
      _pendingUserText = _voicePlaceholder;
      await _controller.insertMessage(
        TextMessage(
          id: localId,
          authorId: ownerUserId,
          createdAt: DateTime.now().toUtc(),
          text: _voicePlaceholder,
        ),
      );

      _voice.update(busy: false);
      await _drainStream(streamVoice(bytes, fileName: 'voice.wav'));
    } on Object catch (error) {
      if (mounted) {
        setState(() => _failure = '$error');
      }
    } finally {
      if (path != null) {
        try {
          await voice_file.deleteVoiceFile(path);
        } on Object {
          // best-effort temp cleanup
        }
      }
      _voice.update(recording: false, busy: false);
    }
  }

  Future<void> _onKitButton(KitButtonPart part) async {
    final activate = widget.onActivateButton;
    if (activate == null) {
      return;
    }
    final offer = part.offerCommandId;
    if (offer == null || offer.isEmpty) {
      return;
    }
    try {
      await activate(
        offerCommandId: offer,
        buttonId: part.buttonId,
        action: part.action,
      );
    } on Object catch (error) {
      if (mounted) {
        setState(() => _failure = '$error');
      }
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
                child: Chat(
                  key: const Key('chat_surface'),
                  chatController: _controller,
                  currentUserId: ownerUserId,
                  resolveUser: (id) async => switch (id) {
                    ownerUserId => _owner,
                    assistantUserId => _assistant,
                    _ => null,
                  },
                  theme: BrainChatTheme.dark(),
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
                            onButtonPressed: widget.onActivateButton == null
                                ? null
                                : _onKitButton,
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
