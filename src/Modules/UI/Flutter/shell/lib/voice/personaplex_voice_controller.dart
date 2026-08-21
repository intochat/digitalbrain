import 'dart:async';
import 'dart:math' as math;

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/foundation.dart';
import 'package:record/record.dart';

import 'pcm_audio_output.dart';

abstract interface class PersonaPlexAudioCapture {
  Future<bool> hasPermission();
  Future<bool> isPcm16Supported();
  Future<Stream<Uint8List>> start();
  Future<void> stop();
  Future<void> dispose();
}

final class RecordPersonaPlexAudioCapture implements PersonaPlexAudioCapture {
  RecordPersonaPlexAudioCapture({AudioRecorder? recorder})
    : _recorder = recorder ?? AudioRecorder();

  static const _config = RecordConfig(
    encoder: AudioEncoder.pcm16bits,
    sampleRate: PersonaPlexVoiceProtocol.sampleRate,
    numChannels: PersonaPlexVoiceProtocol.channelCount,
    autoGain: false,
    echoCancel: false,
    noiseSuppress: false,
    streamBufferSize: PersonaPlexVoiceProtocol.pcmByteCount,
  );

  final AudioRecorder _recorder;
  StreamController<Uint8List>? _stream;
  StreamSubscription<Uint8List>? _sourceSubscription;
  Future<Stream<Uint8List>>? _startFuture;
  Future<void>? _stopFuture;
  Future<void>? _disposeFuture;

  @override
  Future<bool> hasPermission() => _recorder.hasPermission();

  @override
  Future<bool> isPcm16Supported() =>
      _recorder.isEncoderSupported(AudioEncoder.pcm16bits);

  @override
  Future<Stream<Uint8List>> start() {
    if (_stopFuture != null) {
      throw StateError('PersonaPlex capture has already stopped.');
    }
    return _startFuture ??= _start();
  }

  Future<Stream<Uint8List>> _start() async {
    if (_stream != null) {
      throw StateError('PersonaPlex capture has already started.');
    }

    final output = StreamController<Uint8List>();
    _stream = output;
    await _recorder.setOnConfigChanged((config) {
      if (config.sampleRate != PersonaPlexVoiceProtocol.sampleRate ||
          config.numChannels != PersonaPlexVoiceProtocol.channelCount) {
        output.addError(
          PersonaPlexVoiceUnavailableException(
            'Microphone capture changed to ${config.sampleRate} Hz / '
            '${config.numChannels} channels; PersonaPlex requires exactly '
            '24 kHz mono PCM16.',
          ),
        );
      }
    });

    try {
      final source = await _recorder.startStream(_config);
      _sourceSubscription = source.listen(
        output.add,
        onError: output.addError,
        onDone: () {
          if (!output.isClosed) {
            unawaited(output.close());
          }
        },
      );
      return output.stream;
    } on Object {
      await _recorder.setOnConfigChanged(null);
      await output.close();
      rethrow;
    }
  }

  @override
  Future<void> stop() => _stopFuture ??= _stop();

  Future<void> _stop() async {
    final startFuture = _startFuture;
    if (startFuture != null) {
      try {
        await startFuture;
      } on Object {
        // Startup already owns its cleanup on failure.
      }
    }
    await _recorder.setOnConfigChanged(null);
    if (await _recorder.isRecording()) {
      await _recorder.stop();
    }
    await _sourceSubscription?.cancel();
    _sourceSubscription = null;
    final output = _stream;
    if (output != null && !output.isClosed) {
      await output.close();
    }
  }

  @override
  Future<void> dispose() => _disposeFuture ??= _dispose();

  Future<void> _dispose() async {
    await stop();
    await _recorder.dispose();
  }
}

enum PersonaPlexVoicePhase {
  idle,
  connecting,
  active,
  permissionDenied,
  unavailable,
  error,
  stopped,
}

typedef PersonaPlexVoiceControllerFactory =
    PersonaPlexVoiceController Function();

final class PersonaPlexVoiceController extends ChangeNotifier {
  PersonaPlexVoiceController({
    required PersonaPlexAudioCapture capture,
    required PcmAudioOutput output,
    required PersonaPlexVoiceTransport transport,
  }) : this._(capture, output, transport);

