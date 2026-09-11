import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:path_provider/path_provider.dart';
import 'package:record/record.dart';

import '../chat/voice_file_io.dart'
    if (dart.library.js_interop) '../chat/voice_file_web.dart'
    as voice_file;

/// Recording produces a draft. Only the conversation's Send action submits it.
class WorkspaceVoiceButton extends StatefulWidget {
  const WorkspaceVoiceButton({
    super.key,
    required this.onTranscribe,
    required this.onDraft,
    this.enabled = true,
  });
  final Future<String> Function(Uint8List audio, String fileName)? onTranscribe;
  final ValueChanged<String> onDraft;
  final bool enabled;
  @override
  State<WorkspaceVoiceButton> createState() => _WorkspaceVoiceButtonState();
}

class _WorkspaceVoiceButtonState extends State<WorkspaceVoiceButton>
    with WidgetsBindingObserver {
  final _recorder = AudioRecorder();
  bool _recording = false, _busy = false;
  String? _path;
  Timer? _timer;
  int _seconds = 0, _generation = 0;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state != AppLifecycleState.resumed && (_recording || _busy)) {
      unawaited(_cancel());
    }
  }

  @override
  void didUpdateWidget(covariant WorkspaceVoiceButton oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.enabled && !widget.enabled) unawaited(_cancel());
  }

  Future<void> _cancel() async {
    final generation = ++_generation;
    _timer?.cancel();
    final path = _path;
    _path = null;
    Object? failure;
    try {
      await _recorder.cancel();
    } catch (error) {
      failure = error;
    } finally {
      try {
        if (path != null) await voice_file.deleteVoiceFile(path);
      } catch (error) {
        failure ??= error;
      } finally {
        if (mounted && generation == _generation) {
          setState(() {
            _recording = false;
            _busy = false;
          });
          if (failure != null) {
            ScaffoldMessenger.of(context).showSnackBar(
              SnackBar(
                content: Text(
                  'Could not finish cancelling recording: $failure',
                ),
              ),
            );
          }
        }
      }
    }
  }

  Future<void> _toggle() async {
    final generation = ++_generation;
    final onDraft = widget.onDraft;
    final transcribe = widget.onTranscribe!;
    setState(() => _busy = true);
    try {
      if (_recording) {
        _timer?.cancel();
        final path = await _recorder.stop();
        if (mounted) setState(() => _recording = false);
        if (path == null) {
          throw StateError('The recording did not produce audio.');
        }
        _path = path;
        try {
          final bytes = Uint8List.fromList(
            await voice_file.readVoiceBytes(path),
          );
          if (bytes.isEmpty) throw StateError('The recording was empty.');
          if (!mounted || generation != _generation) return;
          final draft = await transcribe(bytes, 'voice.wav');
          if (mounted && generation == _generation) {
            if (draft.trim().isEmpty) {
              throw StateError('No speech was detected. Try again.');
            }
            onDraft(draft);
          }
        } finally {
          // A cancelled transcription can finish after a new recording starts.
          // Clean only the completed capture, never the new generation's path.
          await voice_file.deleteVoiceFile(path);
          if (_path == path) _path = null;
        }
      } else {
        if (!await _recorder.hasPermission()) {
          throw StateError('Microphone permission was denied.');
        }
        if (!await _recorder.isEncoderSupported(AudioEncoder.wav)) {
          throw StateError('WAV recording is unavailable on this device.');
        }
        final directory = kIsWeb ? '' : (await getTemporaryDirectory()).path;
        if (!mounted || generation != _generation) return;
        _path = voice_file.joinVoicePath(
          directory,
          'workspace_voice_${DateTime.now().microsecondsSinceEpoch}.wav',
        );
        await _recorder.start(
          const RecordConfig(
            encoder: AudioEncoder.wav,
            sampleRate: 16000,
            numChannels: 1,
          ),
          path: _path!,
        );
        if (!mounted || generation != _generation) {
          await _recorder.cancel();
          return;
        }
        setState(() {
          _recording = true;
          _seconds = 0;
        });
        _timer = Timer.periodic(const Duration(seconds: 1), (_) {
          if (!mounted) return;
          setState(() => _seconds++);
          if (_seconds >= 120 && !_busy) unawaited(_toggle());
        });
      }
    } catch (error) {
      if (mounted && generation == _generation) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('Voice: $error')));
      }
    } finally {
      if (mounted && generation == _generation) setState(() => _busy = false);
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _generation++;
    _timer?.cancel();
    final path = _path;
    unawaited(
      _recorder.dispose().whenComplete(() async {
        if (path != null) await voice_file.deleteVoiceFile(path);
      }),
    );
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Row(
    mainAxisSize: MainAxisSize.min,
    children: [
      if (_recording) ...[
        Text('${_seconds ~/ 60}:${(_seconds % 60).toString().padLeft(2, '0')}'),
        IconButton(
          tooltip: 'Cancel recording',
          onPressed: _cancel,
          icon: const Icon(Icons.close, size: 18),
        ),
      ],
      IconButton(
        key: const Key('workspace_voice'),
        tooltip: _busy
            ? 'Transcribing…'
            : _recording
            ? 'Stop recording and edit transcript'
            : 'Record a voice draft',
        onPressed: widget.enabled && widget.onTranscribe != null && !_busy
            ? _toggle
            : null,
        color: _recording ? Colors.red : null,
        icon: _busy
            ? const SizedBox.square(
                dimension: 18,
                child: CircularProgressIndicator(strokeWidth: 2),
              )
            : Icon(
                _recording
                    ? Icons.stop_circle_outlined
                    : Icons.mic_none_rounded,
              ),
      ),
    ],
  );
}
