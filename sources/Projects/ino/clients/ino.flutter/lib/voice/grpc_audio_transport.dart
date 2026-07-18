import 'dart:typed_data';

import 'package:grpc/grpc_or_grpcweb.dart';
import 'package:ino_flutter/grpc/generated/ino.pb.dart' as pb;
import 'package:ino_flutter/grpc/generated/ino.pbgrpc.dart';
import 'package:ino_flutter/voice/audio_transport.dart';

class GrpcAudioTransport implements AudioTransport {
  GrpcAudioTransport({required GrpcOrGrpcWebClientChannel channel})
      : _stub = InoClient(channel);

  final InoClient _stub;

  @override
  Future<String> transcribe(
    Stream<Uint8List> audioChunks, {
    int sampleRate = 16000,
    int channels = 1,
  }) async {
    final protoStream = audioChunks.map((bytes) => pb.AudioChunk()
      ..data = bytes
      ..sampleRate = sampleRate
      ..channels = channels
      ..format = 'pcm16');

    final response = await _stub.transcribeAudio(protoStream);
    return response.text;
  }

  @override
  Future<void> close() async {}
}