  PersonaPlexVoiceController._(this._capture, this._output, this._transport);

  final PersonaPlexAudioCapture _capture;
  final PcmAudioOutput _output;
  final PersonaPlexVoiceTransport _transport;
  final List<int> _captureBuffer = [];
  final Map<int, DateTime> _sentAt = {};

  StreamSubscription<PersonaPlexVoiceEvent>? _transportSubscription;
  StreamSubscription<Uint8List>? _captureSubscription;
  Future<void>? _activationFuture;
  Future<void>? _shutdownFuture;
  Future<void>? _disposeFuture;
  bool _notifierDisposed = false;
  int _nextSequence = 1;

  PersonaPlexVoicePhase phase = PersonaPlexVoicePhase.idle;
  String statusMessage = 'Ready to start native voice.';
  double microphoneLevel = 0;
  double speakerLevel = 0;
  int? latencyMilliseconds;

  bool get isActive => phase == PersonaPlexVoicePhase.active;

  Future<void> start() async {
    if (phase != PersonaPlexVoicePhase.idle) {
      return;
    }

    try {
      final hasPermission = await _capture.hasPermission();
      if (_shutdownFuture != null) {
        return;
      }
      if (!hasPermission) {
        _setStatus(
          PersonaPlexVoicePhase.permissionDenied,
          'Microphone permission denied. Allow microphone access to use '
          'PersonaPlex voice.',
        );
        return;
      }
      final isPcm16Supported = await _capture.isPcm16Supported();
      if (_shutdownFuture != null) {
        return;
      }
      if (!isPcm16Supported) {
        _setStatus(
          PersonaPlexVoicePhase.unavailable,
          'This microphone cannot provide 24 kHz mono PCM16 capture.',
        );
        return;
      }

      _setStatus(
        PersonaPlexVoicePhase.connecting,
        'Connecting to PersonaPlex…',
      );
      await _output.start();
      if (_shutdownFuture != null) {
        return;
      }
      _transportSubscription = _transport.events.listen(
        _handleTransportEvent,
        onError: (Object error, StackTrace stackTrace) {
          unawaited(_fail(error));
        },
        onDone: () {
          unawaited(
            _shutdown(
              PersonaPlexVoicePhase.stopped,
              'PersonaPlex voice session ended.',
            ),
          );
        },
      );
      await _transport.start();
      if (_shutdownFuture != null) {
        return;
      }
    } on Object catch (error) {
      await _fail(error);
    }
  }

  void _handleTransportEvent(PersonaPlexVoiceEvent event) {
    switch (event) {
      case PersonaPlexVoiceStatus(:final state, :final message):
        if (state == 'ready') {
          statusMessage = message;
          _notify();
          _activationFuture ??= _activateCapture();
        } else {
          _setStatus(PersonaPlexVoicePhase.connecting, message);
        }
      case PersonaPlexVoiceAudio(:final packet):
        unawaited(_playPacket(packet));
      case PersonaPlexVoiceError(:final code, :final message):
        unawaited(
          _shutdown(
            code == 'unavailable'
                ? PersonaPlexVoicePhase.unavailable
                : PersonaPlexVoicePhase.error,
            message,
          ),
        );
      case PersonaPlexVoiceStopped():
        unawaited(
          _shutdown(
            PersonaPlexVoicePhase.stopped,
            'PersonaPlex voice session ended.',
          ),
        );
    }
  }

  Future<void> _playPacket(PersonaPlexAudioPacket packet) async {
    try {
      final bytes = packet.pcm16Bytes;
      await _output.addPcm16(bytes);
      speakerLevel = _pcmLevel(bytes);
      final sentAt = _sentAt.remove(packet.sequence);
      if (sentAt != null) {
        latencyMilliseconds = DateTime.now().difference(sentAt).inMilliseconds;
      }
      _notify();
    } on Object catch (error) {
      await _fail(error);
    }
  }

