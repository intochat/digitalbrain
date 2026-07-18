import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:web_socket_channel/web_socket_channel.dart';

import '../surface/ui_surface_client.dart';
import '../surface/ui_surface_models.dart';

class GatewayException implements Exception {
  GatewayException(this.code, this.detail);

  final String code;
  final String detail;

  @override
  String toString() => 'GatewayException($code, $detail)';
}

class BrainGateway implements UiSurfaceClient {
  BrainGateway({
    required this.httpBase,
    required this.wsBase,
    http.Client? client,
  }) : _client = client ?? http.Client();

  final String httpBase;
  final String wsBase;
  final http.Client _client;

  int lastSequence = 0;

  @override
  Future<UiSurfaceSnapshot> fetchSnapshot(String surfaceId) async {
    final uri = Uri.parse('$httpBase/ui/surface').replace(
      queryParameters: {'surfaceId': surfaceId},
    );
    final response = await _client.get(uri);
    final body = _decodeBody(response);
    final schemaVersion = body['schemaVersion'];
    if (schemaVersion is int &&
        schemaVersion != UiFeedMessage.supportedSchemaVersion) {
      throw GatewayException(
        'schema.unsupported',
        'unsupported schema version $schemaVersion',
      );
    }
    return UiSurfaceSnapshot.fromJson(body);
  }

  @override
  Future<void> sendSurfaceAction({
    required String surfaceId,
    required String actionId,
    required int expectedRevision,
  }) async {
    final response = await _client.post(
      Uri.parse('$httpBase/ui/surface/action'),
      headers: const {'Content-Type': 'application/json'},
      body: jsonEncode({
        'surfaceId': surfaceId,
        'actionId': actionId,
        'expectedRevision': expectedRevision,
      }),
    );
    _decodeBody(response);
  }

  @override
  Stream<UiFeedMessage> watch({required int cursor}) async* {
    final uri = Uri.parse('$wsBase/ui/watch').replace(
      queryParameters: {'cursor': '$cursor'},
    );
    final channel = WebSocketChannel.connect(uri);
    await channel.ready;
    await for (final message in channel.stream) {
      if (message is! String) {
        continue;
      }
      final frame = mapFrame(message);
      if (frame == null) {
        continue;
      }
      lastSequence = frame.sequence;
      yield frame;
    }
  }

  static UiFeedMessage? mapFrame(String text) {
    try {
      final decoded = jsonDecode(text);
      if (decoded is! Map<String, dynamic>) {
        return null;
      }
      if (decoded['ping'] == true) {
        return null;
      }
      return UiFeedMessage.parse(decoded);
    } on FormatException {
      return null;
    } on TypeError {
      return null;
    }
  }

  Map<String, dynamic> _decodeBody(http.Response response) {
    if (response.statusCode == 409) {
      final body = jsonDecode(response.body) as Map<String, dynamic>;
      throw GatewayException(
        body['code'] as String? ?? 'conflict',
        body['detail'] as String? ?? 'conflict',
      );
    }
    if (response.statusCode != 200) {
      throw GatewayException('http.error', 'status ${response.statusCode}');
    }
    return jsonDecode(response.body) as Map<String, dynamic>;
  }
}
