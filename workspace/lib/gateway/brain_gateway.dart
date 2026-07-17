import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:web_socket_channel/web_socket_channel.dart';

import 'envelope.dart';

class BrainGateway {
  BrainGateway({
    required this.httpBase,
    required this.wsBase,
    http.Client? client,
  }) : _client = client ?? http.Client();

  final String httpBase;
  final String wsBase;
  final http.Client _client;

  int lastSequence = 0;

  Future<Map<String, dynamic>> invoke(
    String address,
    String contract,
    String inputJson,
    String commandId, {
    int? expectedRevision,
  }) async {
    final response = await _client.post(
      Uri.parse('$httpBase/ui/invoke'),
      headers: const {'Content-Type': 'application/json'},
      body: jsonEncode({
        'address': address,
        'contract': contract,
        'inputJson': inputJson,
        'commandId': commandId,
        'expectedRevision': ?expectedRevision,
      }),
    );

    if (response.statusCode == 200) {
      return jsonDecode(response.body) as Map<String, dynamic>;
    }
    if (response.statusCode == 409) {
      final body = jsonDecode(response.body) as Map<String, dynamic>;
      throw GatewayException(body['code'] as String, body['detail'] as String);
    }
    throw GatewayException('http.error', 'status ${response.statusCode}');
  }

  Future<NeuronSnapshot> read(
    String address, {
    String projection = 'default',
  }) async {
    final uri = Uri.parse(
      '$httpBase/ui/read',
    ).replace(queryParameters: {'address': address, 'projection': projection});
    final response = await _client.get(uri);
    return NeuronSnapshot.fromJson(
      jsonDecode(response.body) as Map<String, dynamic>,
    );
  }

  Future<NeuronDescription> describe(String address) async {
    final uri = Uri.parse(
      '$httpBase/ui/describe',
    ).replace(queryParameters: {'address': address});
    final response = await _client.get(uri);
    return NeuronDescription.fromJson(
      jsonDecode(response.body) as Map<String, dynamic>,
    );
  }

  Stream<FeedFrame> watch({
    int cursor = 0,
    String space = 'actor/ui-dev',
  }) async* {
    final uri = Uri.parse(
      '$wsBase/ui/watch',
    ).replace(queryParameters: {'cursor': '$cursor', 'space': space});
    final channel = WebSocketChannel.connect(uri);
    await channel.ready;
    await for (final message in channel.stream) {
      final frame = mapFrame(message as String);
      if (frame == null) continue;
      lastSequence = frame.sequence;
      yield frame;
    }
  }

  static FeedFrame? mapFrame(String text) {
    final decoded = jsonDecode(text) as Map<String, dynamic>;
    if (decoded['ping'] == true) {
      return null;
    }
    return FeedFrame.fromJson(decoded);
  }
}
