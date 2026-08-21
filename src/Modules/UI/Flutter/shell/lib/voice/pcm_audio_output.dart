import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_soloud/flutter_soloud.dart';

final class PersonaPlexVoiceUnavailableException implements Exception {
  const PersonaPlexVoiceUnavailableException(this.message);

  final String message;

  @override
  String toString() => message;
}

abstract interface class PcmAudioOutput {
  Future<void> start();
  Future<void> addPcm16(Uint8List pcm16Bytes);
  Future<void> stop();
  Future<void> dispose();
}

abstract interface class PcmAudioEngine {
  Future<void> initialize({required int sampleRate, required int channelCount});

  Object createPcm16Stream();
  void addPcm16(Object stream, Uint8List pcm16Bytes);
  Future<void> play(Object stream);
  Future<void> release(Object stream);
  Future<void> deinitialize();
}

final class SoLoudPcmAudioOutput implements PcmAudioOutput {
  SoLoudPcmAudioOutput({PcmAudioEngine? engine})
    : _engine = engine ?? _FlutterSoLoudPcmAudioEngine();

  final PcmAudioEngine _engine;
  Object? _stream;
  Future<void>? _playFuture;
  Future<void>? _stopFuture;
  bool _engineInitialized = false;

  @override
  Future<void> start() async {
    if (_stream != null) {
      return;
    }
    try {
      await _engine.initialize(
        sampleRate: PersonaPlexVoiceProtocol.sampleRate,
        channelCount: PersonaPlexVoiceProtocol.channelCount,
      );
      if (_stopFuture != null) {
        await _ignoreFailure(_engine.deinitialize());
        return;
      }
      _engineInitialized = true;
      _stream = _engine.createPcm16Stream();
    } on Object {
      if (_engineInitialized) {
        await _ignoreFailure(_engine.deinitialize());
        _engineInitialized = false;
      }
      throw PersonaPlexVoiceUnavailableException(_unavailableMessage);
    }
  }

  @override
  Future<void> addPcm16(Uint8List pcm16Bytes) async {
    if (pcm16Bytes.length != PersonaPlexVoiceProtocol.pcmByteCount) {
      throw ArgumentError.value(
        pcm16Bytes.length,
        'pcm16Bytes',
        'must contain exactly ${PersonaPlexVoiceProtocol.pcmByteCount} bytes',
      );
    }
    final stream = _stream;
    if (stream == null || _stopFuture != null) {
      throw StateError('PCM audio output is not active.');
    }
    try {
      _engine.addPcm16(stream, pcm16Bytes);
      await (_playFuture ??= _engine.play(stream));
    } on Object {
      throw PersonaPlexVoiceUnavailableException(_unavailableMessage);
    }
  }

  @override
  Future<void> stop() => _stopFuture ??= _stop();

  Future<void> _stop() async {
    final stream = _stream;
    _stream = null;
    if (stream != null) {
      await _ignoreFailure(_engine.release(stream));
    }
    if (_engineInitialized) {
      await _ignoreFailure(_engine.deinitialize());
      _engineInitialized = false;
    }
  }

  @override
  Future<void> dispose() => stop();

  static String get _unavailableMessage => kIsWeb
      ? 'Continuous 24 kHz mono PCM16 playback failed to initialize in this '
            'browser. Verify flutter_soloud web loader scripts and browser '
            'support.'
      : 'Continuous 24 kHz mono PCM16 playback is unavailable on this device.';

  static Future<void> _ignoreFailure(Future<void> future) async {
    try {
      await future;
    } on Object {
      // Continue releasing the other independently owned audio resources.
    }
  }
}

final class _FlutterSoLoudPcmAudioEngine implements PcmAudioEngine {
  final SoLoud _soLoud = SoLoud.instance;
  SoundHandle? _handle;

  @override
  Future<void> initialize({
    required int sampleRate,
    required int channelCount,
  }) {
    if (channelCount != 1) {
      throw ArgumentError.value(channelCount, 'channelCount', 'must be mono');
    }
    return _soLoud.init(
      sampleRate: sampleRate,
      bufferSize: PersonaPlexVoiceProtocol.sampleCount,
      channels: Channels.mono,
      lowLatency: true,
    );
  }

  @override
  Object createPcm16Stream() => _soLoud.setBufferStream(
    maxBufferSizeDuration: const Duration(seconds: 2),
    bufferingType: BufferingType.released,
    bufferingTimeNeeds: 0.08,
    sampleRate: PersonaPlexVoiceProtocol.sampleRate,
    channels: Channels.mono,
    format: BufferType.s16le,
  );

  @override
  void addPcm16(Object stream, Uint8List pcm16Bytes) {
    _soLoud.addAudioDataStream(stream as AudioSource, pcm16Bytes);
  }

  @override
  Future<void> play(Object stream) async {
    _handle = _soLoud.play(stream as AudioSource);
  }

  @override
  Future<void> release(Object stream) async {
    final source = stream as AudioSource;
    _soLoud.setDataIsEnded(source);
    final handle = _handle;
    _handle = null;
    if (handle != null) {
      await _soLoud.stop(handle);
    }
    await _soLoud.disposeSource(source);
  }

  @override
  Future<void> deinitialize() => _soLoud.deinitAsync();
}
