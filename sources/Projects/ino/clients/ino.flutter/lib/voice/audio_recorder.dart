import 'dart:typed_data';

import 'package:record/record.dart';

class AudioRecorderService {
  AudioRecorderService() : _recorder = AudioRecorder();

  final AudioRecorder _recorder;
  bool _isRecording = false;

  bool get isRecording => _isRecording;

  Future<bool> hasPermission() => _recorder.hasPermission();

  Future<Stream<Uint8List>> startRecording() async {
    _isRecording = true;
    final stream = await _recorder.startStream(const RecordConfig(
      encoder: AudioEncoder.pcm16bits,
      sampleRate: 16000,
      numChannels: 1,
      autoGain: true,
      echoCancel: true,
      noiseSuppress: true,
    ));
    return stream.cast<Uint8List>();
  }

  Future<void> stopRecording() async {
    _isRecording = false;
    await _recorder.stop();
  }

  Future<void> dispose() async {
    if (_isRecording) await stopRecording();
    _recorder.dispose();
  }
}
