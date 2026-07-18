import 'dart:typed_data';

abstract class AudioTransport {
  Future<String> transcribe(
    Stream<Uint8List> audioChunks, {
    int sampleRate = 16000,
    int channels = 1,
  });

  Future<void> close();
}
