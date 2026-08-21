import 'dart:typed_data';

final class PersonaPlexAudioPacket {
  PersonaPlexAudioPacket({
    required this.sequence,
    required Uint8List pcm16Bytes,
  }) : _pcm16Bytes = Uint8List.fromList(pcm16Bytes);

  final int sequence;
  final Uint8List _pcm16Bytes;

  Uint8List get pcm16Bytes => Uint8List.fromList(_pcm16Bytes);
}

abstract final class PersonaPlexVoiceProtocol {
  static const version = 1;
  static const sampleRate = 24000;
  static const channelCount = 1;
  static const sampleCount = 1920;
  static const headerByteCount = 16;
  static const pcmByteCount = sampleCount * 2;
  static const packetByteCount = headerByteCount + pcmByteCount;

  static PersonaPlexAudioPacket decodeAudio(Uint8List packet) {
    if (packet.length != packetByteCount) {
      throw const FormatException(
        'PersonaPlex audio packets require exactly 3,840 PCM payload bytes.',
      );
    }

    final header = ByteData.sublistView(packet, 0, headerByteCount);
    if (header.getInt32(0, Endian.little) != version) {
      throw const FormatException(
        'Unsupported PersonaPlex voice protocol version.',
      );
    }

    final sequence = header.getInt64(4, Endian.little);
    if (sequence <= 0) {
      throw const FormatException(
        'PersonaPlex audio sequence numbers must be positive.',
      );
    }
    if (header.getInt32(12, Endian.little) != sampleCount) {
      throw const FormatException(
        'PersonaPlex audio packets require exactly 1,920 samples.',
      );
    }

    return PersonaPlexAudioPacket(
      sequence: sequence,
      pcm16Bytes: Uint8List.sublistView(packet, headerByteCount),
    );
  }

  static Uint8List encodeAudio({
    required int sequence,
    required Uint8List pcm16Bytes,
  }) {
    if (sequence <= 0) {
      throw ArgumentError.value(sequence, 'sequence', 'must be positive');
    }
    if (pcm16Bytes.length != pcmByteCount) {
      throw ArgumentError.value(
        pcm16Bytes.length,
        'pcm16Bytes',
        'must contain exactly $pcmByteCount bytes',
      );
    }

    final packet = Uint8List(packetByteCount);
    ByteData.sublistView(packet, 0, headerByteCount)
      ..setInt32(0, version, Endian.little)
      ..setInt64(4, sequence, Endian.little)
      ..setInt32(12, sampleCount, Endian.little);
    packet.setRange(headerByteCount, packetByteCount, pcm16Bytes);
    return packet;
  }
}
