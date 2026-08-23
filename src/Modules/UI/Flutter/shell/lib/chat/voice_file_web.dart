import 'package:http/http.dart' as http;

// record_web stop() yields a blob URL — fetch bytes for multipart upload.
Future<List<int>> readVoiceBytes(String path) async {
  final response = await http.get(Uri.parse(path));
  if (response.statusCode != 200) {
    throw StateError('Failed to read recorded audio (${response.statusCode}).');
  }
  return response.bodyBytes;
}

String joinVoicePath(String directory, String fileName) => fileName;

Future<void> deleteVoiceFile(String path) async {}