  Future<void> _activateCapture() async {
    try {
      final stream = await _capture.start();
      if (_shutdownFuture != null) {
        return;
      }
      _captureSubscription = stream.listen(
        _handleCaptureBytes,
        onError: (Object error, StackTrace stackTrace) {
          unawaited(_fail(error));
        },
      );
      _setStatus(
        PersonaPlexVoicePhase.active,
        'PersonaPlex is listening and speaking.',
      );
    } on Object catch (error) {
      await _fail(error);
    }
  }

  void _handleCaptureBytes(Uint8List bytes) {
    if (phase != PersonaPlexVoicePhase.active || bytes.isEmpty) {
      return;
    }
    _captureBuffer.addAll(bytes);
    while (_captureBuffer.length >= PersonaPlexVoiceProtocol.pcmByteCount) {
      final pcm16Bytes = Uint8List.fromList(
        _captureBuffer.take(PersonaPlexVoiceProtocol.pcmByteCount).toList(),
      );
      _captureBuffer.removeRange(0, PersonaPlexVoiceProtocol.pcmByteCount);
      final sequence = _nextSequence++;
      microphoneLevel = _pcmLevel(pcm16Bytes);
      _sentAt[sequence] = DateTime.now();
      try {
        _transport.sendAudio(sequence: sequence, pcm16Bytes: pcm16Bytes);
      } on Object catch (error) {
        unawaited(_fail(error));
        return;
      }
      _notify();
    }
  }

  Future<void> _fail(Object error) {
    if (error case PersonaPlexVoiceUnavailableException(:final message)) {
      return _shutdown(PersonaPlexVoicePhase.unavailable, message);
    }
    return _shutdown(
      PersonaPlexVoicePhase.error,
      'PersonaPlex voice encountered an error. Stop and try again.',
    );
  }

  Future<void> stop() =>
      _shutdown(PersonaPlexVoicePhase.stopped, 'PersonaPlex voice stopped.');

  Future<void> _shutdown(
    PersonaPlexVoicePhase terminalPhase,
    String terminalMessage,
  ) {
    return _shutdownFuture ??= _shutdownResources(
      terminalPhase,
      terminalMessage,
    );
  }

  Future<void> _shutdownResources(
    PersonaPlexVoicePhase terminalPhase,
    String terminalMessage,
  ) async {
    final transportCancellation = _transportSubscription?.cancel();
    _transportSubscription = null;
    final captureCancellation = _captureSubscription?.cancel();
    _captureSubscription = null;
    _captureBuffer.clear();
    _sentAt.clear();
    await Future.wait([
      ?transportCancellation,
      ?captureCancellation,
      _ignoreFailure(_capture.stop()),
      _ignoreFailure(_output.stop()),
      _ignoreFailure(_transport.close()),
    ]);
    microphoneLevel = 0;
    speakerLevel = 0;
    _setStatus(terminalPhase, terminalMessage);
  }

  Future<void> disposeAsync() => _disposeFuture ??= _disposeAsync();

  Future<void> _disposeAsync() async {
    await stop();
    await Future.wait([
      _ignoreFailure(_capture.dispose()),
      _ignoreFailure(_output.dispose()),
    ]);
  }

  void _setStatus(PersonaPlexVoicePhase next, String message) {
    phase = next;
    statusMessage = message;
    _notify();
  }

  void _notify() {
    if (!_notifierDisposed) {
      notifyListeners();
    }
  }

  static Future<void> _ignoreFailure(Future<void> future) async {
    try {
      await future;
    } on Object {
      // Best-effort shutdown must continue across independent resources.
    }
  }

  static double _pcmLevel(Uint8List bytes) {
    final alignedLength = bytes.length - (bytes.length % 2);
    if (alignedLength == 0) {
      return 0;
    }
    final data = ByteData.sublistView(bytes, 0, alignedLength);
    var sumSquares = 0.0;
    for (var offset = 0; offset < alignedLength; offset += 2) {
      final normalized = data.getInt16(offset, Endian.little) / 32768.0;
      sumSquares += normalized * normalized;
    }
    return math.sqrt(sumSquares / (alignedLength / 2)).clamp(0.0, 1.0);
  }

  @override
  void dispose() {
    _notifierDisposed = true;
    unawaited(disposeAsync());
    super.dispose();
  }
}
