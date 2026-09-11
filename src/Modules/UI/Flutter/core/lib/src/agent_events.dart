import 'dart:convert';

/// Standard AG-UI event. Unknown types are retained for forward compatibility.
final class AgentEvent {
  AgentEvent(Map<String, Object?> data) : data = Map.unmodifiable(data);
  final Map<String, Object?> data;
  String get type => data['type'] as String;
  String? string(String key) =>
      data[key] is String ? data[key] as String : null;
}

/// Parses SSE independently of network chunk boundaries, including UTF-8 and
/// multiline data. Malformed frames fail visibly instead of losing agent output.
Stream<AgentEvent> decodeAgentEvents(Stream<List<int>> bytes) async* {
  final data = <String>[];
  AgentEvent decode() {
    final value = jsonDecode(data.join('\n'));
    data.clear();
    if (value is! Map || value['type'] is! String) {
      throw const FormatException('Invalid AG-UI event.');
    }
    return AgentEvent(Map<String, Object?>.from(value));
  }

  await for (final line
      in bytes.transform(utf8.decoder).transform(const LineSplitter())) {
    if (line.isEmpty) {
      if (data.isNotEmpty) yield decode();
    } else if (line.startsWith('data:')) {
      var value = line.substring(5);
      if (value.startsWith(' ')) value = value.substring(1);
      data.add(value);
    }
  }
  if (data.isNotEmpty) yield decode();
}
